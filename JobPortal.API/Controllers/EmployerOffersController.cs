using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerOffers;
using JobPortal.Application.Features.EmployerOffers;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/offers")]
[Produces("application/json")]
public sealed class EmployerOffersController(
    IEmployerOfferService offers)
    : ControllerBase
{
    private Guid EmployerUserId =>
        User.GetRequiredUserId();

    [HttpGet]
    public async Task<
        ActionResult<
            ApiResponse<
                PagedResponse<EmployerOfferResponse>>>> Search(
        [FromQuery] EmployerOfferQuery query,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.SearchAsync(
                EmployerUserId,
                query,
                cancellationToken);

        return Ok(
            new ApiResponse<
                PagedResponse<EmployerOfferResponse>>(
                result));
    }

    [HttpGet("{offerId:guid}")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Get(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.GetAsync(
                EmployerUserId,
                offerId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result));
    }

    [HttpGet(
        "~/api/employer/applications/{applicationId:guid}/offer")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>>
        GetForApplication(
            Guid applicationId,
            CancellationToken cancellationToken)
    {
        var result =
            await offers.GetForApplicationAsync(
                EmployerUserId,
                applicationId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result));
    }

    [HttpPost(
        "~/api/employer/applications/{applicationId:guid}/offer")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Create(
        Guid applicationId,
        [FromBody]
        CreateEmployerOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.CreateAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer created successfully."));
    }

    [HttpPut("{offerId:guid}")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Update(
        Guid offerId,
        [FromBody]
        UpdateEmployerOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.UpdateAsync(
                EmployerUserId,
                offerId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer updated successfully."));
    }

    [HttpPost("{offerId:guid}/send")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Send(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.SendAsync(
                EmployerUserId,
                offerId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer sent successfully."));
    }

    [HttpPost("{offerId:guid}/withdraw")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Withdraw(
        Guid offerId,
        [FromBody]
        WithdrawEmployerOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.WithdrawAsync(
                EmployerUserId,
                offerId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Offer withdrawn successfully."));
    }

    //[HttpPost("{offerId:guid}/accept")]
    //public async Task<
    //    ActionResult<
    //        ApiResponse<EmployerOfferResponse>>> Accept(
    //    Guid offerId,
    //    CancellationToken cancellationToken)
    //{
    //    var result =
    //        await offers.MarkAcceptedAsync(
    //            EmployerUserId,
    //            offerId,
    //            cancellationToken);

    //    return Ok(
    //        new ApiResponse<EmployerOfferResponse>(
    //            result,
    //            "Offer marked as accepted."));
    //}

    //[HttpPost("{offerId:guid}/decline")]
    //public async Task<
    //    ActionResult<
    //        ApiResponse<EmployerOfferResponse>>> Decline(
    //    Guid offerId,
    //    [FromBody]
    //    DeclineEmployerOfferRequest request,
    //    CancellationToken cancellationToken)
    //{
    //    var result =
    //        await offers.MarkDeclinedAsync(
    //            EmployerUserId,
    //            offerId,
    //            request,
    //            cancellationToken);

    //    return Ok(
    //        new ApiResponse<EmployerOfferResponse>(
    //            result,
    //            "Offer marked as declined."));
    //}

    [HttpPost("{offerId:guid}/hire")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerOfferResponse>>> Hire(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        var result =
            await offers.HireAsync(
                EmployerUserId,
                offerId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerOfferResponse>(
                result,
                "Candidate marked as hired."));
    }
}
