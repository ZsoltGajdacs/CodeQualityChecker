using Cqc.Core;
using System.Xml;

namespace Cqc.Java;

internal static class MavenTool
{
    internal static string Select(string root, string project)
    {
        var boundary = Path.GetFullPath(root);
        var directory = Path.GetDirectoryName(Path.GetFullPath(project))!;
        while (directory == boundary || directory.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var wrapper = Path.Combine(directory, OperatingSystem.IsWindows() ? "mvnw.cmd" : "mvnw");
            if (Exists(wrapper)) return wrapper;
            directory = Path.GetDirectoryName(directory)!;
            if (directory is null) break;
        }
        return "mvn";
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static IEnumerable<Diagnostic> InspectPrerequisites(ProjectSnapshot snapshot, Func<string, bool> onPath)
    {
        ProjectModule[] roots;
        try { roots = MavenBuildRoots.Select(snapshot.Modules.Where(m => m.Language == "java" && m.Supported).ToArray(), snapshot.Root).ToArray(); }
        catch (Exception exception) when (exception is IOException or XmlException or InvalidOperationException)
        {
            return [new("java.maven_manifest", "Cannot inspect Maven build roots: " + exception.Message, Language: "java")];
        }
        return roots.Select(root => InspectRoot(snapshot.Root, root, onPath)).OfType<Diagnostic>().ToArray();
    }

    private static Diagnostic? InspectRoot(string directory, ProjectModule root, Func<string, bool> onPath)
    {
        var tool = Select(directory, Path.Combine(directory, root.Path));
        if (tool == "mvn") return onPath("mvn") ? null : new("missing_build_tool", "Install Maven or add a Maven Wrapper to enable explicit project execution.", root.Path, "java");
        if (ExecutableWrapper(tool)) return null;
        return new("maven_wrapper_unexecutable", "Maven Wrapper must be a regular executable file: " + Path.GetRelativePath(directory, tool), root.Path, "java");
    }

    private static bool ExecutableWrapper(string tool) => (File.GetAttributes(tool) & FileAttributes.ReparsePoint) == 0 &&
        (OperatingSystem.IsWindows() || (File.GetUnixFileMode(tool) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);

    internal static async Task<CommandResult> RunRequired(string executable, string[] args, string directory, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(executable, args, directory, token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{executable} exited {result.ExitCode}: {result.Error}\n{result.Output}");
        return result;
    }
}
