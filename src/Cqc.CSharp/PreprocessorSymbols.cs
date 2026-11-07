using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

internal static class PreprocessorSymbols
{
    public static string DefaultLanguage(Dictionary<string, string> properties) =>
        TryMajor(properties.GetValueOrDefault("TargetFramework", "net10.0"), out var major) ? (major + 4).ToString(System.Globalization.CultureInfo.InvariantCulture) : "latest";

    public static string[] Read(Dictionary<string, string> properties, List<Diagnostic> diagnostics, string file)
    {
        var constants = properties.GetValueOrDefault("DefineConstants", "DEBUG;TRACE").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = constants.Where(SyntaxFacts.IsValidIdentifier).ToHashSet(StringComparer.Ordinal);
        if (valid.Count != constants.Distinct(StringComparer.Ordinal).Count())
            diagnostics.Add(new("csharp.compile_configuration", "DefineConstants contains an unsupported identifier or expression.", file, "csharp"));
        if (!properties.GetValueOrDefault("DisableImplicitConfigurationDefines", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) valid.Add("DEBUG");
        if (properties.ContainsKey("TargetFrameworks"))
            diagnostics.Add(new("csharp.compile_configuration", "Multiple target frameworks require separate evaluated compilation contexts.", file, "csharp"));
        if (properties.GetValueOrDefault("DisableImplicitFrameworkDefines", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) return valid.Order(StringComparer.Ordinal).ToArray();
        var target = properties.GetValueOrDefault("TargetFramework", "net10.0");
        if (!TryMajor(target, out var major))
        {
            diagnostics.Add(new("csharp.compile_configuration", "Cannot statically determine framework symbols for " + target + ".", file, "csharp"));
            return valid.Order(StringComparer.Ordinal).ToArray();
        }
        AddFramework(valid, major);
        return valid.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool TryMajor(string target, out int major)
    {
        major = 0;
        return target.StartsWith("net", StringComparison.Ordinal) && target.EndsWith(".0", StringComparison.Ordinal) &&
            int.TryParse(target[3..^2], out major) && major >= 5;
    }

    private static void AddFramework(HashSet<string> symbols, int major)
    {
        symbols.UnionWith(["NET", "NETCOREAPP", $"NET{major}_0", "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER", "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER"]);
        for (var version = 5; version <= major; version++) symbols.Add($"NET{version}_0_OR_GREATER");
    }
}
