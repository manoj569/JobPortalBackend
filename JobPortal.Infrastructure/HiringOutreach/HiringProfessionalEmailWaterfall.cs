using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed partial class HiringProfessionalEmailWaterfall(
    ProspeoHiringProfessionalEmailFinder prospeo,
    SnovHiringProfessionalEmailFinder snov,
    HunterHiringProfessionalEmailFinder hunter,
    ILogger<HiringProfessionalEmailWaterfall> logger)
    : IHiringProfessionalEmailFinder
{
    private const int MaximumIdentitiesPerDiscovery = 4;

    public string Name => "ProfessionalEmailWaterfall";

    public bool IsAvailable =>
        prospeo.IsAvailable ||
        snov.IsAvailable ||
        hunter.IsAvailable;

    private readonly Dictionary<string, HiringEmailFindingResult> completed =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> requests =
        new(StringComparer.Ordinal);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Professional email provider {Provider} result {Result}")]
    private static partial void Outcome(
        ILogger logger,
        string provider,
        string result);

    public async Task<HiringProviderContact?> FindAsync(
        HiringPersonIdentity person,
        CancellationToken ct) =>
        (await FindResultAsync(person, false, ct)).Contact;

    public Task<HiringEmailFindingResult> FindResultAsync(
        HiringPersonIdentity person,
        bool diagnostic,
        CancellationToken ct) =>
        FindUsingAsync(
            person,
            diagnostic,
            [prospeo, snov, hunter],
            ct);

    internal async Task<HiringEmailFindingResult> FindUsingAsync(
        HiringPersonIdentity person,
        bool diagnostic,
        IHiringProfessionalEmailFinder[] providers,
        CancellationToken ct)
    {
        var key =
            person.Provider + "|" +
            person.Domain + "|" +
            person.Reference + "|" +
            HiringContactPolicy.Normalize(person.FullName) + "|" +
            diagnostic;

        if (completed.TryGetValue(key, out var previous))
            return previous;

        if (completed.Count >= MaximumIdentitiesPerDiscovery)
            return new(null, false, []);

        // Reserve the identity before any provider call so duplicate
        // identities cannot repeat paid requests during this discovery.
        completed[key] = new(null, true, []);

        var failures = new List<string>();
        var uncertain = false;

        foreach (var provider in providers)
        {
            if (!provider.IsAvailable ||
                !diagnostic && !provider.AllowCandidateDisplay)
            {
                continue;
            }

            var maximum = provider switch
            {
                ProspeoHiringProfessionalEmailFinder p =>
                    p.MaximumRequests,

                SnovHiringProfessionalEmailFinder s =>
                    s.MaximumRequests,

                _ => MaximumIdentitiesPerDiscovery
            };

            if (requests.GetValueOrDefault(provider.Name) >= maximum)
                continue;

            if (ct.IsCancellationRequested)
            {
                uncertain = true;
                failures.Add(provider.Name + "OutcomeUnknown");
                break;
            }

            requests[provider.Name] =
                requests.GetValueOrDefault(provider.Name) + 1;

            try
            {
                var contact =
                    await provider.FindAsync(person, ct);

                if (contact is not null &&
                    contact.Verification == "valid" &&
                    HiringContactPolicy.ProfessionalEmail(
                        contact.Email,
                        person.Domain) &&
                    HiringContactPolicy.Normalize(contact.FullName) ==
                    HiringContactPolicy.Normalize(person.FullName))
                {
                    Outcome(
                        logger,
                        provider.Name,
                        "Found");

                    var result = new HiringEmailFindingResult(
                        contact with
                        {
                            Provider =
                                person.Provider +
                                "+" +
                                provider.Name,

                            Source = "EmailFinder",

                            Reference = person.Reference
                        },
                        uncertain,
                        failures);

                    completed[key] = result;

                    return result;
                }

                Outcome(
                    logger,
                    provider.Name,
                    "NoResult");
            }
            catch (HiringProviderException ex)
            {
                failures.Add(ex.Code);

                uncertain |=
                    !HiringProviderFailures
                        .ConfirmedRejection(ex.Code);

                Outcome(
                    logger,
                    provider.Name,
                    ex.Code);
            }
            catch (Exception ex)
                when (ex is HttpRequestException
                      or OperationCanceledException
                      or System.Text.Json.JsonException)
            {
                var code =
                    provider.Name +
                    "OutcomeUnknown";

                failures.Add(code);

                uncertain = true;

                Outcome(
                    logger,
                    provider.Name,
                    code);
            }
        }

        var final =
            new HiringEmailFindingResult(
                null,
                uncertain,
                failures);

        completed[key] = final;

        return final;
    }
}

internal static class HiringEmailProviderData
{
    public static string Text(
        System.Text.Json.JsonElement data,
        string field) =>
        data.ValueKind ==
            System.Text.Json.JsonValueKind.Object &&
        data.TryGetProperty(
            field,
            out var value) &&
        value.ValueKind ==
            System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    public static bool Flag(
        System.Text.Json.JsonElement data,
        string field,
        bool expected) =>
        data.ValueKind ==
            System.Text.Json.JsonValueKind.Object &&
        data.TryGetProperty(
            field,
            out var value) &&
        value.ValueKind ==
            (expected
                ? System.Text.Json.JsonValueKind.True
                : System.Text.Json.JsonValueKind.False);

    public static (string First, string Last)? SplitName(
        HiringPersonIdentity person)
    {
        var name = person.FullName.Trim();

        var split =
            name.LastIndexOf(' ');

        return split > 0 &&
               split < name.Length - 1 &&
               !name.Contains('*')
            ? (
                name[..split],
                name[(split + 1)..])
            : null;
    }

    public static HiringProviderContact Contact(
        HiringPersonIdentity p,
        string email,
        string provider) =>
        new(
            p.FullName,
            p.FirstName,
            email,
            p.Title,
            p.Department,
            "valid",
            95,
            null,
            p.Reference,
            p.Location,
            "EmailFinder",
            p.Provider + "+" + provider);

    public static string Failure(
        System.Net.HttpStatusCode status,
        string provider) =>
        provider +
        (status switch
        {
            System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden =>
                "ConfigurationRequired",

            System.Net.HttpStatusCode.PaymentRequired =>
                "QuotaExceeded",

            System.Net.HttpStatusCode.TooManyRequests =>
                "RateLimited",

            System.Net.HttpStatusCode.BadRequest
                or System.Net.HttpStatusCode.UnprocessableEntity =>
                "InvalidRequest",

            _ =>
                "Unavailable"
        });
}