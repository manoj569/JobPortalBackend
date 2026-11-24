using System.Net.Mail;

namespace JobPortal.Application.Features.HiringOutreach;

public static class HiringContactPolicy
{
    private static readonly (string Name,string[] Hints)[] Families =
    [
        ("Operations",["TRUST","SAFETY","OPERATIONS","MODERATION"]), ("Data",["DATA","ANALYTICS"]),
        ("Engineering",["ENGINEER","DEVELOP","TECHNICAL","SOFTWARE",".NET","JAVA","CLOUD","TECHNOLOGY"," IT "]),
        ("Finance",["FINANC","ACCOUNTING"]), ("Product",["PRODUCT"]), ("Design",["DESIGN"]),
        ("Marketing",["MARKETING"]), ("Sales",["SALES","ACCOUNT EXECUTIVE"])
    ];
    private static readonly string[] Executives=["CEO","FOUNDER","CHIEF","ACCOUNT EXECUTIVE"];
    private static readonly string[] Hr=["HUMAN RESOURCES","HR BUSINESS","HRBP","PEOPLE PARTNER","PEOPLE OPERATIONS"];
    private static readonly string[] PersonalMailDomains=["gmail.com","googlemail.com","outlook.com","hotmail.com","yahoo.com","icloud.com"];
    public static bool ProfessionalDomain(string domain)=>!PersonalMailDomains.Contains(domain,StringComparer.OrdinalIgnoreCase);
    public static string Normalize(string value)=>string.Join(' ',value.ToUpperInvariant().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    public static string Family(string title,string hints="",string description="")
    {
        var main=" "+Normalize(title)+" ";
        foreach(var family in Families) if(family.Hints.Any(x=>main.Contains(x,StringComparison.Ordinal))) return family.Name;
        var secondary=" "+Normalize(hints.Length>2000 ? hints[..2000] : hints)+" ";
        foreach(var family in Families) if(family.Hints.Any(x=>secondary.Contains(x,StringComparison.Ordinal))) return family.Name;
        var fallback=" "+Normalize(description.Length>2000 ? description[..2000] : description)+" ";
        foreach(var family in Families) if(family.Hints.Any(x=>fallback.Contains(x,StringComparison.Ordinal))) return family.Name;
        return "";
    }
    public static bool Verified(string status,int confidence,HiringOutreachOptions options)=>status=="valid" ||
        options.AllowUnverifiedContacts && status is "unknown" or "accept_all" && confidence>=options.MinimumUnverifiedConfidence;
    public static string Type(string title)
    {
        var text=title.ToUpperInvariant();
        if(text.Contains("TALENT",StringComparison.Ordinal)) return "TalentAcquisition";
        if(text.Contains("RECRUIT",StringComparison.Ordinal)) return "Recruiter";
        if(Hr.Any(x=>text.Contains(x,StringComparison.Ordinal)) || text=="HR" || text.StartsWith("HR ",StringComparison.Ordinal)) return "HR";
        return text.Contains("MANAGER",StringComparison.Ordinal) || text.Contains("LEAD",StringComparison.Ordinal) ? "HiringManager" : "TeamMember";
    }
    public static bool ProfessionalEmail(string email,string domain)=>ProfessionalDomain(domain) && email.Length<=254 && !email.Any(char.IsControl) &&
        MailAddress.TryCreate(email.Trim(),out var address) && string.Equals(address.Address,email.Trim(),StringComparison.OrdinalIgnoreCase) &&
        address.Host.Equals(domain,StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyList<HiringProviderContact> Select(IEnumerable<HiringProviderContact> source,string domain,string job,string department,HiringOutreachOptions options)
        =>Select(source,new("",domain,job,department),options);
    public static IReadOnlyList<HiringProviderContact> Select(IEnumerable<HiringProviderContact> source,HiringDiscoveryContext context,HiringOutreachOptions options)
    {
        return source.Take(100).Where(x=>!string.IsNullOrWhiteSpace(x.FullName) && ProfessionalEmail(x.Email,context.Domain) &&
            Verified(x.Verification.ToLowerInvariant(),x.Confidence,options))
            .Select(x=>x with { Email=x.Email.Trim().ToLowerInvariant(),Verification=x.Verification.ToLowerInvariant(),FullName=Clean(x.FullName,200),FirstName=Clean(x.FirstName,100),
                Title=Clean(x.Title,200),Department=Clean(x.Department,200),Location=Clean(x.Location,200),Reference=Clean(x.Reference,200),LinkedInUrl=LinkedIn(x.LinkedInUrl) })
            .Select(x=>(Contact:x,Score:Score(x,context))).Where(x=>x.Score>0)
            .OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Contact.Confidence).ThenBy(x=>x.Contact.Email,StringComparer.Ordinal)
            .DistinctBy(x=>x.Contact.Email,StringComparer.OrdinalIgnoreCase).Take(Math.Min(options.MaximumContacts,4)).Select(x=>x.Contact).ToArray();
    }
    public static int Score(HiringProviderContact person,HiringDiscoveryContext context)
    {
        var role=Normalize(person.Title);
        if(Executives.Any(x=>role.Contains(x,StringComparison.Ordinal))) return 0;
        var target=Family(context.JobTitle,context.Department,context.Description);
        if(role.Contains("SALES",StringComparison.Ordinal) && target!="Sales") return 0;
        var family=Family(person.Title,person.Department);
        // Specialist recruiters cannot be reused as generic recruiters for another job family.
        if(family.Length>0 && family!=target) return 0;
        var relevant=family.Length>0 && family==target; var type=Type(role);
        var score=role.Contains("RECRUIT",StringComparison.Ordinal) || role.Contains("TALENT",StringComparison.Ordinal) ? relevant ? 110 : 95 :
            type=="HR" ? role.Contains("PARTNER",StringComparison.Ordinal) || role.Contains("HRBP",StringComparison.Ordinal) ? 85 : 65 :
            relevant ? type=="HiringManager" ? 80 : 50 : 0;
        if(score==0) return 0;
        var location=Normalize(person.Location); var jobLocation=Normalize(context.Location);
        if(location.Length>0 && jobLocation.Length>0)
            score+=location is "INDIA" or "IN" or "GLOBAL" or "WORLDWIDE" ? 5 :
                location==jobLocation || jobLocation.Contains(location,StringComparison.Ordinal) || location.Contains(jobLocation,StringComparison.Ordinal) ? 20 : -15;
        return score+Math.Clamp(person.Confidence,0,100)/10;
    }
    private static string Clean(string value,int max)=>new(value.Where(x=>!char.IsControl(x)).Take(max).ToArray());
    private static string? LinkedIn(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="https" &&
        (uri.Host=="linkedin.com" || uri.Host.EndsWith(".linkedin.com",StringComparison.Ordinal)) && uri.AbsolutePath.StartsWith("/in/",StringComparison.Ordinal) && value!.Length<=500 ? value : null;
}
