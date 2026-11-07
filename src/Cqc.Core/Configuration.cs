using System.Text.Json;

namespace Cqc.Core;

public static class Configuration
{
    public static (QualityConfig Config, string Fingerprint) Load(string root, string? path)
    {
        var file = path is null ? Path.Combine(root, ".cqc.json") : Path.GetFullPath(path, root);
        if (path is not null && !File.Exists(file)) throw new ArgumentException("Configuration file does not exist.");
        var config = File.Exists(file)
            ? JsonSerializer.Deserialize<QualityConfig>(File.ReadAllText(file), Wire.Json)
              ?? throw new ArgumentException("Configuration must be an object.")
            : new QualityConfig();
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
