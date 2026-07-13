# SharpProof module and dependency layers

The checked source of truth is
[`scripts/architecture-modules.json`](../../scripts/architecture-modules.json).
Every handwritten or generated production C# file must match exactly one module
path rule. Project references must name an allowed dependency and point to the
same or a lower layer.

| Layer | Responsibility | Modules |
| --- | --- | --- |
| 0 | Contracts and shared structural identities | Attributes, Shared |
| 1 | Formula reasoning and solver ownership | ProofCore |
| 2 | Symbolic domain, lowering, flow, and typed queries | Symbolic |
| 3 | Roslyn analyzer composition and diagnostics | Analyzer |
| 4 | Code-fix and executable adapters | CodeFixes, EffectSummary, SymbolicCli, FuzzCore, FuzzCli, BaselineCore, BaselineCli, CorpusReportCore, CorpusReportCli, VsixHarness |
| 5 | Packaging, IDE distribution, samples, and consumers | Packaging, VSIX, Samples, PackageConsumers |

Shared files are linked source rather than an assembly. They may define schema,
identity, and framework-model data used by multiple assemblies, but must not
depend on analyzer, symbolic, solver, or tool implementation types. ProofCore
must not reference Symbolic, Analyzer, or tools. Symbolic may reference
ProofCore. Analyzer may reference Symbolic and ProofCore. Adapter and packaging
layers may reference only the modules listed in the manifest.

`Get-SharpProofProductionMetrics.ps1` validates coverage, ambiguity, dependency
direction, and allowed project references while reporting the size gates.
