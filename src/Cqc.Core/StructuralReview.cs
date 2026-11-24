namespace Cqc.Core;

internal static class StructuralReview
{
    internal static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var complexity = new StaticComplexity(context);
        var types = context.Facts.Types.Where(type => !type.IsTest).ToArray();
        var blockers = types.GroupBy(type => type.Id, StringComparer.Ordinal).ToDictionary(group => group.Key,
            group => group.Any(type => complexity.DeclarationBlockers(type).Length != 0), StringComparer.Ordinal);
        var assessed = types.Where(type => complexity.HasExecutables(type.Id) || blockers[type.Id]).ToArray();
        var findings = assessed.Select(type => type.Id).Distinct(StringComparer.Ordinal).SelectMany(id => complexity.Findings(id, config));
        var unavailable = context.Structure.UnavailableSelectedFiles;
        var report = context.Finish(new()
        {
            Family = "code", Types = assessed, Findings = findings, Incomplete = unavailable.Count != 0,
            Contributions = complexity.Contributions(sourceOnly: true),
            IncludeTouchedDeclaration = (type, scope) => scope.Includes(type.Location) && complexity.DeclarationBlockers(type).Length != 0,
            Aggregate = selected => selected.Assess(staticScore: true,
                declarationBlockers: selected.Declarations.SelectMany(complexity.DeclarationBlockers),
                staticDeclarationBlockers: selected.Declarations.SelectMany(complexity.DeclarationBlockers))
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
