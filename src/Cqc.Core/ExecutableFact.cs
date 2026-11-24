namespace Cqc.Core;

public sealed record ExecutableFact
{
    public required string Id { get; init; }
    public required string TypeId { get; init; }
    public required string Language { get; init; }
    public required Location Location { get; init; }
    public string? Kind { get; init; }
    public int Complexity { get; init; }
    public string[] Fingerprints { get; init; } = [];
    public int NodeCount { get; init; }
    public int? DuplicationLines { get; init; }
}
