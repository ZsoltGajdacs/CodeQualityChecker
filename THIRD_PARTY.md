# Third-party attribution

CQC adapts documented ideas from Robert C. Martin's quality tools. No upstream source files were copied into this implementation. `upstream.lock.json` records exact reference revisions; CQC does not fetch or execute those repositories at runtime.

| Reference | Pinned revision | License at reference revision | Adaptation |
| --- | --- | --- | --- |
| [crapper](https://github.com/unclebob/crapper) | `6fd92c4c9bcc56efb64a2f8743d5a2993bcfd42d` | 0BSD | Complexity and CRAP; CQC uses branch coverage with branchless line fallback, evidence-aware scoring and Java/C# adapters |
| [dryer](https://github.com/unclebob/dryer) | `66ff6d21a42c04afcad89c78a80066176d1294b0` | 0BSD | Structural normalization, Jaccard similarity, initial candidate defaults; CQC adds grouped findings and file/class deductions |
| [SCRAP](https://github.com/unclebob/scrap) | `f793b28906d3c60e30b128e1ec08df6269a72b13` | No license declared in repository root | Independent implementation of described test-structure concepts for JUnit 5/xUnit; no Speclj source port or full SCRAP formula parity |
| [dependency-checker](https://github.com/unclebob/dependency-checker) | `61097ea222a377b781f003712bc54a73bb8a2d4d` | No license declared in repository root | Independent implementation of graph/architecture concepts with Java/C# semantic references and JSON boundary policy |

The 0BSD reference license states copyright © 2026 Robert C. Martin and permits use, copying, modification, and distribution without fee. The full reference text remains in each pinned repository's `LICENSE`. The absence of a declared license for the other references is not treated as permission to copy their code.

## Runtime dependencies

| Component | License / notice source |
| --- | --- |
| Official Model Context Protocol C# SDK 2.3.1 | Apache-2.0; pinned source license and notices in `docs/licenses/` |
| .NET runtime, Microsoft.Extensions, Roslyn | MIT; runtime and NuGet notices |
| JavaParser / symbol solver | Apache-2.0 or LGPL-3.0 dual license; use Apache-2.0 option |
| Gson | Apache-2.0 |
| JavaParser transitives (Guava, Javassist and annotations) | Their embedded Maven/JAR license metadata and notices |
| Node.js runtime | Bundled distribution license and third-party notices in `legal/node/LICENSE` |
| TypeScript compiler | Apache-2.0 and third-party notices in `legal/typescript/` |
| JetBrains OpenJDK-derived runtime | GPL-2.0 with Classpath Exception and included per-module third-party notices |
| xUnit, .NET test SDK, Coverlet | Development/explicit-execution dependencies; their package license declarations apply |

Distribution packages retain `runtime/legal/`, .NET runtime `LICENSE`/third-party notices under `legal/dotnet/`, and collected NuGet license declarations under `legal/nuget/`. The shaded Java worker retains its embedded Maven metadata/notices; dependency licenses are also recorded under `legal/java/` in the package. Do not remove legal files when redistributing.

CQC scores are its own transparent heuristic model and are not CodeScene's proprietary Code Health metric.
