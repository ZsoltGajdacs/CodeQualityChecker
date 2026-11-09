using Cqc.Core;

namespace Cqc.CSharp;

/// <summary>Nearest containing project, preserving the first module when directories tie.</summary>
internal sealed class ProjectOwnership
{
    private readonly Dictionary<string, ProjectModule> projects = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguous = new(StringComparer.Ordinal);

    internal ProjectOwnership(IEnumerable<ProjectModule> modules)
    {
        var candidates = modules.Where(module => module.Language == "csharp").ToArray();
        foreach (var group in candidates.GroupBy(module => Directory(module.Path), StringComparer.Ordinal))
        {
            if (group.Count() > 1) ambiguous.Add(group.Key);
            var project = group.FirstOrDefault(module => module.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (project is not null) projects.Add(group.Key, project);
        }
    }

    internal ProjectModule? ForFile(string file)
    {
        for (var directory = Directory(file); ; directory = Directory(directory))
        {
            if (projects.TryGetValue(directory, out var project)) return project;
            if (directory.Length == 0) return null;
        }
    }

    internal bool IsAmbiguous(ProjectModule? project) => project is not null && ambiguous.Contains(Directory(project.Path));
    private static string Directory(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
}
