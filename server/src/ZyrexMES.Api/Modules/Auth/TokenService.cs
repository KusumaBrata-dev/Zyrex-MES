using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Modules.Auth;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user);
}

public class TokenService(IConfiguration cfg) : ITokenService
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(double.Parse(cfg["Jwt:ExpiryHours"] ?? "12"));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ClaimTypes.GivenName, user.FullName),
        };
        var jwt = new JwtSecurityToken(cfg["Jwt:Issuer"], cfg["Jwt:Audience"], claims,
            expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
