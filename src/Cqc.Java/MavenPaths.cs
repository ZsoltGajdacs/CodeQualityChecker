namespace Cqc.Java;

/// <summary>Normalizes evaluated Maven paths, including platform temporary-directory aliases.</summary>
internal static class MavenPaths
{
    internal static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null) return full;
        var resolved = Path.Combine(Canonical(parent), Path.GetFileName(full));
        FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
        if (info.LinkTarget is null) return resolved;
        var target = info.ResolveLinkTarget(true)
            ?? throw new InvalidOperationException("Cannot resolve Maven path: " + path);
        return Canonical(target.FullName);
    }

    internal static string Relative(string root, string path) => Path.GetRelativePath(Canonical(root), Canonical(path)).Replace('\\', '/');

    internal static string ProjectRelative(string root, string path)
    {
        var relative = Relative(root, path);
        if (Path.IsPathRooted(relative) || IsParentRelative(relative))
            throw new InvalidOperationException("Maven source/project path is outside the project root: " + path);
        return relative;
    }

    private static bool IsParentRelative(string path) => path == ".." || path.StartsWith("../", StringComparison.Ordinal);
}
