using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.Java;

public static class JavaCoverageReader
{
    public static MethodCoverage[] ReadJava(string report, IEnumerable<MethodFact> methods)
    {
        var facts = methods.ToArray();
        return XDocument.Load(report).Descendants("class")
            .SelectMany(type => type.Elements("method").Select(method => JavaEntry(type, method, facts)))
            .OfType<MethodCoverage>().ToArray();
    }

    private static MethodCoverage? JavaEntry(XElement type, XElement method, MethodFact[] facts)
    {
        var typeId = ((string?)type.Attribute("name") ?? "").Replace('/', '.').Replace('$', '.');
        var name = (string?)method.Attribute("name");
        if (name == "<clinit>") return null;
        var member = name == "<init>" ? ".ctor" : name;
        var parameters = JvmDescriptor.Parameters((string?)method.Attribute("desc") ?? "");
        var signature = typeId + "." + member + "(" + string.Join(',', parameters) + ")";
        var fact = facts.SingleOrDefault(m => JavaMatches(m, signature));
        if (fact is null && name == "<init>") fact = InnerConstructor(typeId, parameters, facts);
        if (fact is null) return null;
        var branch = Counter(method, "BRANCH");
        var line = Counter(method, "LINE");
        return new(fact.Id, branch.Covered, branch.Total, line.Covered, line.Total);
    }

    private static bool JavaMatches(MethodFact fact, string signature) => fact.Id == signature || fact.Id == "java:" + signature
        || fact.CoverageSignature == signature || fact.CoverageSignature == "java:" + signature;

    private static MethodFact? InnerConstructor(string typeId, string[] parameters, MethodFact[] facts)
    {
        var separator = typeId.LastIndexOf('.');
        if (separator < 0 || parameters.Length == 0) return null;
        if (parameters[0] != typeId[..separator]) return null;
        var signature = typeId + "..ctor(" + string.Join(',', parameters.Skip(1)) + ")";
        return facts.SingleOrDefault(m => JavaMatches(m, signature));
    }

    private static (int Covered, int Total) Counter(XElement method, string name)
    {
        var counter = method.Elements("counter").SingleOrDefault(c => (string?)c.Attribute("type") == name);
        var covered = (int?)counter?.Attribute("covered") ?? 0;
        return (covered, covered + ((int?)counter?.Attribute("missed") ?? 0));
    }
}
