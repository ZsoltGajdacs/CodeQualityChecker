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
        return info.LinkTarget is null ? resolved : info.ResolveLinkTarget(true)?.FullName
            ?? throw new InvalidOperationException("Cannot resolve Maven path: " + path);
    }

    internal static string Relative(string root, string path) => Path.GetRelativePath(Canonical(root), Canonical(path)).Replace('\\', '/');
}
