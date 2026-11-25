namespace Cqc.Java;

internal sealed record JvmMethodSignature(string Type, string Member, string[] Parameters)
{
    internal string Key => Type + "." + Member + "(" + string.Join(',', Parameters) + ")";

    internal JvmMethodSignature Legacy() => this with
    {
        Type = Type.Replace('$', '.'),
        Parameters = Parameters.Select(parameter => parameter.Replace('$', '.')).ToArray()
    };

    internal JvmMethodSignature? WithoutEnclosingInstance()
    {
        if (Member != ".ctor" || Parameters.Length == 0) return null;
        var separator = Type.LastIndexOf('$');
        return separator >= 0 && Parameters[0] == Type[..separator] ? this with { Parameters = Parameters[1..] } : null;
    }
}
