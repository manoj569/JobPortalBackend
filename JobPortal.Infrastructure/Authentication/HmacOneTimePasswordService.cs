//using System.Globalization;
//using System.Security.Cryptography;
//using System.Text;
//using JobPortal.Application.Abstractions.Authentication;
//using Microsoft.Extensions.Configuration;

//namespace JobPortal.Infrastructure.Authentication;

//public sealed class HmacOneTimePasswordService : IOneTimePasswordService
//{
//    private readonly byte[] key;

//    public HmacOneTimePasswordService(IConfiguration configuration)
//    {
//        var configuredKey = configuration["Otp:HashKey"]
//            ?? throw new InvalidOperationException("Otp:HashKey is not configured.");
//        if (configuredKey.Length < 32)
//            throw new InvalidOperationException(
//                "Otp:HashKey must contain at least 32 characters.");
//        key = Encoding.UTF8.GetBytes(configuredKey);
//    }

//    public string Generate() =>
//        RandomNumberGenerator.GetInt32(0, 1_000_000)
//            .ToString("D6", CultureInfo.InvariantCulture);

//    public string Hash(string otp) =>
//        Convert.ToHexString(HMACSHA256.HashData(
//            key,
//            Encoding.UTF8.GetBytes(otp)));

//    public bool Verify(string otp, string expectedHash)
//    {
//        try
//        {
//            return CryptographicOperations.FixedTimeEquals(
//                Convert.FromHexString(Hash(otp)),
//                Convert.FromHexString(expectedHash));
//        }
//        catch (FormatException)
//        {
//            return false;
//        }
//    }
//}
