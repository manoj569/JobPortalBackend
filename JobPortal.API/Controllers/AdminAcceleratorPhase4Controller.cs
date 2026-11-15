using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/v1/projects")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorPhase4Controller(AcceleratorPhase4Service service, AcceleratorProjectAiService projectAi) : ControllerBase
{
    [HttpPost("definitions")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateDefinition(CreateAcceleratorProjectDefinitionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.CreateDefinitionAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPost("versions")]
    public async Task<ActionResult<ApiResponse<AcceleratorDraftResponse>>> CreateDraft(CreateAcceleratorDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorDraftResponse>(await service.CreateDraftAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("versions/{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorAdminProjectVersionResponse>>> Version(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorAdminProjectVersionResponse>(await service.AdminVersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPut("versions/{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorDraftResponse>>> EditDraft(Guid versionId, EditAcceleratorDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorDraftResponse>(await service.EditDraftAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/competencies")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertCompetency(Guid versionId, UpsertAcceleratorProjectCompetencyRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.UpsertCompetencyAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/milestones")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertMilestone(Guid versionId, UpsertAcceleratorProjectMilestoneRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.UpsertMilestoneAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/assignments")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertAssignment(Guid versionId, UpsertAcceleratorProjectAssignmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.UpsertAssignmentAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/rubric")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertRubric(Guid versionId, UpsertAcceleratorProjectRubricRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.UpsertRubricAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/rubric/criteria")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertCriterion(Guid versionId, UpsertAcceleratorProjectCriterionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.UpsertCriterionAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpDelete("versions/{versionId:guid}/competencies/{competencyId:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> RemoveCompetency(Guid versionId, Guid competencyId, [FromQuery] Guid expectedRevision, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.RemoveCompetencyAsync(User.GetRequiredUserId(), versionId, competencyId, expectedRevision, ct)));

    [HttpDelete("versions/{versionId:guid}/milestones/{milestoneId:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> RemoveMilestone(Guid versionId, Guid milestoneId, [FromQuery] Guid expectedRevision, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.RemoveMilestoneAsync(User.GetRequiredUserId(), versionId, milestoneId, expectedRevision, ct)));

    [HttpDelete("versions/{versionId:guid}/assignments/{assignmentId:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> RemoveAssignment(Guid versionId, Guid assignmentId, [FromQuery] Guid expectedRevision, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.RemoveAssignmentAsync(User.GetRequiredUserId(), versionId, assignmentId, expectedRevision, ct)));

    [HttpDelete("versions/{versionId:guid}/rubric/criteria/{criterionId:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> RemoveCriterion(Guid versionId, Guid criterionId, [FromQuery] Guid expectedRevision, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.RemoveCriterionAsync(User.GetRequiredUserId(), versionId, criterionId, expectedRevision, ct)));

    [HttpPost("versions/{versionId:guid}/publish")]
    public async Task<ActionResult<ApiResponse<Guid>>> Publish(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.PublishVersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpGet("versions/{versionId:guid}/validate")]
    public async Task<ActionResult<ApiResponse<Guid>>> Validate(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.ValidateVersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("versions/{versionId:guid}/retire")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retire(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await service.RetireVersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("candidate-projects/{projectId:guid}/submissions/{submissionId:guid}/manual-evaluation")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectEvaluationResponse>>> EvaluateManual(
        Guid projectId, Guid submissionId, ManualAcceleratorEvaluationRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectEvaluationResponse>(await service.EvaluateManualAsync(
            User.GetRequiredUserId(), request.CandidateId, projectId, submissionId, request.Observations, ct)));

    [HttpGet("candidate-projects/{projectId:guid}/progress")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectProgressResponse>>> CandidateProgress(
        Guid projectId, [FromQuery] Guid candidateId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectProgressResponse>(await service.AdminProgressAsync(
            User.GetRequiredUserId(), candidateId, projectId, ct)));

    [HttpGet("candidate-projects/{projectId:guid}/submissions/{submissionId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectSubmissionResponse>>> CandidateSubmission(
        Guid projectId, Guid submissionId, [FromQuery] Guid candidateId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectSubmissionResponse>(await service.AdminSubmissionAsync(
            User.GetRequiredUserId(), candidateId, projectId, submissionId, ct)));

    [HttpGet("candidate-projects/{projectId:guid}/submissions/{submissionId:guid}/evaluation")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectEvaluationResponse?>>> CandidateEvaluation(
        Guid projectId, Guid submissionId, [FromQuery] Guid candidateId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectEvaluationResponse?>(await service.AdminEvaluationAsync(
            User.GetRequiredUserId(), candidateId, projectId, submissionId, ct)));

    [HttpGet("ai-dispatches/unresolved")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DispatchAdminResponse>>>> UnresolvedAiDispatches(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<DispatchAdminResponse>>(await projectAi.UnresolvedAsync(
            User.GetRequiredUserId(), page, pageSize, ct)));
}
