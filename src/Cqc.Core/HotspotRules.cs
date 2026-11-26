namespace Cqc.Core;

internal static class HotspotRules
{
    internal static FamilyResult Evaluate(RuleContext context, GitHotspotEvidence? history, HotspotThresholds thresholds)
    {
        var selected = context.Snapshot.SelectedFiles.ToHashSet(StringComparer.Ordinal);
        var classes = context.Facts.ClassDeclarations.GroupBy(declaration => declaration.Id, StringComparer.Ordinal)
            .Where(group => group.Any(declaration => selected.Contains(declaration.Location.File)))
            .ToDictionary(group => group.Key, group => Measure(group, history, thresholds), StringComparer.Ordinal);
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
                return new(true, false, item.Blockers, [], item.Metrics());
            },
            CalculatePrimaryScore = entity => entity.TypeIds.Min(id => classes[id].Score)
        });
    }

    private static bool IncludesSyntaxGap(SyntaxGap gap) => gap.UnknownEntities && gap.Code != "csharp.unmodeled_executable";

    private static Measurement Measure(IEnumerable<ClassDeclarationFact> parts, GitHotspotEvidence? history,
        HotspotThresholds thresholds)
    {
        var declarations = parts.Distinct().ToArray();
        var locations = declarations.Select(declaration => declaration.Location).Distinct().ToArray();
        var lines = locations.Sum(location => location.EndLine - location.StartLine + 1);
        var parameters = declarations.Max(declaration => declaration.ConstructorParameters);
        int? edits = history is { Window: not null, Diagnostic: null } available ? EditCount(locations, available.Edits) : null;
        var blockers = AssessmentEvidence.Reasons(
            (declarations.All(declaration => declaration.Complete), "syntax.incomplete"),
            (edits.HasValue, "hotspots.history_unavailable"));
        var signals = new HotspotSignals(edits, parameters, lines);
        return new(declarations, blockers, signals, CalculatePenalties(signals, thresholds));
    }

    private static int EditCount(Location[] locations, IReadOnlyDictionary<string, HashSet<string>> edits)
    {
        var commits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in locations.Select(location => location.File).Distinct(StringComparer.Ordinal))
            if (edits.TryGetValue(path, out var fileEdits)) commits.UnionWith(fileEdits);
        return commits.Count;
    }

    private static HotspotPenalties CalculatePenalties(HotspotSignals signals, HotspotThresholds thresholds) => new(
        signals.Edits is { } edits ? Penalty(edits, thresholds.EditCommits, 5) : null,
        Penalty(signals.Parameters, thresholds.ConstructorParameters, 3),
        Penalty(signals.Lines, thresholds.ClassLines, 2));

    private static decimal Penalty(int value, HotspotRange range, int cap) =>
        cap * Math.Clamp(((decimal)value - range.Lower) / (range.Upper - range.Lower), 0, 1);

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

    private sealed record Measurement(ClassDeclarationFact[] Declarations, string[] Blockers,
        HotspotSignals Signals, HotspotPenalties Penalties)
    {
        internal double Score { get; } = (double)Math.Round(10 - (Penalties.Edits ?? 0) - Penalties.Constructor - Penalties.Length,
            2, MidpointRounding.AwayFromZero);

        internal SortedDictionary<string, double?> Metrics() => new(StringComparer.Ordinal)
        {
            ["editCommits"] = Signals.Edits,
            ["constructorParameters"] = Signals.Parameters,
            ["classLines"] = Signals.Lines,
            ["editPenalty"] = (double?)Penalties.Edits,
            ["constructorPenalty"] = (double)Penalties.Constructor,
            ["lengthPenalty"] = (double)Penalties.Length
        };
    }

    private sealed record HotspotSignals(int? Edits, int Parameters, int Lines);
    private sealed record HotspotPenalties(decimal? Edits, decimal Constructor, decimal Length);
}
