# Release-owner evidence

The repository can enforce code, package, protocol, coverage, fuzz, corpus,
performance, security, provenance, and exact-SHA gates. It cannot manufacture
pilot-library history, independent human review, or GitHub repository
protection settings.

The preview and RC tags run the automated exact-SHA qualification job. Before
creating `v1.0.0`, the repository owner must copy
`human-gates.template.json` to the intentionally untracked-by-default evidence
path `human-gates.json`, replace every placeholder with real evidence for the
exact release commit, and commit that file. The final publication job validates:

- at least two pilot libraries and at least 100 selected claims in total;
- four consecutive passing weekly cycles per pilot with no unwaived `Unknown`;
- no open P0 or P1 defects;
- two distinct independent soundness reviewers approving the exact commit; and
- protected branches, release tags, publishing environments, required checks,
  and independent-review enforcement.

The automated qualification for that same commit must also retain passing
corpus/performance/fuzz/coverage/dependency evidence and a trusted-boundary
mutation summary. The mutation gate archives the exact commit and requires
every deterministic mutation - including postcondition replay,
effect-witness replay, cache/manifest binding, protocol equality, and process
containment - to compile and be killed by its focused assertion. A missing
summary, survivor, or timeout prevents the qualification record from marking
`mutations=passed`.

The template is documentation, not evidence. Its `example.invalid` URLs and
zero commit deliberately cannot qualify a release.
