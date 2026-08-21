# 2026-08-08 relational interprocedural verification

This dated note records the first bounded relational-callee tranche. It does
not widen the general SharpProof language gate. Current behavior remains
defined by `SEMANTICS.md` and `docs/coverage-and-limits.md`.

## Semantic change

The Windows worker can now prove a caller using a quantifier-free callee
relation constructed by the build-time compiler collector. All three admitted
origins produce the same typed IR relation and enter the same Z3 obligation:

- one exact, direct, acyclic static scalar source body in the current
  compilation;
- exact implementation IL from a locked file-backed PE; or
- an explicitly enabled audited relation from the embedded schema-1
  specification-pack catalog.

This replaces a method-by-method growth path with reusable relation inference.
It does not add loops, recursion, virtual dispatch, generics, `ref` state,
references, a heap, or broad SMT theories.

## Authority and failure boundary

Source relations are compiler-bound and contribute their source-declaration
digest. Implementation relations require an external static scalar method, a
non-reference implementation assembly, a managed method body, raw metadata
equality with Roslyn's backing metadata, bounded IL and evaluation stacks, an
acyclic control-flow graph, and only the admitted scalar opcode set. Reference
assemblies, facades, missing or changed images, unresolved calls, cycles,
recursion, unsupported opcodes, and budget exhaustion abstain.

Specification packs are disabled by default. A consumer can select only an ID
from the catalog embedded in `SharpProof.Specs`; arbitrary files are never
pack authority. Pack parsing rejects unknown fields, noncanonical ordering,
invalid assembly/signature constraints, excessive depth or size, and terms
outside the closed scalar operator vocabulary. The initial `dotnet.scalar@1`
pack contains the audited `System.Math.Max(int, int)` relation.

Every summary call seals origin, SHA-256 evidence, pack identity when present,
and a canonical transitive dependency-evidence closure. The worker rejects
missing, reordered, malformed, identity-inconsistent, or implementation-IL
evidence whose digest is absent from the sealed reference-module set.
Compiler artifact schema 14, worker protocol 11, cache schema 13,
relational-summary schema 2, and specification-pack schema 1 form a deliberate
wire break.

Counterexample replay still executes only concrete whole-body IR. An executed
API-spec or relational-summary call is therefore
`Unknown(CounterexampleNotReplayable)`, never `Refuted`; an unselected modeled
call does not block replay. `Proven` still requires Z3 UNSAT and proof-core
hygiene.

## Discriminating evidence

The focused regression set covers:

- direct and two-hop source relations, with loops abstaining;
- exact external implementation IL, private transitive calls, branches, and
  unchecked Int32 wrapping;
- loop and recursion abstention plus rejection of reference-assembly body
  authority;
- mixed source/implementation composition and tampered dependency evidence;
- pack disabled/enabled behavior, unknown pack IDs, exact pack identity, and
  package-consumer deployment;
- schema-version pins and malformed relational evidence;
- a satisfiable summary-call path producing nonfatal
  `CounterexampleNotReplayable`; and
- shared relation-builder branch, composition, provenance, cycle rejection,
  and resource-bound behavior.

The final tranche is accepted only after generated-output verification, the
full Debug solution, Release acceptance, fuzz, corpus, performance, packaging,
and mutation gates pass on the same tree.
