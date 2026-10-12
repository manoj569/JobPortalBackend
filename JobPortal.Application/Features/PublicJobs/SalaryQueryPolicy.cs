using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.PublicJobs;

/// <summary>The legacy candidate salary slider is explicitly INR per year (rupees/LPA).</summary>
public static class SalaryQueryPolicy
{
    public static bool UsesSalary(PublicJobQuery query) => query.MinAmount.HasValue || query.MaxAmount.HasValue ||
        query.MinimumSalary.HasValue || query.MaximumSalary.HasValue ||
        query.SortBy is PublicJobSort.SalaryHighToLow or PublicJobSort.MinimumSalary or PublicJobSort.MaximumSalary;

    public static PublicJobQuery Normalize(PublicJobQuery query, bool salaryFacet = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Defaults describe the request contract only. Never infer currency/period for persisted jobs.
        return query.CurrencyCode is null && query.SalaryPeriod is null && (salaryFacet || UsesSalary(query))
            ? query with { CurrencyCode = "INR", SalaryPeriod = SalaryPeriod.Yearly }
            : query;
    }
}
