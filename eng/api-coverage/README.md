# .NET API coverage inventory

The automatic inventory covers every declared public, protected, and protected-internal
type/member in **Microsoft.NETCore.App.Ref 9.0.18 / net9.0**, supplied by the canonical
.NET SDK 9.0.316 container. This is a framework API inventory, not an inventory of
SDK command-line tools. It does not load implementation assemblies or execute API bodies.

The unit is `(defining assembly name, XML documentation declaration ID)`. Overloads
and generic arity have distinct identities. Nested types are included only when their
containing types are externally visible. Inherited declarations and type forwarders
are counted at their definitions; properties/events own their accessors. Private,
internal, and private-protected declarations are outside the public universe.
Type declarations are explicitly classified as `excluded-container`; they contribute
no behavioral support. Methods, constructors, operators, properties, fields, and events
are classified independently. No API is declared fully modeled.

`partially-modeled` records evidence from the production `ApiSpecResolver`, including
each facet and postcondition count, or the production static/instance string-equality
intrinsic predicates. A getter spec does not establish setter support. `unmodeled`
means no evidence from those sources, not that every possible use must fail verification.
Contextual language lowering (arrays, operators, conversions, field reads, etc.) is not
a blanket API specification. Opt-in relational packs, ASP.NET Core, Windows Desktop,
workload packs, NuGet libraries, and other target frameworks are outside this initial
inventory. These exclusions must remain visible when interpreting the report.

## Reports and CI

Run the same required gate as CI:

```sh
docker compose run --rm tooling test \
  -Target SharpProof.Effects.Test/SharpProof.Effects.Test.csproj \
  -TestFilter FullyQualifiedName~DotnetApiInventoryTests
```

CI publishes an assembly summary, modeled facets, and bounded change lists in the job
summary. The `dotnet-api-inventory-*` artifact contains the complete `inventory.tsv`,
`manifest.json`, `delta.json`, and `report.md`, even when the baseline comparison fails.
TSV has no header: assembly, declaration ID, symbol kind, status, evidence (`-` when absent).

The reviewed `net9.0.tsv` baseline gates additions, removals, and every changed evidence
row. Equal totals cannot conceal churn. Loss of a catalog row, known facet, or recognized
intrinsic fails the gate. The manifest additionally pins SDK identity, all reference
assembly SHA-256 checksums, and the catalog semantic digest, so semantic changes with
unchanged facet counts also require review. Resolver failures fail closed. Missing
packs, duplicate identities, and missing baselines are errors.

## Which modeled behaviors have tests?

The same required CI job also runs `ApiSpecRuntimeOracleTests`. Its
`ModeledFacetsHaveExecutedTestEvidence` gate derives required rows from the current
default catalog, not from a manually maintained list of tests. Each known effect,
allocation, exception, nullness, cardinality, termination facet and each postcondition
must have exactly one runtime witness, named edge inputs, a passing declared-behavior
check, and a check that rejects a deliberately wrong claim. Unknown and not-applicable
facets do not claim behavior and therefore do not count as tested facets.

`catalog-test-evidence.json` and `.md` map each API declaration and required facet to
the fully qualified test method that actually executed those checks, its edge inputs,
and `passed`, `missing`, or `failed` status. These reports are written before the gate
asserts success, so a missing witness or failed behavior remains visible in failed CI
artifacts. The registry's existing exact-coverage test additionally rejects stale or
duplicate registrations. Mutation controls verify that removing an API/facet or
postcondition witness, accepting a wrong claim, or omitting edge inputs is detected.

`intrinsic-test-evidence.json` and `.md` cover the inventory's two audited string-equality
intrinsics. The inventory test compiles equality expressions, lowers them through the
production frontend, and interprets their result and receiver-safety conditions against
runtime expectations. Inputs cover equal/unequal strings, empty strings, null receivers
and arguments, embedded nulls, and ordinal Unicode behavior; an inverted-result claim
must be rejected. Every inventoried intrinsic requires a registered behavioral probe.

All four evidence files are included in the API inventory artifact, and both Markdown
matrices appear in the CI job summary. To reproduce the catalog gate separately:

```sh
docker compose run --rm tooling test \
  -Target SharpProof.Specs.Test/SharpProof.Specs.Test.csproj \
  -TestFilter FullyQualifiedName~ApiSpecRuntimeOracleTests
```

This mapping is bounded runtime/semantic evidence, not line coverage or a proof that
every possible input is exercised. A passing allocation or termination witness records
the observed inputs and does not establish those properties universally. Opt-in packs
and other contextual intrinsics remain outside this mapping, as outside the inventory.

## Reviewing an intentional update

Generate a candidate; this does **not** change the reviewed baseline:

```sh
docker compose run --rm -e SHARPPROOF_API_INVENTORY_CANDIDATE=1 tooling test \
  -Target SharpProof.Effects.Test/SharpProof.Effects.Test.csproj \
  -TestFilter FullyQualifiedName~DotnetApiInventoryTests
```

Inspect the complete delta and manifest diff. Investigate support losses rather than
approving a lower count. After reviewing intentional changes, copy the candidate:

```sh
docker compose run --rm tooling dev -lc \
  'cp artifacts/api-coverage/inventory.tsv eng/api-coverage/net9.0.tsv && cp artifacts/api-coverage/manifest.json eng/api-coverage/net9.0.manifest.json'
```

Run the gate again without candidate mode and commit both baselines with an explanation.
For an SDK/reference-pack upgrade, change the inventory's explicit pack pin as well;
never select the highest installed pack or silently fall back to runtime assemblies.
New support evidence still needs semantic tests; this inventory measures registrations
and drift, not behavioral correctness for every runtime input or proof pipeline stage.
