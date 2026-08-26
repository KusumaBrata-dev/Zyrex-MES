using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ZyrexMES.Api.Common;
using ZyrexMES.Api.Hubs;
using ZyrexMES.Api.Modules.Auth;
using ZyrexMES.Api.Modules.MasterData;
using ZyrexMES.Api.Modules.Migration;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Api.Modules.Printing;
using ZyrexMES.Api.Modules.Quality;
using ZyrexMES.Api.Modules.Reports;
using ZyrexMES.Infrastructure.Legacy;
using ZyrexMES.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<LegacyImportService>();
builder.Services.AddScoped<LegacyTransactionImporter>();
// Interface consumers (e.g. importer) share the typed client instance.
builder.Services.AddScoped<ILegacyMesClient>(sp => sp.GetRequiredService<LegacyMesClient>());
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IScanResultBroadcaster, SignalRScanResultBroadcaster>();

// Legacy MES (READ-ONLY): GetToken / CheckFlow / GetMesData only.
builder.Services.Configure<LegacyOptions>(builder.Configuration.GetSection("Legacy"));
// Runtime override: MES_LEGACY__URL / MES_LEGACY__USERID / MES_LEGACY__PASSWORD
// (documented convention; standard env binding would map them to the wrong section).
builder.Services.PostConfigure<LegacyOptions>(o =>
{
    o.Url = builder.Configuration["MES_LEGACY__URL"] ?? o.Url;
    o.UserId = builder.Configuration["MES_LEGACY__USERID"] ?? o.UserId;
    o.Password = builder.Configuration["MES_LEGACY__PASSWORD"] ?? o.Password;
});
builder.Services.AddHttpClient<LegacyMesClient>((sp, client) =>
{
    var opt = sp.GetRequiredService<IOptions<LegacyOptions>>().Value;
    if (string.IsNullOrWhiteSpace(opt.Url))
    {
        throw new InvalidOperationException("Legacy:Url is not configured (set Legacy:Url or MES_LEGACY__URL).");
    }
    client.BaseAddress = new Uri(opt.Url);
    client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseMiddleware<AuditMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapAuthEndpoints();
app.MapPost("/api/admin/ping", () => Results.Ok(new { pong = true }))
   .RequireRoles(Roles.Admin);
app.MapLinesEndpoints();
app.MapStationsEndpoints();
app.MapProductsEndpoints();
app.MapNgCodesEndpoints();
app.MapMigrationEndpoints();
app.MapReconciliationEndpoints();
app.MapProductionEndpoints();
app.MapQualityEndpoints();
app.MapPrintingEndpoints();
app.MapReportsEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<ProductionHub>("/hubs/production");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    Seeder.SeedDefaults(db, app.Configuration);
}

app.Run();

public partial class Program { }
