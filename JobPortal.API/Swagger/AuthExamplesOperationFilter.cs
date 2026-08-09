using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace JobPortal.API.Swagger;

public sealed class AuthExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestBody = operation.RequestBody;
        if (requestBody is null || !requestBody.Content.TryGetValue("application/json", out var mediaType)) return;

        mediaType.Example = context.MethodInfo.Name switch
        {
            "Register" => Registration(),
            "VerifyRegistrationOtp" => Object(("challengeId", Guid.Empty.ToString()), ("otp", "000000")),
            "ResendRegistrationOtp" => Object(("challengeId", Guid.Empty.ToString())),
            "Login" => Object(("identifier", "candidate@example.com"), ("password", "abc123")),
            "RequestLoginOtp" => Object(("phoneNumber", "9876543210")),
            "RequestPasswordReset" => Object(("email", "user@example.com")),
            "LoginWithOtp" =>
                Object(("phoneNumber", "9876543210"), ("otp", "000000")),
            "CompletePasswordReset" => Object(
                ("token", "Password reset token from the email link"),
                ("newPassword", "abc123")),
            "Refresh" or "Logout" => Object(("refreshToken", "Base64 refresh token")),
            "ChangePassword" => Object(("currentPassword", "abc123"), ("newPassword", "newpass")),
            "CreateOrder" => new OpenApiObject(),
            "Confirm" => Object(
                ("razorpayOrderId", "order_test_example"),
                ("razorpayPaymentId", "pay_test_example"),
                ("razorpaySignature", new string('a', 64))),
            "UpdateStatus" => Object(
                ("status", "Shortlisted"),
                ("internalNote", "Strong experience; schedule an interview.")),
            "UpdateOnboarding" => Onboarding(),
            _ => null
        };
    }

    private static OpenApiObject Object(params (string Key, string Value)[] values)
    {
        var example = new OpenApiObject();
        foreach (var (key, value) in values) example[key] = new OpenApiString(value);
        return example;
    }

    private static OpenApiObject Registration() => new()
    {
        ["fullName"] = new OpenApiString("Manoj Shekapure"),
        ["email"] = new OpenApiString("user@example.com"),
        ["password"] = new OpenApiString("abc123"),
        ["phoneNumber"] = new OpenApiString("9876543210"),
        ["hasAcceptedTermsAndPrivacy"] = new OpenApiBoolean(true)
    };

    private static OpenApiObject Onboarding() => new()
    {
        ["careerStage"] = new OpenApiInteger(3),
        ["desiredOpportunities"] = new OpenApiArray
        {
            new OpenApiInteger(3)
        },
        ["city"] = new OpenApiString("Pune"),
        ["skills"] = new OpenApiArray
        {
            new OpenApiString("C#"),
            new OpenApiString("SQL")
        },
        ["workPreferences"] = new OpenApiArray
        {
            new OpenApiInteger(1),
            new OpenApiInteger(2)
        },
        ["college"] = new OpenApiString("Example Institute"),
        ["degree"] = new OpenApiString("B.Tech"),
        ["graduationYear"] = new OpenApiInteger(2024),
        ["yearsOfExperience"] = new OpenApiDouble(2.5)
    };
}
