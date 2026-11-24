namespace Cqc.Core;

internal static class StructuralReview
{
    internal static FamilyResult[] Evaluate(RuleContext context, QualityConfig config) =>
        new[] { Complexity(context, config), DuplicationRules.Evaluate(context, config) }
            .Where(family => family.Status != "notApplicable").ToArray();

    private static FamilyResult Complexity(RuleContext context, QualityConfig config)
    {
        var complexity = new StaticComplexity(context);
        var types = context.Facts.Types.Where(type => !type.IsTest).ToArray();
        var blockers = types.GroupBy(type => type.Id, StringComparer.Ordinal).ToDictionary(group => group.Key,
            group => group.SelectMany(complexity.Blockers).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var assessed = types.Where(type => complexity.HasExecutables(type.Id) || blockers[type.Id].Length != 0).ToArray();
        var findings = assessed.Select(type => type.Id).Distinct(StringComparer.Ordinal).SelectMany(id => complexity.Findings(id, config));
        var unavailable = context.Structure.UnavailableSelectedFiles;
        var report = context.Finish(new()
        {
            Family = "code", Types = assessed, Findings = findings, Incomplete = unavailable.Count != 0,
            Blockers = id => blockers[id], StaticBlockers = id => blockers[id],
            Metrics = id => complexity.Metrics(id)
        });
        return WithFileCounts(report, unavailable);
    }

    private static FamilyResult WithFileCounts(FamilyResult report, HashSet<string> unavailable)
    {
        var files = report.Entities.Where(entity => entity.Kind == "file").ToArray();
        var unscored = files.Where(entity => entity.StaticComplexity?.Score is null).Select(entity => entity.File);
        return report with
        {
            Entities = report.Entities.Select(entity => entity with { Score = null }).ToArray(),
            ScoredFileCount = files.Count(entity => entity.StaticComplexity?.Score is not null),
            UnscoredFileCount = unscored.Union(unavailable, StringComparer.Ordinal).Count()
        };
    }

}
