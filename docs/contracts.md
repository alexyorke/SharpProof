# SharpProof Contracts

SharpProof's primary user surface is analyzer contracts: attributes that turn a
bounded proof question into normal build diagnostics.

## Contract Attributes

- `[EnforcePure]` / `[Pure]`: require a method-like member or property/indexer
  getter to be proven pure.
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
  in the annotated method-like body or aliased property/indexer getter.
  Violations produce `SP0013`.
- `[AllowedCapabilities(...)]`: restrict proven side-effect capabilities such
  as `IO`, `Console`, `FileRead`, `FileWrite`, `Network`, `Process`,
  `Environment`, `Reflection`, and `NativeInterop`. Violations produce
  `SP0015`; unverifiable operations produce `SP0016`.
- `[DoesNotThrow]`: require that no exception escapes the annotated method-like
  body or aliased property/indexer getter. Escaping exceptions produce
  `SP0030`.
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
| `[EnforcePure]` | `SP0003` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[Pure]` | `SP0003` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[ZeroAllocations]` | `SP0014` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[AllowedCapabilities(...)]` | `SP0017` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[Requires("condition")]` | `SP0029` | Preconditions remain on method-like declarations, including an explicit `get` accessor. |
| `[Ensures("condition")]` | `SP0020` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[DoesNotThrow]` | `SP0031` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[AllowedExceptions(...)]` | `SP0031` | Getter-bearing property and indexer aliases apply to the getter only. |
| `[ExpectedComplexity(...)]` | `SP0023` | Getter-bearing property and indexer aliases apply to the getter only. |

A contract placed on a getter-bearing property or indexer is an ergonomic alias
for its getter. It never constrains the setter. Expression-bodied members alias
their implicit getter, and accessor-list members alias their explicit `get`.
Setter-only properties and indexers are not valid alias targets.

```csharp
public sealed class Constants
{
    [EnforcePure]
    [ZeroAllocations]
    [AllowedCapabilities(SharpProofCapability.None)]
    [Ensures("result == 42")]
    [DoesNotThrow]
    [ExpectedComplexity(ComplexityKind.Constant)]
    public int Answer => 42;

    [Pure]
    [AllowedExceptions(typeof(System.ArgumentException))]
    public int this[int index]
    {
        [Requires("index >= 0")]
        get => index;
    }
}
```

`[Requires]` deliberately remains accessor-level for properties and indexers
because its call-site semantics differ from a getter effect contract. The
`SP0029` code fix moves a property/indexer-level `[Requires]` to an existing
getter, or creates a getter around an expression body. Auto-property getters
support exact effect aliases such as purity, zero allocations, no capabilities,
constant complexity, and no escaping exceptions. An auto-property `[Ensures]`
remains conservative and reports `SP0019` because its result expression is not
source-visible.

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
`SP0002` through `SP0039`.

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

### Inferred contract suggestions

Contract adoption hints are opt-in and informational by default. Set
`sharpproof_suggest_inferred_contracts = true` to enable `SP0034`-`SP0039` for
methods whose current bounded evidence supports a reviewable contract. Each
diagnostic includes the exact proposed attribute, confidence, evidence summary,
baseline identity, and an add-attribute code fix.

Use `sharpproof_suggest_inferred_contracts_scope` (`all`, `public`, `internal`,
or `off`) to limit member visibility. Use
`sharpproof_suggest_inferred_contracts_kinds` to select any of
`zero-allocations`, `capabilities`, `complexity`, `exceptions`, `ensures`, and
`requires`. `sharpproof_suggest_inferred_contracts_minimum_confidence` accepts
`high` or `medium` and defaults to `high`.

High-confidence candidates require closed evidence: no visible allocation
sites, exact capability or complexity results with no unknowns, a trivial
non-throwing body, identical simple return facts, or a leading parameter guard
that throws. Medium confidence is currently reserved for a finite bounded set
of resolved exception types. Unsupported, conservative, recursive, or unknown
symbolic results do not produce a suggestion. The feature defaults to off, and
the bundled profiles never promote these adoption hints to errors.

### Exact-proof external diagnostic suppression

`sharpproof_suppress_proven_diagnostics = true` opts into the packaged Roslyn
`DiagnosticSuppressor`. It can suppress only a static allowlist of non-error
compiler and third-party analyzer IDs when the matching runtime-hazard trigger
is proved unreachable at the same source span with concrete, non-truncated
evidence. `sharpproof_suppression_diagnostic_ids` narrows that allowlist.
Unknown, unsupported, timed-out, over-budget, and truncated proofs leave the
original diagnostic visible. The bundled profiles keep suppression disabled.

See [exact-proof diagnostic suppression](proven-diagnostic-suppression.md) for
the `SPS*` descriptors, supported external IDs, exact proof gate, audit trail,
and proof-query workflow.

The complete generated reference includes parser-valid values, exact defaults,
related diagnostics, and copyable global/per-tree samples:
[Analyzer configuration reference](configuration-reference.md).

Use the root README for quick start instructions and the proof-query docs when
you need to inspect why a contract failed.
