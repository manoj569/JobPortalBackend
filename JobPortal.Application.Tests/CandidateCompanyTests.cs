using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateCompanyTests
{
    private static readonly Guid CandidateId = Guid.NewGuid();

    [Fact]
    public async Task SearchOrdersExactThenPrefixThenAlphabeticalAndReturnsMinimalDto()
    {
        var f = Fixture("Acme Labs", "Mega Acme", "Acme", "Acme Systems");
        var results = await f.Service.SearchAsync(CandidateId, " ACME ", 10);
        Assert.Equal(["Acme", "Acme Labs", "Acme Systems", "Mega Acme"], results.Select(x => x.CompanyName));
        Assert.Equal([nameof(CompanyOption.CompanyName), nameof(CompanyOption.Id)],
            typeof(CompanyOption).GetProperties().Select(x => x.Name).OrderBy(x => x));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("https://example.com")]
    [InlineData("example.com")]
    [InlineData("Company { payload }")]
    [InlineData("A\u0001B")]
    public async Task InvalidNamesAreRejected(string name) =>
        await Assert.ThrowsAsync<BadRequestException>(() => Fixture().Service.CreateAsync(CandidateId, new(name)));

    [Fact]
    public async Task ExistingNormalizedEquivalentIsReturnedWithoutDuplicate()
    {
        var f = Fixture("Nexora Technologies");
        var result = await f.Service.CreateAsync(CandidateId, new("  NEXORA   technologies "));
        Assert.False(result.Created);
        Assert.Single(f.Repository.Items);
    }

    [Fact]
    public async Task NewCompanyIsCreatedWithSafeDefaultsAndAuditedWithoutName()
    {
        var f = Fixture();
        var result = await f.Service.CreateAsync(CandidateId, new("Nexora Technologies"));
        var company = Assert.Single(f.Repository.Items);
        Assert.True(result.Created);
        Assert.False(company.IsVerified);
        Assert.Equal(CompanySubmissionSource.CandidateSubmitted, company.SubmissionSource);
        Assert.Equal(CandidateId, company.SubmittedByCandidateId);
        Assert.Equal(f.Repository.AdministratorId, company.OwnerUserId);
        var audit = Assert.Single(f.Audit.Events);
        Assert.Equal(company.Id.ToString(), audit.EntityId);
        Assert.Null(audit.Metadata);
    }

    [Fact]
    public async Task RateLimitCountsOnlyNewCompaniesInRollingTwentyFourHours()
    {
        var f = Fixture();
        f.Repository.CreatedInWindow = 10;
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.CreateAsync(CandidateId, new("Eleventh Company")));
        Assert.Equal(429, error.StatusCode);
        Assert.Equal("company_creation_daily_limit", error.Code);
    }

    [Fact]
    public async Task ConcurrentEquivalentRequestsResultInOneCompany()
    {
        var f = Fixture();
        var tasks = Enumerable.Range(0, 12).Select(i => f.Service.CreateAsync(CandidateId,
            new(i % 2 == 0 ? "Concurrent Company" : " concurrent   COMPANY ")));
        var results = await Task.WhenAll(tasks);
        Assert.Single(f.Repository.Items);
        Assert.Single(results, x => x.Created);
        Assert.Equal(11, results.Count(x => !x.Created));
    }

    [Fact]
    public async Task NonCandidateIsRejected()
    {
        var f = Fixture();
        f.Repository.IsCandidate = false;
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.SearchAsync(CandidateId, "ac", 10));
    }

    private static TestFixture Fixture(params string[] names)
    {
        var repository = new RepositoryFake();
        repository.Items.AddRange(names.Select(name => new Company
        {
            Name = name, NormalizedName = CompanyNameNormalizer.Normalize(name), Slug = name.ToLowerInvariant().Replace(' ', '-'),
            OwnerUserId = repository.AdministratorId
        }));
        var audit = new AuditFake();
        return new(repository, audit, new(repository, audit, TimeProvider.System));
    }

    private sealed record TestFixture(RepositoryFake Repository, AuditFake Audit, CandidateCompanyService Service);

    private sealed class AuditFake : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RepositoryFake : ICandidateCompanyRepository
    {
        private readonly object gate = new();
        private Company? pending;
        public Guid AdministratorId { get; } = Guid.NewGuid();
        public bool IsCandidate { get; set; } = true;
        public int CreatedInWindow { get; set; }
        public List<Company> Items { get; } = [];
        public Task<bool> IsCandidateAsync(Guid candidateId, CancellationToken ct) => Task.FromResult(IsCandidate);
        public Task<IReadOnlyCollection<CompanyOption>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            IReadOnlyCollection<CompanyOption> result = Items.Where(x => x.NormalizedName.Contains(query))
                .OrderBy(x => x.NormalizedName == query ? 0 : x.NormalizedName.StartsWith(query, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(x => x.Name).Take(limit).Select(x => new CompanyOption(x.Id, x.Name)).ToArray();
            return Task.FromResult(result);
        }
        public Task<Company?> FindByNormalizedNameAsync(string name, CancellationToken ct)
        {
            lock (gate) return Task.FromResult(Items.SingleOrDefault(x => x.NormalizedName == name));
        }
        public Task<int> CountCreatedSinceAsync(Guid candidateId, DateTime since, CancellationToken ct) => Task.FromResult(CreatedInWindow);
        public Task<Guid?> FindActiveAdministratorIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(AdministratorId);
        public Task AddAsync(Company company, CancellationToken ct)
        {
            pending = company;
            return Task.CompletedTask;
        }
        public Task SaveAsync(CancellationToken ct)
        {
            lock (gate)
            {
                if (pending is null) return Task.CompletedTask;
                if (Items.Any(x => x.NormalizedName == pending.NormalizedName))
                    throw new UniqueConstraintException("duplicate");
                Items.Add(pending);
                pending = null;
            }
            return Task.CompletedTask;
        }
        public void DiscardPendingChanges() => pending = null;
    }
}
