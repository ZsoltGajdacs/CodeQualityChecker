using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cqc.Core;

public sealed record HotspotRange(int Lower, int Upper);

public sealed record HotspotThresholds
{
    internal static HotspotThresholds Defaults { get; } = new();

    public HotspotRange EditCommits { get; init; } = new(10, 30);
    public HotspotRange ConstructorParameters { get; init; } = new(7, 10);
    public HotspotRange ClassLines { get; init; } = new(400, 800);
}

internal sealed class HotspotThresholdsReader : JsonConverter<HotspotThresholds>
{
    public override bool HandleNull => true;

    public override HotspotThresholds Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var settings = JsonSerializer.Deserialize<Overrides>(ref reader, options)
            ?? throw new JsonException("Setting 'hotspots' must be an object.");
        var defaults = HotspotThresholds.Defaults;
        return new()
        {
            EditCommits = Resolve(settings.EditCommits, defaults.EditCommits, "hotspots.editCommits"),
            ConstructorParameters = Resolve(settings.ConstructorParameters, defaults.ConstructorParameters, "hotspots.constructorParameters"),
            ClassLines = Resolve(settings.ClassLines, defaults.ClassLines, "hotspots.classLines")
        };
    }

    public override void Write(Utf8JsonWriter writer, HotspotThresholds value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, Wire.Json);

    private static HotspotRange Resolve(Bounds settings, HotspotRange defaults, string key)
    {
        var lower = ReadBound(settings.Lower, defaults.Lower, key + ".lower");
        var upper = ReadBound(settings.Upper, defaults.Upper, key + ".upper");
        if (upper <= lower)
            throw new JsonException($"Setting '{key}.upper' must exceed '{key}.lower'.");
        return new(lower, upper);
    }

    private static int ReadBound(JsonElement setting, int fallback, string key) => setting.ValueKind switch
    {
        JsonValueKind.Undefined => fallback,
        JsonValueKind.Number when setting.TryGetInt32(out var bound) && bound >= 0 => bound,
        _ => throw new JsonException($"Setting '{key}' must be a non-negative integer.")
    };

    private sealed record Overrides
    {
        public Bounds EditCommits { get; init; }
        public Bounds ConstructorParameters { get; init; }
        public Bounds ClassLines { get; init; }
    }

    private readonly record struct Bounds(JsonElement Lower, JsonElement Upper);
}
