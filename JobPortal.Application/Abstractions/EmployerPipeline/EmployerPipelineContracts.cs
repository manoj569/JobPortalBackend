using JobPortal.Application.Features.EmployerPipeline;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.EmployerPipeline;

public interface IEmployerPipelineService
{
    Task<PagedResponse<EmployerPipelineResponse>> SearchAsync(
        Guid employerUserId,
        EmployerPipelineQuery query,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> GetAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> StartScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        StartEmployerScreeningRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> UpdateScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerScreeningRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> PassScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> FailScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerPipelineResponse> UpdateStageAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerPipelineStageRequest request,
        CancellationToken cancellationToken = default);
}
