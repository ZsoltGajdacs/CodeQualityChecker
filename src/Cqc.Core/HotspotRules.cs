namespace Cqc.Core;

internal static class HotspotRules
{
    internal static FamilyResult Evaluate(RuleContext context, GitHotspotEvidence? history)
    {
        var selected = context.Snapshot.SelectedFiles.ToHashSet(StringComparer.Ordinal);
        var classes = context.Facts.ClassDeclarations.GroupBy(declaration => declaration.Id, StringComparer.Ordinal)
            .Where(group => group.Any(declaration => selected.Contains(declaration.Location.File)))
            .ToDictionary(group => group.Key, group => Measure(group, history), StringComparer.Ordinal);
        var diagnostics = history?.Diagnostic is { } failure ? new[] { failure } : [];
        var unknownClasses = context.Facts.SyntaxGaps.Any(gap => selected.Contains(gap.File) && IncludesSyntaxGap(gap));
        var analyzerFailed = context.Facts.Diagnostics.Any(StructuralEvidence.IsAnalyzerFailure);

        return context.Finish(new()
        {
            Family = "hotspots",
            Types = classes.Values.SelectMany(assessment => assessment.Declarations)
                .Select(declaration => new TypeFact(declaration.Id, declaration.Language, declaration.Location, false)),
            Findings = classes.Select(entry => Finding(entry.Key, entry.Value)).OfType<Finding>(),
            Diagnostics = diagnostics,
            Incomplete = unknownClasses || analyzerFailed || (diagnostics.Length != 0 && classes.Count != 0),
            IncludesSyntaxGap = IncludesSyntaxGap,
            IncludeTouchedDeclaration = (type, scope) => scope.Includes(type.Location),
            Aggregate = evidence =>
            {
                var item = classes[evidence.TypeId];
                return new(true, false, item.Blockers, [], item.Metrics);
            },
            CalculatePrimaryScore = entity => entity.TypeIds.Min(id => classes[id].Score)
        });
    }

    private static bool IncludesSyntaxGap(SyntaxGap gap) => gap.UnknownEntities && gap.Code != "csharp.unmodeled_executable";

    private static Measurement Measure(IEnumerable<ClassDeclarationFact> parts, GitHotspotEvidence? history)
    {
        var declarations = parts.Distinct().ToArray();
        var locations = declarations.Select(declaration => declaration.Location).Distinct().ToArray();
        var lines = locations.Sum(location => location.EndLine - location.StartLine + 1);
        var parameters = declarations.Max(declaration => declaration.ConstructorParameters);
        int? edits = history is { Window: not null, Diagnostic: null } available ? EditCount(locations, available.Edits) : null;
        var blockers = AssessmentEvidence.Reasons(
            (declarations.All(declaration => declaration.Complete), "syntax.incomplete"),
            (edits.HasValue, "hotspots.history_unavailable"));
        return new(declarations, blockers, new(edits, parameters, lines));
    }

    private static int EditCount(Location[] locations, IReadOnlyDictionary<string, HashSet<string>> edits)
    {
        var commits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in locations.Select(location => location.File).Distinct(StringComparer.Ordinal))
            if (edits.TryGetValue(path, out var fileEdits)) commits.UnionWith(fileEdits);
        return commits.Count;
    }

    private static decimal Penalty(int value, int lower, int upper, int cap) =>
        cap * Math.Clamp((decimal)(value - lower) / (upper - lower), 0, 1);

    private static Finding? Finding(string id, Measurement assessment)
    {
        if (assessment.Blockers.Length != 0 || assessment.Score >= 7) return null;
        var signals = assessment.Signals;
        return RuleContext.Finding(new()
        {
            Rule = "hotspots.change",
            Key = id,
            Message = FormattableString.Invariant($"Change hotspot score {assessment.Score:0.##}/10 from {signals.Edits} edit commits, {signals.Parameters} constructor parameters and {signals.Lines} class lines."),
            Advice = "Review recent edits and look for responsibilities that can change independently.",
            Entities = [id],
            Locations = assessment.Declarations.Select(declaration => declaration.Location),
            Deduction = 0
        });
    }

    private sealed record Measurement(ClassDeclarationFact[] Declarations, string[] Blockers, HotspotSignals Signals)
    {
        internal double Score => Signals.Score;
        internal SortedDictionary<string, double?> Metrics => Signals.Metrics;
    }

    private sealed record HotspotSignals(int? Edits, int Parameters, int Lines)
    {
        private decimal? EditPenalty => Edits is { } count ? Penalty(count, 10, 30, 5) : null;
        private decimal ConstructorPenalty => Penalty(Parameters, 4, 10, 3);
        private decimal LengthPenalty => Penalty(Lines, 400, 800, 2);
        internal double Score => (double)Math.Round(10 - (EditPenalty ?? 0) - ConstructorPenalty - LengthPenalty,
            2, MidpointRounding.AwayFromZero);
        internal SortedDictionary<string, double?> Metrics => new(StringComparer.Ordinal)
        {
            ["editCommits"] = Edits,
            ["constructorParameters"] = Parameters,
            ["classLines"] = Lines,
            ["editPenalty"] = (double?)EditPenalty,
            ["constructorPenalty"] = (double)ConstructorPenalty,
            ["lengthPenalty"] = (double)LengthPenalty
        };
    }
}
