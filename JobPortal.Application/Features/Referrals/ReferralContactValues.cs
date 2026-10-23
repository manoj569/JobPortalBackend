using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.Referrals;

internal sealed record ReferralContactValues(string? LinkedInUrl, string? ContactEmail, string? ContactPhone)
{
    public static ReferralContactValues Validate(bool linkedIn, bool email, bool phone,
        string? linkedInUrl, string? contactEmail, string? contactPhone)
    {
        if (!linkedIn && !email && !phone) Invalid("Select at least one contact method.");
        linkedInUrl = linkedIn ? linkedInUrl?.Trim() : null;
        contactEmail = email ? contactEmail?.Trim() : null;
        contactPhone = phone ? contactPhone?.Trim() : null;
        if (linkedIn && (string.IsNullOrEmpty(linkedInUrl) || linkedInUrl.Length > 2048 ||
            !Uri.TryCreate(linkedInUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("www.linkedin.com", StringComparison.OrdinalIgnoreCase)) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/in/[A-Za-z0-9_%\-]+/?$", RegexOptions.CultureInvariant)))
            Invalid("Enter a valid HTTPS LinkedIn profile URL.");
        if (email && (string.IsNullOrEmpty(contactEmail) || contactEmail.Length > 254 ||
            contactEmail.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(contactEmail)))
            Invalid("Enter a valid contact email address.");
        if (phone && (string.IsNullOrEmpty(contactPhone) || contactPhone.Length > 40 ||
            !Regex.IsMatch(contactPhone, @"^\+?[0-9][0-9 ()\-\.]*[0-9]$", RegexOptions.CultureInvariant) ||
            contactPhone.Count(char.IsAsciiDigit) is < 7 or > 15))
            Invalid("Enter a valid international contact phone number (7 to 15 digits).");
        return new(linkedInUrl, contactEmail, contactPhone);
    }

    private static void Invalid(string message) => throw new BadRequestException(message, "validation_error");
}
