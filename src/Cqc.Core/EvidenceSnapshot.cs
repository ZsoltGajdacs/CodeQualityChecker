namespace Cqc.Core;

/// <summary>One saved evidence view, with request-owned files when backed by a bundle.</summary>
internal sealed class EvidenceSnapshot(DirectoryInfo legacyDirectory, DirectoryInfo? materialized = null) : IDisposable
{
    internal string? ReferenceDirectory => materialized?.FullName;
    internal string CoveragePath => EvidenceStore.EvidencePath(materialized ?? legacyDirectory, EvidenceStore.CoverageFileName);

    public void Dispose() => materialized?.Delete(recursive: true);
}
