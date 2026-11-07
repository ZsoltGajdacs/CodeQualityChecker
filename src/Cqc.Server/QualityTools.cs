using System.ComponentModel;
using Cqc.Core;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

[McpServerToolType]
public sealed class QualityTools(AnalysisService service)
{
    [McpServerTool(Name = "inspect_project", UseStructuredContent = true, OutputSchemaType = typeof(InspectionResponse)), Description("Detect Java/C# modules, integrations and prerequisites without executing project code. 10 is the desired score.")]
    public Task<CallToolResult> InspectProject(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.InspectAsync(request with { Execute = false }, cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_code_quality", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse)), Description("Production complexity and branch-coverage CRAP with file/class scores and method deductions. Missing evidence prevents scoring. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckCodeQuality(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.AnalyzeAsync(request, ["code"], cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_test_quality", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse)), Description("JUnit 5/xUnit test structure, assertions and helper complexity with evidence locations. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckTestQuality(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.AnalyzeAsync(request, ["tests"], cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_duplication", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse)), Description("Structural duplicate groups within each language, using whole-repository context and selected file/class scores. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckDuplication(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.AnalyzeAsync(request, ["duplication"], cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_dependencies", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse)), Description("Semantic type dependencies, cycles, coupling, and optional component boundaries. Incomplete resolution prevents scoring. Not a package vulnerability scan. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckDependencies(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.AnalyzeAsync(request, ["dependencies"], cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_all", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse)), Description("Run all four quality families with shared discovery and parsing. Scores are separate per family and file/class. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckAll(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(() => service.AnalyzeAsync(request, ["code", "tests", "duplication", "dependencies"], cancellationToken), cancellationToken);
}
