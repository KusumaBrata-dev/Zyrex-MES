using Microsoft.Extensions.Options;
using ZyrexMES.PrintAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<AgentOptions>(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);
builder.Services.AddHttpClient<IAgentApiClient, ApiClient>();
builder.Services.AddSingleton<ILabelPrinter, ProcessLabelPrinter>();
builder.Services.AddHostedService<PollingService>();
// Runs as a console app normally; install with `sc create` / New-Service and it
// runs as a Windows Service (AddWindowsService wires the lifetime).
builder.Services.AddWindowsService(o => o.ServiceName = "ZyrexMES PrintAgent");

var host = builder.Build();
host.Run();
