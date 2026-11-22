using System.Net.Mail;

namespace JobPortal.Application.Features.HiringOutreach;

public static class HiringContactPolicy
{
    private static readonly string[] UnrelatedTitles=["SALES","ACCOUNT EXECUTIVE","CHIEF","CEO","FOUNDER"];
    private static readonly string[] JobFamilies=["ENGINEER","DEVELOP","TECHNICAL","PRODUCT","DESIGN","DATA","FINANCE","MARKETING","OPERATIONS"];
    private static readonly string[] EngineeringTitles=["ENGINEER","DEVELOP","TECHNICAL","SOFTWARE"];
    public static bool Verified(string status,int confidence,HiringOutreachOptions options)=>status=="valid" ||
        options.AllowUnverifiedContacts && status is "unknown" or "accept_all" && confidence>=options.MinimumUnverifiedConfidence;
    public static string Type(string title)
    {
        var text=title.ToUpperInvariant();
        return text.Contains("TALENT",StringComparison.Ordinal) ? "TalentAcquisition" : text.Contains("RECRUIT",StringComparison.Ordinal) ? "Recruiter" :
            text.Contains("MANAGER",StringComparison.Ordinal) || text.Contains("LEAD",StringComparison.Ordinal) ? "HiringManager" :
            text.Contains("HUMAN RESOURCES",StringComparison.Ordinal) || text=="HR" ? "Other" : "TeamMember";
    }
    public static IReadOnlyList<HiringProviderContact> Select(IEnumerable<HiringProviderContact> source,string domain,string job,string department,HiringOutreachOptions options)
    {
        return source.Take(100).Where(x=>x.Email.Length<=254 && !x.Email.Any(char.IsControl) && MailAddress.TryCreate(x.Email.Trim(),out var address) &&
            string.Equals(address.Address,x.Email.Trim(),StringComparison.OrdinalIgnoreCase) && address.Host.Equals(domain,StringComparison.OrdinalIgnoreCase) &&
            Verified(x.Verification.ToLowerInvariant(),x.Confidence,options))
            .Select(x=>x with { Email=x.Email.Trim().ToLowerInvariant(),Verification=x.Verification.ToLowerInvariant(),FullName=Clean(x.FullName,200),FirstName=Clean(x.FirstName,100),
                Title=Clean(x.Title,200),Department=Clean(x.Department,200),Reference=Clean(x.Reference,200),LinkedInUrl=LinkedIn(x.LinkedInUrl) })
            .Select(x=>(Contact:x,Score:Score(x.Title,x.Department,job,department))).Where(x=>x.Score>0)
            .OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Contact.Confidence).ThenBy(x=>x.Contact.Email,StringComparer.Ordinal)
            .DistinctBy(x=>x.Contact.Email,StringComparer.OrdinalIgnoreCase).Take(options.MaximumContacts).Select(x=>x.Contact).ToArray();
    }
    private static int Score(string title,string department,string job,string jobDepartment)
    {
        var role=title.ToUpperInvariant(); var context=(job+" "+jobDepartment).ToUpperInvariant(); var contact=(role+" "+department.ToUpperInvariant());
        if(UnrelatedTitles.Any(x=>role.Contains(x,StringComparison.Ordinal))) return 0;
        var relevant=JobFamilies
            .Any(x=>context.Contains(x,StringComparison.Ordinal) && contact.Contains(x,StringComparison.Ordinal)) ||
            EngineeringTitles.Any(x=>context.Contains(x,StringComparison.Ordinal)) && EngineeringTitles.Any(x=>contact.Contains(x,StringComparison.Ordinal));
        if(role.Contains("RECRUIT",StringComparison.Ordinal) || role.Contains("TALENT",StringComparison.Ordinal)) return relevant ? 100 : 90;
        if(relevant) return Type(title)=="HiringManager" ? 70 : 50;
        return role.Contains("HUMAN RESOURCES",StringComparison.Ordinal) || role=="HR" ? 10 : 0;
    }
    private static string Clean(string value,int max)=>new(value.Where(x=>!char.IsControl(x)).Take(max).ToArray());
    private static string? LinkedIn(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="https" &&
        (uri.Host=="linkedin.com" || uri.Host.EndsWith(".linkedin.com",StringComparison.Ordinal)) && uri.AbsolutePath.StartsWith("/in/",StringComparison.Ordinal) && value!.Length<=500 ? value : null;
}
