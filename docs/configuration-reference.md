# Analyzer configuration reference

<!-- Generated from ConfigKeys.cs and AnalyzerConfigurationOptionRegistry.cs by scripts/Generate-ConfigurationReference.ps1. -->

SharpProof reads these `sharpproof_*` analyzer options from global AnalyzerConfig and, where noted, per-tree `.editorconfig` sections. Invalid values are reported as `SP0025`; they do not silently change the effective configuration.

## Option reference

| Key | Scope | Valid values | Default | Related diagnostics | Description |
| --- | --- | --- | --- | --- | --- |
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
| `sharpproof_runtime_hazard_mode` | Global and per-tree | `none`, `sites`, `summaries`, `all` | `none` | SP0010, SP0011; SP0025 for invalid values | Controls SP0010 and SP0011 runtime-hazard reporting. |
| `sharpproof_smt_max_expression_nodes` | Global-only | positive integer | `mode default: 2048 (bounded/off), 8192 (deep)` | SMT-backed proof results; SP0025 for invalid values | Maximum SMT expression nodes considered per query. |
| `sharpproof_smt_max_path_conditions` | Global-only | positive integer | `mode default: 192 (bounded/off), 512 (deep)` | SMT-backed proof results; SP0025 for invalid values | Maximum SMT path conditions considered per method. |
| `sharpproof_smt_method_budget_ms` | Global-only | positive integer | `mode default: 5000 ms (bounded/off), 15000 ms (deep)` | SMT-backed proof results; SP0025 for invalid values | Per-method SMT budget in milliseconds. |
| `sharpproof_smt_mode` | Global-only | `disabled`, `bounded`, `deep` | `bounded` | SMT-backed proof results; SP0025 for invalid values | Controls bounded SMT proof mode. |
| `sharpproof_smt_timeout_ms` | Global-only | positive integer | `mode default: 750 ms (bounded/off), 2000 ms (deep)` | SMT-backed proof results; SP0025 for invalid values | Per-query SMT timeout in milliseconds. |
| `sharpproof_suggest_missing_enforce_pure` | Global and per-tree | boolean (`true` or `false`) | `true` | SP0004; SP0025 for invalid values | Controls SP0004 inferred purity suggestions. |
| `sharpproof_suggest_missing_enforce_pure_exclude_generated` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0004; SP0025 for invalid values | Suppresses SP0004 in generated-looking source paths. |
| `sharpproof_suggest_missing_enforce_pure_exclude_tests` | Global and per-tree | boolean (`true` or `false`) | `false` | SP0004; SP0025 for invalid values | Suppresses SP0004 in test-looking namespaces and source paths. |
| `sharpproof_suggest_missing_enforce_pure_min_complexity` | Global and per-tree | non-negative integer | `0` | SP0004; SP0025 for invalid values | Minimum inferred complexity required before SP0004 is suggested. |
| `sharpproof_suggest_missing_enforce_pure_namespace_filters` | Global and per-tree | `;`, `,`, or newline-delimited values | `` | SP0004; SP0025 for invalid values | Namespace prefixes eligible for SP0004 suggestions. |
| `sharpproof_suggest_missing_enforce_pure_scope` | Global and per-tree | `all`, `public`, `internal`, `off` | `all` | SP0004; SP0025 for invalid values | Controls which method visibility SP0004 can suggest. |

## Global AnalyzerConfig example

Global-only options must be set in a global AnalyzerConfig file. Global-and-tree options can also be set here as defaults before a matching `.editorconfig` override.

```ini
is_global = true
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
sharpproof_smt_max_expression_nodes = 1000
sharpproof_smt_max_path_conditions = 1000
sharpproof_smt_method_budget_ms = 1000
sharpproof_smt_mode = deep
sharpproof_smt_timeout_ms = 1000
sharpproof_suggest_missing_enforce_pure = true
sharpproof_suggest_missing_enforce_pure_exclude_generated = false
sharpproof_suggest_missing_enforce_pure_exclude_tests = false
sharpproof_suggest_missing_enforce_pure_min_complexity = 3
sharpproof_suggest_missing_enforce_pure_namespace_filters = Demo.Namespace.Member
sharpproof_suggest_missing_enforce_pure_scope = public
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
sharpproof_suggest_missing_enforce_pure = true
sharpproof_suggest_missing_enforce_pure_exclude_generated = false
sharpproof_suggest_missing_enforce_pure_exclude_tests = false
sharpproof_suggest_missing_enforce_pure_min_complexity = 3
sharpproof_suggest_missing_enforce_pure_namespace_filters = Demo.Namespace.Member
sharpproof_suggest_missing_enforce_pure_scope = public
```
