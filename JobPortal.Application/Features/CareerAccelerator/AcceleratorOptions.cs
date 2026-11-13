using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorOptions
{
    public const string SectionName = "CareerAccelerator";
    public const string PlanCode = "CAREER_ACCELERATOR";
    public bool Enabled { get; set; }
    public string PlanVersion { get; set; } = "accelerator-v1";
    public decimal BaseAmountInr { get; set; } = 278m;
    public Dictionary<AcceleratorFeature, int> Allowances { get; set; } = new()
    {
        [AcceleratorFeature.Tutor] = 900, [AcceleratorFeature.Review] = 15,
        [AcceleratorFeature.Interview] = 8, [AcceleratorFeature.Roadmap] = 10, [AcceleratorFeature.Practice] = 100
    };
    public bool AIEnabled { get; set; }
    public Dictionary<AcceleratorFeature, AcceleratorModelPolicy> Policies { get; set; } = [];
    public decimal PerRequestCostLimitInr { get; set; } = 2m;
    public decimal UserDailyCostLimitInr { get; set; } = 10m;
    public decimal UserCycleCostLimitInr { get; set; } = 50m;
    public decimal GlobalDailyCostLimitInr { get; set; } = 1000m;
    public decimal GlobalMonthlyCostLimitInr { get; set; } = 10000m;
    public int RequestsPerMinute { get; set; } = 20;
    public int UserConcurrency { get; set; } = 2;
    public int GlobalConcurrency { get; set; } = 20;
    public int ReservationSeconds { get; set; } = 120;
    public int MaintenanceSeconds { get; set; } = 60;
    public bool IsValid() => BaseAmountInr is > 0 and <= 100000 && decimal.Round(BaseAmountInr, 2) == BaseAmountInr &&
        Identifier(PlanVersion) && Allowances.Count == Enum.GetValues<AcceleratorFeature>().Length &&
        Enum.GetValues<AcceleratorFeature>().All(f => Allowances.TryGetValue(f, out var n) && n is > 0 and <= 10000) &&
        PerRequestCostLimitInr is > 0 and <= 100 && UserDailyCostLimitInr >= PerRequestCostLimitInr &&
        UserCycleCostLimitInr >= UserDailyCostLimitInr && GlobalDailyCostLimitInr >= UserDailyCostLimitInr &&
        GlobalMonthlyCostLimitInr >= GlobalDailyCostLimitInr && RequestsPerMinute is >= 1 and <= 120 &&
        UserConcurrency is >= 1 and <= 10 && GlobalConcurrency >= UserConcurrency && GlobalConcurrency <= 100 &&
        ReservationSeconds is >= 10 and <= 300 && MaintenanceSeconds is >= 10 and <= 3600 &&
        (!AIEnabled || Enum.GetValues<AcceleratorFeature>().All(f => Policies.TryGetValue(f, out var p) && p.IsValid()));
    internal static bool Identifier(string? value) => value is { Length: > 0 and <= 80 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

public sealed class AcceleratorModelPolicy
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int MaxInputTokens { get; set; } = 2000;
    public int MaxOutputTokens { get; set; } = 1000;
    public int MaxRequestBytes { get; set; } = 16000;
    public int TimeoutSeconds { get; set; } = 30;
    // Must be explicitly configured from the provider's price contract. No invented rates.
    public decimal InputInrPerMillionTokens { get; set; }
    public decimal OutputInrPerMillionTokens { get; set; }
    public bool IsValid() => AcceleratorOptions.Identifier(Provider) && AcceleratorOptions.Identifier(Model) &&
        MaxInputTokens is >= 1 and <= 16000 && MaxOutputTokens is >= 1 and <= 8000 &&
        MaxRequestBytes is >= 100 and <= 128000 && TimeoutSeconds is >= 1 and <= 120 &&
        InputInrPerMillionTokens is > 0 and <= 100000 && OutputInrPerMillionTokens is > 0 and <= 100000;
    public decimal MaximumCost(int inputTokens) => decimal.Ceiling(
        (inputTokens * InputInrPerMillionTokens + MaxOutputTokens * OutputInrPerMillionTokens)) / 1000000m;
}
