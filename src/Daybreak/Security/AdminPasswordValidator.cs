using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Daybreak.Security;

public sealed class AdminPasswordValidator
{
    private const string PasswordStampClaim = "daybreak:password-stamp";
    private readonly byte[] _expectedHash;
    private readonly string _stamp;

    public AdminPasswordValidator()
    {
        var password = Environment.GetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set DAYBREAK_ADMIN_PASSWORD before starting Daybreak.");
        }

        _expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        _stamp = Convert.ToHexString(_expectedHash.AsSpan(0, 12));
    }

    public bool IsValid(string candidate)
    {
        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(_expectedHash, candidateHash);
    }

    public bool IsCurrent(ClaimsPrincipal? principal) =>
        string.Equals(principal?.FindFirstValue(PasswordStampClaim), _stamp, StringComparison.Ordinal);

    public ClaimsPrincipal CreatePrincipal()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "Administrator"),
                new Claim(ClaimTypes.Role, "Administrator"),
                new Claim(PasswordStampClaim, _stamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
