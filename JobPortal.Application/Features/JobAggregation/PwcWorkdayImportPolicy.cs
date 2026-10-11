using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.JobAggregation;

// Explicit exception for this verified board; company names alone never opt a source in.
public static class PwcWorkdayImportPolicy
{
    public static bool AppliesTo(JobSource source) =>
        source.AtsType == AtsType.Workday &&
        string.Equals(source.AtsIdentifier?.Trim(), "pwc/Global_Experienced_Careers", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(source.CareerPageUrl?.Trim(), UriKind.Absolute, out var board) &&
        board.Scheme == Uri.UriSchemeHttps && board.IsDefaultPort &&
        string.IsNullOrEmpty(board.UserInfo) && string.IsNullOrEmpty(board.Query) && string.IsNullOrEmpty(board.Fragment) &&
        board.IdnHost.Equals("pwc.wd3.myworkdayjobs.com", StringComparison.OrdinalIgnoreCase) &&
        IsCareerSite(board.AbsolutePath);

    private static bool IsCareerSite(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 && segments[0].Equals("Global_Experienced_Careers", StringComparison.OrdinalIgnoreCase) ||
            segments.Length == 2 && segments[0].Length == 5 && segments[0][2] == '-' &&
            char.IsAsciiLetter(segments[0][0]) && char.IsAsciiLetter(segments[0][1]) &&
            char.IsAsciiLetter(segments[0][3]) && char.IsAsciiLetter(segments[0][4]) &&
            segments[1].Equals("Global_Experienced_Careers", StringComparison.OrdinalIgnoreCase);
    }
}
