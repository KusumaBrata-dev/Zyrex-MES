using Microsoft.Extensions.Options;
using ZyrexMES.PrintAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiBaseUrl), "Agent:ApiBaseUrl is required")
    .Validate(o => o.StationId > 0, "Agent:StationId must be a positive integer")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Agent:Username is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "Agent:Password is required")
    .ValidateOnStart(); // fail fast on bad config instead of failing per request
builder.Services.AddSingleton<AgentOptions>(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);
builder.Services.AddHttpClient<IAgentApiClient, ApiClient>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ILabelPrinter, BartenderCliPrinter>();
builder.Services.AddHostedService<PollingService>();
// Runs as a console app normally; install with `sc create` / New-Service and it
// runs as a Windows Service (AddWindowsService wires the lifetime).
builder.Services.AddWindowsService(o => o.ServiceName = "ZyrexMES PrintAgent");

var host = builder.Build();
host.Run();
