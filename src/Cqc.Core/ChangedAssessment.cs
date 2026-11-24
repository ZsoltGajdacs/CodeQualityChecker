namespace Cqc.Core;

/// <summary>Projects completed rule evidence onto touched declarations before entity scoring.</summary>
internal sealed class ChangedAssessment
{
    private readonly FamilyAssessment assessment;
    private readonly ILookup<string, TypePart> partsByType;
    private readonly ILookup<string, TypePart> partsByFile;
    private readonly Dictionary<string, FindingOrigin[]> origins;

    private sealed record TypePart(TypeFact[] Declarations, ScoringContribution[] Contributions)
    {
        internal TypeFact Type => Declarations[0];
    }

    internal ChangedAssessment(FamilyAssessment assessment, ChangedCodeScope scope, ChangedFindings findingScope)
    {
        this.assessment = assessment;
        var contributions = assessment.Contributions.Where(entry => scope.Includes(entry.Location))
            .ToLookup(entry => (entry.TypeId, entry.Location.File));
        var parts = assessment.Types.GroupBy(type => (type.Id, type.Location.File))
            .Select(group => new TypePart(group.ToArray(), contributions[group.Key].ToArray()))
            .Where(part => Applies(part, scope)).ToArray();
        partsByType = parts.ToLookup(part => part.Type.Id, StringComparer.Ordinal);
        partsByFile = parts.ToLookup(part => part.Type.Location.File, StringComparer.Ordinal);
        Types = parts.Select(part => part.Type).ToArray();
        var findings = findingScope.Select(assessment).DistinctBy(entry => entry.Finding.Id).ToArray();
        Findings = findings.Select(entry => entry.Finding).ToArray();
        origins = findings.ToDictionary(entry => entry.Finding.Id, entry => entry.Origins, StringComparer.Ordinal);
    }

    internal TypeFact[] Types { get; }
    internal Finding[] Findings { get; }

    internal FindingOrigin[] Origins(Finding finding) => origins[finding.Id];

    private bool Applies(TypePart part, ChangedCodeScope scope)
    {
        if (part.Contributions.Length != 0) return true;
        if (assessment.Family is "duplication" or "dependencies") return scope.IsWholeFile(part.Type.Location.File);
        return part.Declarations.Any(type => scope.Includes(type.Location) && assessment.SharedStaticBlockers(type).Length != 0);
    }

    internal EntityScoringEvidence For(EntityAssessment entity)
    {
        var byFile = entity.Kind == "file";
        var parts = byFile ? partsByFile[entity.File] : partsByType[entity.Id];
        var entries = parts.SelectMany(part => part.Contributions).ToArray();
        var declarations = parts.SelectMany(part => part.Declarations);
        return new(
            entries.Any(entry => entry.Primary) || parts.Any(PrimaryWithoutContribution),
            entries.Any(entry => entry.Static) || (assessment.StaticBlockers is not null && parts.Any(part => part.Contributions.Length == 0)),
            entries.Where(entry => entry.Primary).SelectMany(entry => entry.Blockers).Concat(declarations.SelectMany(assessment.SharedBlockers)),
            entries.Where(entry => entry.Static).SelectMany(entry => entry.StaticBlockers).Concat(declarations.SelectMany(assessment.SharedStaticBlockers)),
            Metrics(parts, entity, byFile));
    }

    private bool PrimaryWithoutContribution(TypePart part) =>
        part.Contributions.Length == 0 && assessment.PrimaryScoreApplies(part.Type.Id);

    private SortedDictionary<string, double?> Metrics(IEnumerable<TypePart> parts, EntityAssessment entity, bool byFile)
    {
        var metrics = RuleContext.Metrics();
        foreach (var group in parts.GroupBy(part => part.Type.Id, StringComparer.Ordinal))
        {
            var prefix = byFile ? $"class:{group.Key}:" : "";
            var entries = group.SelectMany(part => part.Contributions).ToArray();
            if (assessment.Family == "duplication")
            {
                metrics[prefix + "eligibleMethods"] = entries.Count(entry => entry.DuplicateCandidate == DuplicateCandidateKind.Method);
                metrics[prefix + "eligibleExecutables"] = entries.Count(entry => entry.DuplicateCandidate is not null);
                metrics[prefix + "duplicateGroups"] = entity.Findings.Count(finding => Origins(finding).Any(origin => origin.TypeId == group.Key
                    && (!byFile || origin.Location.File == entity.File)));
            }
            foreach (var (name, value) in entries.SelectMany(entry => entry.Metrics))
                metrics.TryAdd(prefix + name, value);
        }
        return metrics;
    }
}
