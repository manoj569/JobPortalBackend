//using System.Net.Http.Json;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using JobPortal.Application.Abstractions.Authentication;
//using JobPortal.Application.Common.Text;
//using JobPortal.Domain.Enums;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Logging;

//namespace JobPortal.Infrastructure.Services;

//public sealed class Fast2SmsService(
//    HttpClient httpClient,
//    IConfiguration configuration,
//    ILogger<Fast2SmsService> logger) : ISmsService
//{
//    private const string ProviderEndpoint =
//        "https://www.fast2sms.com/dev/bulkV2";

//    private static readonly Action<ILogger, OtpPurpose, string, string, Exception?>
//        DeliveryAttempted = LoggerMessage.Define<OtpPurpose, string, string>(
//            LogLevel.Information,
//            new EventId(1100, nameof(DeliveryAttempted)),
//            "SMS delivery attempt: purpose {Purpose}, mobile suffix {LastFourDigits}, category {ResultCategory}.");

//    private static readonly Action<ILogger, OtpPurpose, string, string, int?, Exception?>
//        DeliveryCompleted = LoggerMessage.Define<OtpPurpose, string, string, int?>(
//            LogLevel.Information,
//            new EventId(1101, nameof(DeliveryCompleted)),
//            "SMS delivery result: purpose {Purpose}, mobile suffix {LastFourDigits}, category {ResultCategory}, HTTP status {HttpStatusCode}.");

//    private static readonly Action<ILogger, OtpPurpose, string, string, int?, string, Exception?>
//        DeliveryFailed = LoggerMessage.Define<OtpPurpose, string, string, int?, string>(
//            LogLevel.Error,
//            new EventId(1102, nameof(DeliveryFailed)),
//            "SMS delivery failure: purpose {Purpose}, mobile suffix {LastFourDigits}, category {ResultCategory}, HTTP status {HttpStatusCode}, exception type {ExceptionType}.");

//    public async Task<SmsDeliveryResult> SendOtpAsync(
//        string normalizedPhoneNumber,
//        string otp,
//        OtpPurpose purpose,
//        CancellationToken cancellationToken = default)
//    {
//        var enabled = configuration.GetValue("Sms:Enabled", false);
//        var provider = configuration["Sms:Provider"]?.Trim() ?? "unconfigured";
//        var validPhone = TryGetNationalNumber(
//            normalizedPhoneNumber,
//            out var nationalNumber,
//            out var lastFourDigits);
//        var safeSuffix = validPhone ? lastFourDigits : "unavailable";

//        DeliveryAttempted(
//            logger,
//            purpose,
//            safeSuffix,
//            "delivery_attempted",
//            null);

//        if (!enabled)
//            return Complete(
//                SmsDeliveryResult.Disabled,
//                purpose,
//                safeSuffix,
//                "disabled");

//        if (!string.Equals(provider, "Fast2Sms", StringComparison.OrdinalIgnoreCase))
//            return Fail(purpose, safeSuffix, "provider_not_configured");

//        if (!validPhone)
//            return Fail(purpose, safeSuffix, "invalid_phone");

//        if (otp is not { Length: 6 } ||
//            otp.Any(character => character is < '0' or > '9'))
//            return Fail(purpose, safeSuffix, "invalid_otp");

//        var apiKey = configuration["Sms:Fast2Sms:ApiKey"];
//        if (string.IsNullOrWhiteSpace(apiKey))
//            return Fail(purpose, safeSuffix, "api_key_missing");

//        try
//        {
//            using var request = new HttpRequestMessage(
//                HttpMethod.Post,
//                ProviderEndpoint);
//            request.Headers.TryAddWithoutValidation("authorization", apiKey);
//            request.Content = JsonContent.Create(
//                new Fast2SmsRequest("otp", nationalNumber, otp),
//                options: JsonSerializerOptions.Web);

//            using var response = await httpClient.SendAsync(
//                request,
//                cancellationToken);

//            if (!response.IsSuccessStatusCode)
//                return Fail(
//                    purpose,
//                    safeSuffix,
//                    "http_failure",
//                    (int)response.StatusCode);

//            Fast2SmsResponse? providerResponse;
//            try
//            {
//                providerResponse = await response.Content.ReadFromJsonAsync<Fast2SmsResponse>(
//                    JsonSerializerOptions.Web,
//                    cancellationToken);
//            }
//            catch (JsonException exception)
//            {
//                return Fail(
//                    purpose,
//                    safeSuffix,
//                    "provider_rejected",
//                    (int)response.StatusCode,
//                    exception);
//            }

//            if (providerResponse?.Return is not true)
//                return Fail(
//                    purpose,
//                    safeSuffix,
//                    "provider_rejected",
//                    (int)response.StatusCode);

//            return Complete(
//                SmsDeliveryResult.Sent,
//                purpose,
//                safeSuffix,
//                "sent",
//                (int)response.StatusCode);
//        }
//        catch (OperationCanceledException exception)
//            when (cancellationToken.IsCancellationRequested)
//        {
//            _ = Fail(purpose, safeSuffix, "cancellation", exception: exception);
//            throw;
//        }
//        catch (OperationCanceledException exception)
//        {
//            return Fail(
//                purpose,
//                safeSuffix,
//                "timeout",
//                exception: exception,
//                result: SmsDeliveryResult.TimedOut);
//        }
//        catch (HttpRequestException exception)
//        {
//            return Fail(purpose, safeSuffix, "network_failure", exception: exception);
//        }
//        catch (Exception exception)
//        {
//            return Fail(purpose, safeSuffix, "unexpected_failure", exception: exception);
//        }
//    }

//    private SmsDeliveryResult Complete(
//        SmsDeliveryResult result,
//        OtpPurpose purpose,
//        string lastFourDigits,
//        string category,
//        int? statusCode = null)
//    {
//        DeliveryCompleted(
//            logger,
//            purpose,
//            lastFourDigits,
//            category,
//            statusCode,
//            null);
//        return result;
//    }

//    private SmsDeliveryResult Fail(
//        OtpPurpose purpose,
//        string lastFourDigits,
//        string category,
//        int? statusCode = null,
//        Exception? exception = null,
//        SmsDeliveryResult result = SmsDeliveryResult.Failed)
//    {
//        DeliveryFailed(
//            logger,
//            purpose,
//            lastFourDigits,
//            category,
//            statusCode,
//            exception?.GetType().Name ?? "none",
//            null);
//        return result;
//    }

//    private static bool TryGetNationalNumber(
//        string? normalizedPhoneNumber,
//        out string nationalNumber,
//        out string lastFourDigits)
//    {
//        nationalNumber = string.Empty;
//        lastFourDigits = string.Empty;
//        if (normalizedPhoneNumber is not { Length: 13 } ||
//            !normalizedPhoneNumber.StartsWith("+91", StringComparison.Ordinal) ||
//            normalizedPhoneNumber[3..].Any(character => character is < '0' or > '9') ||
//            !IndianMobileNumber.TryNormalize(normalizedPhoneNumber, out var normalized) ||
//            !string.Equals(normalized, normalizedPhoneNumber, StringComparison.Ordinal))
//            return false;

//        nationalNumber = normalizedPhoneNumber[3..];
//        lastFourDigits = nationalNumber[^4..];
//        return true;
//    }

//    private sealed record Fast2SmsRequest(
//        string Route,
//        string Numbers,
//        [property: JsonPropertyName("variables_values")] string VariablesValues);

//    private sealed record Fast2SmsResponse(
//        [property: JsonPropertyName("return")] bool Return);
//}
