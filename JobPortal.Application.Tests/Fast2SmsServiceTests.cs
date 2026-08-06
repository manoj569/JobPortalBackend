//using System.Globalization;
//using System.Net;
//using System.Net.Http.Headers;
//using System.Net.Http.Json;
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.Json.Serialization;
//using JobPortal.Application.Abstractions.Authentication;
//using JobPortal.Domain.Enums;
//using JobPortal.Infrastructure;
//using JobPortal.Infrastructure.Services;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging;
//using Xunit;

//namespace JobPortal.Application.Tests;

//public sealed class Fast2SmsServiceTests
//{
//    [Fact]
//    public async Task SuccessfulDeliveryUsesRawAuthorizationAndTenDigitPayload()
//    {
//        var data = CreateSensitiveTestData();
//        var handlerRequestMatched = false;
//        var handler = new StubHandler(async (request, cancellationToken) =>
//        {
//            var hasRawApiKey = request.Headers.TryGetValues(
//                    "authorization",
//                    out var authorizationValues) &&
//                authorizationValues.SingleOrDefault() == data.ApiKey &&
//                !authorizationValues.Single().StartsWith(
//                    "Bearer ",
//                    StringComparison.OrdinalIgnoreCase);
//            var payload = await request.Content!.ReadFromJsonAsync<Fast2SmsRequestSnapshot>(
//                cancellationToken);
//            handlerRequestMatched =
//                request.Method == HttpMethod.Post &&
//                request.RequestUri == new Uri("https://www.fast2sms.com/dev/bulkV2") &&
//                request.Headers.Accept.Any(value => value.MediaType == "application/json") &&
//                hasRawApiKey &&
//                payload?.Route == "otp" &&
//                payload.Numbers == data.NormalizedPhoneNumber[3..] &&
//                payload.VariablesValues == data.Otp;
//            return ProviderResponse(HttpStatusCode.OK, providerAccepted: true);
//        });
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: true, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Registration);

//        Assert.Equal(SmsDeliveryResult.Sent, result);
//        Assert.Equal(1, handler.RequestCount);
//        Assert.True(handlerRequestMatched, "Fast2SMS request did not match the provider contract.");
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("sent", StringComparison.Ordinal) &&
//            entry.Message.Contains(data.PhoneSuffix, StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Fact]
//    public async Task DisabledDeliveryDoesNotCallHttp()
//    {
//        var data = CreateSensitiveTestData();
//        var handler = UnexpectedRequestHandler();
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: false, includeApiKey: true, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Login);

//        Assert.Equal(SmsDeliveryResult.Disabled, result);
//        Assert.Equal(0, handler.RequestCount);
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("disabled", StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Fact]
//    public async Task MissingApiKeyFailsWithoutCallingHttp()
//    {
//        var data = CreateSensitiveTestData();
//        var handler = UnexpectedRequestHandler();
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: false, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Login);

//        Assert.Equal(SmsDeliveryResult.Failed, result);
//        Assert.Equal(0, handler.RequestCount);
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("api_key_missing", StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Theory]
//    [InlineData(HttpStatusCode.Unauthorized)]
//    [InlineData(HttpStatusCode.BadRequest)]
//    [InlineData(HttpStatusCode.InternalServerError)]
//    public async Task NonSuccessHttpStatusesReturnFailed(HttpStatusCode statusCode)
//    {
//        var data = CreateSensitiveTestData();
//        var handler = new StubHandler((_, _) =>
//            Task.FromResult(ProviderResponse(statusCode, providerAccepted: true)));
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: true, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Registration);

//        Assert.Equal(SmsDeliveryResult.Failed, result);
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("http_failure", StringComparison.Ordinal) &&
//            entry.Message.Contains(
//                ((int)statusCode).ToString(CultureInfo.InvariantCulture),
//                StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Fact]
//    public async Task ProviderRejectionReturnsFailed()
//    {
//        var data = CreateSensitiveTestData();
//        var responseBodyMarker = Convert.ToHexString(
//            RandomNumberGenerator.GetBytes(12));
//        var handler = new StubHandler((_, _) => Task.FromResult(
//            new HttpResponseMessage(HttpStatusCode.OK)
//            {
//                Content = new StringContent(
//                    $"{{\"return\":false,\"message\":\"{responseBodyMarker}\"}}",
//                    Encoding.UTF8,
//                    "application/json")
//            }));
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: true, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Registration);

//        Assert.Equal(SmsDeliveryResult.Failed, result);
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("provider_rejected", StringComparison.Ordinal));
//        Assert.DoesNotContain(logger.Entries, entry =>
//            entry.Message.Contains(responseBodyMarker, StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Theory]
//    [InlineData(true, "timeout")]
//    [InlineData(false, "network_failure")]
//    public async Task TimeoutAndNetworkFailuresReturnFailedWithSafeDiagnostics(
//        bool simulateTimeout,
//        string expectedCategory)
//    {
//        var data = CreateSensitiveTestData();
//        var handler = new StubHandler((_, _) => simulateTimeout
//            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
//            : Task.FromException<HttpResponseMessage>(new HttpRequestException()));
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: true, logger);

//        var result = await service.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp,
//            OtpPurpose.Registration);

//        Assert.Equal(
//            simulateTimeout ? SmsDeliveryResult.TimedOut : SmsDeliveryResult.Failed,
//            result);
//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains(expectedCategory, StringComparison.Ordinal) &&
//            entry.Message.Contains(
//                simulateTimeout ? nameof(TaskCanceledException) : nameof(HttpRequestException),
//                StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Fact]
//    public async Task CallerCancellationIsLoggedSafelyAndPropagated()
//    {
//        var data = CreateSensitiveTestData();
//        using var cancellation = new CancellationTokenSource();
//        var handler = new StubHandler((_, token) =>
//        {
//            cancellation.Cancel();
//            return Task.FromCanceled<HttpResponseMessage>(token);
//        });
//        var logger = new CollectingLogger<Fast2SmsService>();
//        var service = CreateService(handler, data, enabled: true, includeApiKey: true, logger);

//        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
//            service.SendOtpAsync(
//                data.NormalizedPhoneNumber,
//                data.Otp,
//                OtpPurpose.Registration,
//                cancellation.Token));

//        Assert.Contains(logger.Entries, entry =>
//            entry.Message.Contains("cancellation", StringComparison.Ordinal) &&
//            entry.Message.Contains(
//                nameof(TaskCanceledException),
//                StringComparison.Ordinal));
//        AssertLogsAreSafe(logger, data);
//    }

//    [Fact]
//    public void InfrastructureRegistersOnlyTypedFast2SmsService()
//    {
//        var configuration = new ConfigurationBuilder()
//            .AddInMemoryCollection(new Dictionary<string, string?>
//            {
//                ["Sms:Enabled"] = bool.FalseString,
//                ["Sms:Provider"] = "Fast2Sms",
//                ["Email:Enabled"] = bool.FalseString
//            })
//            .Build();
//        var services = new ServiceCollection();

//        services.AddLogging();
//        services.AddSingleton<IConfiguration>(configuration);
//        services.AddInfrastructure(configuration);

//        Assert.Single(services, descriptor =>
//            descriptor.ServiceType == typeof(ISmsService));
//        using var provider = services.BuildServiceProvider();
//        Assert.IsType<Fast2SmsService>(provider.GetRequiredService<ISmsService>());
//    }

//    [Fact]
//    public async Task InvalidPhoneAndOtpFailWithoutCallingHttp()
//    {
//        var data = CreateSensitiveTestData();
//        var invalidPhoneHandler = UnexpectedRequestHandler();
//        var invalidPhoneLogger = new CollectingLogger<Fast2SmsService>();
//        var invalidPhoneService = CreateService(
//            invalidPhoneHandler,
//            data,
//            enabled: true,
//            includeApiKey: true,
//            invalidPhoneLogger);

//        var invalidPhoneResult = await invalidPhoneService.SendOtpAsync(
//            data.NormalizedPhoneNumber[3..],
//            data.Otp,
//            OtpPurpose.Registration);

//        Assert.Equal(SmsDeliveryResult.Failed, invalidPhoneResult);
//        Assert.Contains(invalidPhoneLogger.Entries, entry =>
//            entry.Message.Contains("invalid_phone", StringComparison.Ordinal));

//        var invalidOtpHandler = UnexpectedRequestHandler();
//        var invalidOtpLogger = new CollectingLogger<Fast2SmsService>();
//        var invalidOtpService = CreateService(
//            invalidOtpHandler,
//            data,
//            enabled: true,
//            includeApiKey: true,
//            invalidOtpLogger);

//        var invalidOtpResult = await invalidOtpService.SendOtpAsync(
//            data.NormalizedPhoneNumber,
//            data.Otp[..^1],
//            OtpPurpose.Registration);

//        Assert.Equal(SmsDeliveryResult.Failed, invalidOtpResult);
//        Assert.Contains(invalidOtpLogger.Entries, entry =>
//            entry.Message.Contains("invalid_otp", StringComparison.Ordinal));
//        Assert.Equal(0, invalidPhoneHandler.RequestCount);
//        Assert.Equal(0, invalidOtpHandler.RequestCount);
//        AssertLogsAreSafe(invalidPhoneLogger, data);
//        AssertLogsAreSafe(invalidOtpLogger, data);
//    }

//    private static Fast2SmsService CreateService(
//        StubHandler handler,
//        SensitiveTestData data,
//        bool enabled,
//        bool includeApiKey,
//        ILogger<Fast2SmsService> logger)
//    {
//        var settings = new Dictionary<string, string?>
//        {
//            ["Sms:Enabled"] = enabled.ToString(CultureInfo.InvariantCulture),
//            ["Sms:Provider"] = "Fast2Sms"
//        };
//        if (includeApiKey)
//            settings["Sms:Fast2Sms:ApiKey"] = data.ApiKey;

//        var configuration = new ConfigurationBuilder()
//            .AddInMemoryCollection(settings)
//            .Build();
//        var client = new HttpClient(handler)
//        {
//            Timeout = TimeSpan.FromSeconds(15)
//        };
//        client.DefaultRequestHeaders.Accept.Add(
//            new MediaTypeWithQualityHeaderValue("application/json"));
//        return new(client, configuration, logger);
//    }

//    private static SensitiveTestData CreateSensitiveTestData()
//    {
//        var nationalNumber = string.Concat(
//            "98",
//            RandomNumberGenerator.GetInt32(10_000_000, 100_000_000)
//                .ToString(CultureInfo.InvariantCulture));
//        return new(
//            string.Concat("+91", nationalNumber),
//            RandomNumberGenerator.GetInt32(0, 1_000_000)
//                .ToString("D6", CultureInfo.InvariantCulture),
//            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
//    }

//    private static HttpResponseMessage ProviderResponse(
//        HttpStatusCode statusCode,
//        bool providerAccepted) => new(statusCode)
//        {
//            Content = JsonContent.Create(new Dictionary<string, bool>
//            {
//                ["return"] = providerAccepted
//            })
//        };

//    private static StubHandler UnexpectedRequestHandler() => new((_, _) =>
//        Task.FromException<HttpResponseMessage>(
//            new InvalidOperationException("HTTP was not expected.")));

//    private static void AssertLogsAreSafe(
//        CollectingLogger<Fast2SmsService> logger,
//        SensitiveTestData data)
//    {
//        var containsSensitiveValue = logger.Entries.Any(entry =>
//            entry.Message.Contains(data.ApiKey, StringComparison.Ordinal) ||
//            entry.Message.Contains(data.Otp, StringComparison.Ordinal) ||
//            entry.Message.Contains(data.NormalizedPhoneNumber, StringComparison.Ordinal));
//        Assert.False(containsSensitiveValue, "SMS logs contained sensitive delivery data.");
//    }

//    private sealed record SensitiveTestData(
//        string NormalizedPhoneNumber,
//        string Otp,
//        string ApiKey)
//    {
//        public string PhoneSuffix => NormalizedPhoneNumber[^4..];
//    }

//    private sealed record Fast2SmsRequestSnapshot(
//        string Route,
//        string Numbers,
//        [property: JsonPropertyName("variables_values")] string VariablesValues);

//    private sealed class StubHandler(
//        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) :
//        HttpMessageHandler
//    {
//        public int RequestCount { get; private set; }

//        protected override Task<HttpResponseMessage> SendAsync(
//            HttpRequestMessage request,
//            CancellationToken cancellationToken)
//        {
//            RequestCount++;
//            return send(request, cancellationToken);
//        }
//    }

//    private sealed class CollectingLogger<T> : ILogger<T>
//    {
//        public List<LogEntry> Entries { get; } = [];

//        public IDisposable? BeginScope<TState>(TState state)
//            where TState : notnull => null;

//        public bool IsEnabled(LogLevel logLevel) => true;

//        public void Log<TState>(
//            LogLevel logLevel,
//            EventId eventId,
//            TState state,
//            Exception? exception,
//            Func<TState, Exception?, string> formatter) =>
//            Entries.Add(new(formatter(state, exception), exception));
//    }

//    private sealed record LogEntry(string Message, Exception? Exception);
//}
