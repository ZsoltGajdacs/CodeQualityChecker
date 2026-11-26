# Ruleset 3

**10 is the desired score.** Each distinct finding below deducts one point. These scores are `max(0, 10 - deductions)`, separately for every applicable family and class/file. Class scores include all partial declarations. File scores use the deduplicated union of findings located in that file. Scores never average healthy methods against problematic methods. Every deduction retains its locations and advice, including after a score reaches zero.

| Rule ID | Default condition |
| --- | --- |
| `code.complexity` | Method cyclomatic complexity > 8 (9 and above) |
| `code.crap` | Method CRAP > 30 |
| `tests.branching` | Test or resolved fixture/helper has branching |
| `tests.size` | Test plus resolved fixture/helper contribution > 50 executable lines |
| `tests.noAssertion` | No recognized observable assertion in the test or its resolved helpers |
| `duplication.group` | Class participates in a qualifying duplicate group |
| `dependencies.cycle` | Class participates in a type or architectural component cycle |
| `dependencies.boundary` | Distinct source type dependency violates configured boundaries |

CRAP = `CC² × (1 − coverage)³ + CC`. Coverage is a fraction, based on branches when a method has branches, otherwise executable lines. Raw metrics accompany the score. For example a method with CC=12 and no covered branches has CRAP=156: it contributes two findings and lowers the class's production score from 10 to 8. Every production method in the scored class must have valid matching coverage evidence; missing reports do not mean 0% coverage.

Test-helper traversal is cycle-safe and charges each reachable test-support method once per test. Production methods called as the system under test are not charged as test scaffolding. Java `src/test` types and C# test-project types are recognized as test support even when a sibling helper class has no test annotation. Assertions reached only through fixtures do not give the test assertion credit. Fixture methods still contribute size, branching, and evidence completeness. Parameterized cases are recognized. Known assertion, exception-assertion, and verification APIs count as observable assertions; arbitrary custom assertion abstractions can require additional recognition. Unresolved test/fixture/helper facts block affected test scores. Assertion count alone does not establish test effectiveness.

Duplicate candidates must contain at least four lines of body code and default to at least 20 normalized syntax nodes. The line count includes lines with code tokens, including continuations, and excludes declaration signatures, blank lines, comments, brace-only lines, and separately analyzed nested bodies. Set `duplicateMinLines` above four to raise the minimum. Positive values below four use the four-line minimum.

Similarity uses Jaccard sets of structural fingerprints, with threshold 0.82. Qualifying pairs form connected groups. A group deducts once per affected class or file. A connected group can contain nonadjacent members below the pair threshold. Names and literal values normalize away. Language constructs, operators, and call names remain meaningful. Compare Java with Java and C# with C#.

Java and C# constructors containing only direct constructor-parameter-to-instance-field assignments are excluded from duplication. Fields must be declared on the containing type (including C# partial declarations); explicit `this.field` and unambiguous unqualified fields are supported. C# expression-bodied wiring constructors are included. Explicit constructor chaining, property assignments, calculations, calls, validation, compound assignments, and ambiguous or unrecognized statements retain normal duplication eligibility. Empty constructors retain existing behavior. This exclusion does not change other method measurements or evidence requirements and does not require dependency types to resolve.

Dependency metrics include component fan-in/out, instability, abstractness, absolute distance from the main sequence, and zone (`-1` pain, `0` healthy, `1` useless, threshold 0.3). Metrics are advisory unless listed in the scored rules. Component cycles can exist even without individual type cycles. A class already scored for a type cycle does not receive an additional component-cycle deduction. Boundary findings attach to the source class. This check does not scan package vulnerabilities.

The default complexity finding boundary matches the observed Java/C# Code Health cutoff: complexity 8 is accepted and complexity 9 is flagged. `complexityThreshold` is the maximum permitted complexity, so explicit overrides retain their existing meaning (for example, 10 flags 11 and above). CQC scores remain its own one-point deduction model.

Changing the effective default changes the configuration fingerprint. Coverage collected under the previous default must be refreshed with explicit execution, or supplied with matching provenance. The scoring algorithm and ruleset version remain unchanged; the configuration fingerprint identifies the changed policy.

## Change hotspots

The `hotspots` family subtracts three capped penalties from 10 and rounds to two decimal places. Each penalty starts at zero at the lower threshold and rises linearly to its cap at the upper threshold.

| Measurement | Lower threshold | Upper threshold | Maximum penalty |
| --- | ---: | ---: | ---: |
| Commits editing the class's files in 12 months | 10 | 30 | 5 |
| Parameters in the widest constructor | 7 | 10 | 3 |
| Physical class lines | 400 | 800 | 2 |

The history window ends at the pinned local HEAD's committer timestamp. The check walks its first-parent chain and compares merge commits to their first parent. It follows committed renames detected by Git. File creation and pure renames contribute zero. Classes in the same file share its edit history. A partial class counts each commit once across its declarations and sums their physical line spans. A working-tree path absent from HEAD starts with zero edits.

Current source defines class length and constructor arity. Physical lines include blank and comment lines inside the declaration. Constructor arity includes data arguments and service dependencies. C# primary constructors and Java record components contribute to arity. TypeScript hotspots analyze authored classes and skip ambient declarations and conventional test files.

A file receives the lowest score among its selected classes and is incomplete if any of them is unscored. A score below 7 produces the advisory `hotspots.change` and its three measurements. Missing or shallow Git history and incomplete class syntax produce incomplete results. The `hotspotHistory` report field records the HEAD SHA and UTC date window when available.

## Configuration

Project hotspot bounds live in `.cqc/config.json`. An explicit execution that publishes evidence creates this file with the following defaults if it is absent. Analysis-only checks read it without creating it. Subsequent runs preserve edits to the file.

```json
{
  "hotspots": {
    "editCommits": { "lower": 10, "upper": 30 },
    "constructorParameters": { "lower": 7, "upper": 10 },
    "classLines": { "lower": 400, "upper": 800 }
  }
}
```

A missing file or omitted bound uses the corresponding built-in default. Bounds must be non-negative integers with `upper` greater than `lower`. The default constructor penalty is zero through seven parameters, starts at eight, and reaches three points at ten. You can track `.cqc/config.json` in Git.

An optional `.cqc.json` at the project root overrides general quality defaults. When a request supplies `configPath`, that file is the sole configuration source for the request and can contain both the general settings and the `hotspots` section.

```json
{
  "complexityThreshold": 8,
  "crapThreshold": 30,
  "testLinesThreshold": 50,
  "duplicateSimilarity": 0.82,
  "duplicateMinLines": 4,
  "duplicateMinNodes": 20,
  "components": {
    "domain": ["MyApp.Domain.*"],
    "ui": ["MyApp.Ui"]
  },
  "allowedDependencies": {
    "domain": [],
    "ui": ["domain"]
  },
  "forbiddenDependencies": [["domain", "ui"]],
  "exclude": ["**/Experimental/**"]
}
```

Component patterns match qualified type names or TypeScript source paths. Plain patterns match an exact name or namespace prefix. The `*` and `?` globs are case-sensitive. Optional `java:`, `csharp:`, and `typescript:` prefixes limit a pattern to one language. For example, `typescript:src/ui/*` matches source modules under `src/ui/`. First matching component in ordinal component-name order wins. Unmatched C# and Java types use their declared namespace or package, prefixed with the language. This groups nested types with their enclosing type and uses `<global>` for the global namespace or default package. The fallback for facts without namespace metadata uses the name prefix before the final dot. Configure TypeScript components explicitly to group source paths.

An allowed list constrains only the named source component; `"*"` permits any target. Explicit forbidden edges still apply. Self-component edges are permitted. Policy names must refer to declared components. Invalid settings and unknown configuration fields are rejected. The effective configuration has a stable fingerprint in every analysis response.

Default exclusions include Git, Backlog/Serena state, `.cqc`, build output (`bin`, `obj`, `target`, `dist`, `TestResults`), `node_modules`, and `vendor`. Git-ignored files, symlinks, common generated C# filenames, and files with an auto-generated/`@Generated` marker in the first 12 lines are not analyzed. Inspection and evidence-aware analysis report unsupported execution integrations. Source-only reviews report source and analyzer diagnostics.

## Independent evidence requirements

`staticComplexity` scores production classes and files as `max(0, 10 - distinct code.complexity deductions)` and requires complete syntax and executable-body extraction. A partial class aggregates evidence from its declarations. Each file score uses the declarations and executable bodies in that file. Within an evidence-aware report the static score shares the complexity findings used by the combined production assessment. Combined production scoring also requires an unambiguous method identity, verified resolution context, and matching coverage for every production method.

Duplication requires complete structural extraction across the files compared within each language. Tests require resolved identity, assertion and helper calls, and a verified resolution context. Dependencies require complete verified reachable module contexts. Evidence-aware reports describe incomplete assessments with blockers. Source-only reviews omit unavailable assessments and retain diagnostics for structural gaps and analyzer failures. Structural diagnostics include the available parser or configuration reason, construct, and source range.

Java symbol IDs include module and source-set context. Component patterns match qualified language and type names. MCP report envelopes and the dedicated source-only Java worker exchange use schema 3. The evidence-aware Java worker exchange, core facts, and persisted coverage use schema 2.
