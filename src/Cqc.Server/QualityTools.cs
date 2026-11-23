using System.ComponentModel;
using Cqc.Core;
using Cqc.Server.Reporting;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

[McpServerToolType]
public sealed class QualityTools(AnalysisService service, ReportStore reports)
{
    [McpServerTool(Name = "inspect_project", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Detect Java/C# modules, integrations and prerequisites without executing project code. 10 is the desired score.")]
    public Task<CallToolResult> InspectProject(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(async () => await reports.PublishAsync(await service.InspectAsync(request with { Execute = false }, cancellationToken), cancellationToken, request.Scope), cancellationToken);

    [McpServerTool(Name = "check_code_quality", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Review the whole repository. Source-only by default: static complexity and structural duplication. 10 is the desired score. execute=true permits isolated builds and tests and evaluates all quality families.")]
    public Task<CallToolResult> CheckCodeQuality(QualityReviewRequest request, CancellationToken cancellationToken)
        => Review(request, "repository", cancellationToken);

    [McpServerTool(Name = "check_uncommitted_changes_quality", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Review staged, unstaged, and untracked source files using their current working-tree contents and repository context. Source-only by default. 10 is the desired score. execute=true permits isolated builds and tests and evaluates all quality families.")]
    public Task<CallToolResult> CheckUncommittedChangesQuality(QualityReviewRequest request, CancellationToken cancellationToken)
        => Review(request, "changed", cancellationToken);

    private Task<CallToolResult> Review(QualityReviewRequest request, string scope, CancellationToken token)
        => McpReply.Invoke(async () => await reports.PublishAsync(
            await service.ReviewAsync(request.ForScope(scope), token), token), token);

    [McpServerTool(Name = "check_test_quality", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("JUnit 5/xUnit test structure, assertions and helper complexity with evidence locations. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckTestQuality(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(async () => await reports.PublishAsync(await service.AnalyzeAsync(request, ["tests"], cancellationToken), cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_duplication", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Structural duplicate groups within each language, using whole-repository context and selected file/class scores. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckDuplication(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(async () => await reports.PublishAsync(await service.AnalyzeAsync(request, ["duplication"], cancellationToken), cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_dependencies", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Semantic type dependencies, cycles, coupling, and optional component boundaries. Incomplete resolution prevents scoring. Not a package vulnerability scan. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckDependencies(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(async () => await reports.PublishAsync(await service.AnalyzeAsync(request, ["dependencies"], cancellationToken), cancellationToken), cancellationToken);

    [McpServerTool(Name = "check_all", UseStructuredContent = true, OutputSchemaType = typeof(ReportSummary)), Description("Run all four quality families with shared discovery and parsing. Scores are separate per family and file/class. 10 is the desired score. execute=true explicitly permits project builds/tests/restores in an isolated copy.")]
    public Task<CallToolResult> CheckAll(AnalysisRequest request, CancellationToken cancellationToken) => McpReply.Invoke(async () => await reports.PublishAsync(await service.AnalyzeAsync(request, ["code", "tests", "duplication", "dependencies"], cancellationToken), cancellationToken), cancellationToken);
    [McpServerTool(Name = "get_report_page", UseStructuredContent = true, OutputSchemaType = typeof(ReportPage)), Description("Retrieve immutable report details without rerunning analysis. Sections: files, modules, entities, findings, diagnostics, metrics, prerequisites, raw. Follow nextCursor until null. Raw chunks reconstruct the full JSON report. Reports expire after 60 minutes and can be evicted. 10 is the desired score.")]
    public Task<CallToolResult> GetReportPage(ReportPageRequest request, CancellationToken cancellationToken)
        => McpReply.Invoke(() => reports.ReadPageAsync(request, cancellationToken), cancellationToken);
}
