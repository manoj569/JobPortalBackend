using System.Globalization;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public enum IndiaJobEligibilityDecision { Accepted, Rejected, Review }
public sealed record IndiaJobEligibilityResult(IndiaJobEligibilityDecision Decision, string ReasonCode);

public static partial class IndiaJobEligibilityPolicy
{
    public const string MissingLocationLabel = "Location not specified";
    private static readonly HashSet<string> KnownCountries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name)).Where(r => r.TwoLetterISORegionName.Length == 2).SelectMany(r => new[] { r.TwoLetterISORegionName, r.ThreeLetterISORegionName, r.EnglishName })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForeignCountryNames = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name)).Where(r => r.TwoLetterISORegionName.Length == 2 && r.TwoLetterISORegionName != "IN")
        .Select(r => r.EnglishName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool IsForeignLocation(string value) => ForeignLocation().IsMatch(value) ||
        ForeignCountryNames.Any(country => ContainsCountryName(value, country));
    private static bool ContainsCountryName(string value, string country)
    {
        for (var offset = 0; offset < value.Length;)
        {
            var start = value.IndexOf(country, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            var end = start + country.Length;
            if ((start == 0 || !char.IsLetterOrDigit(value[start - 1])) &&
                (end == value.Length || !char.IsLetterOrDigit(value[end]))) return true;
            offset = start + 1;
        }
        return false;
    }

    public static IndiaJobEligibilityResult Evaluate(RawExternalJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var locations = job.AdditionalLocations.Prepend(job.Location ?? string.Empty).Where(Usable).ToArray();
        var countries = job.CountryCodes.Where(Usable).Select(x => x.Trim()).ToArray();
        var remoteText = string.Join("; ", locations.Append(job.RemoteEligibilityText ?? string.Empty).Append(job.WorkplaceTypeText ?? string.Empty));
        if (IndiaExcluded().IsMatch(remoteText)) return new(IndiaJobEligibilityDecision.Rejected, "IndiaRemoteExcluded");
        if (IndiaRemote().IsMatch(remoteText)) return new(IndiaJobEligibilityDecision.Accepted, "ExplicitIndiaRemote");
        var indianLocation = locations.Any(x => IndianLocation().IsMatch(x) && !IsForeignLocation(x));
        var contradictoryLabel = locations.Any(x => IndianLocation().IsMatch(x) && IsForeignLocation(x));
        var indianCountry = countries.Any(IsIndia);
        var foreignCountry = countries.Any(x => KnownCountries.Contains(x) && !IsIndia(x));
        var unknownCountry = countries.Any(x => !KnownCountries.Contains(x) && !IsIndia(x));
        // A primary Indian label contradicts a foreign primary country. Additional
        // genuine Indian hiring locations can independently qualify a foreign primary location.
        if ((!indianLocation && contradictoryLabel) || (indianLocation && foreignCountry && !indianCountry &&
            job.Location is not null && IndianLocation().IsMatch(job.Location)) ||
            (indianCountry && locations.Length > 0 && locations.All(x => IsForeignLocation(x) && !IndianLocation().IsMatch(x))))
            return new(IndiaJobEligibilityDecision.Review, "ContradictoryLocation");
        if (unknownCountry) return new(IndiaJobEligibilityDecision.Review, "UnknownCountry");
        if (indianCountry || indianLocation) return new(IndiaJobEligibilityDecision.Accepted, "IndiaLocationConfirmed");
        if (foreignCountry)
            return new(IndiaJobEligibilityDecision.Rejected, "NonIndiaLocation");
        if (locations.Length == 0 && countries.Length == 0 &&
            job.WorkplaceType != WorkplaceType.Remote && !string.Equals(job.WorkplaceTypeText, "remote", StringComparison.OrdinalIgnoreCase) &&
            !Usable(job.RemoteEligibilityText)) return new(IndiaJobEligibilityDecision.Accepted, "MissingLocationAccepted");
        if (locations.Length > 0 && locations.All(x => IsForeignLocation(x)))
            return new(IndiaJobEligibilityDecision.Rejected, "NonIndiaLocation");
        return new(IndiaJobEligibilityDecision.Review, "AmbiguousLocation");
    }

    public static string DisplayLocation(string? text) => Usable(text) ? text! : MissingLocationLabel;

    private static bool Usable(string? text) => !string.IsNullOrWhiteSpace(text) &&
        text.Trim().ToUpperInvariant() is not ("N/A" or "NA" or "NOT SPECIFIED" or "LOCATION NOT SPECIFIED");
    private static bool IsIndia(string text) => text.Trim().ToUpperInvariant() is "IN" or "IND" or "INDIA";

    [GeneratedRegex(@"\b(?:India|Pune|Bengaluru|Bangalore|Hyderabad|Mumbai|Bombay|Chennai|Madras|New Delhi|Delhi|Gurugram|Gurgaon|Noida|Kolkata|Calcutta|Ahmedabad|Karnataka|Maharashtra|Telangana|Tamil Nadu|Kerala|Gujarat|West Bengal|Uttar Pradesh|Haryana|Jaipur|Indore|Surat|Nagpur|Lucknow|Kochi|Coimbatore|Visakhapatnam|Vadodara|Chandigarh|Bhubaneswar|Mysuru|Mysore|Vijayawada|Nashik|Rajkot|Mohali|Bhopal|Manesar|Faridabad|Ghaziabad|Thiruvananthapuram|Andhra Pradesh|Arunachal Pradesh|Assam|Bihar|Chhattisgarh|Goa|Himachal Pradesh|Jharkhand|Madhya Pradesh|Manipur|Meghalaya|Mizoram|Nagaland|Odisha|Rajasthan|Sikkim|Tripura|Uttarakhand|Ladakh|Lakshadweep|Puducherry|Pondicherry)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex IndianLocation();
    [GeneratedRegex(@"\b(?:United States|USA|US|United Kingdom|UK|London|New York|Canada|Toronto|Germany|Berlin|France|Paris|Australia|Sydney|Singapore|Japan|Tokyo|Ireland|Dublin|Pakistan|Dubai|United Arab Emirates)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex ForeignLocation();
    [GeneratedRegex(@"\b(?:outside|not in|not|no|excluding|excludes?|except|not eligible|not available|not permitted|not hiring|cannot hire)\s+(?:(?:in|from)\s+)?India\b|\bIndia\s+(?:is\s+)?(?:excluded|not eligible|not available|not permitted|unsupported)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex IndiaExcluded();
    [GeneratedRegex(@"\b(?:remote(?:ly)?(?:\s+(?:work|working|hiring|from|in|within|across|throughout|anywhere))*\s*[-,:()\u2013\u2014]*\s*India|India[- ,]*(?:wide[- ,]*)?remote)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex IndiaRemote();
}
