using FluentValidation;
using JobPortal.Application.Abstractions.EmployerTalentPool;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.EmployerTalentPool;

public sealed class EmployerTalentPoolService(
    IEmployerTalentPoolRepository repository,
    IValidator<EmployerTalentPoolQuery> queryValidator,
    IValidator<AddEmployerTalentPoolCandidateRequest> addValidator,
    IValidator<UpdateEmployerTalentPoolNotesRequest> notesValidator)
    : IEmployerTalentPoolService
{
    public async Task<EmployerTalentPoolResponse> GetAsync(
        Guid employerUserId,
        EmployerTalentPoolQuery query,
        CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        var (items, totalCount) = await repository.SearchAsync(
            employerUserId,
            query,
            cancellationToken);

        var responseItems = items
            .Select(Map)
            .ToArray();

        return new EmployerTalentPoolResponse(
            responseItems,
            totalCount,
            query.Page,
            query.PageSize);
    }

    public async Task<EmployerTalentPoolItemResponse> AddAsync(
        Guid employerUserId,
        AddEmployerTalentPoolCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        await addValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var candidate = await repository.GetVisibleCandidateAsync(
            request.CandidateUserId,
            cancellationToken);

        if (candidate is null)
        {
            throw new KeyNotFoundException(
                "Candidate was not found or is not visible to recruiters.");
        }

        var existing = await repository.GetAsync(
            employerUserId,
            request.CandidateUserId,
            cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                "Candidate is already in your talent pool.");
        }

        var entry = new Domain.Entities.EmployerTalentPool
        {
            EmployerUserId = employerUserId,
            CandidateUserId = request.CandidateUserId,
            Notes = NormalizeNotes(request.Notes),
            AddedAtUtc = DateTime.UtcNow
        };

        await repository.AddAsync(
            entry,
            cancellationToken);

        await repository.SaveChangesAsync(
            cancellationToken);

        return new EmployerTalentPoolItemResponse(
            candidate.Id,
            $"{candidate.FirstName} {candidate.LastName}".Trim(),
            candidate.Headline,
            candidate.CurrentCity ?? candidate.Location,
            candidate.YearsOfExperience,
            candidate.ProfileImageUrl,
            entry.Notes,
            entry.AddedAtUtc,
            candidate.CandidateSkills
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToArray());
    }

    public async Task<EmployerTalentPoolItemResponse> UpdateNotesAsync(
        Guid employerUserId,
        Guid candidateUserId,
        UpdateEmployerTalentPoolNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        await notesValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var entry = await repository.GetAsync(
            employerUserId,
            candidateUserId,
            cancellationToken);

        if (entry is null)
        {
            throw new KeyNotFoundException(
                "Talent pool candidate was not found.");
        }

        entry.Notes = NormalizeNotes(request.Notes);

        await repository.SaveChangesAsync(
            cancellationToken);

        var candidate = await repository.GetVisibleCandidateAsync(
            candidateUserId,
            cancellationToken);

        if (candidate is null)
        {
            throw new KeyNotFoundException(
                "Candidate is no longer visible to recruiters.");
        }

        return new EmployerTalentPoolItemResponse(
            candidate.Id,
            $"{candidate.FirstName} {candidate.LastName}".Trim(),
            candidate.Headline,
            candidate.CurrentCity ?? candidate.Location,
            candidate.YearsOfExperience,
            candidate.ProfileImageUrl,
            entry.Notes,
            entry.AddedAtUtc,
            candidate.CandidateSkills
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToArray());
    }

    public async Task RemoveAsync(
        Guid employerUserId,
        Guid candidateUserId,
        CancellationToken cancellationToken = default)
    {
        var entry = await repository.GetAsync(
            employerUserId,
            candidateUserId,
            cancellationToken);

        if (entry is null)
        {
            throw new KeyNotFoundException(
                "Talent pool candidate was not found.");
        }

        entry.IsDeleted = true;

        await repository.SaveChangesAsync(
            cancellationToken);
    }

    private static EmployerTalentPoolItemResponse Map(
        EmployerTalentPoolCandidate candidate)
    {
        return new EmployerTalentPoolItemResponse(
            candidate.CandidateUserId,
            $"{candidate.FirstName} {candidate.LastName}".Trim(),
            candidate.Headline,
            candidate.CurrentCity ?? candidate.Location,
            candidate.YearsOfExperience,
            candidate.ProfileImageUrl,
            candidate.Notes,
            candidate.AddedAtUtc,
            candidate.Skills);
    }

    private static string? NormalizeNotes(string? notes)
    {
        return string.IsNullOrWhiteSpace(notes)
            ? null
            : notes.Trim();
    }
}
