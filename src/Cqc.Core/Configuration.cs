using System.Text.Json;

namespace Cqc.Core;

public static class Configuration
{
    internal const string ProjectFileName = "config.json";
    private static readonly JsonSerializerOptions ReadOptions = new(Wire.Json)
    {
        Converters = { new HotspotThresholdsReader() }
    };

    public static (QualityConfig Config, string Fingerprint) Load(string root, string? path)
    {
        var config = path is null ? LoadProject(new DirectoryInfo(root))
            : Read<QualityConfig>(new FileInfo(Path.GetFullPath(path, root)), ConfigSource.Explicit)!;
        if (!ThresholdsValid(config))
            throw new ArgumentException("Invalid quality threshold.");
        if (!PolicyValid(config))
            throw new ArgumentException("Invalid component policy. Declare components before referring to them.");
        config = config with
        {
            Components = new SortedDictionary<string, string[]>(config.Components, StringComparer.Ordinal),
            AllowedDependencies = new SortedDictionary<string, string[]>(config.AllowedDependencies, StringComparer.Ordinal)
        };
        return (config, Wire.Hash(JsonSerializer.Serialize(config, Wire.Json)));
    }

    private static QualityConfig LoadProject(DirectoryInfo root)
    {
        var general = Read<QualityConfig>(new FileInfo(Path.Combine(root.FullName, ".cqc.json")), ConfigSource.Legacy) ?? new();
        var hotspots = Read<ProjectConfig>(ProjectFile(CqcDirectory.Locate(root.FullName)), ConfigSource.Project);
        return hotspots is null ? general : general with { Hotspots = hotspots.Hotspots };
    }

    private enum ConfigSource { Legacy, Project, Explicit }

    private static T? Read<T>(FileInfo file, ConfigSource source) where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Configuration must be an object.");
            if (source == ConfigSource.Legacy && document.RootElement.EnumerateObject().Any(property =>
                property.Name.Equals("hotspots", StringComparison.OrdinalIgnoreCase)))
                throw new JsonException("Set 'hotspots' in .cqc/config.json or pass this file as configPath.");
            return document.RootElement.Deserialize<T>(ReadOptions)!;
        }
        catch (FileNotFoundException) when (source != ConfigSource.Explicit) { return null; }
        catch (DirectoryNotFoundException) when (source != ConfigSource.Explicit) { return null; }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"Configuration '{file}': {exception.Message}", exception);
        }
    }

    private static FileInfo ProjectFile(DirectoryInfo directory) => new(Path.Combine(directory.FullName, ProjectFileName));

    internal static async Task CreateProjectDefaultsAsync(DirectoryInfo root, CancellationToken token)
    {
        var directory = CqcDirectory.Locate(root.FullName);
        CqcDirectory.EnsureSafe(directory);
        var file = ProjectFile(directory).FullName;
        if (File.Exists(file)) return;

        var staged = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, new ProjectConfig(), Wire.Json, token);
            token.ThrowIfCancellationRequested();
            try { File.Move(staged, file, overwrite: false); }
            catch (IOException) when (File.Exists(file)) { }
        }
        finally { File.Delete(staged); }
    }

    private sealed record ProjectConfig
    {
        public HotspotThresholds Hotspots { get; init; } = HotspotThresholds.Defaults;
    }

    private static bool ThresholdsValid(QualityConfig config)
    {
        var positive = new[] { config.ComplexityThreshold, config.TestLinesThreshold, config.DuplicateMinLines, config.DuplicateMinNodes };
        return positive.All(x => x >= 1) && InRange(config.CrapThreshold, 0, double.MaxValue)
            && InRange(config.DuplicateSimilarity, 0, 1);
    }

    private static bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool PolicyValid(QualityConfig config)
    {
        if (new object?[] { config.Components, config.AllowedDependencies, config.ForbiddenDependencies, config.Exclude }.Any(x => x is null)) return false;
        return config.Components.All(ValidComponent)
            && config.AllowedDependencies.All(entry => ValidAllowed(config, entry))
            && config.ForbiddenDependencies.All(x => ValidForbidden(config, x))
            && config.Exclude.All(x => !string.IsNullOrWhiteSpace(x));
    }

    private static bool ValidComponent(KeyValuePair<string, string[]> component) => !string.IsNullOrWhiteSpace(component.Key)
        && component.Value is not null && component.Value.All(pattern => !string.IsNullOrWhiteSpace(pattern));
    private static bool ValidAllowed(QualityConfig config, KeyValuePair<string, string[]> policy) => config.Components.ContainsKey(policy.Key)
        && policy.Value is not null && policy.Value.All(component => component == "*" || config.Components.ContainsKey(component));
    private static bool ValidForbidden(QualityConfig config, string[] pair) => pair is { Length: 2 } && pair.All(config.Components.ContainsKey);
}
