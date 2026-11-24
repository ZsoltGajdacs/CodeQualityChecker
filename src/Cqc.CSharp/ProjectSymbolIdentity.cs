using Microsoft.CodeAnalysis;

namespace Cqc.CSharp;

/// <summary>Names source symbols using their project of origin in this compilation.</summary>
internal sealed class ProjectSymbolIdentity
{
    private readonly Dictionary<IAssemblySymbol, string> owners = new(SymbolEqualityComparer.Default);
    private readonly string ownProject;
    private static readonly SymbolDisplayFormat Types = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

    public ProjectSymbolIdentity(string project, Compilation compilation, IEnumerable<KeyValuePair<MetadataReference, string>> references)
    {
        ownProject = project;
        owners.Add(compilation.Assembly, project);
        foreach (var reference in references)
            if (compilation.GetAssemblyOrModuleSymbol(reference.Key) is IAssemblySymbol assembly)
                owners.TryAdd(assembly, reference.Value);
    }

    public string OwnType(ITypeSymbol symbol) => Qualify(ownProject, TypeName(symbol));
    public string OwnMethod(IMethodSymbol symbol) => Qualify(ownProject, MethodName(symbol));
    public string? ReferencedType(ITypeSymbol symbol) =>
        ProjectOf(symbol) is { } project ? Qualify(project, TypeName(symbol)) : null;
    public string? ReferencedMethod(IMethodSymbol symbol) =>
        ProjectOf(Definition(symbol)) is { } project ? Qualify(project, MethodName(symbol)) : null;

    private string? ProjectOf(ISymbol symbol) =>
        symbol.ContainingAssembly is { } assembly ? owners.GetValueOrDefault(assembly) : null;

    private static string Qualify(string project, string name) => project == "." ? name : project + "#" + name;

    private static IMethodSymbol Definition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;
    public static string TypeName(ITypeSymbol symbol) => symbol.OriginalDefinition.ToDisplayString(Types);
    public static string NamespaceName(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";

    public static string MethodName(IMethodSymbol symbol)
    {
        symbol = Definition(symbol);
        var name = symbol.MethodKind switch
        {
            MethodKind.Constructor => ".ctor",
            MethodKind.StaticConstructor => ".cctor",
            _ => symbol.MetadataName
        };
        if (symbol.Arity > 0) name += "``" + symbol.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var owner = symbol.MethodKind == MethodKind.LocalFunction && symbol.ContainingSymbol is IMethodSymbol enclosing
            ? MethodName(enclosing) : TypeName(symbol.ContainingType);
        return $"{owner}.{name}({string.Join(",", symbol.Parameters.Select(ParameterName))})";
    }

    private static string ParameterName(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind == RefKind.None ? "" : parameter.RefKind.ToString().ToLowerInvariant() + " ";
        return modifier + parameter.Type.ToDisplayString(Types);
    }
}
