# Literal scoring fixtures

These are deliberately simple, sometimes deliberately poor, **input projects** for deterministic scoring acceptance tests. They are not examples of recommended production code or recommended tests. Do not refactor repetition, remove empty tests, simplify branches, or rewrite the long increment sequences: those shapes are the measured input.

The Java Maven/JUnit 5 project and the C# `Scoring.Cases`/xUnit `Scoring.Tests` projects mirror each other. Dependency versions match the existing mixed-language corpus. All source files are committed as literal text; no test-time source generator creates these cases. The test harness and independently authored expected-result manifest live outside this directory.

## Production cases

Every `evaluate(int)` / `Evaluate(int)` method contains exactly the stated number of independent `if` decisions. The expected complexity is therefore `1 + if count`. The default complexity cutoff is 8: complexity 8 is accepted and complexity 9 receives one deduction. The coverage test class invokes only Healthy, AtComplexity8, OverComplexity9, AtComplexity10, and OverComplexity11, with -1 and 100; all of their branches are exercised. The risk-boundary classes are deliberately never called.

| Class | Independent ifs | Executable lines | Expected coverage | Hand calculation of CRAP | Expected production score |
| --- | ---: | ---: | --- | --- | ---: |
| Healthy | 0 | 1 | Full executable-line coverage | 1 | 10 |
| AtComplexity8 | 7 | 16 | All branches | 8 | 10 |
| OverComplexity9 | 8 | 18 | All branches | 9 | 9 |
| AtComplexity10 | 9 | 20 | All branches | 10 | 9 |
| OverComplexity11 | 10 | 22 | All branches | 11 | 9 |
| AtRisk30 | 4 | 10 | No branches covered | 5² + 5 = 30 | 10 |
| OverRisk42 | 5 | 12 | No branches covered | 6² + 6 = 42 | 9 |
| HighRisk156 | 11 | 24 | No branches covered | 12² + 12 = 156 | 8 |

Expected outputs of the called methods are literal assertions: Healthy(-1)=0, Healthy(100)=101; AtComplexity8(-1)=0, AtComplexity8(100)=7; OverComplexity9(-1)=0, OverComplexity9(100)=8; AtComplexity10(-1)=0, AtComplexity10(100)=9; OverComplexity11(-1)=0, OverComplexity11(100)=10. Coverage tests contain no test-owned branches or loops.

Production source witnesses, counted directly from the files:

| File/class | Java method range | C# method range |
| --- | --- | --- |
| Healthy | 4–6 | 5–8 |
| AtComplexity8 | 4–28 | 5–37 |
| OverComplexity9 | 4–31 | 5–41 |
| AtComplexity10 | 4–34 | 5–45 |
| OverComplexity11 | 4–37 | 5–49 |
| AtRisk30 | 4–19 | 5–25 |
| OverRisk42 | 4–22 | 5–29 |
| HighRisk156 | 4–40 | 5–53 |
| CloneLeft / CloneRight | 4–9 | 5–11 |

Java files are under `java/src/main/java/scoring/`; C# files are under `csharp/Scoring.Cases/`. Each class has its own file bearing the class name. Java package is `scoring`; C# namespace is `Scoring`.

## Test-size, assertion, and rollup cases

The table's method ranges include their test annotation/attribute. Test-owned executable-line counts come from the literal statements, excluding braces and method/attribute lines.

| Case | Literal structure | Executable lines | Java declaration range | C# declaration range | Expected test score |
| --- | --- | ---: | --- | --- | ---: |
| AtSize50Tests | One local + 48 increment statements + one assertion | 50 | 7–59 | 7–60 | 10 |
| OverSize51Tests | One local + 49 increment statements + one assertion | 51 | 7–60 | 7–61 | 9 |
| BranchingTests | Two locals + one if + one increment + one assertion | 5 | 7–15 | 7–17 | 9 |
| WeakTests | Twelve empty tests, each with no observable assertion | 0 per test | `6 + 4n` through `8 + 4n`, n=0…11 | `7 + 5n` through `10 + 5n`, n=0…11 | 0, retaining all 12 deductions |
| RollupTests | One empty test | 0 | 6–8 | 7–10 | 9 per class |
| ExtraRollupTests | One empty test in the same source file | 0 | 12–14 | 15–18 | 9 per class |

The shared `RollupTests.java` / `RollupTests.cs` file has two distinct no-assertion findings, so its file score is 8 although both class scores are 9. Java's second top-level class is package-private, which JUnit 5 supports. Test class filenames end in `Tests` so Maven Surefire actually runs them. Each language's real test runner executes 22 tests, including the deliberately weak empty tests.

Java test files are under `java/src/test/java/scoring/tests/`, package `scoring.tests`; C# test files are under `csharp/Scoring.Tests/`, namespace `Scoring.Tests`.

## Duplication and dependency cases

CloneLeft and CloneRight each have the same four-statement trim/length/double/return body. Their within-language structure is identical, with enough lines and syntax nodes to exceed the configured minimums. Each affected class/file receives one duplicate-group deduction, giving duplication score 9. Java and C# form separate groups. Other intentionally repetitive cases can form their own groups; the corpus does not assume that only the explicitly named clone pair is repetitive.

CycleLeft and CycleRight depend directly on each other. Java's public field reference is at line 4; C#'s nullable auto-property reference is at line 5. Neither class declares an executable method body. The root `.cqc.json` puts each side in a separate component and forbids outgoing dependencies from each left component through an empty allowed list. Consequently CycleLeft has one cycle and one boundary deduction (score 8), while CycleRight has one cycle deduction (score 9). Language-prefixed component patterns keep Java and C# policy independent.

Copy the entire corpus into an isolated temporary directory before executing builds or collecting coverage. Do not write build products, coverage artifacts, or metadata back into the checked-in fixture inputs. Expected scores are the arithmetic above, not snapshots regenerated from the checker's output.
