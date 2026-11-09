using System.Diagnostics;
using System.Text.Json;
using Cqc.Core;

internal sealed class PerformanceSamples : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("cqc-perf-").FullName;
    private readonly string? previousPath = Environment.GetEnvironmentVariable("PATH");
    public List<object> Results { get; } = [];
    internal string DirectoryPath => directory;

    internal PerformanceSamples()
    {
        var wrapper = Path.Combine(directory, "git");
        File.WriteAllText(wrapper, "#!/bin/sh\nprintf 'git\\n' >> \"$CQC_PERF_GIT_LOG\"\nexec /usr/bin/git \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + previousPath);
        Environment.SetEnvironmentVariable("CQC_PERF_GIT_LOG", Path.Combine(directory, "git.log"));
    }

    internal async Task<T> Measure<T>(string name, Func<Task<T>> action)
    {
        await action();
        var measurements = new List<object>();
        T result = default!;
        string? expected = null;
        string? expectedJson = null;
        for (var run = 0; run < 5; run++)
        {
            File.WriteAllText(Path.Combine(directory, "git.log"), "");
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            result = await action();
            watch.Stop();
            var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
            var normalized = result is AnalysisFacts facts ? facts with
            { Diagnostics = facts.Diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.File, StringComparer.Ordinal)
                .ThenBy(d => d.Message, StringComparer.Ordinal).ToArray() } : (object?)result;
            var json = JsonSerializer.Serialize(normalized, Wire.Json);
            var hash = Wire.Hash(json);
            expected ??= hash;
            expectedJson ??= json;
            if (expected != hash)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "expected.json"), expectedJson);
                await File.WriteAllTextAsync(Path.Combine(directory, "actual.json"), json);
                throw new InvalidOperationException("Nondeterministic benchmark: " + name);
            }
            measurements.Add(new { Milliseconds = watch.Elapsed.TotalMilliseconds, AllocatedBytes = bytes,
                GitProcesses = File.ReadAllLines(Path.Combine(directory, "git.log")).Length,
                WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64 });
        }
        Results.Add(new { Name = name, Hash = expected, Samples = measurements });
        Console.Error.WriteLine(name);
        return result;
    }

    internal void Operations(string name, AnalysisFacts facts)
    {
        var sets = facts.Methods.Where(m => m.Location.EndLine - m.Location.StartLine + 1 >= 4 && m.NodeCount >= 20 && m.Fingerprints.Length > 0)
            .GroupBy(m => m.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        var matcher = typeof(QualityEngine).Assembly.GetType("Cqc.Core.DuplicateCandidates");
        var count = matcher?.GetMethod("CountComparisons", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var comparisons = count is null ? sets.GroupBy(m => m.Language).Sum(g => (long)g.Count() * (g.Count() - 1) / 2)
            : (long)count.Invoke(null, [sets, .82, CancellationToken.None])!;
        Results.Add(new { Name = name + ".operations", DuplicateComparisons = comparisons });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", previousPath);
        Environment.SetEnvironmentVariable("CQC_PERF_GIT_LOG", null);
        Directory.Delete(directory, true);
    }
}
