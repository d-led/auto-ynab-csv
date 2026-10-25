using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the protocol itself, so a log line written there would corrupt a message. Everything
// the server says about itself goes to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

await builder.Build().RunAsync();
