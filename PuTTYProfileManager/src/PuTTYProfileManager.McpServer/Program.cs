using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PuTTYProfileManager.McpServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Drain the SSH connection pool cleanly on shutdown.
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(SshConnectionPool.DisconnectAll);
AppDomain.CurrentDomain.ProcessExit += (_, _) => SshConnectionPool.DisconnectAll();

await host.RunAsync();
