using System.Runtime.ExceptionServices;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Features.JobAggregation;

public static partial class WorkdayExecutionDiagnostics
{
    public static void Input(ILogger logger, JobSource source)
    {
        // Only values verified against a public Workday URL enter logs. Invalid
        // identifiers may be pasted URLs/credentials and must remain redacted.
        var derived = WorkdaySourceConfiguration.Validate(source.CareerPageUrl, null).Configuration;
        var value = source.AtsIdentifier?.Trim();
        var safeIdentifier = string.IsNullOrEmpty(value) ? "<empty>" : derived is not null &&
            new[] { derived.Identifier, derived.Tenant, derived.CareerSite }.Contains(value, StringComparer.OrdinalIgnoreCase)
                ? value : "<invalid-or-conflicting>";
        LogInput(logger, source.Id, safeIdentifier, derived?.Host ?? "<invalid-host>",
            derived is null ? "<invalid-path>" : new Uri(source.CareerPageUrl).AbsolutePath);
    }

    public static void Resolved(ILogger logger, Guid sourceId, WorkdaySourceValidation validation) =>
        LogResolved(logger, sourceId, validation.Host, validation.Tenant, validation.CareerSite, validation.ReasonCode);

    public static void Rejected(ILogger logger, Guid sourceId, WorkdaySourceValidation validation) =>
        LogRejected(logger, sourceId, validation.ReasonCode, nameof(InvalidOperationException), validation.Message);

    public static void PreExecutionFailure(ILogger logger, Guid sourceId, string stage, Exception exception)
    {
        const string message = "Workday execution failed before provider processing completed. See the safe reason code and stack trace.";
        // Log an exception with the original stack but a fixed message and no
        // inner exception. Exception messages/Data may contain credentials or bodies.
        var sanitized = new InvalidOperationException(message);
        if (exception.StackTrace is { Length: > 0 } stack) ExceptionDispatchInfo.SetRemoteStackTrace(sanitized, stack);
        LogPreExecution(logger, sanitized, sourceId, stage, exception.GetType().Name, JobSourceFailure.Code(exception), message);
    }

    [LoggerMessage(EventId = 4414, Level = LogLevel.Information,
        Message = "WorkdayConfigurationInput SourceId={SourceId} AtsIdentifier={AtsIdentifier} CareerPageHost={CareerPageHost} CareerPagePath={CareerPagePath}.")]
    private static partial void LogInput(ILogger logger, Guid sourceId, string atsIdentifier, string careerPageHost, string careerPagePath);
    [LoggerMessage(EventId = 4415, Level = LogLevel.Information,
        Message = "WorkdayConfigurationResolved SourceId={SourceId} ResolvedHost={ResolvedHost} ResolvedTenant={ResolvedTenant} ResolvedCareerSite={ResolvedCareerSite} ValidationResult={ValidationResult}.")]
    private static partial void LogResolved(ILogger logger, Guid sourceId, string resolvedHost, string resolvedTenant, string resolvedCareerSite, string validationResult);
    [LoggerMessage(EventId = 4416, Level = LogLevel.Warning,
        Message = "WorkdayConfigurationRejected SourceId={SourceId} ReasonCode={ReasonCode} ErrorType={ErrorType} SanitizedMessage={SanitizedMessage}.")]
    private static partial void LogRejected(ILogger logger, Guid sourceId, string reasonCode, string errorType, string sanitizedMessage);
    [LoggerMessage(EventId = 4417, Level = LogLevel.Error,
        Message = "WorkdayPreExecutionFailure SourceId={SourceId} Stage={Stage} ErrorType={ErrorType} ReasonCode={ReasonCode} SanitizedMessage={SanitizedMessage}.")]
    private static partial void LogPreExecution(ILogger logger, Exception exception, Guid sourceId, string stage, string errorType, string reasonCode, string sanitizedMessage);
}
