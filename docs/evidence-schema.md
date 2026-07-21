# Effect evidence schema

Canonical evidence lives in `MethodEffects`. Each site records its primitive
effect, typed origin, capability flags, exception type, transitive source, escape
status, proof status, operation, symbol, reason, and source span. Structured
exception facts distinguish explicit throws, runtime hazards, callees, metadata,
and contracts.

Analyzer diagnostics project those facts into `sharpproof.effects.*`,
`sharpproof.exceptions.*`, baseline, and explain properties. Corpus tooling reads
the generic effect fields; it does not use purity catalogs or member-name tables.
CLI JSON is the serialized `SharpProofAnalysisResult`, not a parallel report
schema.
