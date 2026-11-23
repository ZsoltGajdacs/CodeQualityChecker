using System.Globalization;
using Cqc.Core;

namespace Cqc.CSharp;

internal sealed record OpenCoverClassContext(string? Project, IReadOnlySet<string> Signatures);

internal sealed class OpenCoverMethodIdentity(string nativeSignature, string? file, OpenCoverClassContext context)
{
    private readonly string signature = NormalizeArity(nativeSignature);
    internal string NativeSignature => nativeSignature;
    internal string? File => file;

    internal bool Owns(MethodFact method) => context.Project is null || method.ContextId == "csharp:" + context.Project;

    internal bool Matches(MethodFact method)
    {
        var candidate = method.CoverageSignature ?? Unqualify(method.Id);
        return candidate == signature || !context.Signatures.Contains(candidate) && EraseArity(candidate) == signature;
    }

    internal static string NormalizeArity(string member)
    {
        var parameters = member.IndexOf('(');
        if (parameters <= 0 || member[parameters - 1] != '>') return member;
        var genericStart = member.LastIndexOf('<', parameters - 1);
        if (genericStart <= 0) return member;
        var arguments = member[(genericStart + 1)..(parameters - 1)];
        var arity = arguments.Count(character => character == ',') + 1;
        return member[..genericStart] + "``" + arity.ToString(CultureInfo.InvariantCulture) + member[parameters..];
    }

    private static string EraseArity(string signature)
    {
        var parameters = signature.IndexOf('(');
        if (parameters <= 0) return signature;
        var marker = signature.LastIndexOf("``", parameters - 1, StringComparison.Ordinal);
        if (marker < 0) return signature;
        var arity = signature.AsSpan(marker + 2, parameters - marker - 2);
        return int.TryParse(arity, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? signature[..marker] + signature[parameters..] : signature;
    }

    private static string Unqualify(string id) => id.StartsWith("csharp:", StringComparison.Ordinal) ? id["csharp:".Length..] : id;
}
