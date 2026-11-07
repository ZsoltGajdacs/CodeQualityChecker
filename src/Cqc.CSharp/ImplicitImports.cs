using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cqc.CSharp;

internal static class ImplicitImports
{
    private const string Imports = "global using System; global using System.Collections.Generic; global using System.IO; " +
        "global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;";

    public static SyntaxTree Create(CSharpParseOptions options) => CSharpSyntaxTree.ParseText(Imports, options, path: "__cqc_implicit_imports.cs");
}
