# SharpProof ordinary correctness and reliability audit

## Scope and method

This report covers ordinary correctness and reliability behavior across the
complete repository. Earlier rounds concentrated on verifier process
supervision and its connected build-task, launcher, publication, protocol,
cache, and Linux-worker paths. The final exhaustive round inspected every
tracked file at commit `8a5141d7d8772d1e9659099531086d156ea11e92` except this
report: 833 files and 248,733 physical lines. Its scope included production
code, tests, analyzers, build and release infrastructure, scripts, container
configuration, samples, specifications, and contract documentation.

Ten read-only audit shards inspected non-overlapping file manifests line by
line. The main audit independently traced every candidate through reachable
control flow, checked its documented contract, searched for duplicate root
causes, and classified unsupported or disproved leads separately. Findings are
static proofs unless an isolated or canonical-container reproduction is
explicitly recorded. Cybersecurity, hacking, adversarial hardening, and other
non-routine threat work remained out of scope.

## Remediated findings

The confirmed findings formerly listed as bugs
1-23, 25-36, 38-57, 59-103 were fixed and regression-checked on this
branch. Their individual reports are intentionally removed from the active
bug log after verification. The implementation spans the prior remediation
commits and this final pass, including timeout-budget reservation, Linux
worker deadline classification, project-relative SARIF paths, the trusted
computing-base inventory pin, and tracked OpenCode dependency manifests.
See Validation below for canonical-container evidence.
## Exhaustive repository audit coverage

The final round used ten non-overlapping, read-only manifests. Every manifest
was reported complete, no file appeared in two shards, and the combined ledger
matched every tracked file except `BUGS.md`.

| Shard | Files | Physical lines |
| --- | ---: | ---: |
| Scripts A | 45 | 17,727 |
| Scripts and infrastructure | 190 | 30,114 |
| Analyzers and meta-analyzers | 80 | 27,649 |
| Compiler pipeline | 55 | 22,196 |
| Effects | 56 | 26,579 |
| Worker tests | 28 | 25,610 |
| Worker and package paths | 67 | 26,555 |
| IR, frontend, and gates | 127 | 31,325 |
| Runtime and tooling | 90 | 21,842 |
| Contracts and specifications | 95 | 19,136 |
| **Total** | **833** | **248,733** |

Verification used four gates before accepting a finding:

1. Trace a normal, reachable trigger from public or in-repository entry points.
2. Compare the behavior with source comments, tests, specifications, and
   repository contracts.
3. Follow the complete relevant control and data flow, including downstream
   validators and cleanup.
4. Search this report for an existing root cause, then independently classify
   severity and confidence.

The initial documentation round accepted 14 independent findings: three High,
ten Medium, and one Low. The subsequent remediation pass verified and fixed
all confirmed findings listed above, adding focused regression coverage where
the existing suite did not already exercise the corrected path. It also
refreshed the trusted-computing-base inventory pin and tracked the OpenCode
dependency manifests required for a fresh checkout.

## Rejected and unconfirmed exhaustive-audit leads

- The proposed performance-probe cancellation race was retracted after tracing
  the synchronous portion of the async chain. `VerifyAsync` reaches the
  backend and sets the probe's entry signal before its first incomplete await,
  so the outer wait cannot abandon the call in the reported state.
- The proposed Linux backslash-path defect was disproved in the canonical
  container. PowerShell normalized `Join-Path` with `..\..` to
  `/workspace/SharpProof`, and `Get-Content` successfully read
  `/workspace/SharpProof/eng\acceptance\contract.json` (39,366 bytes).
- The unused mandatory `RunAttempt` field in
  `GitHubEvidenceArtifact` remains unconfirmed. No reachable
  in-repository caller of the helper was found, so the audit could not establish
  a supported trigger or user-visible failure.

## Post-rebase triage ledger

- Accepted three new High/P1 roots in the only production file changed by the
  squashed merge relative to the audited verifier-supervisor tip (bugs 71-73),
  and fixed all three with focused regressions. The final split convergence
  audit reported no other supported-path finding besides bug 73.
- Exact-commit package-shard validation then reproduced the previously reported
  sequential-disposal race (bug 45). It was fixed with a deterministic retained
  reader lifetime regression before validation resumed.
- The linked-worktree Docker investigation also rejected a proposed network
  bootstrap workaround because it would regress offline archive commands and
  configured non-`master` origins. That experiment was removed and is not part
  of the branch diff.

## Round 17 triage ledger

- No new P0, P1, or P2 root was accepted, and no supplied report was grouped
  into an existing root.
- `P20-R17-C1` is rejected. On ordinary publication-lock timeout,
  `PublicationLock.Acquire` returns false and `AcquirePublicationSet` converts
  that result to `IOException`. The `RunMain` publication boundary catches
  `IOException`, reports that the worker result could not be published, and
  returns exit 3. Its omission of `InvalidOperationException` therefore does not
  let the alleged timeout escape; bug 52 remains the distinct late-success
  timeout defect.
- Groups A, B, C, and E reported no candidates, as did the remaining Group D
  partitions. All 30 fixed partitions produced no new reachable P0-P2 root.

## Round 16 triage ledger

- Accepted `P12-C1` as one new Medium/P2 root (bug 70). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P30-C1` is rejected. `AtomicFile` stages beside the destination and
  `File.Replace`/`File.Move` is the final fallible publication action. After a
  successful rename the temporary path is absent, so the `finally` block has no
  deletion to perform; there is no directory sync, validation, or other
  post-commit operation that can normally fail and report an error for an
  already-published file. Failed-operation cleanup masking remains bug 10, not a
  successful-publication ambiguity.
- Groups A, C, and D reported no candidates, as did the remaining Group B and E
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 15 triage ledger

- Accepted `P12-C1` as one new Medium/P2 root (bug 69). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P17-C1` remains rejected. The transitive targets are evaluated after the
  project body, recompute `_SharpProofToolsDirectory` and all derived runtime
  paths, and require them to equal the package-owned closure before
  publication. The repository also has explicit project-body override coverage;
  public runtime-closure overrides are intentionally unsupported rather than a
  stale derived-property defect.
- Groups A, D, and E reported no candidates, as did the remaining Group B and C
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 14 triage ledger

- Accepted `P19-C1` as one new Medium/P2 root (bug 68). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P02-C1` is rejected. `WriteCleanupReceipt` runs after descendant cleanup and
  emits an unconditional separator newline before the exact authenticated
  receipt line, then flushes. The bounded reader authenticates only a complete
  exact line, and the repository's unterminated-verifier-output contract test
  exercises this framing, so a normal final output fragment cannot concatenate
  with the cleanup record.
- Groups B, C, and E reported no candidates, as did the remaining Group A and D
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 13 triage ledger

- Accepted `P28-C1` as one new Medium/P2 root (bug 67). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P29-R13-C1` remains rejected. Before launching the worker, the launcher
  successfully deserializes and invokes the same deterministic
  `DecodeCallables` implementation on the canonical compiler artifact. The
  worker then binds its read to the launcher's SHA-256 and repeats canonical
  deserialization before decoding; any ordinary byte change is rejected at that
  earlier boundary. No supported writer or state change can therefore make the
  second decode newly fail and overwrite an interruption.
- `P01` through `P24` and the remaining Group E partitions reported no
  candidate. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 12 triage ledger

- Accepted as four new Medium/P2 roots: the grouped `P01`/`P02` absolute-deadline
  arbitration root, `P06`, `P29-C2`, and `P30-C1` (bugs 63 through 66). No new
  P0 or P1 root was accepted.
- Grouped the unsupported-host build-invalidation facet into bug 24 and the
  tokenless cache-capacity scan into bug 28; `P01` and `P02` are one shared
  deadline/arbitration root rather than duplicate entries.
- `P17` remains rejected because target evaluation recomputes and validates the
  exact package-owned runtime closure. `P29-C1` does not establish a separate
  post-cancellation dispatch failure beyond the checked/caught interruption
  paths. The remaining fixed partitions produced no new reachable P0-P2 root;
  in total, 25 of the 30 partitions added no new root.

## Round 11 triage ledger

- Accepted as five new Medium/P2 roots: `P11`, `P13`, `P21`, `P28`, and `P29`
  (bugs 58 through 62). No new P0 or P1 root was accepted.
- No supplied Round 11 report was grouped into an existing root; each accepted
  control-flow failure has a distinct commit, timing, output, projection, or
  cache-transaction boundary.
- The remaining 25 fixed partitions produced no new reachable P0-P2 root. The
  repeated `P24` private-helper report remains rejected because partial class
  declarations expose those helpers to their generated wrappers; all other
  no-new partitions reported no candidate.

## Round 10 triage ledger

- Accepted as eight new Medium/P2 roots: `P01`, `P06`, `P12-C1`, `P19`, the
  grouped `P25`/`P28` association root, `P29-C1`, `P29-C2`, and `P30` (bugs 50
  through 57). No new P0 or P1 root was accepted.
- Grouped two supplied facets rather than adding roots: `P12-C2` broadens bug 18
  with absent case-fold aliases, and `P28` shares bug 54's missing association
  indexes with `P25`.
- The remaining 22 fixed partitions produced no new reachable P0-P2 root.
  `P24` was explicitly rejected because the relevant partial test wrappers do
  expose the private helpers on which that report's inaccessibility premise
  depended; the other reported partitions had zero new candidate.

## Round 9 triage ledger

- Accepted as one new root: `P18-C1` as Medium/P2 (bug 49).
- Grouped two supported facets into existing roots: marker-owned non-regular
  members during launcher publication broaden bug 33, and the post-exit-124
  result deletion broadens bug 25. Neither is a separate root.
- Rejected or no-new outcomes: `P03` is the intentional legacy rightmost-marker
  grammar and current producers use the structured diagnostic transport;
  `P07` through `P12` produced no other root; `P17` cannot override the exact
  package runtime closure; `P18-C2` is blocked by existing-destination ownership,
  worker-tree topology checks, and required native-Z3 preexistence; and the final
  triage cohort reported no candidate.

## Round 8 triage ledger

- Accepted as new roots: `P02` and `P18` as High/P1 (bugs 45 and 46), and
  `P24` and `P29` as Medium/P2 (bugs 47 and 48). The `PdbFile` and
  `_DebugSymbolsIntermediatePath` omissions are two configuration facets of the
  single compiler-inventory root in bug 46.
- No new root was accepted from `P07` through `P12`, `P19`, or `P28` (eight
  rejected/no-new partitions). `P07` through `P12` reported no reachable new
  issue; partial-overlap publication sets in `P19` are explicitly unsupported;
  and the production SMT model extractor in `P28` emits only Boolean and integer
  values, returning malformed-result for other types rather than producing the
  proposed blank string.
- Bug 45 narrows two prior rejections: arbitrary faulted retained output is not
  dereferenced, and concurrent disposal remains unsupported, but the concrete
  task-owned signal fault arises after supported sequential post-`Execute`
  disposal and is converted into failed cleanup authentication.

## Round 7 triage ledger

- Accepted as new roots: `P18` as High/P1 (bug 43) and `P22` as Medium/P2
  (bug 44). The raw `MakeDir` and compiler-output `Include` reports are two
  grouped facets of bug 43, not additional roots.
- No new root was accepted from `P03`, `P07` through `P12`, `P28`, or `P29`
  (nine rejected/no-new partitions). `P03` uses a fixed outer reserve and retains
  cleanup ownership intentionally; `P07` through `P12` reported no reachable new
  issue; `P28` has no production producer beyond malformed input; and supported
  `P29` timeout/cancellation is contained by private-result deletion and the
  launcher/parent-death boundary.
- The accepted round-7 `P18` is a newly supplied MSBuild list-expansion candidate;
  it is not one of the earlier audit reports that happened to use labels in the
  `P13`-through-`P18` range.

## Round 6 triage ledger

- Accepted as new P2 roots: `P03` (bug 40), `P20` (bug 41), and `P29`
  (bug 42).
- The other 27 fixed round-6 partitions, including that round's reports labeled
  `P07` through `P18`, produced no new reachable P0-P2 root after unsupported,
  edge-only, and duplicate reports were discarded. No existing issue required a
  round-6 facet amendment.

## Rejected or not substantiated

- External SIGINT-to-143 supervisor behavior is not a supported invocation path;
  supported cancellation is the authenticated parent/task control flow already
  covered where defective.
- Blank reset-path variants remain rejected: stock defaults and exact ownership
  markers prevent the claimed unowned partial deletion.
- Expanding the remote-filesystem blacklist is hardening/policy work, not a new
  ordinary correctness failure on the supported local publication path.
- Relative `SharpProofToolsDirectory` remains unsupported and does not add a new
  root beyond the configured-path findings already recorded.
- Direct `SharpProofVerify` invocation is skipped by its public condition unless
  an internal state property is forcibly overridden; that forced state is not a
  supported trigger.
- A compiler failure after `_SharpProofInitializeVerify` has run does not leave
  the claimed stale success because initialization already invalidated the prior
  result. Bug 49 records the distinct earlier `ResolveReferences` and other
  pre-editor-config failures for which that hook never runs.
- The runtime-snapshot alias premise is false because `CreateRequest` revalidates
  the resolved snapshot/path set before worker launch.
- All other round-5 partitions reported no in-scope candidate.
- A stale `Interrupted` state premise was false: the cited state is recomputed on
  each classification path rather than retained from the earlier observation.
- Blank invalidation paths do not establish deletion of an owned publication;
  stock paths are supplied by the targets and blank/unowned values fail before
  the claimed partial cleanup.
- Publication marker aliasing is already rejected by
  `ValidatePublicationMetadataAliases`; no distinct supported alias path was
  found.
- Symlink/`..` spellings and bind aliases were not accepted: the former violate
  the explicit lexical/canonical path contract, and the latter are rejected by
  supported publication topology/identity checks.
- Concurrent creation of an unrelated object at a publication destination is an
  unsupported replacement scenario, not a new ordinary cooperative-build root.
- The earlier audit reports labeled `P13` through `P18` did not establish any
  reachable in-scope defect and are retained as no-findings rather than register
  entries; this statement applies to those report instances, not later label
  reuse.
- `Exited(124)` together with a committed worker result is not produced by the
  production launcher/worker control flow, so the proposed projection mismatch
  is unreachable.
- `P27` concerns proof semantics outside this verifier process/publication scope.
- A worker parent that never sends the startup line violates the supported start
  gate contract; the existing hard boundary handles the ordinary supported path.
- Compiler-manifest snapshot loading intentionally uses `CancellationToken.None`
  so timeout/cancellation results remain manifest-accountable; the launcher hard
  limit is the enclosing boundary.
- One initial prohibited-path report was invalidated; its authoritative-worktree
  rerun found no candidate, so no finding from that result is included here.
- Generic faulted retained stdout is not a separate issue because retention
  consumes output only when `IsCompletedSuccessfully`; a faulted capture is not
  dereferenced as successful. Bug 45 records the distinct supported source and
  consequence: sequential task disposal faults the retained reader, which is
  then treated as missing authenticated cleanup.
- Final zombie observation: not accepted as a separate correctness failure. It
  causes the cleanup owner to retain/retry rather than authenticate false
  completion or abandon the process boundary.
- `DeleteIfUnprotected` destination replacement, raw PID reuse, and the native
  resolver timing window were not reopened; they are unsupported replacement or
  hardening scenarios outside this ordinary supported-path scope.
- Relative custom publication paths (`P19`) are not a new root; their build/clean
  base mismatch is now included in bug 23.
- `LauncherMarker`'s trailing semicolon is valid C# 12 simple-type syntax, not a
  parse or build defect.
- The generic post-manifest fallback has no distinct ordinary trigger after the
  throwing backend-dispose path is accounted for by bug 31.
- Compiler-output containment (`P06`): not accepted as a separate deletion bug;
  that check does not delete compiler outputs, and its routine escaping-exception
  behavior is already covered by bug 12.
- Blank reset paths: not accepted. Blank values are filtered before topology and
  deletion, while a nonblank set with mismatched markers is rejected before any
  publication member is removed.
- Relative `ToolsDirectory`: unsupported configuration and, for the general
  relative-base concern, already represented by bug 23; no new supported-path
  defect was established.
- `NoResultFailure(4)`: not accepted because production launcher control flow
  does not supply exit code 4 to that recovery projection.
- Additional protocol assumption enum kinds: not accepted; the protocol is
  intentionally closed to its declared producers, and no production assembler
  emits the proposed extra kinds.
- JSON null admission: rejected because the shape validator requires each
  non-nullable declared value's exact token kind before deserialization.
- Pre-gate signal handling: not accepted. Before authenticated gate release the
  supervisor has not armed or started the managed verifier, so the reported
  post-arm cleanup contract is not reachable.
- The additional ordinary-descendant `P05` report is a duplicate of confirmed
  bug 7, including the transient `/proc` observation root; it is not a separate
  issue.
- Broader remote-filesystem enumeration, destination replacement checks, and
  native-library replacement/resolver proposals were not reopened: they are
  hardening work outside this ordinary-correctness scope, not evidence of a new
  supported-path failure.
- Mixed launcher private request/result generations: not substantiated. Both
  private paths are invocation-owned and sequenced by one launcher; no ordinary
  cross-invocation writer to those exact paths was identified.
- `ValidateAndReport` returning an exclusive-or null out-parameter state: not
  reachable in its current branches, which assign validation state and response
  together before returning.
- Effect-witness locations escaping validation: rejected because full response
  validation already calls `HasValidLocation` for a present witness.
- Null protocol-error entries escaping sanitization: rejected because the
  assembler/validator path normalizes or rejects them before launcher reporting.
- Containing-host crash leaves a detached supervisor: not accepted as a project
  bug under the documented in-process task lifetime. The canonical container is
  the external lifetime boundary, and a child-only change cannot guarantee
  cleanup if either containing process is abruptly gone. A broader lifecycle
  contract would be needed before changing behavior.
- Retained cleanup anchors can live indefinitely: not accepted as an independent
  bug. Source comments and tests deliberately retain ownership until a known
  live supervisor exits. Releasing or time-capping the anchor while that process
  is still alive would abandon the containment boundary rather than fix it. The
  underlying reason a supervisor fails to exit should be fixed instead.
- Ambient .NET diagnostics/instrumentation variables are inherited: not
  substantiated as a defect. Environment inheritance is normal process behavior
  in the trusted canonical container and is required by ordinary tracing and
  coverage workflows. No hermetic child-environment contract was found.
- Concurrent `RunVerifier.Dispose` and `Execute` remains unaccepted without a
  lifecycle contract. `ICancelableTask.Cancel` is the supported concurrent
  operation and is synchronized; neither `IDisposable` nor the MSBuild task
  contract promises concurrent disposal with execution. Bug 45 instead concerns
  ordinary sequential disposal after `Execute` has transferred live reader
  ownership to a retained cleanup anchor.
- Output capture can stop after a high surrogate: the boundary fact is true, but
  no correctness bug was substantiated. .NET strings can contain unpaired UTF-16
  code units, the limit is explicitly measured in characters, protocol parsing
  continues independently of captured logging text, and no downstream scalar or
  serialization contract requiring a complete pair was identified.

## Validation

Post-rebase remediation and the final convergence fixes were validated locally
on 2026-08-23 in the canonical Linux amd64 container from a disposable
conventional clone of commit `0a32288de`. This gave every Git-backed release
fixture authentic repository history. The linked audit worktree itself stores
`.git` as a pointer to metadata outside the Docker bind mount, so it was not
used for the final broad run.

- `docker compose run --rm tooling build`: passed with zero warnings and zero
  errors.
- Focused `BuildTaskTests`: 60 passed, zero failed.
- `docker compose run --rm tooling check`: passed its Debug build, five semantic
  task groups, 14 package shards, and performance smoke; the maximum observed
  package-build ratio was 1.0376 against the 2.0 limit. An earlier exact-commit
  attempt reproduced bug 45 as a package-shard test-host fail-fast; this final
  rerun passed the same 60-test fixture under the same 14-shard load.
- `docker compose run --rm tooling test`: passed every project. The longest
  relevant totals were Worker 597, Architecture 479, Analyzer 389, Package 265
  with one unsupported-host skip, Gates 27, and Fuzz 33; no test failed.
- `scripts/Format-CSharp.ps1 -Verify`: passed after a locked restore in the same
  container environment.

The exhaustive repository round used the identical tracked code and
configuration tree. Excluding `BUGS.md`, audit commit
`8a5141d7d8772d1e9659099531086d156ea11e92` was byte-for-byte identical
to the validated tree at `0a32288de3f615b2786fd3928fcf609e86b449e8`.
Fresh canonical-container evidence for that tree was recorded under Compose
project `sharpproof-validation-0a32288de`:

- `docker compose run --rm tooling build`: zero warnings and zero errors.
- `docker compose run --rm tooling test`: every project passed. Package
  reported 265 passed with one expected unsupported-host skip; Worker 597,
  Architecture 479, Analyzer 389, Effects 194, Gates 27, and Fuzz 33 all
  passed, with no failures.

The initial exhaustive documentation round changed no tracked code or
configuration. The remediation pass after that round changed the implementation,
tests, release inventory pin, and OpenCode dependency manifests; its fresh
canonical-container evidence is recorded below.

After the supplemental reports were fully triaged and normalized on 2026-08-24,
fresh canonical Linux amd64 validation was nevertheless completed against the
same code tree at `be14e47da5eb61891460edae1e4c76ae42c0b2bf`:

- `docker compose run --rm tooling build`: zero warnings and zero errors.
- `docker compose run --rm tooling test`, from a conventional disposable clone
  so Git-backed release fixtures received real repository metadata: every
  project passed. Package reported 265 passed with one expected unsupported-
  host skip; Worker 597, Architecture 479, Analyzer 389, Effects 194, Gates 27,
  and Fuzz 33 all passed with no failures.
- A preceding linked-worktree test attempt was non-authoritative: Docker's task
  copy could not include the worktree's external `.git` directory, and only
  Git/release-metadata fixtures failed for that stated reason. The conventional-
  clone rerun superseded it.

The final remediation commits `3e2105481f0794abd6964b814261f6704d9b7aa1`
and `5cdd9b617a70ed774cfab7002ebc7886ddbee77c` were then pushed to the audit
branch and validated from a conventional clone whose origin points at the
canonical repository. Using the warmed canonical Compose cache on 2026-08-24:

- `docker compose run --rm tooling build -Configuration Debug`: succeeded with
  zero warnings and zero errors.
- `docker compose run --rm tooling test`: every project passed. Package had
  271 passed and one expected unsupported-host skip; Worker 600, Architecture
  479, Analyzer 391, Effects 194, Gates 27, Fuzz 33, and all other projects
  passed with no failures.
- The focused timeout regression passed independently, and the targeted
  cleanup-reserve, OpenCode manifest, and project-relative SARIF contract tests
  passed before the final full run.

Earlier entries marked as previously reproduced still refer to their original
isolated evidence. The historical numeric gaps 24, 37, and 58 remain in the
triage ledgers so old references stay stable; all confirmed findings in the
active inventory above have now been remediated and removed from the active bug
log after verification.

## Supplemental-report validation ledger

The consolidation preserved and reviewed the body of every distinct report,
including later files whose names overlapped earlier inputs. Report text was
treated as candidate evidence, not as a confirmed bug. After tracing each
claim through the current code, the raw appendices were removed so this file
contains only the normalized confirmed inventory and this disposition record.

| Distinct source | Accepted or merged disposition |
| --- | --- |
| Original `BUGS_2.md` | Added bugs 88-94; merged additional facets into bugs 12, 13, 43, and 77. Every other lead was disproved, unreachable under repository contracts, outside ordinary correctness scope, or duplicated an existing root. |
| Original `BUGS_3.md` | Added bugs 98 and 99. Its confirmed supervisor, build-task, worker, protocol, analyzer, Effects, and oracle reports were duplicates of canonical bugs 1-87; the remaining claims were rejected after control-flow, API-contract, or test inspection. |
| Original `BUGS_4.md` | Added no bug. This report consisted of stale line references and generic null, concurrency, disposal, and validation suspicions; current guards, immutable/local state, safe-handle ownership, bounded buffers, existing files/tests, or unreachable malformed inputs contradicted every nonduplicate claim. Cybersecurity and hardening suggestions were excluded from this ordinary-correctness audit. |
| Original `BUGS_5.md` | Added bugs 96, 97, and 100-103; merged nested conversion handling into bug 79. Its other confirmed entries duplicated the canonical list. Remaining SMT, IL, host, test-style, and API-spec claims were intentional fail-closed behavior, bounded/tested behavior, unsupported inputs, or lacked a reachable producer. |
| Late `BUGS_3.md` | Added bug 95. Repeated Z3 resolver installation is idempotent; recursive enumeration depth is bounded by generated formulas; strict fuzz pass criteria and retained-failure limits are explicit gate policy; generic throw/null/loop observations did not identify reachable defects. |
| Late `BUGS_4.md` | Added no bug. Empty replay arrays are the required canonical shape, outcome/feature validation is closed by the evidence catalogs, division definedness separately excludes zero and overflow, the JSON limit is intentional and tested, and the remaining cancellation/encoding assertions supplied no distinct reachable failure. |
| Late `BUGS_5.md` | Added no bug. Scenario counts correctly distinguish work completed before and after backend classification, coverage counts intentionally ignore unrelated expression kinds, the 64-item list is a reporting-retention cap, odd-multiplier unchecked seed arithmetic remains injective over the supported case span modulo 32 bits, and the coverage equality is an invariant check rather than duplicate state. |
| Late `BUGS_6.md` | Added no bug. Protocol arrays are null-normalized before sorting, evidence-authority handling catches a bounded documented exception set, declared-symbol lookup already uses a null-safe pattern, direct-invocation restrictions are intentional lowering admission, and the manifest/dataflow claims named no reachable null producer. |

The four late inputs were distinct despite their overlapping names. Their
pre-consolidation SHA-256 values were:

| Late input | SHA-256 |
| --- | --- |
| `BUGS_3.md` | `609332C1DC7B9EE20D187FCC9706C7D46AA224570AD2E95DA2EE4E280DADE1C5` |
| `BUGS_4.md` | `841E2F0AACEE9FE78D833D4D06A3059770AF409A295F1C01F8038D8ADD52F74F` |
| `BUGS_5.md` | `66440A8CD3D939A5E01DAD03ACD0C296F72703448D170D44BD8732CFB4C1DA79` |
| `BUGS_6.md` | `5186BF509728AD1DAF8E57B9926F845AA05EF7A1E903627AA0E6B2C56F0C9045` |
