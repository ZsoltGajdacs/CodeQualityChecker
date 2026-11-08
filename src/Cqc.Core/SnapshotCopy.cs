namespace Cqc.Core;

internal static class SnapshotCopy
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".cqc", ".backlog", ".serena", "bin", "obj", "target", "node_modules", "dist", "TestResults" };

    internal static void Copy(string source, string target)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
            var name = Path.GetFileName(entry);
            if (Excluded.Contains(name)) continue;
            CopyEntry(entry, Path.Combine(target, name));
        }
    }

    private static void CopyEntry(string entry, string destination)
    {
        if (Directory.Exists(entry)) { Directory.CreateDirectory(destination); Copy(entry, destination); return; }
        File.Copy(entry, destination);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(entry));
    }
}
