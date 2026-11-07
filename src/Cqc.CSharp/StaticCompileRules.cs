using System.Xml.Linq;

namespace Cqc.CSharp;

internal static class StaticCompileRules
{
    private static readonly (Func<XDocument, bool> Applies, string Message)[] Rules =
    [
        (HasConditions, "Conditional MSBuild settings require evaluation; this configuration cannot receive verified scores."),
        (HasCustomBuild, "Custom MSBuild imports or targets may change compilation and are not evaluated by source analysis."),
        (HasCompileItems, "Custom Compile inclusion/exclusion is not evaluated; analyze a project with default SDK source inclusion.")
    ];

    public static IEnumerable<string> Problems(XDocument document) => Rules.Where(rule => rule.Applies(document)).Select(rule => rule.Message);
    private static bool HasConditions(XDocument document) => document.Descendants().Attributes("Condition").Any();
    private static bool HasCustomBuild(XDocument document) => document.Descendants().Any(node => node.Name.LocalName is "Import" or "Target" or "UsingTask");
    private static bool HasCompileItems(XDocument document) => document.Descendants().Any(node => node.Name.LocalName == "Compile" && node.HasAttributes);
}
