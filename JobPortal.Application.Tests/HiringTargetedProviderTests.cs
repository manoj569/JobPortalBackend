using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringProviderAdapterTests
{
    [Fact]
    public async Task TargetedHunterSearchIsBoundedAndUsesOfficialFiltersWithoutSecretInUrl()
    {
        var urls=new List<string>();
        using var handler=new Handler(request=>
        {
            urls.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            Assert.True(request.Headers.Contains("X-API-KEY"));
            Assert.DoesNotContain("test-secret",request.RequestUri.ToString(),StringComparison.Ordinal);
            return Json("{\"data\":{\"emails\":[]}}");
        });
        using var clients=new Clients(handler);
        var provider=new HunterEmployerContactProvider(clients,Options.Create(new HunterHiringOptions { ApiKey="test-secret",MaximumRequests=100 }));
        Assert.Empty(await provider.DiscoverAsync(new("Example","example.org","Software Engineer","Engineering"),default));
        Assert.Equal(4,handler.Calls); Assert.Contains("job_titles=Recruiter,Talent Acquisition,Recruitment,Talent Partner",urls[0]);
        Assert.Contains("department=hr",urls[1]); Assert.Contains("department=it",urls[2]); Assert.Contains("job_titles=Manager,Lead",urls[2]);
        Assert.All(urls,url=> { Assert.DoesNotContain("verification_status=",url); Assert.DoesNotContain("required_field=",url); Assert.Contains("type=personal",url); Assert.Contains("limit=10",url); Assert.DoesNotContain("limit=20",url); });
    }
    [Fact]
    public async Task TwoStrongRecruitersStopFurtherPaidSearches()
    {
        using var handler=new Handler(_=>Json("""
            {"data":{"emails":[
            {"value":"one@example.org","first_name":"One","position":"Technical Recruiter","confidence":99,"verification":{"status":"valid"},"location":{"city":"Hyderabad","country":"India"}},
            {"value":"two@example.org","first_name":"Two","position":"Talent Acquisition","confidence":95,"verification":{"status":"valid"}}]}}
            """));
        using var clients=new Clients(handler);
        var provider=new HunterEmployerContactProvider(clients,Options.Create(new HunterHiringOptions { ApiKey="test-only" }));
        var result=await provider.DiscoverAsync(new("Example","example.org","Software Engineer","Engineering","Hyderabad"),default);
        Assert.Equal(2,result.Count); Assert.Equal(1,handler.Calls); Assert.Contains("Hyderabad",result[0].Location,StringComparison.Ordinal);
    }
    [Fact]
    public async Task HunterStopsAtOneAdditionalStrongContactWhenEarlierProviderAlreadyContributed()
    {
        using var handler=new Handler(_=>Json("""{"data":{"emails":[{"value":"test@example.org","first_name":"Test","position":"Technical Recruiter","confidence":99,"verification":{"status":"valid"}}]}}"""));
        using var clients=new Clients(handler); var provider=new HunterEmployerContactProvider(clients,Options.Create(new HunterHiringOptions { ApiKey="test-only" }));
        Assert.Single(await provider.DiscoverAsync(new("Example","example.org","Engineer","",DesiredStrongContacts:1),default));
        Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task LaterConfirmedRateLimitDoesNotDiscardAnEarlierVerifiedContact()
    {
        var calls=0;
        using var handler=new Handler(_=>++calls==1 ? Json("""
            {"data":{"emails":[{"value":"one@example.org","first_name":"One","position":"Technical Recruiter","confidence":99,"verification":{"status":"valid"}}]}}
            """) : new(System.Net.HttpStatusCode.TooManyRequests));
        using var clients=new Clients(handler);
        var provider=new HunterEmployerContactProvider(clients,Options.Create(new HunterHiringOptions { ApiKey="test-only" }));
        Assert.Single(await provider.DiscoverAsync(new("Example","example.org","Software Engineer","Engineering"),default));
        Assert.Equal(2,handler.Calls);
    }
}
