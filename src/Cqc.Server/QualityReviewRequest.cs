using System.ComponentModel;
using System.Text.Json.Serialization;
using Cqc.Core;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityReviewRequest
{
    public required string ProjectRoot { get; init; }
    [Description("When true, permits project-controlled restores, builds and tests in the checkout. Defaults to false.")]
    public bool Execute { get; init; }
    public string? ConfigPath { get; init; }
    public int TimeoutSeconds { get; init; } = 300;

    internal AnalysisRequest ForScope(string scope) => new()
    {
        ProjectRoot = ProjectRoot, Scope = scope, Execute = Execute, ConfigPath = ConfigPath, TimeoutSeconds = TimeoutSeconds
    };
}
