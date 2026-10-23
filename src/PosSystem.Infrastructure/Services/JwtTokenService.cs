using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PosSystem.Application.Common;
using PosSystem.Domain.Entities;

namespace PosSystem.Infrastructure.Services;

public class JwtTokenService(JwtSettings settings) : ITokenService
{
    public string CreateAccessToken(User user, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("name", user.Username),
            new Claim("role", user.Role.ToString()),
            new Claim(AuthorizationStamp.ClaimType, AuthorizationStamp.Create(user, settings.Key))
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, expires: expiresAt, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string rawToken) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
