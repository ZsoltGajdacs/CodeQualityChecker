using System.Text.Json;

namespace Cqc.Core;

public static class EvidenceStore
{
    internal const string CoverageFileName = "coverage.json";

    public static async Task WriteJsonAsync<T>(DirectoryInfo directory, string name, T value, CancellationToken token)
    {
        var path = EvidencePath(directory, name);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, Wire.Json, token);
    }

    internal static async Task PublishRunAsync(string root, BuildArtifacts artifacts, CoverageEvidence coverage, CancellationToken token)
    {
        var names = artifacts.Results.Select(result => result.ReferenceManifestName).OfType<string>().ToList();
        var source = new DirectoryInfo(artifacts.RunDirectory);
        await WriteJsonAsync(source, CoverageFileName, coverage, token);
        names.Add(CoverageFileName);
        EnsureArtifactDirectory(root);
        var destination = new DirectoryInfo(Path.Combine(root, ".cqc"));
        await PublishAsync(source, destination, names, token);
    }

    private static async Task PublishAsync(DirectoryInfo source, DirectoryInfo destination, List<string> names, CancellationToken token)
    {
        var staged = new List<(string Temporary, string Target)>();
        try
        {
            foreach (var name in names)
            {
                var target = EvidencePath(destination, name);
                if (Directory.Exists(target)) throw new IOException("Evidence destination is a directory: " + target);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((temporary, target));
                await using var input = File.OpenRead(EvidencePath(source, name));
                await using var output = File.Create(temporary);
                await input.CopyToAsync(output, token);
            }
            token.ThrowIfCancellationRequested();
            foreach (var (temporary, target) in staged) File.Move(temporary, target, true);
        }
        finally { foreach (var (temporary, _) in staged) File.Delete(temporary); }
    }

    private static string EvidencePath(DirectoryInfo directory, string name)
    {
        if (name is "" or "." or ".." || Path.GetFileName(name) != name)
            throw new ArgumentException("Evidence name must be a simple filename.", nameof(name));
        return Path.Combine(directory.FullName, name);
    }

    public static CoverageEvidence Read(string json)
    {
        var evidence = JsonSerializer.Deserialize<CoverageEvidence>(json, Wire.Json)
            ?? throw new ArgumentException("Coverage evidence must be an object.");
        if (evidence.SchemaVersion != Wire.SchemaVersion) throw new ArgumentException("Coverage evidence schema must be 2. Refresh evidence with explicit execution.");
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
                throw new ArgumentException("The .cqc directory contains tracked files. Move tracked evidence before requesting execution.");
        }
        catch (System.ComponentModel.Win32Exception) { }
        EnsureArtifactDirectory(root);
    }

    private static bool Valid(MethodCoverage method) => method is not null && !string.IsNullOrWhiteSpace(method.MethodId)
        && ValidCounter(method.CoveredBranches, method.TotalBranches) && ValidCounter(method.CoveredLines, method.TotalLines);
    private static bool ValidCounter(int covered, int total) => covered >= 0 && total >= covered;
}
