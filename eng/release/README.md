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

Before creating `v1.0.0`, freeze the exact product commit for the four pilot
cycles and independent reviews. On the separate orphan branch
`release-evidence`, copy `human-gates.template.json` to
`releases/v1.0.0.json`, replace every placeholder with real evidence naming
that product commit, and create the annotated tag `evidence/v1.0.0` at the
evidence commit. The product workflow fetches that branch and tag from the
remote without incorporating the evidence commit into the product tree.
Protect both `v*` and `evidence/v*` tags against creation by untrusted actors,
update, and deletion. The final publication job validates:

- at least two pilot libraries and at least 100 selected claims in total;
- one exact three-package graph, Windows x64 runtime, product commit, worker
  protocol, and strict `all`/`require-proven`/`error` policy per pilot;
- four consecutive passing weekly cycles per pilot, each bound to complete
  outcome counts, a result and request hash, and an immutable workflow run;
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
effect-witness replay, cache/manifest binding, protocol equality, and process
containment - to compile and be killed by its focused assertion. A missing
summary, survivor, or timeout prevents the qualification record from marking
`mutations=passed`.

Qualification evidence starts in `running`, changes to `passed` only after
every automated and applicable human gate succeeds, and is overwritten as
`failed` after a failed or canceled gate before the always-run artifact upload.

The template is documentation, not evidence. Its `example.invalid` URLs and
zero commit deliberately cannot qualify a release.
