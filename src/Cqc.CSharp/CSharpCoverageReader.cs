using System.Globalization;
using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.CSharp;

public static class CSharpCoverageReader
{
    public static MethodCoverage[] ReadReport(string report, IEnumerable<MethodFact> methods)
        => ReadReports([report], methods);

    public static MethodCoverage[] ReadReports(IEnumerable<string> reports, IEnumerable<MethodFact> methods)
    {
        var facts = methods.ToArray();
        return reports.Order(StringComparer.Ordinal).SelectMany(report => XDocument.Load(report).Descendants("Module"))
            .SelectMany(module => OpenCoverModule(module, facts)).GroupBy(p => p.MethodId, StringComparer.Ordinal)
            .Select(MergePoints).OrderBy(p => p.MethodId, StringComparer.Ordinal).ToArray();
    }

    private sealed record PointCoverage(string MethodId, (string Key, bool Hit)[] Lines, (string Key, bool Hit)[] Branches);

    private static MethodCoverage MergePoints(IGrouping<string, PointCoverage> group)
    {
        var lines = group.SelectMany(p => p.Lines).GroupBy(p => p.Key).Select(g => g.Any(p => p.Hit)).ToArray();
        var branches = group.SelectMany(p => p.Branches).GroupBy(p => p.Key).Select(g => g.Any(p => p.Hit)).ToArray();
        return new(group.Key, branches.Count(x => x), branches.Length, lines.Count(x => x), lines.Length);
    }

    private static IEnumerable<PointCoverage> OpenCoverModule(XElement module, MethodFact[] facts)
    {
        var files = module.Descendants("File").ToDictionary(x => (string)x.Attribute("uid")!, x => (string)x.Attribute("fullPath")!);
        return module.Descendants("Class").SelectMany(type => type.Descendants("Method")
            .Select(method => OpenCoverEntry(type, method, files, facts))).OfType<PointCoverage>();
    }

    private static PointCoverage? OpenCoverEntry(XElement type, XElement method, Dictionary<string, string> files, MethodFact[] facts)
    {
        var typeName = type.Element("FullName")?.Value.Replace('/', '.').Replace('+', '.');
        var fullName = method.Element("Name")?.Value ?? "";
        var separator = fullName.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0) return null;
        var member = fullName[(separator + 2)..];
        var signature = typeName + "." + member;
        var exact = facts.Where(m => m.Id == signature || m.Id == "csharp:" + signature).ToArray();
        var points = method.Descendants("SequencePoint").ToArray();
        var fact = exact.Length == 1 ? exact[0] : SourceMatch(method, files, points, facts);
        if (fact is null) return null;
        var lines = points.Select(p => (fact.Location.File + ":" + (string?)p.Attribute("sl"), Visited(p))).ToArray();
        var branches = method.Descendants("BranchPoint").Select(p =>
            (signature + ":" + p.Attribute("offset")?.Value + ":" + p.Attribute("path")?.Value + ":" + p.Attribute("ordinal")?.Value, Visited(p))).ToArray();
        return new(fact.Id, lines, branches);
    }

    // Source points preserve identity for generic and compiler-transformed methods.
    // Ambiguous same-line declarations deliberately remain unavailable.
    private static MethodFact? SourceMatch(XElement method, Dictionary<string, string> files, XElement[] points, MethodFact[] facts)
    {
        var reference = (string?)method.Element("FileRef")?.Attribute("uid");
        if (reference is null || !files.TryGetValue(reference, out var file)) return null;
        var line = points.Select(p => (int?)p.Attribute("sl")).OfType<int>().DefaultIfEmpty(-1).Min();
        var candidates = facts.Where(m => MatchesFile(file, m.Location.File) && m.Location.StartLine <= line && m.Location.EndLine >= line)
            .OrderBy(m => m.Location.EndLine - m.Location.StartLine).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length > 1 && candidates[0].Location == candidates[1].Location) return null;
        return candidates[0];
    }

    private static bool MatchesFile(string absolute, string relative) => absolute.Replace('\\', '/').EndsWith("/" + relative, StringComparison.Ordinal) || absolute == relative;
    private static bool Visited(XElement point) => long.TryParse((string?)point.Attribute("vc"), CultureInfo.InvariantCulture, out var visits) && visits > 0;
}
