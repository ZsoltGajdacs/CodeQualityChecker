using System.IO.Enumeration;

namespace Cqc.Core;

internal static class ComponentAssignment
{
    internal static string Resolve(TypeFact type, QualityConfig config)
    {
        foreach (var component in config.Components)
            if (component.Value.Any(pattern => Matches(Name(type, pattern.Contains(':')), pattern))) return component.Key;
        var scope = type.NamespaceName switch
        {
            null => DefaultScope(Name(type, qualified: false)),
            "" => "<global>",
            var declared => declared
        };
        return type.Language + ":" + scope;
    }

    private static string DefaultScope(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0 ? "<global>" : name[..separator];
    }

    private static string Name(TypeFact type, bool qualified)
    {
        if (type.QualifiedName is { } name) return qualified ? type.Language + ":" + name : name;
        if (qualified) return type.Id;
        var separator = type.Id.IndexOf(':');
        return separator < 0 ? type.Id : type.Id[(separator + 1)..];
    }

    private static bool Matches(string name, string pattern) => pattern.Contains('*') || pattern.Contains('?')
        ? FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: false)
        : name == pattern || name.StartsWith(pattern + ".", StringComparison.Ordinal);
}
