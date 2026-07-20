# Bounded Analysis Limits

SharpProof bounds fact collection and state merging so analyzer hosts and
interactive queries cannot grow without limit. A bound is conservative: when
it is exceeded, SharpProof drops the excess proof facts and records stable
truncation evidence. A truncated result must not be treated as a complete
proof merely because the remaining facts are consistent.

## Limits

| API property | Analyzer configuration key | CLI `--analysis-limit` name | Default | Event code |
| --- | --- | --- | ---: | --- |
| `MaxMergedIfElseFacts` | `sharpproof_analysis_max_merged_if_else_facts` | `merged-if-else-facts` | 16 | `analysis_limit.if_else_fact_merge` |
| `MaxMergedSwitchFacts` | `sharpproof_analysis_max_merged_switch_facts` | `merged-switch-facts` | 32 | `analysis_limit.switch_fact_merge` |
| `MaxMergedTryFacts` | `sharpproof_analysis_max_merged_try_facts` | `merged-try-facts` | 16 | `analysis_limit.try_fact_merge` |
| `MaxTryCompletionBranches` | `sharpproof_analysis_max_try_completion_branches` | `try-completion-branches` | 8 | `analysis_limit.try_completion_branches` |
| `MaxFiniteForeachElementFacts` | `sharpproof_analysis_max_finite_foreach_element_facts` | `finite-foreach-element-facts` | 8 | `analysis_limit.foreach_element_facts` |
| `MaxScopedBlockCompletionStatements` | `sharpproof_analysis_max_scoped_block_completion_statements` | `scoped-block-completion-statements` | 32 | `analysis_limit.scoped_block_completion_statements` |
| `MaxStructuralNullStateDepth` | `sharpproof_analysis_max_structural_null_state_depth` | `structural-null-state-depth` | 4 | `analysis_limit.structural_null_state_depth` |
| `MaxMergedPathConditions` | `sharpproof_analysis_max_merged_path_conditions` | `merged-path-conditions` | 32 | `analysis_limit.merged_path_conditions` |
| `MaxMergeableFactsPerTargetPerState` | `sharpproof_analysis_max_mergeable_facts_per_target_per_state` | `mergeable-facts-per-target-per-state` | 4 | `analysis_limit.mergeable_facts_per_target_per_state` |
| `MaxFactChoiceCombinationsPerTarget` | `sharpproof_analysis_max_fact_choice_combinations_per_target` | `fact-choice-combinations-per-target` | 64 | `analysis_limit.fact_choice_combinations_per_target` |
| `MaxGuardFactsPerTargetPerState` | `sharpproof_analysis_max_guard_facts_per_target_per_state` | `guard-facts-per-target-per-state` | 6 | `analysis_limit.guard_facts_per_target_per_state` |

All values must be positive. SharpProof records an event only when work would
exceed a limit, not merely when the retained count equals it. Raising limits
can improve proof precision, but also increases analyzer time and memory use.
These controls are separate from SMT timeout, expression-node, and solver path
condition budgets.

## Analyzer Configuration

Analysis limits are compilation-global because the analyzer shares purity,
state, and solver services across syntax callbacks. Configure them in a global
AnalyzerConfig section or through matching MSBuild properties:

```ini
is_global = true

sharpproof_analysis_max_merged_if_else_facts = 32
sharpproof_analysis_max_finite_foreach_element_facts = 16
sharpproof_analysis_max_structural_null_state_depth = 6
sharpproof_analysis_max_merged_path_conditions = 64
```

Invalid, zero, or negative values produce `SP0025` and the corresponding
default remains active. When truncated evidence contributes to a contract
diagnostic, its properties include:

- `sharpproof.analysis.truncated = True`
- `sharpproof.analysis.limit_codes`, a comma-separated stable code set
- `sharpproof.analysis.limit_events`, with
  `code|limit|observed|spanStart|provenance` entries

## .NET API

Create an immutable override set and attach it to the analysis session:

```csharp
var budget = new SharpProofAnalysisBudget(
    MaxFiniteForeachElementFacts: 16,
    MaxMergedPathConditions: 64);

using var session = SharpProofAnalysisSession.FromText(
    sourceText,
    "Example.cs",
    new SharpProofAnalysisOptions(AnalysisBudget: budget));
var result = session.Analyze(
    new SharpProofQuery(SharpProofQueryKind.Invariant, target));

if (result.Budget.IsExhausted)
{
    foreach (var item in result.Budget.Truncations)
        Console.WriteLine($"{item.Code}: {item.Observed} > {item.Limit}");
}
```

`AnalysisTruncation` is exposed on query, condition-proof, and runtime-hazard
results. Each event includes its
stable `Code`, typed `Kind`, retained `Limit`, attempted or observed count,
proof `Provenance`, and source span start when available. Aggregate results
deduplicate the same event while preserving the largest observed count.

## CLI

Repeat `--analysis-limit <name>=<positive-integer>` to override one or more
limits:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --all-lines `
  --analysis-limit finite-foreach-element-facts=16 `
  --analysis-limit structural-null-state-depth=6 `
  --json
```

Text output prints an `Analysis limits hit` section. JSON includes
`analysisTruncation.isTruncated` and the complete event array. Runtime
hazard entries also retain the truncation evidence for the individual
candidate analysis.
