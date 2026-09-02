using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Merpeeps MCP server (stdio). Exposes the format engine so a coding agent can read
// and modify diagram metadata WITHOUT pulling whole files into its context.
var builder = Host.CreateApplicationBuilder(args);

// stdio is the transport: anything on stdout that isn't protocol corrupts it,
// so all logging is routed to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
