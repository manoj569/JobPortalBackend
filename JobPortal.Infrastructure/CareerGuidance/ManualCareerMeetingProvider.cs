using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using Microsoft.AspNetCore.DataProtection;

namespace JobPortal.Infrastructure.CareerGuidance;

public sealed class ManualCareerMeetingProvider : ICareerMeetingProvider
{
    public string Name => "Manual";
    public Task<CareerMeeting> CreateAsync(Guid sessionId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(Descriptor(sessionId)); }
    public Task<CareerMeeting?> GetAsync(Guid sessionId, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult<CareerMeeting?>(Descriptor(sessionId)); }
    private static CareerMeeting Descriptor(Guid id) => new("Manual", $"manual_{id:N}", null, null);
}

public sealed class CareerMeetingProtector(IDataProtectionProvider provider) : ICareerMeetingProtector
{
    private IDataProtector Purpose(Guid id, bool host) => provider.CreateProtector("CareerHarbor", "CareerGuidance", "Meeting", "v1", id.ToString("N"), host ? "host" : "participant");
    public byte[] Protect(Guid sessionId, bool host, string value) => Purpose(sessionId, host).Protect(Encoding.UTF8.GetBytes(value));
    public string Unprotect(Guid sessionId, bool host, byte[] value)
    {
        try { return Encoding.UTF8.GetString(Purpose(sessionId, host).Unprotect(value)); }
        catch (CryptographicException) { throw new ConflictException("Meeting link is unavailable; an administrator must reconfigure it.", "meeting_key_unavailable"); }
    }
}
