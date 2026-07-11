# Potential Bugs - Triaged Backlog

Last triaged: 2026-07-10

This file contains only code-confirmed behaviors that still need a regression
test or a deliberate behavior change. Original audit numbers are preserved so
old review notes remain searchable.

An entry belongs here only when it names a concrete code path and an observable
correctness, evidence, reliability, or bounded-resource risk. File size, style,
future-proofing, and unmeasured performance concerns belong in `PLAN.md` or a
profile-backed issue instead.

## P1 - Proof Correctness And Host Reliability

These can change a purity or reachability conclusion, omit a real hazard, or
grow process state across a long-running host. Add a failing regression before
changing proof behavior.

| Audit ID | Code-confirmed behavior | Required closure |
| --- | --- | --- |

## P2 - Precision And Evidence Quality

These are conservative or output-quality gaps. They should not outrank a P1
item unless a regression shows a wrong proof rather than an `Unknown` result.

| Audit ID | Code-confirmed behavior | Required closure |
| --- | --- | --- |

## Disposition Of The 2026-07-07 Audit

All 123 entries present at the start of this triage are accounted for below.

### Fixed Or Covered By Regression Tests

IDs: 1, 5, 6, 7, 11, 15, 20, 31, 35, 40, 45, 48, 51, 52, 54, 59, 60, 61,
63, 65, 67, 70, 71, 72, 73, 77, 78, 85, 86, 89, 90, 93, 94, 96, 98, 100, 101, 102, 103, 106, 107,
115, 119, 124, 125, 126, 135, 136, 137.

Notable closures include stable public unknown reasons, bounded-analysis
truncation evidence, SMT retry/recycle health controls, immutable catalogs,
validated embedded summaries, symbolic index identity, member-specific state
invalidation, job-bounded VSIX builds, and staged NuGet publication.

This triage added or confirmed regressions in commits `223c846b`, `08bb0e25`,
`f18d0ad3`, and `1f731cbd`.

### Maintenance Or Existing Roadmap Work, Not Standalone Bugs

IDs: 8, 9, 16, 17, 18, 19, 22, 25, 27, 38, 43, 56, 57, 74, 75,
79, 80, 81, 83, 87, 92, 104, 138, 139, 140, 141, 142, 144, 146,
149.

These cover file decomposition, fallback inventory, prospective assertions,
unmeasured performance, test-host cleanup, and packaging architecture. The
actionable packaging and concurrency portions already have dedicated high
priority items in `PLAN.md`; the rest require a profile or failing test before
they should become implementation work.

### Disproved, Duplicate, Or Intentional Conservative Behavior

IDs: 12, 14, 21, 24, 28, 29, 30, 32, 33, 36, 37, 39, 41, 42, 44,
46, 49, 50, 53, 55, 58, 66, 76, 82, 84, 88, 91, 99, 105, 108, 109,
114, 116, 118, 121, 122, 131, 132, 134, 143, 145, 147, 148, 150.

Representative findings:

- resource-budget conversion saturates before its unsigned cast;
- `global.json` explicitly rolls forward to the latest feature band;
- a missing delegate-map entry during `+=` means the prior invocation list is
  unknown, so `Unresolved` is conservative;
- merging `Unresolved` delegate targets is absorbing and order-independent;
- formula fallback and unsupported nonlinear updates intentionally return
  conservative evidence instead of inventing facts;
- 32-bit subsequence limits model C# string/array index APIs, not arbitrary SMT
  integers;
- a negated conjunction cannot be flattened into independent public facts
  without changing its meaning;
- CLI console redirection is protected by an inner `finally`, analyzer options
  are immutable, and deterministic fuzz seeds are intentional reproducibility.

When a removed concern gains a minimal reproduction, re-add it under P1 or P2
with the expected observable result and the exact test needed to close it.
