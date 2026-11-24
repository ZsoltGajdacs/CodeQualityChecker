package cqc;

import com.github.javaparser.JavaToken;
import com.github.javaparser.Range;
import java.util.Comparator;
import java.util.HashSet;
import java.util.Set;
import static com.github.javaparser.GeneratedJavaParserConstants.LBRACE;
import static com.github.javaparser.GeneratedJavaParserConstants.RBRACE;

/** Counts occupied source lines from the tokens owned by one executable. */
final class DuplicationLines {
    static int count(ExecutableDiscovery.Body body) {
        return body.root().getTokenRange().map(tokens -> {
            var lines = new HashSet<Integer>();
            var excluded = body.excludedRanges().stream().sorted(Comparator.comparing(range -> range.begin)).toList();
            int next = 0;
            for (var token : tokens) {
                if (!isCode(token)) continue;
                var range = token.getRange().orElseThrow();
                while (next < excluded.size() && excluded.get(next).end.isBefore(range.begin)) next++;
                if (next == excluded.size() || !excluded.get(next).contains(range)) addLines(token, range, lines);
            }
            return lines.size();
        }).orElse(0);
    }

    private static boolean isCode(JavaToken token) {
        return !token.getCategory().isWhitespaceOrComment() && token.getKind() != LBRACE && token.getKind() != RBRACE;
    }

    private static void addLines(JavaToken token, Range range, Set<Integer> lines) {
        if (range.begin.line == range.end.line) {
            lines.add(range.begin.line);
            return;
        }
        var parts = token.getText().lines().toList();
        for (int index = 0; index < parts.size(); index++)
            if (!parts.get(index).isBlank()) lines.add(range.begin.line + index);
    }
}
