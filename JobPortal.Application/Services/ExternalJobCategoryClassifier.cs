using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;

namespace JobPortal.Application.Services;

public sealed class ExternalJobCategoryClassifier : IExternalJobCategoryClassifier
{
    private static readonly (string Slug, Regex Title, string[] Departments)[] Rules =
    [
        Rule("software-engineering", @"(?:software|backend|frontend|full[- ]stack) engineer|software developer", "Software Engineering", "Software Development"),
        Rule("human-resources-recruitment", @"recruiter|talent acquisition(?: specialist| partner| manager)?|recruitment specialist|human resources (?:manager|partner|specialist)", "Human Resources & Recruitment", "Human Resources", "Recruiting", "Recruitment", "Talent Acquisition"),
        Rule("quality-assurance-testing", @"QA engineer|SDET|software tester|test automation engineer", "Quality Assurance & Testing", "Quality Assurance"),
        Rule("devops-cloud-engineering", @"DevOps(?: engineer)?|SRE|site reliability engineer|cloud engineer|platform infrastructure engineer", "DevOps & Cloud Engineering", "DevOps", "Cloud Engineering"),
        Rule("ai-machine-learning", @"machine learning engineer|AI researcher|AI engineer", "AI & Machine Learning", "Machine Learning"),
        Rule("data-science-analytics", @"data scientist|data analyst|analytics engineer", "Data Science & Analytics", "Data Science", "Analytics"),
        Rule("product-management", @"product manager", "Product Management"),
        Rule("ui-ux-design", @"UX designer|UI designer|UI/UX designer|product designer", "UI/UX Design", "UX Design", "UI Design"),
        Rule("cybersecurity", @"security engineer|security analyst", "Cybersecurity", "Information Security"),
        Rule("engineering-management", @"engineering manager|director of engineering|VP of engineering", "Engineering Management"),
        Rule("it-support-administration", @"IT support(?: specialist)?|systems? administrator", "IT Support & Administration", "IT Support"),
        Rule("sales-business-development", @"account executive|business development (?:representative|manager|executive)|sales (?:representative|manager|executive|director)", "Sales & Business Development", "Sales", "Business Development"),
        Rule("marketing", @"marketing (?:manager|specialist|director|coordinator)|content marketer", "Marketing"),
        Rule("finance-accounting", @"accountant|finance (?:manager|analyst|director)|financial analyst", "Finance & Accounting", "Finance", "Accounting"),
        Rule("operations", @"operations (?:manager|analyst|specialist|coordinator)", "Operations"),
        Rule("customer-success-support", @"customer (?:success|support)(?: manager|representative|specialist|engineer)?", "Customer Success & Support", "Customer Success", "Customer Support"),
        Rule("legal-compliance", @"legal counsel|compliance (?:officer|analyst|manager)|general counsel", "Legal & Compliance", "Legal", "Compliance")
    ];

    private static (string, Regex, string[]) Rule(string slug, string title, params string[] departments) =>
        (slug, new Regex(@"\b(?:" + title + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
            RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)), departments);

    public string? Classify(RawExternalJob normalizedJob)
    {
        ArgumentNullException.ThrowIfNull(normalizedJob);
        if (normalizedJob.Title.Length > 250) return null;
        try
        {
            var titles = Rules.Where(r => r.Title.IsMatch(normalizedJob.Title) ||
                r.Departments.Contains(normalizedJob.Title, StringComparer.OrdinalIgnoreCase)).Select(r => r.Slug).Distinct().ToArray();
            if (titles.Length != 0) return titles.Length == 1 ? titles[0] : null;
            // Whole department labels only. Description keywords never classify a job.
            var departments = Rules.Where(r => r.Departments.Contains(normalizedJob.ExternalCategory, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.Slug).Distinct().ToArray();
            return departments.Length == 1 ? departments[0] : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    public static bool MatchesCategory(string slug, string? categorySlug, string categoryName) =>
        string.Equals(slug, categorySlug, StringComparison.OrdinalIgnoreCase) ||
        Rules.Any(r => r.Slug == slug && r.Departments.Contains(categoryName, StringComparer.OrdinalIgnoreCase));
}
