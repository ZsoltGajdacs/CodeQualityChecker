namespace Cqc.Core;

public static class ProjectPaths
{
    public static string ResolveDirectory(DirectoryInfo directory)
    {
        if (directory.LinkTarget is not null && directory.ResolveLinkTarget(true) is { } target)
            return ResolveDirectory(new DirectoryInfo(target.FullName));
        return directory.Parent is { } parent ? Path.Combine(ResolveDirectory(parent), directory.Name) : directory.FullName;
    }
}
