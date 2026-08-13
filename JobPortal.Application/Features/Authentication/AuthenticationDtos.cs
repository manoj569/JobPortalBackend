using System.Text.Json.Serialization;

namespace JobPortal.Application.Features.Authentication;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string PhoneNumber,
    bool HasAcceptedTermsAndPrivacy);

public sealed record RegistrationChallengeResponse(
    Guid ChallengeId,
    string Message,
    DateTime ExpiresAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerifyRegistrationOtpRequest(Guid ChallengeId, string Otp);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResendRegistrationOtpRequest(Guid ChallengeId);

public sealed record RegistrationResponse(string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest(string Identifier, string Password);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GoogleAuthenticationIntent { Login = 1, Register }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoogleAuthenticationRequest(
    string Credential,
    GoogleAuthenticationIntent Intent,
    bool AcceptTerms = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestLoginOtpRequest(string PhoneNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginWithOtpRequest(string PhoneNumber, string Otp);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestPasswordResetRequest(string Email);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompletePasswordResetRequest(
    string Token,
    string NewPassword);

public sealed record MessageResponse(string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefreshTokenRequest(string RefreshToken);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthenticatedUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role);

public sealed record AuthenticationResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    AuthenticatedUserDto User);
