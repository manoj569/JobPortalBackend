using JobPortal.API.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace JobPortal.API.Swagger;

public sealed class SupportTicketsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(SupportTicketsController) ||
            context.MethodInfo.Name != nameof(SupportTicketsController.Create)) return;
        // Overrides the document-wide bearer requirement for this optional-authentication endpoint.
        operation.Security = [];
        operation.Summary = "Create a guest or authenticated support ticket with one optional screenshot.";
        operation.Description = "Login is optional. For a valid bearer token, name/email come from the account. " +
            "Screenshot: JPG, JPEG, PNG or WebP, maximum 5 MB. Maximum five requests per IP per 15 minutes.";
    }
}
