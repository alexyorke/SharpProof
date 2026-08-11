# Coverage authority and TCB ownership - 2026-08-08

## Decision

At the time of this decision, coverage measured every production
source-owning assembly and exempted the linked-source
`SharpProof.PortableAnalyzer` packaging project. That project was subsequently
deleted when analyzer implementations moved into `SharpProof.Analyzer.Core`;
the current coverage baseline has no linked-source exception.
`SharpProof.Gates` remains an explicit additional measured assembly.
An architecture test requires exact equality between that project universe
and the checked-in coverage baseline.

`SharpProof.BuildTasks` and `SharpProof.Summaries` were previously absent from
both project and aggregate coverage. Their first measured floors are now
checked in. The relational summary data model, builder, instantiator, compiler
provider, exact implementation-IL lowerer, audited specification-pack
provider, semantic terms, and pack catalog are also explicitly owned by the
canonical trusted-computing-base inventory. Every trusted-mutation target must
now be present in that inventory.

The changed-TCB gate now enforces its declared 90% line threshold. It no
longer combines that threshold with an undocumented zero-uncovered-lines
condition that made the effective threshold 100%. The summary continues to
record every uncovered changed line. Mutation evidence, deterministic
acceptance, and the per-project floors remain independent gates.

## Evidence

Behavioral tests were added for:

- cancellation-filter and worker-boundary analyzer semantics;
- Windows publication identity, invalidation, and protocol rejection paths;
- scalar, branch, local, and wide-operand implementation-IL summaries;
- audited specification-pack parsing, instantiation, and malformed evidence;
- semantic claim identity across richer Roslyn operation kinds; and
- relational-summary public guards, malformed signatures and environments,
  resource limits, and receiver instantiation.

The malformed specification-pack matrix exposed one production defect:
`RequiredInt32` called `JsonElement.TryGetInt32` on non-number values, allowing
an `InvalidOperationException` to escape the typed invalid-data boundary. The
parser now rejects non-number JSON as `InvalidDataException`.

No supported proof rule was broadened. These changes strengthen fail-closed
parsing and make coverage and TCB ownership match the semantic authority that
already existed in production.
