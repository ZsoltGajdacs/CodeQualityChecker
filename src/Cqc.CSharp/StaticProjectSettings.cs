using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.CSharp;

internal sealed record StaticProjectSettings(Dictionary<string, string> Properties, List<string> Problems)
{
    private bool hasXunitPackage;
    public bool IsTestProject => XunitProject.IsTestProject(hasXunitPackage, Properties.GetValueOrDefault("IsTestProject"));
    private static readonly HashSet<string> Relevant = new(StringComparer.OrdinalIgnoreCase)
        { "LangVersion", "DefineConstants", "TargetFramework", "TargetFrameworks", "ImplicitUsings", "IsTestProject", "Configuration", "DisableImplicitFrameworkDefines", "DisableImplicitConfigurationDefines", "EnableDefaultCompileItems", "EnableDefaultItems", "DefaultItemExcludes", "DefaultExcludesInProjectFolder" };

    public static StaticProjectSettings Read(ProjectSnapshot snapshot, ProjectModule? project)
    {
        var root = new DirectoryInfo(snapshot.Root);
        var result = new StaticProjectSettings(new(StringComparer.OrdinalIgnoreCase), []);
        var file = project is null ? null : new FileInfo(Path.Combine(root.FullName, project.Path));
        var directory = file?.Directory ?? root;
        var props = Nearest(root, directory, "Directory.Build.props");
        if (props != null) result.AddFile(props);
        if (file != null) result.AddFile(file);
        if (result.MissingFramework(project))
            result.Problems.Add("No statically declared TargetFramework was found for this SDK project.");
        if (Nearest(root, directory, "Directory.Build.targets") != null)
            result.Problems.Add("Directory.Build.targets can change compilation. Static analysis does not evaluate it; scores remain unavailable.");
        return result;
    }

    private bool MissingFramework(ProjectModule? project) => project != null &&
        !Properties.Keys.Any(key => key is "TargetFramework" or "TargetFrameworks");

    private static FileInfo? Nearest(DirectoryInfo root, DirectoryInfo directory, string name)
    {
        for (var current = directory; current != null && IsWithin(root, current); current = current.Parent)
        {
            var file = new FileInfo(Path.Combine(current.FullName, name));
            if (file.Exists) return file;
        }
        return null;
    }

    private static bool IsWithin(DirectoryInfo root, DirectoryInfo directory) => directory.FullName == root.FullName ||
        directory.FullName.StartsWith(Path.TrimEndingDirectorySeparator(root.FullName) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private void AddFile(FileInfo file)
    {
        try
        {
            var xml = XDocument.Load(file.FullName);
            Problems.AddRange(StaticCompileRules.Problems(xml));
            hasXunitPackage |= XunitProject.HasPackage(xml);
            foreach (var property in xml.Descendants().Where(x => Relevant.Contains(x.Name.LocalName))) Apply(property);
        }
        catch (System.Xml.XmlException) { Problems.Add("Invalid project/property XML: " + file.Name); }
    }

    private void Apply(XElement property)
    {
        var name = property.Name.LocalName;
        var value = ExpandedValue(property);
        if (value.Contains("$(", StringComparison.Ordinal) || value.Contains("@(", StringComparison.Ordinal))
            Problems.Add("Cannot statically evaluate " + name + " expressions.");
        if (ChangesSourceInclusion(name, value))
            Problems.Add("Custom default source inclusion/exclusion cannot receive verified scores.");
        if (name == "Configuration" && !value.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            Problems.Add("Only the static default Debug compilation configuration is supported for verified scores.");
        Properties[name] = value;
    }

    private string ExpandedValue(XElement property)
    {
        var value = property.Value.Trim();
        return property.Name.LocalName == "DefineConstants"
            ? value.Replace("$(DefineConstants)", Properties.GetValueOrDefault("DefineConstants", "DEBUG;TRACE"), StringComparison.OrdinalIgnoreCase)
            : value;
    }

    private static bool ChangesSourceInclusion(string name, string value) => name switch
    {
        "DefaultItemExcludes" or "DefaultExcludesInProjectFolder" => true,
        "EnableDefaultCompileItems" or "EnableDefaultItems" => value.Equals("false", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
