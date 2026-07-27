# ApiSpec result-domain projection - 2026-07-25

> Dated evidence: this note records the bounded result-domain tranche as
> reviewed on 2026-07-25. At that reviewed checkpoint, the worker wrapped these
> proofs in protocol version 3 manifests with exact claim accounting; see
> [Coverage and limits](../coverage-and-limits.md).

## Scope

The out-of-process worker can use validated `ApiSpec` result facts for two
observations:

- null equality on string, reference, and array results;
- `Length` on array results.

The worker represents nullness with a Boolean proxy and array length with an
integer proxy. Each proxy constraint is an ordinary `SpecJustification` tied to
the resolved `ApiSpec` row. This keeps the SMT backend within its existing
Boolean/integer theory and keeps the proof core accountable to spec evidence.

`IntervalDomain` supplies source-width and length bounds,
`NullnessDomain` supplies the closed null/non-null refinement, and
`SequenceCardinalityDomain` supplies empty, non-empty, and exact array-length
facts. `Array.Empty<T>` is the first sequence-valued row exercised end to end.
Its normal-return nullness and cardinality have executable runtime witnesses
and resolve against every supported reference surface. The effects of both
`Array.Empty<T>` and `Enumerable.Empty<T>` are `Unknown`: acquiring a cached
generic singleton can trigger type initialization, and shallow runtime
observation does not establish semantic purity. Allocation also remains
`Unknown`.

## Fail-closed boundary

- Only calls resolved by exact compiler symbol identity are eligible.
- Calls normally require `SpecEffect.None`. One narrow partial-correctness path
  accepts an effect-unknown call only when it is a direct static nullary call
  returning an array and its exact spec has explicit non-null/cardinality
  result facets but no postconditions. The frontend still emits its memory
  havoc. The executor consumes only the immediately adjacent, memory-only,
  empty-variable havoc with the same operation identity; every other havoc,
  intervening instruction, load, or store remains unsupported.
- This exception uses only normal-return result facts. It neither treats the
  call as pure nor preserves claims about heap or ambient state across it.
- Cardinality is projected only for array-backed sequence IR and only with an
  explicit non-null result facet.
- `IEnumerable<T>` is not treated as executable sequence IR.
- Unknown, inapplicable, contradictory, or type-mismatched facets add no proof
  fact or make the call unsupported.
- Aliases are supported only where ordinary worker substitution reduces the
  observation to the exact call-result variable. Heap state, element contents,
  sequence access, arbitrary reference equality, source-callee summaries,
  loops, and general points-to reasoning remain unsupported.
- A counterexample involving a spec-modeled call result is still withheld when
  concrete replay cannot validate it.

This tranche does not implement roslyn-analyzers entity/points-to framework
integration or general modular source-callee assume/guarantee.

## Executable regressions

- Worker tests prove `string.Concat` non-nullness and `Array.Empty<T>`
  non-nullness/zero length with spec-only proof cores.
- A worker regression keeps `Enumerable.Empty<T>` cardinality unsupported.
- A regression confirms the one-shot havoc allowance does not authorize a
  later impure call.
- Projection tests cover unknown facts, type mismatches, non-null cardinality
  preconditions, and conservative interval bounds.
- Runtime-oracle registry tests cover exactly the known, executable facets;
  unknown effect and allocation facets intentionally have no runtime witness.
- Existing cross-TFM resolution tests cover the new row.
