using System.Xml.Linq;

namespace Cqc.CSharp;

/// <summary>Reads literal project references without evaluating MSBuild items or metadata.</summary>
internal sealed class StaticProjectReferences
{
    public List<string> Includes { get; } = [];
    public List<string> Problems { get; } = [];

    public void Read(XDocument document)
    {
        foreach (var reference in document.Descendants().Where(item => item.Name.LocalName == "ProjectReference"))
            ReadReference(reference);
    }

    private void ReadReference(XElement reference)
    {
        var include = reference.Attribute("Include")?.Value.Trim() ?? "";
        if (!TryReadReference(reference, include, out var includeOutput))
            Problems.Add("Cannot statically evaluate ProjectReference " + (include.Length == 0 ? "(missing Include)" : include) + ".");
        else if (includeOutput)
            Includes.Add(include);
    }

    private static bool TryReadReference(XElement reference, string include, out bool includeOutput)
    {
        includeOutput = false;
        return LiteralInclude(include) && UnconditionalItem(reference) && TryReadOutput(reference, out includeOutput);
    }

    private static bool LiteralInclude(string include) =>
        include.Length > 0 && include.IndexOfAny(['$', '@', '%', '*', '?', ';']) < 0;

    private static bool UnconditionalItem(XElement reference) =>
        reference.Parent?.Name.LocalName == "ItemGroup" && reference.Parent.Parent == reference.Document?.Root
        && reference.AncestorsAndSelf().All(item => item.Attribute("Condition") is null);

    private static bool TryReadOutput(XElement reference, out bool includeOutput)
    {
        includeOutput = true;
        if (reference.Attributes().Any(attribute => attribute.Name.LocalName is not ("Include" or "ReferenceOutputAssembly"))) return false;
        var elements = reference.Elements().ToArray();
        var attribute = reference.Attribute("ReferenceOutputAssembly");
        return elements.Length == 0 ? attribute is null || bool.TryParse(attribute.Value, out includeOutput)
            : attribute is null && TryReadOutputElement(elements, out includeOutput);
    }

    private static bool TryReadOutputElement(XElement[] elements, out bool includeOutput)
    {
        includeOutput = true;
        return elements is [{ Name.LocalName: "ReferenceOutputAssembly", HasElements: false, HasAttributes: false } item]
            && bool.TryParse(item.Value, out includeOutput);
    }
}
