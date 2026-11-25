using System.ComponentModel;
using System.Text.Json.Serialization;
using Cqc.Core;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HotspotRequest
{
    public required string ProjectRoot { get; init; }
    [Description("Select whole files using repository (default), paths, or changed scope.")]
    public string Scope { get; init; } = "repository";
    public string[] Paths { get; init; } = [];
    [Description("Optional comparison base for changed-file selection. Hotspot history always starts at HEAD.")]
    public string? GitBase { get; init; }
    public string? ConfigPath { get; init; }
    public int TimeoutSeconds { get; init; } = 300;

    internal AnalysisRequest ToAnalysisRequest() => new()
    {
        ProjectRoot = ProjectRoot, Scope = Scope, Paths = Paths, GitBase = GitBase,
        ConfigPath = ConfigPath, TimeoutSeconds = TimeoutSeconds
    };
}
