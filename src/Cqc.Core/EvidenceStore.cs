using System.Text.Json;

namespace Cqc.Core;

public static class EvidenceStore
{
    internal const string CoverageFileName = "coverage.json";
    private static readonly char[] InvalidNameCharacters = [.. Path.GetInvalidFileNameChars(), '\\'];

    public static async Task WriteJsonAsync<T>(DirectoryInfo directory, string name, T value, CancellationToken token)
    {
        var path = EvidencePath(directory, name);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, Wire.Json, token);
    }

    internal static async Task PublishRunAsync(DirectoryInfo root, BuildArtifacts artifacts, CoverageEvidence coverage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var names = artifacts.Results.Select(result => result.ReferenceManifestName).OfType<string>()
            .Append(CoverageFileName).ToArray();
        var source = new DirectoryInfo(artifacts.RunDirectory);
        await WriteJsonAsync(source, CoverageFileName, coverage, token);
        var destination = CqcDirectory.Locate(root.FullName);
        CqcDirectory.EnsureSafe(destination);
        var target = EvidencePath(destination, EvidenceBundle.FileName);
        var staged = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await EvidenceBundle.WriteAsync(staged, source, names, token);
            await using (var output = File.OpenRead(staged))
                await EvidenceBundle.ReadAsync(output, destination: null, token);
            token.ThrowIfCancellationRequested();
            File.Move(staged, target, overwrite: true);
        }
        finally { File.Delete(staged); }
    }

    internal static async Task<EvidenceSnapshot> OpenAsync(DirectoryInfo root, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var legacy = CqcDirectory.Locate(root.FullName);
        try
        {
            await using var input = OpenPublishedBundle(legacy);
            return input is null ? new(legacy) : await MaterializeAsync(legacy, input, token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("evidence_invalid", "Saved evidence bundle could not be read: " + exception.Message));
            return new(legacy, Directory.CreateTempSubdirectory("cqc-evidence-"));
        }
    }

    private static FileStream? OpenPublishedBundle(DirectoryInfo directory)
    {
        try
        {
            // A reader keeps its original file handle if another process publishes a replacement.
            return new(EvidencePath(directory, EvidenceBundle.FileName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static async Task<EvidenceSnapshot> MaterializeAsync(DirectoryInfo legacy, Stream input, CancellationToken token)
    {
        var temporary = Directory.CreateTempSubdirectory("cqc-evidence-");
        try
        {
            await EvidenceBundle.ReadAsync(input, temporary, token);
            return new(legacy, temporary);
        }
        catch
        {
            temporary.Delete(recursive: true);
            throw;
        }
    }

    internal static bool IsEvidenceName(string name) => name is not ("" or "." or "..")
        && name.IndexOfAny(InvalidNameCharacters) < 0;

    internal static string EvidencePath(DirectoryInfo directory, string name)
    {
        if (!IsEvidenceName(name))
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

    public static void EnsureArtifactDirectory(string root) => CqcDirectory.EnsureSafe(CqcDirectory.Locate(root));

    public static Task EnsureWritableAsync(string root, CancellationToken token) => CqcDirectory.EnsureWritableAsync(root, token);

    private static bool Valid(MethodCoverage method) => method is not null && !string.IsNullOrWhiteSpace(method.MethodId)
        && ValidCounter(method.CoveredBranches, method.TotalBranches) && ValidCounter(method.CoveredLines, method.TotalLines);
    private static bool ValidCounter(int covered, int total) => covered >= 0 && total >= covered;
}
