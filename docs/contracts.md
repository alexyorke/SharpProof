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
- `[Ensures("condition")]`: require every reachable completion site to prove a
  C#-like postcondition, including return sites and block-bodied
  void/constructor fall-through and expression-bodied void/constructor
  completions. Conditions can reference `result` for value-returning members,
  the annotated member's parameters including `out` and `ref` parameters, and
  current-instance fields/properties through `this` or implicit member access.
  Supported postcondition predicates include nullable `HasValue`/`Value`,
  array `Length`, and exact `List<T>.Count` facts from parameterless list
  constructions and collection initializers when the symbolic state can prove
  them. Conditions can use `old(...)` to snapshot supported parameter
  expressions and current-instance member reads at method entry; simple
  self-referential `ref` parameter updates can be proven against those
  snapshots when the symbolic state preserves the entry relation.
  Failures produce `SP0018`; unsupported conditions produce `SP0019`.
  Parameter, result, and member nullness also consume the shared Roslyn and
  `System.Diagnostics.CodeAnalysis` contract model described in
  [shared nullable-flow facts](nullable-flow-facts.md).
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

CodeAnalysis nullable attributes are compiler contracts rather than SharpProof
attributes, but their facts are shared across analyzer families. `AllowNull`,
`DisallowNull`, `MaybeNull`, `NotNull`, their conditional variants, member
postconditions, and non-returning contracts are summarized in
[the nullable-flow contract guide](nullable-flow-facts.md).

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
`SP0002` through `SP0033`.

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
`SP0032` analyzer-input diagnostics include
`sharpproof.additional_file.path`, `sharpproof.additional_file.reason`, and
`sharpproof.additional_file.reason_code`; stale effect-summary reason codes
distinguish assembly, module, method, method-body, framework, and package-source
mismatches.

## Configuration

SharpProof uses `sharpproof_*` analyzer configuration keys. SMT-related keys
control bounded proof work, including mode, timeout, method budget, path
condition budget, expression-node budget, transient retry count, and thread
context recycling. Unsupported or over-budget proof obligations remain
conservative. See [SMT lifecycle and health](smt-lifecycle.md) for recovery and
cleanup behavior.

Project-aware CLI and API queries preserve the build's references, parse and
compilation options, analyzer configuration, baselines, and effect-summary
AdditionalFiles. See [project-aware proof queries](project-aware-queries.md).

Scope is explicit:

- Global-only keys are read once per compilation. Set them in a global
  AnalyzerConfig file, for example `.globalconfig`, or through an MSBuild
  property exposed as `build_property.<key>`. If a global-only key appears in a
  per-tree `.editorconfig` section, SharpProof reports `SP0025`.
- Global-and-tree keys can be set globally and overridden for matching source
  files from `.editorconfig`.

The complete generated reference includes parser-valid values, exact defaults,
related diagnostics, and copyable global/per-tree samples:
[Analyzer configuration reference](configuration-reference.md).

Use the root README for quick start instructions and the proof-query docs when
you need to inspect why a contract failed.
