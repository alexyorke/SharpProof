# SharpProof corpus gate

`SharpProof.Gates` is a deterministic console gate:

```text
docker compose run --rm tooling corpus -Configuration Release
```

Run `sp corpus-update -Configuration Release` from the persistent Dev
Container when intentionally updating checked-in corpus evidence. The update
then occurs in the container-owned Git workspace rather than a disposable task
checkout.

## Analyzer corpus

The checked-in snapshot covers 228 base methods:

- 200 distinct real-world methods from the MIT-licensed
  `aalhour/C-Sharp-Algorithms` repository, pinned to a full commit and spread
  across 87 upstream source files; and
- 28 focused synthetic semantic seeds: 18 effect-contract cases and 10
  compiler-bound `Requires` call-site cases.

The 200-method release floor applies only to the open-source methods. Their
exact source, file/method hashes, path and line provenance, commit, and license
are checked in under `Corpus/`; generated transformations cannot satisfy that
floor. The runner adds `EnforcePure` to each selected declaration without
rewriting its body or dependencies and analyzes the pinned upstream project as
one compilation.

Compiler-bound `Requires` seeds are rendered in ten source forms, while the
effect-contract seeds are rendered in nine forms because they have no contract
formals to alpha-rename. Together they produce 262 independently compiled
metamorphic cases:

1. baseline;
2. method, class, parameter, and helper rename;
3. escaped C# identifiers;
4. comment and whitespace trivia;
5. redundant parentheses;
6. a local temporary;
7. an `if (true)` wrapper;
8. a named argument replacing a positional argument;
9. alpha-renamed contract formals (compiler-bound seeds only);
10. reordered independent statements.

Together with the 200 open-source cases, these produce 462 recorded cases.
The runner compares real
`SharpProofAnalyzer` output with
`Corpus/expected.canonical.snapshot`. Each entry records the analyzer's
internal semantic outcome independently of diagnostics, so diagnostic silence
can never be interpreted as proof. Canonical diagnostics include ID, effective
severity, normalized source location, and the invariant-culture message.
Diagnostics and cases are sorted deterministically. The gate also replays every
synthetic baseline against the same Roslyn compilation (the cache path) and
analyzes one synthetic case per variant concurrently. See
`Corpus/README.md` for licensing, instrumentation, and the reproducible import
workflow.

Every case also carries an explicit reviewed `Supported` or
`IntentionallyUnsupported` label that is stored independently from its
expected verdict and canonical snapshot. A supported case that produces either
`Unknown` or `SilentUnknown` fails with zero tolerance; supported cases must
produce an accountable `Proven` or `Refuted` semantic outcome. Unknown results
in the intentionally unsupported set are counted by deterministic diagnostic-ID
bucket (or `silent-unclassified`). `Corpus/unknown-reason-ratchet.json` floors
the supported total and supported OSS-method count and caps both the total
Unknown count and every known bucket. A new bucket, a reduced supported count,
or any Unknown count above its reviewed maximum fails, so rewriting the
canonical snapshot cannot silently expand the unsupported surface.

Any diagnostic mismatch fails except an expected `Proven` result becoming
`Unknown` when the exact case is listed in `Corpus/proven-to-unknown.json`
with a non-empty explanation. Unused, stale, duplicate, or unexplained
allowances fail. The checked-in allowlist is intentionally empty.
