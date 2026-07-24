using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AzerothCore_UI.Api.Security;

public static class ApiAccessPolicy
{
    public const string HeaderName = "X-AzerothCore-Admin-Key";
    public const int MinimumKeyLength = 32;

    public static bool IsAuthorized(
        IPAddress? remoteAddress,
        string? suppliedKey,
        string? expectedKey,
        bool allowLoopbackWithoutKey)
    {
        if (!string.IsNullOrEmpty(expectedKey) && !string.IsNullOrEmpty(suppliedKey))
        {
            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            return suppliedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }

        return allowLoopbackWithoutKey
            && remoteAddress is not null
            && IPAddress.IsLoopback(remoteAddress);
    }

    public static void ValidateProductionKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < MinimumKeyLength)
            throw new InvalidOperationException(
                $"Security:ApiKey must contain at least {MinimumKeyLength} characters in Production.");
    }
}
