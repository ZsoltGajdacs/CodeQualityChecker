using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.Java;

public static class JavaCoverageReader
{
    public static MethodCoverage[] ReadJava(string report, IEnumerable<MethodFact> methods) =>
        ReadJava(report, methods, null);

    internal static MethodCoverage[] ReadJava(string report, IEnumerable<MethodFact> methods, Action<string>? reportAmbiguity)
    {
        var facts = methods.ToLookup(CoverageKey, StringComparer.Ordinal);
        return XDocument.Load(report).Descendants("class")
            .SelectMany(type => type.Elements("method").Select(method => JavaEntry(type, method, facts, reportAmbiguity)))
            .OfType<MethodCoverage>().ToArray();
    }

    private static MethodCoverage? JavaEntry(XElement type, XElement method, ILookup<string, MethodFact> facts, Action<string>? reportAmbiguity)
    {
        var typeId = ((string?)type.Attribute("name") ?? "").Replace('/', '.');
        var name = (string?)method.Attribute("name") ?? "";
        if (name is "" or "<clinit>") return null;
        var member = name == "<init>" ? ".ctor" : name;
        var parameters = JvmDescriptor.Parameters((string?)method.Attribute("desc") ?? "");
        var signature = new JvmMethodSignature(typeId, member, parameters);
        var matches = Matches(facts, signature).Take(2).ToArray();
        if (matches.Length > 1) reportAmbiguity?.Invoke(signature.Key);
        if (matches.Length != 1) return null;
        var branch = Counter(method, "BRANCH");
        var line = Counter(method, "LINE");
        return new(matches[0].Id, branch.Covered, branch.Total, line.Covered, line.Total);
    }

    private static string CoverageKey(MethodFact fact)
    {
        var signature = fact.CoverageSignature ?? fact.Id;
        return signature.StartsWith("java:", StringComparison.Ordinal) ? signature["java:".Length..] : signature;
    }

    private static IEnumerable<MethodFact> Matches(ILookup<string, MethodFact> facts, JvmMethodSignature signature)
    {
        var key = signature.Key;
        if (facts.Contains(key)) return facts[key];
        var legacy = signature.Legacy().Key;
        if (facts.Contains(legacy)) return facts[legacy];
        return signature.WithoutEnclosingInstance() is { } inner ? facts[inner.Legacy().Key] : [];
    }

    private static (int Covered, int Total) Counter(XElement method, string name)
    {
        var counter = method.Elements("counter").SingleOrDefault(c => (string?)c.Attribute("type") == name);
        var covered = (int?)counter?.Attribute("covered") ?? 0;
        return (covered, covered + ((int?)counter?.Attribute("missed") ?? 0));
    }
}
