using FluentValidation;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.API.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly Action<ILogger, string, Exception?> RequestCancelled =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(3001, nameof(RequestCancelled)),
            "Request cancelled by client for {RequestPath}");

    private static readonly Action<ILogger, string, string, Exception?> UnhandledRequestException =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(3002, nameof(UnhandledRequestException)),
            "Unhandled exception for {RequestMethod} {RequestPath}");

    private static readonly Action<ILogger, int, string, string, string, string, Exception?> ExpectedRequestFailure =
        LoggerMessage.Define<int, string, string, string, string>(LogLevel.Warning, new EventId(3003, nameof(ExpectedRequestFailure)),
            "Request failed with {StatusCode} {ErrorCode} for {RequestMethod} {RequestPath}: {ErrorMessage}");

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            // 1. Handle Client Disconnects gracefully
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                RequestCancelled(logger, context.Request.Path, exception);
                return;
            }

            // 2. Map Exceptions to Status Codes and Error Responses
            var (statusCode, error) = exception switch
            {
                PendingMembershipCheckoutException pending => (
                    pending.StatusCode,
                    (object)new PendingMembershipCheckoutErrorResponse(
                        pending.Code, pending.Message, pending.Recovery.Provider,
                        pending.Recovery.PublicReference, pending.Recovery.Status,
                        pending.Recovery.CreatedAtUtc, pending.Recovery.CanResume,
                        pending.Recovery.CanCancel)),
                // Explicitly handles your Quota Exception to include 'redirectToMembership'
                ApplicationQuotaExceededException quotaException => (
                    quotaException.StatusCode,
                    (object)new ApplicationQuotaLimitErrorResponse(
                        false,
                        quotaException.Code,
                        quotaException.Message,
                        quotaException.RedirectToMembership)),

                AppException appException => (appException.StatusCode, (object)new ApiError(appException.Code, appException.Message)),

                ValidationException validationException => (StatusCodes.Status400BadRequest, (object)new ApiError(
                    "validation_error",
                    "One or more validation errors occurred.",
                    validationException.Errors
                        .GroupBy(x => x.PropertyName)
                        .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()))),

                BadHttpRequestException => (StatusCodes.Status400BadRequest, (object)new ApiError("invalid_request", "The request is invalid.")),

                // Fixed: Concurrency & Constraint handling
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, (object)new ApiError("concurrency_conflict", "The resource was modified by another request.")),
                UniqueConstraintException => (StatusCodes.Status409Conflict, (object)new ApiError("data_conflict", "A resource with the same unique value already exists.")),
                DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                    (StatusCodes.Status409Conflict, (object)new ApiError("data_conflict", "A resource with the same unique value already exists.")),

                // 500 Internal Server Error fallback
                _ => (StatusCodes.Status500InternalServerError, (object)ApiError.InternalServerError())
            };

            // 3. Log the error
            if (statusCode >= 500)
                UnhandledRequestException(logger, context.Request.Method, context.Request.Path, exception);
            else
                ExpectedRequestFailure(logger, statusCode, exception switch
                {
                    AppException app => app.Code,
                    ValidationException => "validation_error",
                    _ => "unknown"
                }, context.Request.Method, context.Request.Path, exception.Message, null);

            // 4. Ensure response hasn't started, then write JSON
            if (context.Response.HasStarted) throw;

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            // Use the cancellation token from the context to prevent writing if client disconnects
            await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
        }
    }
}

public sealed record PendingMembershipCheckoutErrorResponse(
    string Code, string Message,
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter<JobPortal.Domain.Enums.PaymentProvider>))]
    JobPortal.Domain.Enums.PaymentProvider Provider,
    string PublicReference, MembershipCheckoutStatus Status, DateTime CreatedAtUtc,
    bool CanResume, bool CanCancel);
