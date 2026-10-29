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
    private static readonly Action<ILogger, string, string, Exception?> RequestCancelled =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(3001, nameof(RequestCancelled)),
            "Request cancelled by client for {RequestPath}. TraceId={TraceId}.");

    private static readonly Action<ILogger, string, string, Exception?> UnhandledRequestException =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(3002, nameof(UnhandledRequestException)),
            "Unhandled exception for {RequestMethod} {RequestPath}");

    private static readonly Action<ILogger, int, string, string, string, string, Exception?> ExpectedRequestFailure =
        LoggerMessage.Define<int, string, string, string, string>(LogLevel.Warning, new EventId(3003, nameof(ExpectedRequestFailure)),
            "Request failed with {StatusCode} {ErrorCode} for {RequestMethod} {RequestPath}: {ErrorMessage}");

    private static readonly Action<ILogger, string, string, Exception?> SafeBulkImportFailure =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(3004, "BulkImportFailure"),
            "Bulk import request failed CorrelationId={CorrelationId} ExceptionType={ExceptionType}");

    private static void BulkImportRequestFailed(ILogger logger, string correlationId, string exceptionType) =>
        SafeBulkImportFailure(logger, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? correlationId, exceptionType, null);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            // 1. Handle Client Disconnects gracefully
            if (context.RequestAborted.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                RequestCancelled(logger, context.Request.Path, TraceId(context), null);
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

                GoogleRegistrationConflictException google => (google.StatusCode,
                    (object)new ApiError(google.Code, google.Message, LoginMethod: "google")),
                JobPortal.Application.Abstractions.Jobs.RippleHireDiscoveryException discoveryException =>
                    (discoveryException.StatusCode, (object)new { code = discoveryException.Code, message = discoveryException.Message, discovery = discoveryException.Discovery }),
                AppException appException => (appException.StatusCode, (object)new ApiError(appException.Code, appException.Message)),

                ValidationException validationException => (StatusCodes.Status400BadRequest, (object)new ApiError(
                    "validation_error",
                    "One or more validation errors occurred.",
                    validationException.Errors
                        .GroupBy(x => x.PropertyName)
                        .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()))),

                BadHttpRequestException => (StatusCodes.Status400BadRequest, (object)new ApiError("invalid_request", "The request is invalid.")),
                OperationCanceledException => (StatusCodes.Status503ServiceUnavailable,
                    (object)new ApiError("dependency_timeout", "A required service did not respond in time.")),
                TimeoutException => (StatusCodes.Status503ServiceUnavailable,
                    (object)new ApiError("dependency_timeout", "A required service did not respond in time.")),
                // Fixed: Concurrency & Constraint handling
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, (object)new ApiError("concurrency_conflict", "The resource was modified by another request.")),
                DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505", ConstraintName:
                    "UX_Payments_UnresolvedUserPlan" or "IX_Memberships_UserId_PlanCode" or "IX_PaymentHistory_ProviderEventId" } } =>
                    (StatusCodes.Status409Conflict, (object)new ApiError("payment_state_conflict", "Payment state changed. Reload the payment status before retrying.")),
                UniqueConstraintException => (StatusCodes.Status409Conflict, (object)new ApiError("data_conflict", "A resource with the same unique value already exists.")),
                DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                    (StatusCodes.Status409Conflict, (object)new ApiError("data_conflict", "A resource with the same unique value already exists.")),
                DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "57014" } } =>
                    (StatusCodes.Status503ServiceUnavailable,
                        (object)new ApiError("dependency_timeout", "A required service did not respond in time.")),
                Npgsql.PostgresException { SqlState: "57014" } => (StatusCodes.Status503ServiceUnavailable,
                    (object)new ApiError("dependency_timeout", "A required service did not respond in time.")),
                DbUpdateException { InnerException: Npgsql.NpgsqlException } =>
                    (StatusCodes.Status503ServiceUnavailable,
                        (object)new ApiError("dependency_unavailable", "A required service is temporarily unavailable.")),
                Npgsql.NpgsqlException => (StatusCodes.Status503ServiceUnavailable,
                    (object)new ApiError("dependency_unavailable", "A required service is temporarily unavailable.")),

                // 500 Internal Server Error fallback
                _ => (StatusCodes.Status500InternalServerError, (object)ApiError.InternalServerError())
            };

            // 3. Log the error
            if (statusCode >= 500 && context.Request.Path.Equals(new PathString("/api/internal/v1/jobs/bulk-import")))
                BulkImportRequestFailed(logger, context.TraceIdentifier, exception.GetType().Name);
            else if (statusCode >= 500)
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
            try { await context.Response.WriteAsJsonAsync(error, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                RequestCancelled(logger, context.Request.Path, TraceId(context), null);
            }
        }
    }

    private static string TraceId(HttpContext context) =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}

public sealed record PendingMembershipCheckoutErrorResponse(
    string Code, string Message,
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter<JobPortal.Domain.Enums.PaymentProvider>))]
    JobPortal.Domain.Enums.PaymentProvider Provider,
    string PublicReference, MembershipCheckoutStatus Status, DateTime CreatedAtUtc,
    bool CanResume, bool CanCancel);
