namespace Cqc.Server.Reporting;

internal static class ReportPageValidation
{
    private static readonly string[] Sections = ["files", "modules", "entities", "findings", "diagnostics", "metrics", "prerequisites", "raw"];
    public static void Validate(StoredReport report, ReportPageRequest request)
    {
        if (!Sections.Contains(request.Section) || request.Limit is < 1 or > 200) throw new ArgumentException("Use a supported section and limit between 1 and 200.");
        if (request.Section == "prerequisites" && !report.Inspection) throw new ArgumentException("Prerequisites require an inspection report.");
        ValidateFilters(report, request);
    }

    private static void ValidateFilters(StoredReport report, ReportPageRequest request)
    {
        if (!ValidFamily(report, request))
            throw new ArgumentException("Family filter is not valid for this report section.");
        if (request.EntityId is not null && request.Section is not ("entities" or "findings" or "metrics"))
            throw new ArgumentException("Entity filter is not valid for this section.");
        if (request.DiagnosticCode is not null && request.Section is not ("diagnostics" or "prerequisites"))
            throw new ArgumentException("Diagnostic code filter is not valid for this section.");
    }
    private static bool ValidFamily(StoredReport report, ReportPageRequest request) => request.Family is null
        || (report.Families.Contains(request.Family) && SupportsFamily(request.Section));
    private static bool SupportsFamily(string section) => section is "entities" or "findings" or "metrics" or "diagnostics";
}
