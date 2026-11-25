namespace Cqc.Java;

internal sealed record CollectedMavenModule(MavenMetadata Metadata, string Output)
{
    internal string? LocalCoverage => OwnedFile(Metadata.Coverage?.Report, "jacoco.xml");
    internal string? ExecutionData => OwnedFile(Metadata.Coverage?.Data, "jacoco.exec");

    internal static bool IsRegularOutput(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private string? OwnedFile(string? reported, string name)
    {
        if (reported is null || !Path.IsPathFullyQualified(reported)) return null;
        var path = Path.Combine(Output, name);
        return IsRegularOutput(path) && MavenPaths.Canonical(reported) == MavenPaths.Canonical(path) ? path : null;
    }
}
