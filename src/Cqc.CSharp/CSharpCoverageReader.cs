using System.Globalization;
using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.CSharp;

public static class CSharpCoverageReader
{
    public static MethodCoverage[] ReadReport(string report, IEnumerable<MethodFact> methods)
        => ReadReports([report], methods);

    public static MethodCoverage[] ReadReports(IEnumerable<string> reports, IEnumerable<MethodFact> methods)
        => ReadReports(reports, new OpenCoverMethodMatcher(methods, null), null, _ => null);

    internal static MethodCoverage[] ReadReports(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        ReadReports(build.Reports, new OpenCoverMethodMatcher(methods, build.SourceRoot), new OpenCoverModuleOwners(build.ProjectOutputs),
            report => TestOutputDirectory(build, report));

    private static MethodCoverage[] ReadReports(IEnumerable<string> reports, OpenCoverMethodMatcher matcher,
        OpenCoverModuleOwners? owners, Func<string, string?> testOutputDirectory)
    {
        return reports.Order(StringComparer.Ordinal)
            .SelectMany(report => XDocument.Load(report).Descendants("Module")
                .SelectMany(module => OpenCoverModule(module, matcher, owners, testOutputDirectory(report))))
            .GroupBy(p => p.MethodId, StringComparer.Ordinal)
            .Select(MergePoints).OrderBy(p => p.MethodId, StringComparer.Ordinal).ToArray();
    }

    private sealed record PointCoverage(string MethodId, (string Key, bool Hit)[] Lines, (string Key, bool Hit)[] Branches);

    private static MethodCoverage MergePoints(IGrouping<string, PointCoverage> group)
    {
        var lines = group.SelectMany(p => p.Lines).GroupBy(p => p.Key).Select(g => g.Any(p => p.Hit)).ToArray();
        var branches = group.SelectMany(p => p.Branches).GroupBy(p => p.Key).Select(g => g.Any(p => p.Hit)).ToArray();
        return new(group.Key, branches.Count(x => x), branches.Length, lines.Count(x => x), lines.Length);
    }

    private static string? TestOutputDirectory(LanguageBuildResult build, string report) =>
        build.ReportModules.TryGetValue(report, out var project) && build.ProjectOutputs.TryGetValue(project, out var target)
            ? Path.GetDirectoryName(target) : null;

    private static IEnumerable<PointCoverage> OpenCoverModule(XElement module, OpenCoverMethodMatcher matcher,
        OpenCoverModuleOwners? owners, string? outputDirectory)
    {
        var project = owners?.Find(module.Element("ModulePath")?.Value, outputDirectory);
        if (owners is not null && project is null) return [];
        var files = module.Descendants("File").ToDictionary(x => (string)x.Attribute("uid")!, x => (string)x.Attribute("fullPath")!);
        return module.Descendants("Class").SelectMany(type => OpenCoverClass(type, files, matcher, project));
    }

    private static IEnumerable<PointCoverage> OpenCoverClass(XElement type, Dictionary<string, string> files,
        OpenCoverMethodMatcher matcher, string? project)
    {
        var typeName = type.Element("FullName")?.Value.Replace('/', '.').Replace('+', '.');
        var members = type.Descendants("Method").Select(method => (Entry: method, Member: MemberName(method)))
            .Where(item => item.Member is not null).ToArray();
        var signatures = members.Select(item => OpenCoverMethodIdentity.NormalizeArity(typeName + "." + item.Member)).ToHashSet(StringComparer.Ordinal);
        var context = new OpenCoverClassContext(project, signatures);
        return members.SelectMany(item => OpenCoverEntry(item.Entry,
            new(typeName + "." + item.Member, FileFor(item.Entry, files), context), matcher));
    }

    private static string? MemberName(XElement method)
    {
        var fullName = method.Element("Name")?.Value ?? "";
        var separator = fullName.IndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? null : fullName[(separator + 2)..];
    }

    private static string? FileFor(XElement method, Dictionary<string, string> files) =>
        (string?)method.Element("FileRef")?.Attribute("uid") is { } reference ? files.GetValueOrDefault(reference) : null;

    private static IEnumerable<PointCoverage> OpenCoverEntry(XElement method, OpenCoverMethodIdentity identity, OpenCoverMethodMatcher matcher)
    {
        var sequence = method.Descendants("SequencePoint").ToArray();
        var branches = method.Descendants("BranchPoint").ToArray();
        var linesInReport = sequence.Concat(branches).Select(p => (int?)p.Attribute("sl")).OfType<int>().Distinct().ToArray();
        var fact = matcher.Find(identity, linesInReport);
        if (fact is not null) return [ReadPoints(fact, identity.NativeSignature, sequence, branches)];

        var factsByLine = linesInReport.ToDictionary(line => line,
            line => matcher.Find(identity, [line], allowSourceFallback: false));
        if (factsByLine.Values.Any(owner => owner is null) || sequence.Concat(branches).Any(point => point.Attribute("sl") is null))
            return [];
        return factsByLine.Values.OfType<MethodFact>().DistinctBy(owner => owner.Id).Select(owner => ReadPoints(owner, identity.NativeSignature,
            PointsFor(owner, sequence, factsByLine), PointsFor(owner, branches, factsByLine)));
    }

    private static IEnumerable<XElement> PointsFor(MethodFact method, XElement[] points, Dictionary<int, MethodFact?> factsByLine) =>
        points.Where(point => factsByLine[(int)point.Attribute("sl")!]?.Id == method.Id);

    private static PointCoverage ReadPoints(MethodFact fact, string signature, IEnumerable<XElement> sequence, IEnumerable<XElement> branches)
    {
        var lines = sequence.Select(p => (fact.Location.File + ":" + (string?)p.Attribute("sl"), Visited(p))).ToArray();
        var branchPoints = branches.Select(p =>
            (signature + ":" + p.Attribute("offset")?.Value + ":" + p.Attribute("path")?.Value + ":" + p.Attribute("ordinal")?.Value, Visited(p))).ToArray();
        return new(fact.Id, lines, branchPoints);
    }

    private static bool Visited(XElement point) => long.TryParse((string?)point.Attribute("vc"), CultureInfo.InvariantCulture, out var visits) && visits > 0;
}
