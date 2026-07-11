# Analyzer configuration reference

<!-- Generated from ConfigKeys.cs and AnalyzerConfigurationOptionRegistry.cs by scripts/Generate-ConfigurationReference.ps1. -->

SharpProof reads these `sharpproof_*` analyzer options from global AnalyzerConfig and, where noted, per-tree `.editorconfig` sections. Invalid values are reported as `SP0025`; they do not silently change the effective configuration.

## Option reference

| Key | Scope | Valid values | Default | Related diagnostics | Description |
| --- | --- | --- | --- | --- | --- |
| `sharpproof_analysis_max_fact_choice_combinations_per_target` | Global-only | positive integer | `64` | configuration consumers; SP0025 for invalid values | Maximum fact-choice combinations explored per merge target. |
| `sharpproof_analysis_max_finite_foreach_element_facts` | Global-only | positive integer | `8` | configuration consumers; SP0025 for invalid values | Maximum finite collection elements modeled for foreach facts. |
| `sharpproof_analysis_max_guard_facts_per_target_per_state` | Global-only | positive integer | `6` | configuration consumers; SP0025 for invalid values | Maximum guard facts retained per target and state. |
| `sharpproof_analysis_max_mergeable_facts_per_target_per_state` | Global-only | positive integer | `4` | configuration consumers; SP0025 for invalid values | Maximum mergeable facts retained per target and state. |
| `sharpproof_analysis_max_merged_if_else_facts` | Global-only | positive integer | `16` | configuration consumers; SP0025 for invalid values | Maximum facts retained while merging if/else branches. |
| `sharpproof_analysis_max_merged_path_conditions` | Global-only | positive integer | `32` | configuration consumers; SP0025 for invalid values | Maximum synthesized path conditions retained across merged states. |
| `sharpproof_analysis_max_merged_switch_facts` | Global-only | positive integer | `32` | configuration consumers; SP0025 for invalid values | Maximum facts retained while merging switch branches. |
| `sharpproof_analysis_max_merged_try_facts` | Global-only | positive integer | `16` | configuration consumers; SP0025 for invalid values | Maximum facts retained while merging try/catch/finally branches. |
| `sharpproof_analysis_max_scoped_block_completion_statements` | Global-only | positive integer | `32` | configuration consumers; SP0025 for invalid values | Maximum completed statements scanned while deriving scoped block facts. |
| `sharpproof_analysis_max_structural_null_state_depth` | Global-only | positive integer | `4` | configuration consumers; SP0025 for invalid values | Maximum structural expression depth inspected for null-state facts. |
| `sharpproof_analysis_max_try_completion_branches` | Global-only | positive integer | `8` | configuration consumers; SP0025 for invalid values | Maximum try/catch completion branches analyzed at a program point. |
| `sharpproof_attribute_stub_namespaces` | Global-only | `;`, `,`, or newline-delimited values | `SharpProof.Attributes` | configuration consumers; SP0025 for invalid values | Namespaces accepted for source-only SharpProof attribute stubs. |
| `sharpproof_checked_exceptions` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0011; SP0025 for invalid values | Emits optional SP0011 exception site diagnostics. |
| `sharpproof_emit_explanations` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0009; SP0025 for invalid values | Emits optional SP0009 proof explanation diagnostics. |
| `sharpproof_enable_effect_summary_json` | Global-only | boolean (`true` or `false`) | `false` | SP0002, SP0010, SP0011; SP0025 for invalid values | Loads analyzer AdditionalFiles effect-summary JSON. |
| `sharpproof_known_impure_methods` | Global-only | `;`, `,`, or newline-delimited values | `` | SP0002; SP0025 for invalid values | Additional method symbols treated as impure. |
| `sharpproof_known_impure_namespaces` | Global-only | `;`, `,`, or newline-delimited values | `` | SP0002; SP0025 for invalid values | Namespaces treated as impure trust boundaries. |
| `sharpproof_known_impure_types` | Global-only | `;`, `,`, or newline-delimited values | `` | SP0002; SP0025 for invalid values | Types treated as impure trust boundaries. |
| `sharpproof_known_pure_methods` | Global-only | `;`, `,`, or newline-delimited values | `` | SP0002; SP0025 for invalid values | Additional method symbols treated as pure. |
| `sharpproof_purity_profile` | Global-only | `strict`, `balanced`, `pragmatic` | `balanced` | SP0002; SP0025 for invalid values | Purity strictness profile. |
| `sharpproof_report_bcl_fallback_guesses` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0012; SP0025 for invalid values | Emits optional SP0012 BCL fallback guess diagnostics. |
| `sharpproof_report_exceptions` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0010; SP0025 for invalid values | Emits optional SP0010 exception summary diagnostics. |
| `sharpproof_runtime_hazard_mode` | Global and per-tree | `none`, `sites`, `summaries`, `all`, `unknowns`, `sites-and-unknowns`, `all-and-unknowns` | `none` | SP0010, SP0011, SP0033; SP0025 for invalid values | Controls SP0010, SP0011, and opt-in SP0033 runtime-hazard reporting. |
| `sharpproof_smt_dispose_thread_context_on_service_dispose` | Global-only | boolean (`true` or `false`) | `false` | SMT-backed proof results; SP0025 for invalid values | Disposes the current thread's shared solver context with the analysis service. |
| `sharpproof_smt_max_expression_nodes` | Global-only | positive integer | `mode default: 2048 (bounded/off), 8192 (deep)` | SMT-backed proof results; SP0025 for invalid values | Maximum SMT expression nodes considered per query. |
| `sharpproof_smt_max_path_conditions` | Global-only | positive integer | `mode default: 192 (bounded/off), 512 (deep)` | SMT-backed proof results; SP0025 for invalid values | Maximum SMT path conditions considered per method. |
| `sharpproof_smt_method_budget_ms` | Global-only | positive integer | `mode default: 5000 ms (bounded/off), 15000 ms (deep)` | SMT-backed proof results; SP0025 for invalid values | Per-method SMT budget in milliseconds. |
| `sharpproof_smt_mode` | Global-only | `disabled`, `bounded`, `deep` | `bounded` | SMT-backed proof results; SP0025 for invalid values | Controls bounded SMT proof mode. |
| `sharpproof_smt_recycle_context_on_transient_failure` | Global-only | boolean (`true` or `false`) | `true` | SMT-backed proof results; SP0025 for invalid values | Recycles the current thread's solver context after a transient failure. |
| `sharpproof_smt_timeout_ms` | Global-only | positive integer | `mode default: 750 ms (bounded/off), 2000 ms (deep)` | SMT-backed proof results; SP0025 for invalid values | Per-query SMT timeout in milliseconds. |
| `sharpproof_smt_transient_retry_count` | Global-only | non-negative integer | `1` | SMT-backed proof results; SP0025 for invalid values | Retries after a transient Z3 context failure. |
| `sharpproof_suggest_inferred_contracts` | Global and per-tree | boolean (`true` or `false`) | `false` | configuration consumers; SP0025 for invalid values | Controls opt-in SP0034-SP0039 inferred contract suggestions. |
| `sharpproof_suggest_inferred_contracts_kinds` | Global and per-tree | `zero-allocations`, `capabilities`, `complexity`, `exceptions`, `ensures`, `requires` | `zero-allocations, capabilities, complexity, exceptions, ensures, requires` | configuration consumers; SP0025 for invalid values | Selects inferred contract families. |
| `sharpproof_suggest_inferred_contracts_minimum_confidence` | Global and per-tree | `medium`, `high` | `high` | configuration consumers; SP0025 for invalid values | Minimum confidence for inferred contract suggestions. |
| `sharpproof_suggest_inferred_contracts_scope` | Global and per-tree | `all`, `public`, `internal`, `off` | `all` | configuration consumers; SP0025 for invalid values | Controls which method visibility can receive inferred contract suggestions. |
| `sharpproof_suggest_missing_enforce_pure` | Global and per-tree | boolean (`true` or `false`) | `true` | SP0004; SP0025 for invalid values | Controls SP0004 inferred purity suggestions. |
| `sharpproof_suggest_missing_enforce_pure_exclude_generated` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0004; SP0025 for invalid values | Suppresses SP0004 in generated-looking source paths. |
| `sharpproof_suggest_missing_enforce_pure_exclude_tests` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0004; SP0025 for invalid values | Suppresses SP0004 in test-looking namespaces and source paths. |
| `sharpproof_suggest_missing_enforce_pure_min_complexity` | Global and per-tree | non-negative integer | `0` | SP0004; SP0025 for invalid values | Minimum inferred complexity required before SP0004 is suggested. |
| `sharpproof_suggest_missing_enforce_pure_namespace_filters` | Global and per-tree | `;`, `,`, or newline-delimited values | `` | SP0004; SP0025 for invalid values | Namespace prefixes eligible for SP0004 suggestions. |
| `sharpproof_suggest_missing_enforce_pure_scope` | Global and per-tree | `all`, `public`, `internal`, `off` | `all` | SP0004; SP0025 for invalid values | Controls which method visibility SP0004 can suggest. |
| `sharpproof_suppress_proven_diagnostics` | Global and per-tree | boolean (`true` or `false`) | `false` | SPS0001-SPS0018; SP0025 for invalid values | Controls opt-in suppression of allowlisted external diagnostics backed by exact SharpProof proofs. |
| `sharpproof_suppression_diagnostic_ids` | Global and per-tree | `none`, `cs8509`, `cs8524`, `cs8602`, `cs8605`, `cs8629`, `cs8655`, `cs8670`, `cs8846`, `cs8847`, `s2259`, `s3655`, `v3064`, `v3080`, `v3095`, `v3106`, `v3151`, `v3152`, `v3218` | `CS8509, CS8524, CS8602, CS8605, CS8629, CS8655, CS8670, CS8846, CS8847, S2259, S3655, V3064, V3080, V3095, V3106, V3151, V3152, V3218` | SPS0001-SPS0018; SP0025 for invalid values | Restricts exact-proof suppression to supported external diagnostic IDs. |

## Global AnalyzerConfig example

Global-only options must be set in a global AnalyzerConfig file. Global-and-tree options can also be set here as defaults before a matching `.editorconfig` override.

```ini
is_global = true
sharpproof_analysis_max_fact_choice_combinations_per_target = 1000
sharpproof_analysis_max_finite_foreach_element_facts = 1000
sharpproof_analysis_max_guard_facts_per_target_per_state = 1000
sharpproof_analysis_max_mergeable_facts_per_target_per_state = 1000
sharpproof_analysis_max_merged_if_else_facts = 1000
sharpproof_analysis_max_merged_path_conditions = 1000
sharpproof_analysis_max_merged_switch_facts = 1000
sharpproof_analysis_max_merged_try_facts = 1000
sharpproof_analysis_max_scoped_block_completion_statements = 1000
sharpproof_analysis_max_structural_null_state_depth = 1000
sharpproof_analysis_max_try_completion_branches = 1000
sharpproof_attribute_stub_namespaces = SharpProof.Attributes; My.Contracts
sharpproof_checked_exceptions = false
sharpproof_emit_explanations = false
sharpproof_enable_effect_summary_json = false
sharpproof_known_impure_methods = Demo.Namespace.Member
sharpproof_known_impure_namespaces = Demo.Namespace.Member
sharpproof_known_impure_types = Demo.Namespace.Member
sharpproof_known_pure_methods = Demo.Namespace.Member
sharpproof_purity_profile = balanced
sharpproof_report_bcl_fallback_guesses = false
sharpproof_report_exceptions = false
sharpproof_runtime_hazard_mode = all
sharpproof_smt_dispose_thread_context_on_service_dispose = false
sharpproof_smt_max_expression_nodes = 1000
sharpproof_smt_max_path_conditions = 1000
sharpproof_smt_method_budget_ms = 1000
sharpproof_smt_mode = deep
sharpproof_smt_recycle_context_on_transient_failure = true
sharpproof_smt_timeout_ms = 1000
sharpproof_smt_transient_retry_count = 3
sharpproof_suggest_inferred_contracts = false
sharpproof_suggest_inferred_contracts_kinds = zero-allocations, capabilities, complexity, exceptions, ensures, requires
sharpproof_suggest_inferred_contracts_minimum_confidence = high
sharpproof_suggest_inferred_contracts_scope = public
sharpproof_suggest_missing_enforce_pure = true
sharpproof_suggest_missing_enforce_pure_exclude_generated = false
sharpproof_suggest_missing_enforce_pure_exclude_tests = false
sharpproof_suggest_missing_enforce_pure_min_complexity = 3
sharpproof_suggest_missing_enforce_pure_namespace_filters = Demo.Namespace.Member
sharpproof_suggest_missing_enforce_pure_scope = public
sharpproof_suppress_proven_diagnostics = false
sharpproof_suppression_diagnostic_ids = CS8509, CS8524, CS8602, CS8605, CS8629, CS8655, CS8670, CS8846, CS8847, S2259, S3655, V3064, V3080, V3095, V3106, V3151, V3152, V3218
```

## Per-tree `.editorconfig` example

Only global-and-tree options can be overridden in a per-tree section. Global-only options placed in such a section are invalid and produce `SP0025`.

```ini
root = true

[src/**/*.cs]
sharpproof_checked_exceptions = false
sharpproof_emit_explanations = false
sharpproof_report_bcl_fallback_guesses = false
sharpproof_report_exceptions = false
sharpproof_runtime_hazard_mode = all
sharpproof_suggest_inferred_contracts = false
sharpproof_suggest_inferred_contracts_kinds = zero-allocations, capabilities, complexity, exceptions, ensures, requires
sharpproof_suggest_inferred_contracts_minimum_confidence = high
sharpproof_suggest_inferred_contracts_scope = public
sharpproof_suggest_missing_enforce_pure = true
sharpproof_suggest_missing_enforce_pure_exclude_generated = false
sharpproof_suggest_missing_enforce_pure_exclude_tests = false
sharpproof_suggest_missing_enforce_pure_min_complexity = 3
sharpproof_suggest_missing_enforce_pure_namespace_filters = Demo.Namespace.Member
sharpproof_suggest_missing_enforce_pure_scope = public
sharpproof_suppress_proven_diagnostics = false
sharpproof_suppression_diagnostic_ids = CS8509, CS8524, CS8602, CS8605, CS8629, CS8655, CS8670, CS8846, CS8847, S2259, S3655, V3064, V3080, V3095, V3106, V3151, V3152, V3218
```
