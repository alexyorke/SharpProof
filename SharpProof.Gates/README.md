# SharpProof corpus gate

Run the deterministic gate in the canonical container:

```text
docker compose run --rm tooling corpus -Configuration Release
```

The gate checks canonical observations, reviewed support classifications, metamorphic invariance, native outcomes, and Unknown ratchets. Updating observed text does not authorize changing semantic expectations.

## Case universe

The checked-in corpus has 228 base cases:

- 200 distinct open-source methods from the pinned MIT-licensed C-Sharp-Algorithms source bundle.
- 18 synthetic effect-contract seeds.
- 10 synthetic compiler-bound call-precondition seeds.

Effect seeds have nine source forms and precondition seeds have ten. Those 262 metamorphic cases plus 200 OSS targets yield 462 recorded cases. Renames, escaped identifiers, trivia, parentheses, temporaries, constant branches, named arguments, formal renames where applicable, and independent statement reordering test invariance.

The open-source floor counts original declarations only, not transformations. [Corpus provenance](Corpus/README.md) describes the source pin, hashes, licensing, and instrumentation.

## Observations and native evidence

Portable analyzer diagnostics are canonicalized by ID, effective severity, source location, and invariant-culture message. Native corpus verification builds the closed compiler artifact and runs the same worker claim pipeline used by builds, in process with the required native resolver.

The gate keeps semantic outcomes separate from diagnostic output. Silence is not proof. It also checks synthetic replay and concurrent variant observations. See [CorpusGate.cs](Corpus/CorpusGate.cs), [OpenSourceCorpusRunner.cs](Corpus/OpenSourceCorpusRunner.cs), and [NativeCorpusVerifier.cs](Corpus/NativeCorpusVerifier.cs).

## Independent classifications and ratchet

Each case has reviewed `Supported` or `IntentionallyUnsupported` classification, independent of expected verdict and snapshot. Supported cases must yield accountable Proven or Refuted; supported Unknown is a failure.

[unknown-reason-ratchet.json](Corpus/unknown-reason-ratchet.json) currently requires at least 240 supported cases and 14 supported OSS methods, and caps total Unknown at 222 with per-bucket maxima. These are floors/caps, not a claim that every run has exactly those counts.

The gate rejects new Unknown buckets, reduced supported coverage, exceeded caps, and unexplained verdict changes. [proven-to-unknown.json](Corpus/proven-to-unknown.json) is the explicit allowance mechanism; stale, duplicate, unused, or unexplained entries fail. Snapshot refresh alone cannot expand the unsupported surface.

## Deliberate updates

Run `sp corpus-update -Configuration Release` inside the persistent Dev Container workspace, then review all affected observations. A finite task workspace is disposable and is not the source of an intentional corpus edit.

Changes to upstream source, support classifications, expected outcomes, or ratchets require distinct reviewed justification. Preserve semantic expectations for diagnostic wording-only changes. Do not normalize a regression into the baseline.
