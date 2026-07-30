# Release-owner evidence

The repository can enforce code, package, protocol, coverage, fuzz, corpus,
performance, security, provenance, and exact-SHA gates. It cannot manufacture
pilot-library history, independent human review, or GitHub repository
protection settings.

The preview and RC tags run the automated exact-SHA qualification job. Coverage
is measured across the complete release delta: `preview.1` is anchored to the
checked-in pre-hardening commit, and each later release is anchored to the
resolved commit of its allowlisted predecessor tag. The qualification evidence
records both immutable commit SHAs, and the job rejects a missing, equal, or
non-ancestor baseline.

Before creating `v1.0.0`, freeze the exact RC package bytes, production digest,
and trusted-computing-base digest for the four pilot cycles. The stable product
commit may differ from the qualified RC commit only in approved version,
changelog, and release metadata; its independently computed production and TCB
digests must remain identical to the RC digests. Any other production, TCB, or
package-content change restarts qualification.
The qualified RC tag must be annotated. Its immutable tagger timestamp and the
RC commit timestamp are lower bounds for `qualifiedAtUtc`; pilot cycles before
that bound do not count toward the four-week window.

`scripts/Get-SharpProofReleaseDigests.ps1` computes both digests directly from
each Git commit. The production domain includes the complete tracked tree,
including product, tests, samples, generators, workflows, release scripts, and
security/evidence controls. Its only exclusions are the explicitly approved
documentation metadata (`docs/`, root policy/README/semantics Markdown, the
changelog, and nested `README.md` files). The package-version value in
`SharpProof.Release.props` is normalized while every other included byte
remains bound. The TCB domain is the exact path inventory declared by that
commit's `eng/acceptance/contract.json`, including the contract itself. Paths
are deduplicated and sorted with ordinal semantics. Each digest binds the Git
tree entry mode and type, path, and blob content under a separate versioned
domain prefix, so executable-bit or file/symlink transitions cannot preserve a
digest. Human evidence must match the independently computed RC and stable
digests; copying the same asserted value into both records is insufficient.

On the separate orphan branch `release-evidence`, copy
`human-gates.template.json` to `releases/v1.0.0.json`, replace every placeholder
with schema-4 evidence naming both the qualified RC and exact stable product
commit, and create the annotated tag `evidence/v1.0.0` at the evidence commit.
The product workflow fetches that branch and tag from the remote without
incorporating the evidence commit into the product tree. Protect both `v*` and
`evidence/v*` tags against creation by untrusted actors, update, and deletion.
The final publication job validates:

- at least two pilot libraries and at least 100 selected claims in total;
- one exact qualified-RC three-package graph, Windows x64 runtime, RC product
  commit, worker protocol, and strict `all`/`require-proven`/`error` policy per
  pilot;
- equal qualified-RC and stable production/TCB digests, with only explicitly
  allowlisted metadata differences;
- exact worker-assembly, packaged runtime-closure, and specification-catalog
  digests shared by every pilot;
- four consecutive passing weekly cycles per pilot, each bound to complete
  outcome and typed-reason counts, explicit assumption/trusted-evidence
  records, stable compiler-input and claim-manifest digests, a result and
  request hash, one unchanged pilot repository/source commit, and a downloaded
  content-hashed artifact from the exact authenticated workflow run;
- no open P0 or P1 defects;
- two distinct independent soundness reviewers approving the exact commit; and
- protected branches, release tags, publishing environments, required checks,
  and independent-review enforcement.

The annotated evidence tag resolves to an external evidence commit and names
the exact product SHA, so the evidence no longer needs to embed its own
self-changing commit hash. Qualification records the resolved tag object,
evidence commit, and exact evidence-document SHA-256. The automated
qualification for that product commit must also retain passing
corpus/performance/fuzz/coverage/dependency evidence and a trusted-boundary
mutation summary. The mutation gate archives the exact commit and requires
every deterministic mutation - including postcondition replay,
fail-closed effect-result assembly, cache/manifest binding, protocol equality,
and process containment - to compile and be killed by its focused assertion. A missing
summary, survivor, or timeout prevents the qualification record from marking
`mutations=passed`.
The dependency gate consumes NuGet's JSON report, requires the exact
repository-owned `auditSources` set and complete solution-project coverage,
rejects every warning/error problem record, and rejects every reported direct
or transitive vulnerable package. A successful process exit without usable
audit data is not passing evidence.

Qualification evidence starts in `running`. Each package, consumer, security,
attestation, baseline, locked-restore, acceptance, fuzz, mutation, corpus,
performance, coverage, dependency-audit, and applicable human gate is recorded
separately as `pending`, `running`, `passed`, `failed`, `not-run`, or
`not-required`.
The record changes to `passed` only after every required gate succeeds. A
failed or canceled run preserves already-passed gates, marks the active gate
failed, marks untouched gates not-run, and writes top-level `failed` before the
always-run artifact upload. Every successful gate transition writes a receipt
that binds the gate's result artifact digest and the exact GitHub Actions
repository, run, attempt, workflow, job, ref, and commit. The workflow uploads
each receipt immediately as an immutable Actions artifact. The terminal
transition redownloads those artifacts, requires byte-for-byte equality with
the running record, and rehashes every retained gate result. Rewriting both
local receipts and progress state therefore cannot manufacture a successful
record. For stable 1.0 the terminal transition also reruns the immutable
evidence-tag validator and requires the fresh binding to equal both the
retained validation artifact and the stored progress record. Setup or checkout
failures still produce a minimal failed qualification record.

The template is documentation, not evidence. Reserved placeholder or local
hosts, private and loopback addresses, zero hashes, and zero commits
deliberately cannot qualify a release. The qualified RC is authenticated
against the exact successful package-consumer workflow, attempt-scoped
qualification artifact, schema-5 terminal record, package artifact, release
manifest, every manifest-listed package/symbol/SBOM byte and size, and every
non-null schema-2 qualification receipt together with the exact retained gate
evidence named and hashed by that receipt. Artifact creation and update
timestamps must fall inside the authenticated workflow-attempt window, so an
artifact retained from an earlier attempt cannot qualify under a new attempt
number. Its
`qualifiedAtUtc` is the authenticated workflow completion time, not a
self-asserted date. The stable
candidate is bound by the current final-tag qualification job and its locally
sealed gate receipts; it cannot cite a not-yet-completed artifact from its own
run.

Each pilot cycle uses the fixed
`.github/workflows/sharpproof-strict-weekly.yml` `workflow_dispatch` path at
one frozen source commit. The run URL and GitHub API metadata must agree on
repository, workflow path and event, run ID, attempt, successful status,
commit, and timestamps. The validator then downloads the attempt-qualified
`sharpproof-pilot-evidence-{commit}-{run}-{attempt}` artifact, verifies its API
and archive SHA-256 digests, rejects unsafe or extra ZIP entries, and requires
its schema-1 JSON record to match every declared package, runtime, tool,
policy, outcome, evidence-use, result, and workflow field.

The frozen pilot workflow and source commit are part of the human release
evidence trust boundary. The validator authenticates the workflow run and its
record bytes; it does not independently execute code from an external pilot
repository. Moving pilot execution into a SharpProof-owned, commit-pinned
reusable workflow and retaining the canonical worker response and claim
manifest would narrow that boundary further.

The validator requires an Actions-readable token in
`SHARPPROOF_GITHUB_TOKEN` (or `GITHUB_TOKEN`). The release workflow exposes the
owner-configured `SHARPPROOF_PILOT_EVIDENCE_TOKEN` only to final human
validation and terminal revalidation; preview, RC, fuzz, mutation, coverage,
and other candidate-controlled steps receive no cross-repository token. Use a
read-only fine-grained token restricted to the pilot repositories' Actions
artifacts. Assemble stable evidence before the artifacts' 90-day retention
expires.

Reviewer and governance entries remain owner assertions. They are accepted
only as part of the independently reviewed, protected evidence commit and
protected publishing environment; stable publication remains blocked until
those owner-controlled protections and reviewer identities actually exist.
