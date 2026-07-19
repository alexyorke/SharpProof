# Semantic Pipeline Preview Migration

This preview replaces independently implemented source, proof, purity,
configuration, and metadata decisions with one bounded pipeline:

```text
Roslyn source -> typed Symbolic IR -> normalized state -> proof orchestration -> SharpProof.ProofCore/Z3 -> diagnostics/API/CLI
```

Diagnostic IDs and SharpProof attribute type names remain stable. The changes
below are intentionally breaking for persisted evidence, effect summaries,
configured member allowlists, and proof-result consumers.

## Evidence schema 2

Analyzer evidence now requires `evidenceSchemaVersion: 2` and
`evidenceSchemaCompatibility: exact-v2`. Analyzer readers reject missing,
version-1, mismatched, and future evidence with `SP0032` instead of interpreting
it loosely.

Migrate an existing diagnostic baseline explicitly:

```powershell
dotnet run --project Tools/SharpProof.Baseline -- migrate --baseline SharpProof.Baseline.json --output SharpProof.Baseline.json
```

The retained migration command validates and normalizes current version `2`
baselines. Pre-release unversioned and additive-v1 inputs must be regenerated
from current SARIF; neither the tool nor analyzer carries legacy readers.

## Effect-summary schema 5

Regenerate every schema 1-4 effect summary with the current
`Tools/SharpProof.EffectSummary` tool. Active analyzer readers accept only
schema 5.

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 run --project Tools\SharpProof.EffectSummary\SharpProof.EffectSummary.csproj -- --assembly path\to\Library.dll --output artifacts\effect-summary\Library.SharpProof.EffectSummary.json
```

Schema 5 replaces display and exact-symbol aliases with a structural
`Identity` and one `CanonicalKey`. The identity includes containing metadata
type, method kind/name, generic arity, parameter types and ref kinds, return
type, and return ref kind. Exception provenance now has separate nullable
`SourcePath`, typed `CallChain`, and nullable `CalleeIdentity` fields.
Framework and package provenance is trusted only when the actual assembly
source can be established.

## Configuration and CLI modes

`sharpproof_smt_mode` and CLI `--smt-mode` accept only `disabled`, `bounded`,
or `deep`. Boolean, `off`, `default`, and `aggressive` aliases are rejected.
`sharpproof_runtime_hazard_mode` accepts only the values listed in the
generated [configuration reference](configuration-reference.md).

`sharpproof_known_pure_methods` and `sharpproof_known_impure_methods` now
accept only schema-v5 structural `CanonicalKey` values. Copy the key from a
fresh effect summary. For a property accessor, append `.get` or `.set` to the
corresponding accessor method key. Display strings, shortened names, generic
aliases, nullable-stripped aliases, and bare property names produce `SP0025`
and do not influence classification.

## Property policy

Property-level verification and pure-trust attributes apply to the getter.
Property-level `[Impure]` applies to both accessors. An attribute written on an
accessor applies only to that accessor. Code fixes resolve the exact attribute
symbol and can edit accessor attribute lists without removing same-named
unrelated attributes.

## Proof API and behavior

Lowering reports `Exact`, `Approximate`, or `Unsupported` with provenance and
one stable unknown reason. Exact typed IR is authoritative; approximate data
may strengthen diagnostics but cannot prove reachability or purity.

`PurityProofResult` exposes `PathCheck` and `ImpurityCheck` as
`ProofCheckInfo { WasAttempted, Feasibility, Witness }`. Public symbolic proof
records include the stage and support level that stopped or completed the
single pipeline. Normalization happens before configurable node/path budgets;
the hard pre-normalization depth guard has a distinct reason.

The old source-to-formula translators, formula-to-IR production round trips,
migration selector, and shadow pipeline have been removed. Consumers should
use typed symbolic facts/conditions and the public query services rather than
constructing or interpreting legacy SMT-shaped source models.

## ProofCore assembly rename

The private solver implementation assembly has moved from `SearchLib.dll` to
`SharpProof.ProofCore.dll`, and its namespaces now begin with
`SharpProof.ProofCore`. Package and analyzer payloads contain the renamed
assembly. Consumers that manually inspect or load package assets must update
the file name; normal `SharpProof.Symbolic` API and CLI consumers require no
code or configuration change and should not reference ProofCore directly.

## Validation after migration

Run the generated-document checks, warning-free release validation, all test
lanes, and package consumers:

```powershell
.\scripts\Generate-Readme.ps1 -Verify
.\scripts\Generate-ConfigurationReference.ps1 -Verify
.\scripts\Invoke-SharpProofReleaseValidation.ps1 -Configuration Release -NoRestore
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All
.\scripts\Test-SharpProofPackageConsumers.ps1 -Configuration Release
```
