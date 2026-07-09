# SharpProof Contracts

SharpProof's primary user surface is analyzer contracts: attributes that turn a
bounded proof question into normal build diagnostics.

## Contract Attributes

- `[EnforcePure]` / `[Pure]`: require a method-like member to be proven pure.
  Violations produce `SP0002`; pure-looking members without a contract can
  produce `SP0004`.
- `[Requires("condition")]`: require callers to prove a C#-like precondition
  before invoking the method-like member. Failed call-site proofs produce
  `SP0027`; unsupported preconditions produce `SP0028`. Valid preconditions
  are also fed into `[Ensures]`, runtime-hazard, and purity proof queries
  inside the callee.
- `[Ensures("condition")]`: require every reachable return site to prove a
  C#-like postcondition. Conditions can reference `result` and the annotated
  method's parameters. Failures produce `SP0018`; unsupported conditions
  produce `SP0019`.
- `[ZeroAllocations]`: require no direct source-visible heap allocation sites
  in the annotated method-like body. Violations produce `SP0013`.
- `[AllowedCapabilities(...)]`: restrict proven side-effect capabilities such
  as `IO`, `Console`, `FileRead`, `FileWrite`, `Network`, `Process`,
  `Environment`, `Reflection`, and `NativeInterop`. Violations produce
  `SP0015`; unverifiable operations produce `SP0016`.
- `[DoesNotThrow]`: require that no exception escapes the annotated method-like
  body. Escaping exceptions produce `SP0030`.
- `[AllowedExceptions(...)]`: allow only the listed exception types, including
  derived exception types, to escape the annotated method-like body. Disallowed
  escaping exceptions produce `SP0030`.
- `[ExpectedComplexity(...)]`: require the best proven method complexity to be
  at or below the declared bound. Exceeded bounds produce `SP0021`; unknown
  bounds produce `SP0022`.

## Attribute Placement Policy

SharpProof intentionally keeps several public contract attributes declared with
`AttributeTargets.All` even though the analyzer only gives them meaning on a
smaller set of declarations. This lets unsupported placements compile far
enough for SharpProof to report fixable `SP*` analyzer diagnostics, include the
misuse in SARIF and baselines, and show the same guidance across IDE and CI
hosts instead of relying on compiler `CS0592` rejection.

The broad-usage attributes and their placement diagnostics are:

| Attribute | Analyzer placement diagnostic | Notes |
| --- | --- | --- |
| `[EnforcePure]` | `SP0003` | Analyzer-validated so misplaced purity contracts can be removed by a code fix. |
| `[Pure]` | `SP0003` | Property and indexer getter contracts are accepted; unsupported targets remain analyzer diagnostics. |
| `[ZeroAllocations]` | `SP0014` | Misplaced allocation contracts stay visible as SharpProof usage errors. |
| `[AllowedCapabilities(...)]` | `SP0017` | Capability contract placement is validated before capability reasoning runs. |
| `[Requires("condition")]` | `SP0029` | Preconditions are accepted only on method-like declarations. |
| `[Ensures("condition")]` | `SP0020` | Postconditions are accepted only on method-like declarations. |
| `[DoesNotThrow]` | `SP0031` | Exception contracts are accepted only on method-like declarations. |
| `[AllowedExceptions(...)]` | `SP0031` | Exception contracts are accepted only on method-like declarations. |
| `[ExpectedComplexity(...)]` | `SP0023` | Complexity contracts are accepted only on method-like declarations. |

Attributes whose supported target set is already stable and compiler-enforceable
remain narrowed in metadata, such as `[AllowSynchronization]`,
`[PureExternal]`, and `[Impure]`.

## Attribute Identity

SharpProof contract analyzers accept attributes from `SharpProof.Attributes` by
default. This includes the package-provided attributes and source-only stubs
declared in the same namespace for projects that cannot reference the package
directly.

Projects that intentionally keep source-only contract stubs in another
namespace can opt in with:

```ini
build_property.sharpproof_attribute_stub_namespaces = My.Contracts;Other.Contracts
```

Use `<global>` in that list only when a project deliberately declares global
namespace stubs. Attributes with SharpProof contract names such as
`EnforcePureAttribute`, `EnsuresAttribute`, `RequiresAttribute`,
`DoesNotThrowAttribute`, `AllowedExceptionsAttribute`, or
`ZeroAllocationsAttribute` from unaccepted namespaces are ignored as contracts
and reported as `SP0026`.
Recognized external purity annotations such as
`JetBrains.Annotations.PureAttribute` and
`System.Diagnostics.Contracts.PureAttribute` remain boundary evidence rather
than SharpProof contract attributes.

## Diagnostic Reference

The generated [diagnostic example gallery](diagnostic-examples.md) contains at
least one code-plus-output example for every public analyzer diagnostic from
`SP0002` through `SP0031`.

The gallery is generated from committed example inputs and committed output
snapshots, and the test suite verifies that it stays current.

Known diagnostics can be managed with
[SharpProof diagnostic baselines](baselines.md), including generation from SARIF
or current project diagnostics, match explanations, and stale-entry pruning.

SharpProof diagnostics also carry editor/tooling properties for deeper proof
inspection. Diagnostics with a source location include
`sharpproof.explain.file`, `sharpproof.explain.line`,
`sharpproof.explain.column`, and `sharpproof.explain.query`, where the query is
a ready-to-run `SharpProof.SymbolicCli explain --file ... --line ... --column ...`
command. Contract diagnostics also include
`sharpproof.explain.contract`; proof diagnostics include
`sharpproof.explain.proof_status` and, when available,
`sharpproof.explain.unknown_reason` normalized as lower snake case.

## Configuration

SharpProof uses `sharpproof_*` analyzer configuration keys. SMT-related keys
control bounded proof work, including mode, timeout, method budget, path
condition budget, and expression-node budget. Unsupported or over-budget proof
obligations remain conservative.

Scope is explicit:

- Global-only keys are read once per compilation. Set them in a global
  AnalyzerConfig file, for example `.globalconfig`, or through an MSBuild
  property exposed as `build_property.<key>`. If a global-only key appears in a
  per-tree `.editorconfig` section, SharpProof reports `SP0025`.
- Global-and-tree keys can be set globally and overridden for matching source
  files from `.editorconfig`.

| Key | Scope | Values | Default | Notes |
| --- | --- | --- | --- | --- |
| `sharpproof_known_impure_methods` | Global-only | `;`, `,`, or newline-delimited symbols | empty | Treat matching methods as impure trust boundaries. |
| `sharpproof_known_pure_methods` | Global-only | `;`, `,`, or newline-delimited symbols | empty | Treat matching methods as pure trust boundaries. |
| `sharpproof_known_impure_namespaces` | Global-only | `;`, `,`, or newline-delimited namespaces | empty | Treat matching namespaces as impure. |
| `sharpproof_known_impure_types` | Global-only | `;`, `,`, or newline-delimited type names | empty | Treat matching types as impure. |
| `sharpproof_attribute_stub_namespaces` | Global-only | `;`, `,`, or newline-delimited namespaces; `<global>` for global namespace | `SharpProof.Attributes` | Accept source-only SharpProof attribute stubs from configured namespaces. |
| `sharpproof_purity_profile` | Global-only | `strict`, `balanced`, `pragmatic` | `balanced` | Controls purity strictness for shared purity analysis. |
| `sharpproof_enable_debug_logging` | Global-only | boolean | `false` | Reserved for analyzer-host-safe debug logging. |
| `sharpproof_enable_effect_summary_json` | Global-only | boolean | `false` | Loads `*.SharpProof.EffectSummary.json` AdditionalFiles. |
| `sharpproof_smt_mode` | Global-only | `disabled`, `bounded`, `deep`, or boolean | `bounded` | Controls shared SMT proof mode. |
| `sharpproof_smt_timeout_ms` | Global-only | positive integer | mode default | Per-query SMT timeout in milliseconds. |
| `sharpproof_smt_method_budget_ms` | Global-only | positive integer | mode default | Per-method SMT budget in milliseconds. |
| `sharpproof_smt_max_path_conditions` | Global-only | positive integer | mode default | Maximum SMT path conditions per method. |
| `sharpproof_smt_max_expression_nodes` | Global-only | positive integer | mode default | Maximum SMT expression nodes per query. |
| `sharpproof_suggest_missing_enforce_pure` | Global and per-tree | boolean | `true` | Controls `SP0004` suggestions. |
| `sharpproof_suggest_missing_enforce_pure_scope` | Global and per-tree | `all`, `public`, `internal`, `off` | `all` | Narrows `SP0004` by member visibility. |
| `sharpproof_suggest_missing_enforce_pure_exclude_generated` | Global and per-tree | boolean | `false` | Suppresses `SP0004` in generated-looking files. |
| `sharpproof_suggest_missing_enforce_pure_exclude_tests` | Global and per-tree | boolean | `false` | Suppresses `SP0004` in test-looking namespaces and paths. |
| `sharpproof_suggest_missing_enforce_pure_min_complexity` | Global and per-tree | non-negative integer | `0` | Minimum inferred complexity before `SP0004` is suggested. |
| `sharpproof_suggest_missing_enforce_pure_namespace_filters` | Global and per-tree | `;`, `,`, or newline-delimited namespace prefixes | empty | Limits `SP0004` suggestions to matching namespaces. |
| `sharpproof_emit_explanations` | Global and per-tree | boolean | `false` | Emits optional `SP0009` proof explanation diagnostics. |
| `sharpproof_report_bcl_fallback_guesses` | Global and per-tree | boolean | `false` | Emits optional `SP0012` BCL fallback guess diagnostics. |
| `sharpproof_runtime_hazard_mode` | Global and per-tree | `none`, `sites`, `summaries`, `all`, or boolean | `none` | Controls runtime-hazard `SP0010`/`SP0011` reporting. |
| `sharpproof_report_exceptions` | Global and per-tree | boolean | `false` | Emits optional `SP0010` exception summary diagnostics. |
| `sharpproof_checked_exceptions` | Global and per-tree | boolean | `false` | Emits optional `SP0011` exception site diagnostics. |

Use the root README for quick start instructions and the proof-query docs when
you need to inspect why a contract failed.
