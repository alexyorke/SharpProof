# Solver witnesses and input domains

Z3 satisfying assignments, counterexamples, and synthesized input domains are
core analysis evidence. SharpProof keeps their typed internal representation so
analyzer projections and tests can distinguish exact, approximate, unsupported,
and absent witnesses.

A satisfying assignment is one model accepted by the bounded solver, not the
complete input set. Domain synthesis conservatively summarizes supported facts
such as integer bounds, nullness, string predicates, collection lengths, and
index relationships. Unsupported translations, timeouts, solver failures, and
budget exhaustion stay unknown.

The supported `SharpProofAnalysisResult` does not serialize full solver models
or domain graphs. Each `SharpProofProofFact` exposes only:

- the source condition and compact status;
- the proof reason and relevant symbolic condition;
- an optional summarized counterexample such as `value=0`.

Runtime hazards use the same approach: the result contains the hazard status,
reason, source span, and optional compact trigger counterexample. Full
`SymbolicInputWitness`, satisfying assignments, domain summaries, Z3 proof
results, cache metadata, and solver diagnostics remain inside the analysis
pipeline and retain dedicated test coverage.

This split keeps the default CLI JSON stable and small without weakening the Z3
proof path or discarding evidence needed for diagnostics and validation.
