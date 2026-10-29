using FluentValidation;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Middleware;

public sealed record JobSourceRequestError(string Code, string Message,
    IReadOnlyDictionary<string, string[]> Errors, IReadOnlyDictionary<string, string[]> ReasonCodes,
    string TraceId, RippleHireDiscoveryResult? Discovery = null);

// Shared by automatic MVC binding failures and application validation failures.
public static class JobSourceRequestErrors
{
    private static readonly char[] JsonPathPrefix = ['$', '.'];
    private static readonly string[] BindingReason = ["INVALID_REQUEST_FIELD"];
    private static readonly string[] KnownFields = ["CompanyId", "AtsType", "AtsIdentifier", "CareerPageUrl", "ScanIntervalMinutes", "MissingCompleteSnapshotThreshold", "IsActive", "AutoPublishEnabled"];
    private static readonly Action<ILogger, string, string, string, string, string, Exception?> Rejected =
        LoggerMessage.Define<string, string, string, string, string>(LogLevel.Warning, new EventId(3010, "JobSourceRequestRejected"),
            "Job source request rejected TraceId={TraceId} Method={Method} Code={Code} Fields={Fields} ReasonCodes={ReasonCodes}.");

    public static JobSourceRequestError FromException(Exception exception, HttpContext context)
    {
        var discovery = (exception as RippleHireDiscoveryException)?.Discovery;
        var failures = exception is ValidationException validation
            ? validation.Errors.Select(error => (error.PropertyName, error.ErrorMessage, error.ErrorCode)).ToArray()
            : new[] { (exception is AppException { Code: "invalid_company" } ? "CompanyId" :
                discovery?.ReasonCode == "RIPPLEHIRE_CONFIGURATION_CONFLICT" ? "AtsIdentifier" : discovery is not null ? "CareerPageUrl" : "Request",
                exception is AppException app ? app.Message : "The request body is invalid. Supply valid JSON and job-source field values.",
                exception is AppException coded ? coded.Code : "invalid_request") };
        var code = exception is AppException known ? known.Code : exception is ValidationException ? "validation_error" : "invalid_request";
        return new(code, exception is AppException expected ? expected.Message : "One or more job-source fields are invalid.",
            failures.GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).Distinct().ToArray()),
            failures.GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.Select(x => x.Item3).Distinct().ToArray()), Trace(context), discovery);
    }

    public static BadRequestObjectResult BindingFailure(ActionContext context)
    {
        var fields = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .Select(entry => CanonicalField(entry.Key)).Distinct(StringComparer.Ordinal).ToArray();
        var error = new JobSourceRequestError("validation_error", "The request body contains missing or invalid job-source fields.",
            fields.ToDictionary(field => field, BindingMessage),
            fields.ToDictionary(field => field, _ => BindingReason), Trace(context.HttpContext));
        Log(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JobSourceValidation"), context.HttpContext, error);
        return new(error);
    }

    public static void Log(ILogger logger, HttpContext context, JobSourceRequestError error) =>
        Rejected(logger, error.TraceId, context.Request.Method, error.Code, string.Join(",", error.Errors.Keys),
            string.Join(",", error.ReasonCodes.Values.SelectMany(codes => codes).Distinct()), null);

    private static string[] BindingMessage(string field) => [field switch
    {
        "CompanyId" => "CompanyId must be the GUID of an existing company.",
        "AtsType" => "AtsType must be a supported numeric provider value (RippleHire is 8).",
        "AtsIdentifier" => "AtsIdentifier must be a string or null; leave it empty for automatic discovery.",
        "CareerPageUrl" => "CareerPageUrl must be a public career URL string.",
        "ScanIntervalMinutes" => "ScanIntervalMinutes must be an integer between 1 and 10080.",
        "MissingCompleteSnapshotThreshold" => "MissingCompleteSnapshotThreshold must be an integer between 2 and 100.",
        _ => "Supply a JSON object with valid job-source fields and matching value types."
    }];

    private static string CanonicalField(string key)
    {
        var field = key.TrimStart(JsonPathPrefix);
        return KnownFields
            .FirstOrDefault(known => known.Equals(field, StringComparison.OrdinalIgnoreCase)) ?? "Request";
    }
    private static string Trace(HttpContext context) => System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}
