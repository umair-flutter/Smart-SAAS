using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Security;

namespace SmartSchool.Infrastructure.Security;

public class JwtSettings
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "SmartSchool";
    public string Audience { get; set; } = "SmartSchool.Client";

    /// <summary>HMAC signing key, at least 32 characters. Must be overridden in production.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 480;
}

public class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Owner = new();

    public string Hash(string password) => Hasher.HashPassword(Owner, password);

    public bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) && Hasher.VerifyHashedPassword(Owner, hash, password) != PasswordVerificationResult.Failed;
}

public class JwtTokenService(IOptions<JwtSettings> options) : ITokenService
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user, Role role, IEnumerable<Guid> schoolIds, Guid sessionId)
    {
        var expires = DateTime.UtcNow.AddMinutes(_settings.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(AppClaims.UserId, user.Id.ToString()),
            new(AppClaims.Email, user.Email),
            new(AppClaims.Name, user.FullName),
            new(AppClaims.Role, role.Type.ToString()),
            new(AppClaims.RoleType, ((int)role.Type).ToString()),
            new(AppClaims.SessionId, sessionId.ToString()),
            new(AppClaims.Language, user.PreferredLanguage),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.TenantId is { } tid) claims.Add(new(AppClaims.TenantId, tid.ToString()));
        claims.AddRange(schoolIds.Select(id => new Claim(AppClaims.SchoolIds, id.ToString())));
        claims.AddRange(role.Permissions.Select(p => new Claim(AppClaims.Permission, p.Permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var token = new JwtSecurityToken(
            _settings.Issuer,
            _settings.Audience,
            claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
