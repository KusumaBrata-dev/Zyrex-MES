using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Port=5433;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd",
                ["Jwt:Key"] = "unit-test-signing-key-0123456789abcdef-unit-test",
                ["Jwt:Issuer"] = "zyrex-mes-test",
                ["Jwt:Audience"] = "zyrex-mes-clients",
                ["Jwt:ExpiryHours"] = "12",
            }));
    }

    public AppDbContext CreateDb()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>Idempotent: called from constructors of test classes that need accounts.</summary>
    public void EnsureSeedUsers()
    {
        using var db = CreateDb();
        db.Database.Migrate();
        void Ensure(string u, string p, string fullName, Domain.Entities.UserRole role)
        {
            if (!db.Users.Any(x => x.Username == u))
                db.Users.Add(new Domain.Entities.AppUser
                {
                    Username = u,
                    PasswordHash = Infrastructure.Security.PasswordHasher.Hash(p),
                    FullName = fullName,
                    Role = role,
                    IsActive = true,
                });
        }
        Ensure("admin", "Adm1n!pwd", "Sys Admin", Domain.Entities.UserRole.Admin);
        Ensure("leader1", "Lead!pwd12", "Leader Satu", Domain.Entities.UserRole.Leader);
        Ensure("op1", "Op!pwd123", "Operator Satu", Domain.Entities.UserRole.Operator);
        db.SaveChanges();
    }
}
