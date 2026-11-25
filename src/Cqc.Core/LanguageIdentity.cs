namespace Cqc.Core;

/// <summary>Separate identical qualified symbols in mixed-language repositories.</summary>
public static class LanguageIdentity
{
    public static AnalysisFacts Qualify(AnalysisFacts facts, string language)
    {
        string Id(string id) => language + ":" + id;
        return facts with
        {
            Types = facts.Types.Select(t => t with { Id = Id(t.Id) }).ToArray(),
            ClassDeclarations = facts.ClassDeclarations.Select(declaration => declaration with { Id = Id(declaration.Id) }).ToArray(),
            Methods = facts.Methods.Select(m => m with { Id = Id(m.Id), TypeId = Id(m.TypeId), Calls = m.Calls.Select(Id).ToArray(), CoverageSignature = m.CoverageSignature }).ToArray(),
            Executables = facts.Executables.Select(e => e with { Id = Id(e.Id), TypeId = Id(e.TypeId) }).ToArray(),
            SyntaxGaps = facts.SyntaxGaps.Select(g => g.TypeId is null ? g : g with { TypeId = Id(g.TypeId) }).ToArray(),
            Dependencies = facts.Dependencies.Select(d => d with { From = Id(d.From), To = Id(d.To) }).ToArray()
        };
    }
}
