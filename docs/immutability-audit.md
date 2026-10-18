# Comprehensive immutability audit

Baseline: origin/master, commit 4d456246a4113c6c8f2ff0728ddd03bd684b10e1.
Branch: codex/comprehensive-immutability.

## Scope and ownership

The audit covers all 2,301 baseline tracked files, including 1,805 C# files.
Ten Luna agents at max reasoning perform read-only audits. The coordinator alone
writes this inventory and source, checks findings against definitions and callers,
and runs verification in the canonical Linux amd64 container.

Use immutable collections and value records for fixed data and snapshots, readonly
for construction-only fields, and const for compile-time values. Preserve the
behavior of mutable test subjects, including embedded source programs. Resource
handles, external setter contracts, and stateful algorithms require explicit review;
a readonly collection reference alone does not make its contents immutable.

The existing original checkout is separate and has not been edited by this work.
No vendored code will be changed.

## Verification

- Baseline canonical container solution build: passed, 0 warnings, 0 errors.
- Baseline source inventory: 2,301 files / 1,805 C# files.

## Confirmed violations

The following findings were traced by the coordinator before implementation.
Line numbers refer to the baseline. Status is updated after validation.

| ID | Baseline file / line | Violation and correction | Status |
| --- | --- | --- | --- |
| IMM-001 | `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:39` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-002 | `SharpProof.CompilerCollector/CompilerArtifact/CompilerTotalIlBodyProvider.cs:238` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Open |
| IMM-003 | `SharpProof.Gates/Corpus/AllocationIlOracle.cs:10` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Open |
| IMM-004 | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:10` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-005 | `SharpProof.Host/TrustedChildEnvironment.cs:8` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-006 | `SharpProof.Host/TrustedChildEnvironment.cs:23` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-007 | `SharpProof.Testing/WellSortedIrGenerator.cs:51` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-008 | `SharpProof.Testing/WellSortedIrGenerator.cs:55` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-009 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:527` | Fixed enum membership stored in a mutable HashSet; use ImmutableHashSet. | Open |
| IMM-010 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:702` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-011 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:715` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-012 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:731` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-013 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:740` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-014 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:749` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-015 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:755` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-016 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:785` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-017 | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:27` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-018 | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:657` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-019 | `Tools/SharpProof.Fuzz/TotalProgramFuzzing.cs:47` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |

## Audit coverage and remaining review

The ten partitions are under review. Additional findings and explicit exclusions
will be reconciled here before the audit is considered complete. This inventory
is not yet a claim that every violation has been identified.
