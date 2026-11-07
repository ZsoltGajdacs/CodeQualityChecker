using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<ILanguageAnalyzer, CSharpAnalyzer>();
builder.Services.AddSingleton<ILanguageAnalyzer, JavaAnalyzer>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<QualityTools>();
await builder.Build().RunAsync();
