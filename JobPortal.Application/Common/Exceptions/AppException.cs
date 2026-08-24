namespace JobPortal.Application.Common.Exceptions;

using JobPortal.Application.Features.Payments;

public class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class BadRequestException(string message, string code = "bad_request") : AppException(message, 400, code);
public sealed class UnauthorizedException(string message = "Authentication failed.") : AppException(message, 401, "unauthorized");
public sealed class NotFoundException(string message) : AppException(message, 404, "not_found");
public sealed class ConflictException(string message, string code = "conflict") : AppException(message, 409, code);
public sealed class PendingMembershipCheckoutException(PendingMembershipCheckoutRecovery recovery) :
    AppException("A portal membership payment order is already pending.", 409, "pending_membership_checkout")
{
    public PendingMembershipCheckoutRecovery Recovery { get; } = recovery;
}
public sealed class AuthenticationFlowException(string message, int statusCode, string code) :
    AppException(message, statusCode, code);

public sealed class ApplicationQuotaExceededException(
    string code,
    string message,
    bool redirectToMembership) : AppException(message, 403, code)
{
    public bool RedirectToMembership { get; } = redirectToMembership;
}

public sealed class UniqueConstraintException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
