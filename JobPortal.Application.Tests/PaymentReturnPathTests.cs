using Xunit;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Payments;

namespace JobPortal.Application.Tests;

public sealed class PaymentReturnPathTests
{
    [Theory]
    [InlineData("/dashboard/jobs")]
    [InlineData("/dashboard/jobs?mode=referral")]
    [InlineData("/dashboard/interview-insights")]
    [InlineData("/dashboard/membership")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact")]
    public void AcceptsExactVerifiedFrontendDestinations(string path)
    {
        Assert.Equal(path, PaymentReturnPath.Validate(path));
    }

    [Fact]
    public void ExistingContactDestinationRetainsGuidCanonicalization()
    {
        Assert.Equal("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact",
            PaymentReturnPath.Validate("/dashboard/jobs/referral/AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA/contact"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void MissingDestinationRetainsExistingDefaultBehavior(string? path)
    {
        Assert.Null(PaymentReturnPath.Validate(path));
    }

    [Theory]
    [InlineData("https://evil.example/dashboard/jobs")]
    [InlineData("http://localhost/dashboard/jobs")]
    [InlineData("https://careerharbor.example/dashboard/jobs")]
    [InlineData("//evil.example/dashboard/jobs")]
    [InlineData("///evil.example/dashboard/jobs")]
    [InlineData("\\\\evil.example\\dashboard\\jobs")]
    [InlineData("/\\evil.example/dashboard/jobs")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("file:///dashboard/jobs")]
    [InlineData("dashboard/jobs")]
    [InlineData("/jobs")]
    [InlineData("/membership")]
    [InlineData("/admin")]
    [InlineData("/dashboard/referrals")]
    [InlineData("/dashboard/jobs/referral")]
    [InlineData("/dashboard/jobs/unknown-job")]
    [InlineData("/dashboard/jobs/")]
    [InlineData("/Dashboard/jobs")]
    [InlineData(" /dashboard/jobs")]
    [InlineData("/dashboard/jobs ")]
    [InlineData("/dashboard/jobs\r\n")]
    [InlineData("/dashboard/jobs?next=https://evil.example")]
    [InlineData("/dashboard/jobs?returnTo=/admin")]
    [InlineData("/dashboard/jobs?mode=all")]
    [InlineData("/dashboard/jobs?mode=referral&next=https://evil.example")]
    [InlineData("/dashboard/jobs?mode=referral&returnTo=/admin")]
    [InlineData("/dashboard/jobs?mode=referral&token=example")]
    [InlineData("/dashboard/jobs?mode=referral&mode=all")]
    [InlineData("/dashboard/jobs?mode=Referral")]
    [InlineData("/dashboard/jobs?mode=%72eferral")]
    [InlineData("/dashboard/jobs?mode=referral#section")]
    [InlineData("/dashboard/interview-insights?next=/admin")]
    [InlineData("/dashboard/membership?returnTo=https://evil.example")]
    [InlineData("/dashboard/membership?access_token=example")]
    [InlineData("/dashboard/membership#token=example")]
    [InlineData("/dashboard/jobs#section")]
    [InlineData("/%64ashboard/jobs")]
    [InlineData("/dashboard/../dashboard/jobs")]
    [InlineData("/dashboard/jobs/referral/contact")]
    [InlineData("/dashboard/jobs/referral//contact")]
    [InlineData("/dashboard/jobs/referral/ aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa /contact")]
    [InlineData("/dashboard/jobs/referral/not-a-guid/contact")]
    [InlineData("/dashboard/jobs/referral/00000000-0000-0000-0000-000000000000/contact")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/contact")]
    [InlineData("/dashboard/jobs/referral/{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}/contact")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaag/contact")]
    [InlineData("/dashboard/jobs/referral/../contact")]
    [InlineData("/dashboard/jobs/referral/%2e%2e/contact")]
    [InlineData("/dashboard/jobs/referral/%2F/contact")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact/")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact?next=https://evil.example")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact#token=example")]
    public void RejectsEveryDestinationOutsideTheExactAllowlist(string path)
    {
        var error = Assert.Throws<BadRequestException>(() => PaymentReturnPath.Validate(path));
        Assert.Equal("invalid_return_to", error.Code);
    }
}
