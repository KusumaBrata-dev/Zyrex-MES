using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;
using ZyrexMES.Infrastructure.Security;

namespace ZyrexMES.Api.Modules.Auth;

public record LoginResponse(string Token, UserProfile User);
public record UserProfile(string Username, string FullName, string Role);

public class AuthService(AppDbContext db, ITokenService tokens)
{
    private static readonly string _dummyHash = PasswordHasher.Hash("dummy-password-for-timing");

    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null)
        {
            PasswordHasher.Verify(password, _dummyHash);
            return null;
        }
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;
        var (token, _) = tokens.Issue(user);
        return new LoginResponse(token, new UserProfile(user.Username, user.FullName, user.Role.ToString()));
    }
}
