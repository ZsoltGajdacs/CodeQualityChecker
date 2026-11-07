using System.Text.Json;

namespace Cqc.Core;

public static class EvidenceStore
{
    public static CoverageEvidence Read(string json)
    {
        var evidence = JsonSerializer.Deserialize<CoverageEvidence>(json, Wire.Json)
            ?? throw new ArgumentException("Coverage evidence must be an object.");
        if (evidence.Methods is null || evidence.ToolVersions is null) throw new ArgumentException("Coverage methods and tool versions are required.");
        if (evidence.Methods.Any(m => !Valid(m))) throw new ArgumentException("Invalid coverage counters or method identity.");
        if (evidence.Methods.DistinctBy(m => m.MethodId).Count() != evidence.Methods.Length)
            throw new ArgumentException("Coverage evidence contains duplicate method identities.");
        return evidence;
    }

    public static void EnsureArtifactDirectory(string root)
    {
        var directory = Path.Combine(root, ".cqc");
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("The .cqc artifact directory must not be a symbolic link.");
        Directory.CreateDirectory(directory);
    }

    public static async Task EnsureWritableAsync(string root, CancellationToken token)
    {
        var directory = Path.Combine(root, ".cqc");
        if (!Directory.Exists(directory)) return;
        try
        {
            var tracked = await ProcessRunner.RunAsync("git", ["ls-files", "-z", "--", ".cqc"], root, token);
            if (tracked.ExitCode == 0 && tracked.Output.Length > 0)
                throw new ArgumentException("The .cqc directory contains tracked files. Move tracked artifacts before requesting execution; source/configuration files are never overwritten.");
        }
        catch (System.ComponentModel.Win32Exception) { }
        EnsureArtifactDirectory(root);
    }

    private static bool Valid(MethodCoverage method) => method is not null && !string.IsNullOrWhiteSpace(method.MethodId)
        && ValidCounter(method.CoveredBranches, method.TotalBranches) && ValidCounter(method.CoveredLines, method.TotalLines);
    private static bool ValidCounter(int covered, int total) => covered >= 0 && total >= covered;
}
