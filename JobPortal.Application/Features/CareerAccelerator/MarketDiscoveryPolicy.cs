namespace JobPortal.Application.Features.CareerAccelerator;

public static class MarketDiscoveryPolicy
{
    public static bool ReadyForReview(int distinctJobs, int distinctCompanies,
        MarketIntelligenceOptions options) =>
        distinctJobs >= options.DiscoveryMinimumJobs &&
        distinctCompanies >= options.DiscoveryMinimumCompanies;
}
