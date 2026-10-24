using System.Security.Cryptography;
using System.Text;
using PosSystem.Domain.Entities;

namespace PosSystem.Infrastructure.Services;

public static class AuthorizationStamp
{
    public const string ClaimType = "auth_stamp";

    public static string Create(User user, string signingKey)
    {
        var value = $"{user.Id}\n{user.PasswordHash}\n{user.Role}\n{user.IsActive}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(signature);
    }

    public static bool Matches(string? presented, User user, string signingKey)
    {
        if (string.IsNullOrEmpty(presented)) return false;
        var expected = Encoding.UTF8.GetBytes(Create(user, signingKey));
        var actual = Encoding.UTF8.GetBytes(presented);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
