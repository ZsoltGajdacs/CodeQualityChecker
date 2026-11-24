using Microsoft.CodeAnalysis;

namespace Cqc.CSharp;

internal static class DuplicationLineCount
{
    public static int Measure(SyntaxNode? body)
    {
        if (body is null) return 0;

        var sourceLines = body.SyntaxTree.GetText().Lines;
        var codeLines = new HashSet<int>();
        foreach (var token in MethodDeclarations.BodyCodeTokens(body))
        {
            int line = sourceLines.GetLineFromPosition(token.SpanStart).LineNumber;
            foreach (var part in token.Text.AsSpan().EnumerateLines())
            {
                if (!part.IsWhiteSpace()) codeLines.Add(line);
                line++;
            }
        }
        return codeLines.Count;
    }
}
