namespace Cqc.Core;

internal static class CqcDirectory
{
    private const string Name = ".cqc";

    internal static DirectoryInfo Locate(string root) => new(Path.Combine(root, Name));

    internal static void EnsureSafe(DirectoryInfo directory)
    {
        RejectLinks(directory);
        directory.Create();
    }

    internal static async Task EnsureWritableAsync(string root, CancellationToken token)
    {
        RejectLinks(Locate(root));
        var configPath = Name + "/" + Configuration.ProjectFileName;
        try
        {
            var tracked = await ProcessRunner.RunAsync("git", ["ls-files", "-z", "--", Name], root, token);
            if (tracked.ExitCode == 0 && tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(path => path != configPath))
                throw new ArgumentException("The .cqc directory contains tracked evidence or other artifacts. Move them before requesting execution.");
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void RejectLinks(DirectoryInfo directory)
    {
        directory.Refresh();
        if (IsLink(directory))
            throw new ArgumentException("The .cqc artifact directory must not be a symbolic link.");
    }

    private static bool IsLink(DirectoryInfo directory) => directory.LinkTarget is not null ||
        (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint));
}
