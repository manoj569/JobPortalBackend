using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerOffers;
using JobPortal.Application.Features.EmployerOffers;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/candidate/offers")]
[Produces("application/json")]
public sealed class CandidateOffersController(
    IEmployerOfferService offers)
    : ControllerBase
{
    private Guid CandidateUserId =>
        User.GetRequiredUserId();

    [HttpPost("{offerId:guid}/accept")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Accept(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.AcceptByCandidateAsync(
                CandidateUserId,
                offerId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer accepted successfully."));
    }

    [HttpPost("{offerId:guid}/decline")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Decline(
        Guid offerId,
        [FromBody]
        DeclineEmployerOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.DeclineByCandidateAsync(
                CandidateUserId,
                offerId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer declined successfully."));
    }
}
