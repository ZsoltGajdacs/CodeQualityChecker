using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class PartialDuplicationTests
{
    private static readonly EvidenceState Verified = new(true, true, true, true, true, []);
    private static readonly string[] Fingerprints = ["match"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedMatchesSurviveGapsWithoutScoringTheAffectedLanguage(bool sourceOnly)
    {
        var selected = Type("Selected", "java", "Selected.java");
        var context = Type("Context", "java", "Context.java");
        var unavailable = Type("Unavailable", "java", "Unavailable.java") with { Evidence = Verified with { Syntax = false } };
        var otherLanguage = Type("Other", "csharp", "Other.cs");
        var facts = new AnalysisFacts([selected, context, unavailable, otherLanguage],
            [Method(context, Verified), Method(unavailable, Verified with { Syntax = false }), Method(otherLanguage, Verified)],
            [], [], false)
        {
            Executables = [new()
            {
                Id = "Selected#initializer", TypeId = selected.Id, Language = selected.Language, Location = selected.Location,
                Complexity = 1, Fingerprints = Fingerprints, NodeCount = 30
            }]
        };
        var family = Evaluate(facts, ["Selected.java", "Other.cs"], sourceOnly);
        var finding = Assert.Single(family.Findings);
        Assert.Equal(new[] { "Context.java", "Selected.java" }, finding.Locations.Select(location => location.File));
        Assert.Equal("incomplete", family.Status);
        var affected = Assert.Single(family.Entities, entity => entity.EntityId == selected.Id);
        Assert.Null(affected.Score);
        Assert.Equal(new[] { "structure.incomplete" }, affected.Blockers);
        Assert.Equal(0, affected.Metrics["eligibleMethods"]);
        Assert.Equal(1, affected.Metrics["eligibleExecutables"]);
        Assert.Equal(10, Assert.Single(family.Entities, entity => entity.EntityId == otherLanguage.Id).Score);
    }

    [Fact]
    public void ParserFailureRemainsVisibleWhenNoCandidateExists()
    {
        var facts = new AnalysisFacts([], [], [], [], false)
        {
            SyntaxGaps = [new("Broken.java", "java", null, "java.parse")]
        };
        var family = Evaluate(facts, ["Broken.java"], sourceOnly: true);
        Assert.Equal("incomplete", family.Status);
        Assert.Empty(family.Entities);
        Assert.Empty(family.Findings);
    }

    private static TypeFact Type(string id, string language, string file) =>
        new(id, language, new(file, 1, 15), false) { Evidence = Verified };

    private static MethodFact Method(TypeFact owner, EvidenceState evidence) =>
        new(owner.Id + ".work", owner.Id, owner.Language, owner.Location, 1, 15,
            false, false, false, 0, [], Fingerprints, 30) { Evidence = evidence };

    private static FamilyResult Evaluate(AnalysisFacts facts, string[] selected, bool sourceOnly)
    {
        var files = facts.Types.Select(type => type.Location.File).Concat(selected).Distinct().ToArray();
        var request = new EvaluationRequest
        {
            Facts = facts,
            Snapshot = new("/repo", files, selected, [], "source", []) { SourceOnly = sourceOnly },
            Families = ["duplication"],
            ConfigurationFingerprint = "config"
        };
        return Assert.Single(QualityEngine.Evaluate(request), family => family.Family == "duplication");
    }
}
