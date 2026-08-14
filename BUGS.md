# SharpProof current convergence register

This is the finite active code and technical-debt register for the
container-only `1.0.0-preview.1` candidate. Historical audit queues and
completed Windows-hosted qualification records are archived under
`eng/agent-notes/archive/`; they are not active backlogs.

Exact-commit mutation, package, pilot, SBOM, and publication-plan artifacts
are generated after the final source commit. They are external qualification
evidence, not checked-in debt rows: recording their result in this file would
change the commit they qualify.

## Priority rubric

- **P0:** Release blockers: false proofs, missing verifier obligations, destructive supported behavior, verifier bypasses, or release authority accepting invalid candidate bytes.
- **P1:** Material supported-surface defects: incorrect verdicts or diagnostics, missing required qualification, or workflows that produce the wrong result.
- **P2:** Fail-closed reliability and evidence-integrity defects: lifecycle, provenance, canonicality, resource, or reporting failures without a demonstrated false proof or invalid release.
- **P3:** Precision, documentation, and developer-experience debt that does not change a supported proof or release decision.

The active backlog contains 59 root-cause rows. Stable IDs are not renumbered; merged historical IDs remain aliases below.

## Merged and removed historical IDs

- SP-AUDIT-026 (Pilot qualification accepts stale verifier outputs) -> SP-AUDIT-003.
- SP-AUDIT-030 (Definitely null conditional access retains skipped effects) -> SP-AUDIT-024.
- SP-AUDIT-032 (Primary-constructor contracts are absent from the manifest) -> SP-AUDIT-008.
- SP-AUDIT-055 (Protected release tags may be explicitly excluded) -> SP-AUDIT-053.
- SP-AUDIT-059 (Cancellation rethrow ignores replacing finally blocks) -> SP-AUDIT-025.
- SP-AUDIT-064 (Effects profile skips generated ContractFor validation) -> SP-AUDIT-056.
- SP-AUDIT-068 (Mixed partial generated companions escape all validation) -> SP-AUDIT-056.
- SP-AUDIT-077 (The canonical Dockerfile frontend is not digest-pinned) -> SP-AUDIT-046.
- SP-AUDIT-081 (Reference modules do not authenticate the manifest-first slot) -> SP-AUDIT-034.
- SP-AUDIT-082 (Release tags never run the required Debug solution gate) -> SP-AUDIT-004.
- SP-AUDIT-084 (Strict verifier diagnostics bypass the MSBuild error channel) -> SP-AUDIT-076.
- SP-AUDIT-085 (Cache paths nested under outputs pass topology validation) -> SP-AUDIT-075.
- SP-AUDIT-087 (Explicitly selected nested callables are silently skipped) -> SP-AUDIT-069.
- SP-AUDIT-090 (Trailing separators create alternate compilation identities) -> SP-AUDIT-034.
- SP-AUDIT-096 (VerifyTarget cancellation checks certify arbitrary catch tails) -> SP-AUDIT-025.
- SP-AUDIT-099 (Compiler integer domains can be narrowed to fabricate proofs) -> SP-AUDIT-040.
- SP-AUDIT-100 (Summary result and existential roles are interchangeable) -> SP-AUDIT-173.
- SP-AUDIT-103 (Disabled cache paths are still treated as active topology) -> SP-AUDIT-075.
- SP-AUDIT-104 (Worker cancellation reification ignores argument evaluation) -> SP-AUDIT-025.
- SP-AUDIT-108 (Checked arithmetic re-evaluates a mutated left operand) -> SP-AUDIT-101.
- SP-AUDIT-113 (Nested body evidence accepts non-producer ordering) -> SP-AUDIT-045.
- SP-AUDIT-116 (Mutation-bearing initializers fabricate scalar facts) -> SP-AUDIT-101.
- SP-AUDIT-120 (Coverage policy can authorize its own removal) -> SP-AUDIT-050.
- SP-AUDIT-123 (Cache hits ignore a lowered byte cap) -> SP-AUDIT-121.
- SP-AUDIT-127 (Peer-generated target members evade ContractFor validation) -> SP-AUDIT-056.
- SP-AUDIT-128 (Partial-property definition contracts use the wrong body) -> SP-AUDIT-042.
- SP-AUDIT-133 (Release qualification omits the release-configuration certifier) -> SP-AUDIT-004.
- SP-AUDIT-137 (Whitespace-only SARIF configuration fails verification) -> SP-AUDIT-136.
- SP-AUDIT-139 (Duplicate additional files make the collector reject its own snapshot) -> SP-AUDIT-034.
- SP-AUDIT-145 (Release qualification omits portable OS consumers) -> SP-AUDIT-004.
- SP-AUDIT-147 (The canonical TCB inventory can authorize its own shrink) -> SP-AUDIT-107.
- SP-AUDIT-148 (Evaluated production Compile items can escape all certifier universes) -> SP-AUDIT-050.
- SP-AUDIT-149 (Complexity gates ignore production preprocessor symbols) -> SP-AUDIT-050.
- SP-AUDIT-150 (Generated-output exclusions lack generator provenance) -> SP-AUDIT-050.
- SP-AUDIT-151 (UnsupportedContract callable coverage is unreachable) -> SP-AUDIT-088.
- SP-AUDIT-154 (Strict protocol validation accepts noncanonical order and spelling) -> SP-AUDIT-153.
- SP-AUDIT-157 (Release version identity is case-folded) -> SP-AUDIT-134.
- SP-AUDIT-161 (Acceptance omits repeated forced-termination stability) -> SP-AUDIT-004.
- SP-AUDIT-163 (Hydration accepts bodyless successful postcondition callables) -> SP-AUDIT-162.
- SP-AUDIT-164 (Portable IR encode/decode type-depth domains disagree) -> SP-AUDIT-045.
- SP-AUDIT-169 (Standalone gate consumers ignore the acceptance schema) -> SP-AUDIT-160.
- SP-AUDIT-170 (Disabling verification preserves a stale success commit) -> SP-AUDIT-057.
- SP-AUDIT-171 (Minimum-SDK package compatibility is not qualified) -> SP-AUDIT-004.
- SP-AUDIT-172 (Value-returning lowered bodies may omit the return value) -> SP-AUDIT-162.
- SP-AUDIT-176 (Publication durability omits data and directory sync) -> SP-AUDIT-063.
- SP-AUDIT-177 (Pre-canceled publication locking still mutates disk) -> SP-AUDIT-029.
- SP-AUDIT-179 (Source-location validation permits overflowing spans) -> SP-AUDIT-132.
- SP-AUDIT-185 (SBOM document name is not version bound) -> SP-AUDIT-007.
- SP-AUDIT-186 (Z3 model extraction escapes resource accounting) -> SP-AUDIT-165.
- SP-AUDIT-187 (Relational dependency expansion is unbounded) -> SP-AUDIT-140.
- SP-AUDIT-188 (Outer gate failures preserve stale passing receipts) -> SP-AUDIT-168.
- SP-AUDIT-190 (Generated partial attributes suppress handwritten analysis) -> SP-AUDIT-037.
- SP-AUDIT-191 (Git-quoted TCB paths bypass changed-line coverage) -> SP-AUDIT-175.
- SP-AUDIT-193 (Release manifests coerce invalid JSON scalar types) -> SP-AUDIT-049.
- SP-AUDIT-194 (README assigns memory limits to the worker) -> SP-AUDIT-181.
- SP-AUDIT-195 (Specification-pack identity limits disagree) -> SP-AUDIT-102.
- SP-AUDIT-198 (Offline feed state is attributed to a real destination) -> SP-AUDIT-144.
- SP-AUDIT-201 (Module references accept impossible property combinations) -> SP-AUDIT-034.
- SP-AUDIT-202 (Manifest assumptions admit producer-impossible kinds) -> SP-AUDIT-155.
- SP-AUDIT-203 (Trusted effect certainty lacks trusted provenance) -> SP-AUDIT-152.
- SP-AUDIT-205 (Duplicate publication inputs poison corrected builds) -> SP-AUDIT-075.
- SP-AUDIT-206 (Canceled launchers leak staged worker closures) -> SP-AUDIT-048.
- SP-AUDIT-208 (Named arguments are lowered in source order) -> SP-AUDIT-173.
- SP-AUDIT-211 (Compiler errors bypass callable-shape validation) -> SP-AUDIT-089.
- SP-AUDIT-212 (Empty syntax-tree provenance accepts impossible fields) -> SP-AUDIT-034.
- SP-AUDIT-213 (Plan output creates the collision it certified absent) -> SP-AUDIT-143.
- SP-AUDIT-214 (SBOM checksum arrays accept scalar objects) -> SP-AUDIT-049.
- SP-AUDIT-215 (Fixed SBOM vocabulary is case-folded) -> SP-AUDIT-049.
- SP-AUDIT-217 (Proven preconditions remain marked unused) -> SP-AUDIT-041.
- SP-AUDIT-218 (Callable coverage need not match owned claims) -> SP-AUDIT-043.
- SP-AUDIT-219 (Claim reasons are not bound to claim kind) -> SP-AUDIT-043.
- SP-AUDIT-220 (Manifest spans are not bound to sealed source length) -> SP-AUDIT-132.
- SP-AUDIT-224 (Release evidence output is not self-contained) -> SP-AUDIT-184.
- SP-AUDIT-225 (Typed-Unknown docs misstate Unavailable certainty) -> SP-AUDIT-209.
- SP-AUDIT-227 (Ill-formed UTF-16 constants collapse in Z3) -> SP-AUDIT-126.
- SP-AUDIT-228 (Specification equality loses concrete operand types) -> SP-AUDIT-222.
- SP-AUDIT-229 (SBOM row collections accept nested singleton arrays) -> SP-AUDIT-049.
- SP-AUDIT-234 (Trusted effect proofs mark their boundary unused) -> SP-AUDIT-152.
- SP-AUDIT-235 (Semantic IDs can be renamed self-consistently) -> SP-AUDIT-155.
- SP-AUDIT-236 (Manifest display coordinates are not source-bound) -> SP-AUDIT-132.
- SP-AUDIT-237 (Run failure reason is not bound to error identity) -> SP-AUDIT-043.
- SP-AUDIT-239 (Transitive summary provenance disappears from proof cores) -> SP-AUDIT-058.
- SP-AUDIT-240 (Retained archive validation accepts an empty release bundle) -> SP-AUDIT-184.
- SP-AUDIT-062 removed from the supported-preview backlog: The discriminating case requires user-defined operators or conversions, which the documented subset rejects.
- SP-AUDIT-012 removed from the supported-preview backlog: The reproducer requires foreach, which the documented language-subset gate rejects before effect proof.
- SP-AUDIT-061 removed from the supported-preview backlog: The reproducer requires ref/out calls, which the documented verifier subset rejects before effect proof.

## Fixed during remediation

### SP-AUDIT-183 - Publication plans falsely claim symbol preflight (fixed)

- [x] Fixed by `cf51421a9` (`fix: model symbol publication actions`).
- Publication now validates separate main and symbol destination state/action
  tuples. Registry main packages can be preflighted, while symbol packages are
  explicitly `Unchecked/CollisionOnPush`; targetless and fixture modes cannot
  claim remote checks. Only canonical main `.nupkg` URLs enter preflight.
- Regression coverage includes targetless, fixture, inherited/distinct registry
  destinations, mocked main 404/200/503 responses, exactly one main-only call,
  illegal tuple swaps, canonical ordering, and a symbol-action mutation. The
  focused matrix passed 26/26 and Architecture 307/307.

### SP-AUDIT-156 - SHA256SUMS byte canonicality is not validated (fixed)

- [x] Fixed by `d38f9d133` (`fix: validate canonical checksum bytes`).
- Evidence generation, final validation, and publication planning now share one
  raw-byte authority for ordinal rows, lowercase SHA-256, the exact separator,
  strict UTF-8 without BOM, LF-only lines, and exactly one terminal LF.
- Regression coverage includes UTF-8 BOM, UTF-16LE/BE, invalid UTF-8, CRLF,
  mixed and CR newlines, missing/double terminal LF, digest casing, spacing,
  reordered/extra/missing/duplicate rows, canonical bytes, and a comparison-
  removal mutation. The focused matrix passed 17/17; the corrected independent
  release-authority closure passed with 100 paths.

### SP-AUDIT-153 - Protocol JSON accepts noncanonical or incomplete producer shapes (fixed)

- [x] Fixed by `49c3c4591` (`fix: enforce canonical protocol JSON shapes`).
- The protocol generator now emits exact nested object shapes, and one recursive
  pre-deserialization authority enforces required properties, canonical ordinal
  names/order and enum spelling, exact token kinds, arrays, nullability, and
  non-null elements before generated defaults can change wire meaning.
- Regression coverage spans reachable nested model omissions plus extra,
  duplicate, case-variant, reordered, token-swapped, null, and array/object
  shapes, canonical request/response round trips, and an authority-removal
  mutation. The full Worker suite passed 491/491 and Architecture 279/279.

### SP-AUDIT-144 - Publication destination and fixture authority are conflated (fixed)

- [x] Fixed by `b4bc0b027` (`fix: bind publication destination authority`).
- Publication now uses an explicit, mutually exclusive registry, fixture, or
  targetless authority. Registry plans validate and separately record absolute
  HTTPS main and effective symbol destinations; fixture plans bind canonical
  directory and entry identities without representing them as remote state.
- Regression coverage includes invalid and relative destinations, inherited and
  distinct symbol targets, targetless plans, registry/fixture conflicts, changed
  fixtures, projection removal, and an exclusivity-removal mutation. The focused
  matrix passed 15/15 and the full Architecture command exited successfully.

### SP-AUDIT-143 - Publication-plan output topology is unsafe (fixed)

- [x] Fixed by `f06640c8c` (`fix: protect publication plan topology`).
- Planning now resolves and snapshots every bundle and fixture input before
  validation, rejects lexical/canonical/inode aliases and reserved evidence
  names, writes through a same-directory private temporary file, and restores
  the prior output on failure. A post-write replay verifies that no certified
  input path, inode, length, or SHA-256 changed during publication.
- Regression coverage includes every artifact role, relative and absolute
  aliases, symlink and hardlink aliases, valid and existing disjoint outputs,
  injected writer failure cleanup, post-write input mutation, and a guard-
  removal mutation. The focused matrix passed 16/16 and Architecture 264/264.

### SP-AUDIT-134 - Release version identity has no exact single authority (fixed)

- [x] Fixed by `ccdc496af` (`fix: bind exact release version authority`).
- One shared authority now derives the exact ordinal package version and
  authenticated `SharpProof.Release.props` identity. Evidence generation, final
  artifact validation, tag qualification, and publication planning require
  exact equality across package filenames/nuspecs, manifest, SBOM, tag, and plan.
- Regression coverage includes a self-consistent six-package foreign version,
  case-only prerelease drift, mixed versions, stale manifest/SBOM/plan evidence,
  a stale authority hash, and a comparison-removal mutation. The focused matrix
  passed 9/9 and the full Architecture suite passed 248/248.

### SP-AUDIT-130 - SBOM attestation includes undescribed symbol packages (fixed)

- [x] Fixed by `e8a02a3bc` (`fix: define SBOM symbol package scope`).
- The release contract now explicitly keeps symbol packages as exact manifest
  and provenance-attestation artifacts while limiting SBOM subjects and
  first-party checksums to the three main `.nupkg` files. Evidence generation,
  final validation, and publication each enforce the exact six-artifact release
  set and main-only SBOM scope, and the workflow uses `nupkgs/*.nupkg` for its
  SBOM attestation; the broader provenance step remains unchanged.
- Regression-first fixtures cover the canonical six-artifact bundle, checked-in
  workflow, missing/extra/swapped symbols, symbol checksum substitution,
  fabricated symbol rows, broad and symbol-only globs, every consumer, and a
  mutation. Focused tests passed 9/9, full Architecture 239/239, all touched
  PowerShell parsed, and `git diff --check` passed.

### SP-AUDIT-121 - Cache capacity enforcement is not transactional (fixed)

- [x] Fixed by `a71ae6a6b` (`fix: make cache writes transactional`).
- Cache operations now hold the directory lock across active-cap enforcement,
  replay, exact-key publication, staged LRU eviction, commit, and rollback.
  Attempt-owned writes are removed on every later failure/cancellation, any
  pre-existing exact-key bytes and staged entries are restored, and reads evict
  oversized/stale LRU state before admitting a hit. Competing instances cannot
  observe staged or newly published state during rollback.
- Regression-first tests cover cancellation after publish and later backend
  rerun/hit, pre-existing exact bytes, lowered single- and multi-entry caps,
  symlink eviction failure, cross-instance rollback isolation, unrelated files,
  concurrency, and a removal mutation. Transaction tests passed 5/5, cache
  matrix 36/36, full Worker 486/486, final concurrency discriminator 1/1, prior
  Architecture 230/230, and `git diff --check` passed.
- Consolidated case fixed: SP-AUDIT-123.

### SP-AUDIT-119 - SBOM accepts contradictory SHA-256 identities (fixed)

- [x] Fixed by `ad418aac6` (`fix: validate exact SPDX checksums`).
- A shared SPDX checksum authority now requires the raw `checksums` value to be
  an array containing exactly one non-null row with exactly `algorithm` and
  `checksumValue`, case-exact `SHA256`, and the authenticated lowercase digest.
  Evidence generation, final validation, and publication planning all invoke
  this authority.
- Regression-first fixtures cover first- and third-party canonical rows,
  duplicate same/different digests, extra algorithms, missing/wrong/stale/case
  values, extra/missing properties, scalar/null/object shapes, every consumer,
  and a removal mutation. Focused tests passed 14/14, structural plus focused
  15/15, full Architecture 230/230, and `git diff --check` passed.

### SP-AUDIT-115 - Methodless scopes hide rejected control attributes (fixed)

- [x] Fixed by `7b4018d6c` (`fix: report rejected scope attributes`).
- Rejected Suppress/Trusted identities are now validated directly on assembly
  and named-type declarations and reported at the exact attribute syntax.
  Application tree/span dedup gives one diagnostic per attribute across partial
  and callable paths; method-owned rejected attributes retain their existing
  reporting, while enclosing rejected scopes still select/abstain methods.
- Regression-first tests cover methodless, nested, partial, and method-bearing
  types; assembly scope; Suppress and Trusted; source-shadowed and referenced-
  project lookalikes; exact real attributes; generated policy; exact locations;
  and a removal mutation. Focused tests passed 4/4, full Analyzer 313/313,
  Architecture 216/216, and `git diff --check` passed.

### SP-AUDIT-052 - An empty release secret set cannot be certified (fixed)

- [x] Fixed by `a6691e61f` (`fix: validate empty release secret sets`).
- Release configuration set validation now explicitly admits an empty expected
  array and delegates every variables/secrets comparison to ordinal,
  duplicate-rejecting exact-set equality. Empty secret contracts are always
  validated instead of bypassed.
- Regression-first fixtures cover zero, one, and multiple tags, variables, and
  secrets; the checked-in public empty-secret contract; and missing, extra,
  duplicate, case-changed, and unexpected-secret negatives, plus a binder-
  removal mutation. The focused configuration test passed 1/1, full
  Architecture 216/216, and `git diff --check` passed. No network or live GitHub
  writes occurred.

### SP-AUDIT-047 - SBOM validators accept fabricated topology (fixed)

- [x] Fixed by `8f83dfa16` (`fix: validate exact SBOM topology`).
- One shared topology authority now derives the exact canonical first-party and
  component package identities, globally unique SPDX IDs, first-party
  `documentDescribes`, and complete `DESCRIBES`/`CONTAINS`/`DEPENDS_ON` triple
  set from authenticated dependencies and component ownership. Generation,
  final validation, and publication planning require exact total equality.
- Regression-first fixtures cover extra, missing, reversed, duplicate, and
  unknown-type relationships; wrong and colliding SPDX IDs; self-consistent
  rewrites; extra packages/descriptions; canonical controls; every consumer;
  and a removal mutation. Focused topology passed 12/12, the final consumer
  matrix 13/13, full Architecture 215/215, all touched PowerShell parsed, and
  `git diff --check` passed. No long package/evidence run was performed.

### SP-AUDIT-045 - Lowered IR wire form is not the exact encoder image (fixed)

- [x] Fixed by `b6a98897e` (`fix: require canonical portable IR image`).
- Portable IR decoding now re-encodes the decoded roots and program together
  with only independently authenticated external variable slots, preserves the
  separately authenticated member documentation identities, and byte-compares
  the entire graph. Unused metadata and non-producer ordering therefore fail
  before hydration; no schema field changed.
- Regression-first tests cover unused type, identity, variable, member,
  operation, and term rows; non-producer ordering/nested arrays; explicit valid
  external variables; canonical controls; block boundaries; and a closure-
  removal mutation. Focused tests passed 9/9 and boundaries 2/2; full Worker
  480/480, Analyzer 310/310, and release closure 96/96 passed. Architecture
  exposed two TCB/closure bookkeeping gaps, which were corrected, but the full
  suite was not run a third time under the two-failure rule. `git diff --check`
  passed.

### SP-AUDIT-044 - Release artifact roles are not bound to file types (fixed)

- [x] Fixed by `e6cadcd07` (`fix: bind release package roles`).
- The shared symbol-package validator now derives exact main and symbol
  filenames from package ID, version, and role; opens both archives once;
  rejects duplicate entries; authenticates inner nuspec ID/version/repository
  commit; enforces main-without-PDB and symbol-with-PDB layout; and then performs
  exact PDB/DLL/SourceLink pairing. Evidence, final validation, publisher, and
  package-consumer paths all pass an independently derived version.
- Regression-first fixtures cover exact valid packages, full byte role swaps,
  renamed main/symbol files, cross-ID pairs, wrong inner commit, and all release
  authority call sites, plus a role-validation mutation. Focused fixtures passed
  7/7, Architecture 203/203, and `git diff --check` passed. An additional
  end-to-end SBOM fixture was removed after its setup hit a separate restored-
  assets ownership error; no broad package suite was restarted.

### SP-AUDIT-039 - Request-bound results accept fabricated runtime provenance (fixed)

- [x] Fixed by `955288300` (`fix: bind worker runtime provenance`).
- Request-bound response validation now requires an independently supplied
  authenticated version summary and exactly compares all seven protocol,
  manifest, cache, worker, API-spec, and content/binary identity fields. The
  launcher derives this expectation from the staged runtime closure,
  FileVersionInfo, and the authenticated API-spec table, then threads it through
  worker results, launcher failures, and publication validation.
- Regression-first tests cover arbitrary and case-changed values, well-formed
  wrong hashes and cross-swaps, honest producer/launcher paths, and a removal
  mutation. Focused provenance passed 5/5, full Worker 473/473, Architecture
  203/203, launcher validation 3/3, the canonical build completed with zero
  warnings/errors, and `git diff --check` passed.

### SP-AUDIT-038 - Final release validation trusts fabricated component inventory (fixed)

- [x] Fixed by `568ef6e6f` (`fix: derive release component inventory`).
- Generation, final validation, and publication now compare the release
  manifest against an independently derived exact third-party catalog
  projection before authenticating package payloads. The same catalog-owned
  inventory drives exact component package identities and every owner-to-
  component SPDX `CONTAINS` edge.
- Regression-first fixtures cover fabricated, missing, duplicate, swapped-owner,
  foreign-entry, and self-consistently rewritten manifest/SBOM inventories plus
  missing/extra containment and canonical controls. The component matrix passed
  9/9 twice, the adjacent license matrix 15/15, all edited PowerShell parsed,
  and `git diff --check` passed. Long ReleaseEvidence and OfflinePlan tests were
  skipped after the same evidence path timed out in the preceding tranche.

### SP-AUDIT-036 - Release certifiers ignore SBOM license declarations (fixed)

- [x] Fixed by `d2bb1ef4f` (`fix: validate exact SBOM licenses`).
- Release generation, final validation, and publication planning now consume one
  exact SBOM license graph derived from the existing first-party package
  contract and third-party component catalog. Every package must match exact
  identity/version, declared and concluded license, `NOASSERTION` download
  location, `filesAnalyzed=false`, and the closed license-property set.
- Regression-first fixtures cover first- and third-party `NOASSERTION`, wrong,
  case, missing, and extra license fields; unknown and duplicate components;
  invalid download/file-analysis fields; canonical control; and a removal
  mutation. The focused matrix passed 15/15 and `git diff --check` passed. The
  full release-evidence integration command timed out once after 124 seconds
  without a result; it was not restarted and OfflinePlan was not run.

### SP-AUDIT-027 - Release-tag validation accepts branch refs (fixed)

- [x] Fixed by `7280c988d` (`fix: require exact release tag identity`).
- Release-tag validation now unconditionally requires the exact
  `refs/tags/v<version>` ref and name, `GITHUB_SHA` equal to checkout HEAD, an
  annotated tag object whose peeled commit equals that SHA, and ancestry from
  `origin/master`.
- A regression-first disposable local bare-remote fixture covers the exact
  annotated control; branch, empty, non-version, and wrong-version refs; wrong
  name/SHA/HEAD/tag commit; missing and lightweight tags; and missing/diverged
  `origin/master`. The focused fixture passed 1/1, full Architecture 183/183,
  and `git diff --check` passed. The broader release-publication test command
  timed out once after 124 seconds without a test result and was not restarted.

### SP-AUDIT-238 - Definitely-null throws retain an impossible declared type (fixed)

- [x] Fixed by `f2c7c147b` (`fix: project definitely null throws`).
- Explicit throws whose operand is proven null now project only
  `NullReferenceException`; proven non-null operands retain the declared type,
  and unknown operands retain the conservative declared-plus-null union. Catch
  reachability consumes the same proven-null fact so an impossible declared-type
  handler is not selected.
- Regression-first tests cover null literals and locals, branches,
  conditionals, explicit conversions, null-forgiving syntax, non-null and
  maybe-null operands, fields, coalescing, catch reachability,
  AllowedExceptions, generated projection, and a removal mutation. Focused
  Effects and Analyzer tests passed; full Effects 172/172 and Analyzer 310/310.
  After extracting the policy, the Architecture complexity check passed 1/1,
  the mutation was killed, and `git diff --check` passed.

### SP-AUDIT-232 - Implicit base initializers skip precondition replay (fixed)

- [x] Fixed by `fe3836ea6` (`fix: replay implicit base preconditions`).
- Source constructors without an explicit initializer now discover the exact
  compiler-selected parameterless base constructor once and anchor replay to
  Roslyn's real block or expression body operation. Explicit `base` and `this`
  initializers retain their existing path; structs, record copy constructors,
  and synthesized source-less constructors remain excluded.
- Regression-first tests cover classes and records, multiple constructors,
  exact-once behavior, valid/object bases, explicit base and this chains,
  generated and synthesized constructors, source and metadata targets, and a
  discovery-removal mutation. Focused tests passed 8/8, full Analyzer 309/309,
  Architecture 182/182, the mutation was killed, and `git diff --check` passed.

### SP-AUDIT-231 - ContractFor conflates positive and negative zero defaults (fixed)

- [x] Fixed by `1d1fee582` (`fix: compare floating defaults by bits`).
- Explicit float and double defaults now compare by their exact IEEE bit
  patterns, preserving the observable distinction between positive and negative
  zero. Other default-value types retain the existing boxed comparison, and NaN
  follows the canonical value exposed by Roslyn symbols.
- Regression-first tests cover both zero directions for float and double,
  compilation references, generated/current source, ordinary values,
  infinities, canonical NaN, non-floating defaults, overloads, binder behavior,
  and float/double comparator-removal mutations. Focused Generator passed 22/22
  and Contracts 3/3; full Generator 108/108, Contracts 108/108, Analyzer
  302/302, Architecture 182/182, and `git diff --check` passed.

### SP-AUDIT-221 - Function-pointer convention order is overconstrained (fixed)

- [x] Fixed by `ce98abf91` (`fix: compare function pointer conventions as sets`).
- Function-pointer unmanaged calling conventions now compare as exact unordered
  multisets. Roslyn's duplicate convention-owned return modopts are excluded
  only from their positional modifier comparison; convention identity and
  multiplicity remain authoritative, and all unrelated return/ref modifiers,
  ref kinds, types, nullability, and scoped state retain exact matching.
- Regression-first tests cover reversed return and parameter conventions, three
  conventions, duplicate cardinality, missing/substituted identities, metadata,
  generated companions, and existing generic/ref controls. Focused Generator
  passed 9/9 and Contracts 1/1; full Generator 86/86, Contracts 105/105,
  Analyzer 302/302, Architecture 182/182, the mutation was killed, and
  `git diff --check` passed.

### SP-AUDIT-216 - Parentheses disable direct precondition replay (fixed)

- [x] Fixed by `32b02f182` (`fix: replay parenthesized preconditions`).
- Direct-call ownership now unwraps only transparent parenthesized expressions
  before comparing the invocation site. Expression bodies, returns, local
  initializers, assignments, and nested parentheses therefore replay the same
  preconditions as their unparenthesized equivalents; conversions, checked
  expressions, and null-forgiving wrappers remain fail closed.
- Regression-first tests cover every supported owner shape, nested and argument
  parentheses, plain and valid controls, generated code, nontransparent
  wrappers, and a mutation that restores the ownership failure. Focused tests
  passed 3/3, full Analyzer 302/302, Architecture 182/182, the mutation was
  killed by the focused test, and `git diff --check` passed.

### SP-AUDIT-204 - Verifier libz3 pollutes application runtime assets (fixed)

- [x] Fixed by `bb5ec9e3d` (`fix: isolate verifier native tool asset`).
- The certified native library now lives under the verifier's build-tool
  closure at `tools/native/linux-x64/libz3.so`, and package targets require
  that exact payload. It is no longer classified by NuGet as an application
  runtime asset; the canonical worker continues to load its independently
  verified container-native root.
- A regression-first isolated `linux-x64` consumer with `PrivateAssets=all`
  verifies that project assets, runtime/native copy-local items, build output,
  and publish output contain no `libz3.so`. Package layout and canonical worker
  Z3 controls passed 2/2, the focused consumer passed 1/1, Architecture
  182/182, the container contract, and `git diff --check` passed. The full
  Package suite was not rerun.

### SP-AUDIT-197 - Ref-readonly expression bodies appear bodyless (fixed)

- [x] Fixed by `f8cd971c2` (`fix: resolve ref expression bodies`).
- Contract inventory now resolves a ref expression body through its owning
  declaration, where Roslyn exposes the method-body operation. Ordinary
  expression bodies remain expression-rooted, while block bodies, partial
  implementations, and generated ownership retain their existing paths.
- Regression-first tests cover ref and ref-readonly expression and block
  returns, direct inventory body discovery, ordinary expression bodies,
  genuinely missing bodies, partial implementations, generated-named source,
  and a mutation restoring the isolated-ref lookup. Focused generator tests
  passed 7/7 and inventory tests 2/2; full Generator 77/77, Contracts 104/104,
  Analyzer 299/299, Architecture 182/182, and `git diff --check` passed.

### SP-AUDIT-196 - Exact ref-readonly parameters are rejected (fixed)

- [x] Fixed by `f165c6772` (`fix: match ref readonly parameters`).
- Parameter custom-modifier matching now removes only the compiler-generated
  InAttribute when both sides are readonly input ref kinds (`in` or
  `ref readonly`). Exact ref-kind equality still runs first, so `ref`, `out`,
  `in`, and `ref readonly` remain distinct; scoped, return, and other custom
  modifiers remain exact.
- Regression-first tests cover interface, abstract, virtual, metadata, and
  generated-named methods; scoped exact/mismatch; overload selection; return
  modifiers; binder clauses; and every ref-kind mismatch. Focused Generator
  passed 11/11 and binder 1/1; full Generator 70/70, Contracts 102/102,
  Analyzer 299/299, Architecture 182/182, and `git diff --check` passed.

### SP-AUDIT-174 - Non-generic wrappers misalign generic ContractFor owners (fixed)

- [x] Fixed by `ca98966f1` (`fix: align generic ContractFor owners`).
- Type-parameter owners are now compared by ordinal position within the filtered
  sequence of generic owner layers, so intervening non-generic lexical wrappers
  no longer shift ownership. Existing exact arity, order, constraints,
  nullability, and constructed-type checks remain authoritative.
- Regression-first tests cover target-only, companion-only, aligned, and
  multiple wrappers; generated-named sources; constructed binding; and existing
  reordered, constraint, and closed-type negatives. Focused generator and
  Contracts tests passed 5/5 each, full Generator 59/59, Contracts 101/101,
  Analyzer 299/299, Architecture 182/182, and `git diff --check` passed.

### SP-AUDIT-167 - Fuzz campaign validation accepts non-schema JSON (fixed)

- [x] Fixed by `93863ba1e` (`fix: validate fuzz runner evidence`).
- The campaign now delegates every schema-4 runner result to a strict
  System.Text.Json token validator. It requires exact top-level, failure, and
  FrontendCoverage property sets; integer and Boolean token kinds; non-null
  arrays; invocation identity; complete count relationships; positive full
  frontend coverage; passing status; and zero failures.
- Regression-first fixtures cover numeric strings, null and malformed arrays,
  omitted/extra fields, wrong schema/status, count and invocation mismatches,
  incomplete coverage, nonempty failures, and canonical rotating/retained runs.
  Focused tests passed 1/1, Architecture 182/182, the container contract, and
  `git diff --check` passed. The SP159 schema-3 campaign envelope is unchanged,
  and the long fuzz campaign was not rerun locally.

### SP-AUDIT-160 - Standalone gate evidence is not source and result bound (fixed)

- [x] Fixed by `900b035c0` (`fix: authenticate standalone gate evidence`).
- Standalone evidence now performs a fresh Release Rebuild with the exact source
  commit embedded in the gate assembly, then binds the DLL and PDB SHA-256,
  module MVID, acceptance-contract hash, commit, gate identity, and passing
  result. The stale `--no-build` evidence path is removed.
- A strict shared decoder enforces the exact corpus or performance result schema,
  property types, gate/status/commit/build identities, and empty failure set.
  Interactive non-certifying commands retain raw output compatibility.
- Regression-first fixtures cover `{}`, stale binaries, wrong schema/gate/status/
  commit/build identity, missing/extra fields, and valid corpus/performance controls.
  Focused Architecture tests passed 2/2, the gate producer test 1/1, release
  closure and container contract passed, and `git diff --check` passed. The long
  corpus/performance campaigns were not rerun locally.

### SP-AUDIT-159 - The nightly fuzz campaign is disconnected (fixed)

- [x] Fixed by `8c2af405d` (`fix: connect nightly fuzz campaign`).
- The container dispatcher now exposes a Release-only, clean exact-commit
  `fuzz-nightly` command, and the nightly workflow invokes it after acceptance.
  PR acceptance keeps its fixed pull-request campaign. Nightly uses the
  catalog-owned case count, a rotating UTC seed, and every retained seed.
- Fuzz evidence schema 3 binds the exact commit, status, rotating and retained
  counts/seeds, retained manifest hash, per-run result hashes, observed cases,
  and validation results. Regression tests cover command/workflow connectivity,
  seed and case behavior, retained replay, schema/status evidence, and the
  disconnected-script mutation. Focused and authority tests passed, Architecture
  179/179, the container contract, and `git diff --check` passed. The long
  10,000-case campaign was intentionally not run locally.

### SP-AUDIT-138 - Compiler-elided invocations still receive SP0027 (fixed)

- [x] Fixed by `56dc75db2` (`fix: skip compiler-elided invocations`).
- Effects and Requires analysis now share one compiler-emission policy for
  source/metadata Conditional methods and unimplemented partial methods.
  Requires discovery prunes an elided invocation's entire subtree, including
  its arguments, and analyzer declaration processing skips non-executable
  partial declarations.
- Regression-first tests cover source Conditional methods with multiple symbols,
  metadata Debug.Assert with and without DEBUG, missing and implemented partial
  methods, exact SP0027 counts, and absence of spurious SP0047. Focused tests
  passed 6/6; final Analyzer passed 299/299, Architecture 178/178, and
  `git diff --check` passed.

### SP-AUDIT-136 - Packaged MSBuild configuration lacks one normalized authority (fixed)

- [x] Fixed by `1d90a6f16` (`fix: normalize late package configuration`).
- Portable and verifier targets now derive analyzer, generator, collector,
  tools, worker, launcher, and protocol paths after project-body evaluation.
  Runtime-closure validation is an explicit dependency before verification
  initialization, so mismatches fail before invalidation, output, or ownership
  marker mutation. BuildTasks remains package-owned; private overrides are
  restricted to explicit test seams.
- Regression-first package tests cover project-body analyzer/collector effective
  paths and late tools/worker/launcher mismatches with no result or marker.
  The full container build completed with zero warnings/errors, focused tests
  passed 4/4, final Architecture passed 178/178, and `git diff --check` passed.

### SP-AUDIT-124 - One SARIF path breaks multitarget verification (fixed)

- [x] Fixed by `6863cee87` (`fix: scope multitarget SARIF outputs`).
- A configured SARIF path keeps its existing meaning for single-target projects.
  For multitarget projects, each inner build now owns
  `<configured-directory>/<target-framework>/<filename>` for both relative and
  absolute paths. Invalidation, launcher publication, and messages all use the
  same effective path.
- Regression-first package tests cover parallel relative net8/net9 builds with
  exact marker binding; reversed serial three-framework absolute builds;
  incremental rebuild and Clean/rebuild; the default no-SARIF case; and
  single-target compatibility. The focused matrix passed 3/3, the single-target
  control 1/1, Architecture 178/178, and `git diff --check` passed.

### SP-AUDIT-114 - ContractFor reports foreign-compilation source locations (fixed)

- [x] Fixed by `d97f35ec7` (`fix: bind ContractFor diagnostic locations`).
- ContractFor validation now threads the active Compilation through every symbol
  and attribute location decision and accepts a source location only when its
  syntax tree belongs to that compilation. CompilationReference and metadata
  targets fall back to the current companion attribute; local targets retain
  their target-member location.
- Regression-first tests cover foreign source, metadata, and local source
  targets with exact SPCF0004 IDs, locations, and counts, plus a trusted mutation
  that removes active-compilation ownership. Focused tests passed 3/3,
  ContractFor Generator 54/54, Analyzer 293/293, Architecture 178/178, and
  `git diff --check` passed.

### SP-AUDIT-112 - Valid long filenames overflow publication metadata names (fixed)

- [x] Fixed by `572cdc7ee` (`fix: bound publication metadata names`).
- Publication locks and ownership markers now use fixed-size SHA-256 identities
  of canonical paths under a private sibling `.sharpproof-publication` directory.
  The directory must be owned by the effective user and inaccessible to group
  and other users; legacy suffixes and the new namespace remain reserved.
- Regression-first tests cover all four publication members at Linux NAME_MAX,
  multibyte UTF-8 boundaries, stable/distinct and cross-directory identities,
  private mode, namespace collisions, unowned directories, and existing
  overlap/rollback/symlink behavior. The baseline failed with errno 36; final
  publication tests passed 20/20, package lock tests 2/2, Architecture 178/178,
  the full container build with zero warnings/errors, and `git diff --check`.

### SP-AUDIT-105 - Semantic cache policy misses aliases and indexer writes (fixed)

- [x] Fixed by `525b14823` (`fix: trace semantic cache writes`).
- SPMETA010 now shares one semantic-answer classifier across cache method calls,
  indexer assignments, and property assignments. On-demand local reaching-value
  analysis follows direct aliases and straight-line reassignment, conservatively
  joins conditional writes, excludes nested callable writes, and treats unresolved
  SharpProof answer/result/outcome values as non-cacheable.
- Source return syntax and semantic types distinguish safe Proven values and
  external lookalikes from Unknown, Timeout/TimedOut, Error, and Failure/Failed
  states. Regression tests cover Add/AddOrUpdate/Write, indexer/property writes,
  direct and branched aliases, safe overwrite, unresolved parameters, and safe
  name/lookalike controls. Focused tests passed 23/23, Meta Analyzer 83/83,
  Architecture 178/178, and `git diff --check` passed.

### SP-AUDIT-093 - Pilot qualification accepts duplicate and vacuous libraries (fixed)

- [x] Fixed by `9ce2acfdd` (`fix: require substantive pilot coverage`).
- The pilot authority now validates the exact five-row catalog, unique pilot IDs
  and canonical projects, and unique external package identities and versions
  derived from each project PackageReference. The schema-2 report binds those
  identities and the exact claim projection from each hash-bound worker result.
- Category coverage is independently derived from manifest claim kinds:
  effect-heavy pilots require an Effect claim, contract-heavy pilots require a
  Postcondition, and the mixed strict pilot requires both. The receipt reuses
  the same validator; three pilots now contain real postcondition obligations.
- Regression-first fixtures cover the canonical five libraries, duplicate
  report/catalog IDs, projects, and libraries; mislabeled categories; zero
  report and underlying claims; wrong claim kinds and project references;
  unknown catalog fields; and the existing package-authority cases. The fixture,
  focused Architecture test, Architecture 178/178, container contract, and
  `git diff --check` passed. The real pilot restore was not run because it
  requires external NuGet access.

### SP-AUDIT-092 - Contract companion bodies are analyzed as implementations (fixed)

- [x] Fixed by `abc61e5b1` (`fix: exclude contract companion bodies`).
- Full and lightweight operation-block analysis now use the shared effective
  ContractFor companion inventory to exclude companion method bodies from
  implementation analysis. Symbol/control validation, companion clause
  extraction, target binding, and target implementation diagnostics remain
  active.
- Regression-first tests cover unsupported delegate, write, and throw dummy
  bodies under contracts/all/effects; retained Requires binding; generated and
  mixed generated/handwritten companions; and invalid generated companions.
  Focused tests passed 7/7, Analyzer 293/293, Architecture 178/178, relevant
  Worker companion-parity tests 4/4, and `git diff --check` passed.

### SP-AUDIT-083 - Compose projects share one mutable tooling image tag (fixed)

- [x] Fixed by `44afbbcfa` (`fix: isolate compose tooling images`).
- The default tooling image is now `<COMPOSE_PROJECT_NAME>-tooling:local`, so
  Compose project isolation covers images as well as containers and volumes.
  Build and run share the same project-private tag; a reviewed explicit
  `SHARPPROOF_TOOLING_IMAGE` override remains supported and documented.
- Regression-first tests cover distinct project names, stable same-project
  resolution, explicit override, structural decoys, and restoration of the old
  global tag. Container authority passed 13/13, Architecture 178/178, the full
  container contract, actual `compose config --images`, cached build/run smoke,
  and `git diff --check` passed.

### SP-AUDIT-079 - Backend renewal failure is erased as a method timeout (fixed)

- [x] Fixed by `3c3050f0a` (`fix: preserve backend renewal failures`).
- Lane renewal now returns typed success, unsupported, backend-unavailable, or
  infrastructure outcomes. The completed callable retains MethodTimeout; only
  unclaimed targets receive the renewal failure, and shared retirement prevents
  later claims. Native/Z3 loading and reused-backend failures remain backend
  unavailable; arbitrary factory/null/disposal failures are infrastructure.
- Regression-first tests cover typed renewal failures, successful renewal,
  reused/null/disposal states, multiple targets, and response validation while
  preserving cancellation/project-timeout precedence. Focused tests passed 8/8,
  Protocol 55/55, Architecture 176/176, Worker 465/465, and `git diff --check`.

### SP-AUDIT-076 - Text diagnostic transport loses structured MSBuild semantics (fixed)

- [x] Fixed by `4acbda3f0` (`fix: transport structured verifier diagnostics`).
- Launcher and BuildTask now share a strict versioned JSON-line transport for
  SP0047/SP0048 warnings and errors, preserving exact code, severity, path,
  line, column, message, punctuation, Unicode, and embedded newlines. Structured
  errors suppress only the redundant generic exit-code error; infrastructure
  failures retain it. The legacy parser uses the valid rightmost grammar boundary.
- Regression-first tests cover marker-like paths, structured and legacy records,
  malformed/locationless records, severity-specific MSBuild logging, packaged
  warning policy, and target topology. BuildTask tests passed 14/14, packaged
  policy tests 2/2, topology 1/1, Architecture 176/176, and `git diff --check`.
  One unrelated existing launcher fixture was blocked earlier by the separate
  `response.callable_projection` invariant and was not modified.

### SP-AUDIT-070 - Never-invoked lambda bodies are analyzed as executed (fixed)

- [x] Fixed by `568cda5a9` (`fix: gate lambda body analysis by execution`).
- Parent callable traversal now exclusively owns nested Requires execution
  analysis. It tracks reachable delegate aliases and analyzes invoked,
  conditionally invoked, returned, passed, or otherwise escaped lambdas, while
  unused lambda bodies and nested bodies under dead lambdas remain unexecuted.
  SP-AUDIT-069 declaration validation remains independent.
- Regression-first tests cover dead, invoked, copied, conditional, returned,
  passed, nested-live/dead, expression-tree, generated, and exact-count cases.
  The focused matrix passed, the nested suite 15/15, Analyzer 287/287,
  Architecture 176/176, and `git diff --check` passed.

### SP-AUDIT-069 - Nested callable declarations bypass full method policy (fixed)

- [x] Fixed by `6b92ef813` (`fix: validate nested callable controls`).
- Independent syntax-node declaration analysis now semantically resolves trusted
  `SharpProofSuppress` and `SharpProofTrusted` attributes on local functions and
  lambdas regardless of invocation reachability. Tree/span deduplication prevents
  duplicates when reachable-callable analysis later visits the same attribute;
  generated trees remain excluded and nested bodies are not executed.
- Regression-first tests cover unused, invoked, and method-group locals;
  unused/invoked/expression-tree lambdas; nested valid controls; exact diagnostic
  counts; generated exclusion; and the unannotated anonymous-method grammar
  control. Focused tests passed 2/2, the nested suite 14/14, Analyzer 286/286,
  Architecture 176/176, and `git diff --check` passed.

### SP-AUDIT-051 - Nested catch rethrows fabricate an outer exception (fixed)

- [x] Fixed by `7cf5c5d54` (`fix: bind rethrows to nearest catch`).
- Bare rethrows are now attributed only to their nearest lexical catch, while
  lambdas and local functions remain separate execution owners. Nested catches
  no longer cause the outer caught exception to escape.
- Regression-first tests cover nested, direct outer, multiply nested, filtered,
  `finally`, sibling, lambda/local-function, and generated selected analyzer
  cases. Focused regressions passed, Effects passed 171/171, Analyzer 284/284,
  Architecture 176/176, and `git diff --check` passed.

### SP-AUDIT-043 - Worker result state is not an exact producer projection (fixed)

- [x] Fixed by `9b4096793` (`fix: validate exact worker result state`).
- Request-bound validation now derives exact claim-kind reason tuples, callable
  coverage, error-code failure category, failure reason, and run status from the
  validated evidence and requires equality. Producers emit explicit pre-manifest
  timeout/cancellation identities, while semantic backend failures remain
  distinct from thrown infrastructure failures.
- Regression-first tests cover false timeout/cancel/failure, fabricated callable
  coverage, cross-kind claim reasons, compiler/backend/containment/malformed
  error swaps, genuine interruption/failure, mixed and zero-claim controls, and
  producer parity. Projection tests passed 26/26, Protocol 55/55, Worker Program
  7/7, Architecture 176/176, protocol generation verification, the container
  contract, and `git diff --check`. Full Worker was 459/460 before the final
  producer-parity correction; the sole failing scenario and its matrix passed
  targeted validation afterward.

### SP-AUDIT-042 - Partial callable normalization lacks one executable owner (fixed)

- [x] Fixed by `0fb6bd878` (`fix: assign one partial callable owner`).
- Analyzer sessions now normalize partial methods to their implementation and
  atomically assign one executable-analysis owner. Definition-side attributes
  remain available, while only the implementation body and location produce
  diagnostics and semantic outcomes.
- Regression-first tests cover definition-, implementation-, both-, and
  conflicting-attribute placement; valid and violating bodies; partial
  properties; generated/handwritten splits; concurrent runs; Requires call
  sites; exact locations; and compiler collector ownership. Focused Analyzer
  tests passed 9/9, Worker ownership controls 2/2, Analyzer 282/282,
  Architecture 176/176, the container contract, and `git diff --check` passed.

### SP-AUDIT-037 - Generated classification can suppress handwritten analysis (fixed)

- [x] Fixed by `45335eef4` (`fix: require exact generated headers`).
- Generated-header detection now examines only the first significant leading
  trivia and accepts an exact conventional auto-generated token in a single-line
  or inline block comment. Embedded mentions, malformed or suffixed tokens,
  documentation comments, and markers after license comments remain handwritten;
  provider, filename-suffix, and attribute authorities are unchanged.
- Regression-first tests cover exact token variants and case, whitespace and
  block comments, explanatory/license mentions, malformed/embedded/suffixed
  text, and behavioral SP0027 suppression. The focused matrix passed 19/19,
  Analyzer passed 274/274, Architecture passed 176/176, the container contract
  passed, and `git diff --check` passed.

### SP-AUDIT-021 - Documented ordinary interpolation is rejected (fixed)

- [x] Fixed by `406c6b80f` (`fix: support ordinary interpolated strings`).
- The generated operation catalog now admits ordinary interpolation. Constant
  interpolation is effect-free; runtime interpolation accounts for allocation,
  expression effects, and shared implicit `ToString` resolution. Alignment,
  format clauses, custom handlers, and user conversions continue to abstain.
- Regression-first tests cover constants, strings, scalars, escaped braces,
  expression effects, throwing `ToString`, unsupported alignment/format and
  handlers, and generated selected projection. Frontend passed 64/64, Effects
  170/170, Analyzer 257/257, Architecture 176/176, the container contract
  passed, and `git diff --check` passed.

### SP-AUDIT-020 - Exact safe array stores report type-mismatch throws (fixed)

- [x] Fixed by `297d71965` (`fix: prove exact fresh array stores safe`).
- Effect analysis now records fresh array runtime element types by fresh-region
  identity, follows local alias unions, and suppresses
  `ArrayTypeMismatchException` only for a single exact fresh array with an
  implicit CLR assignment conversion. Covariant, parameter, field, and
  multi-provenance arrays remain conservative; null, sealed, and value-array
  behavior is preserved.
- Regression-first tests cover fresh object/base/interface/boxing stores, local
  aliases, covariant arrays, parameters, fields, null, value arrays, and
  generated selected analyzer projection. Effects passed 169/169, Analyzer
  passed 256/256, Architecture passed 176/176, the container contract passed,
  and `git diff --check` passed.

### SP-AUDIT-019 - Unreachable catch handlers contribute effects (fixed)

- [x] Fixed by `fe0f8af81` (`fix: model exception handler reachability`).
- A shared exception-handler reachability authority now derives potential known
  and unknown exceptions from the protected region and applies ordered catch
  type and constant-filter selection. Catch/filter effects are scanned only
  when reachable; `finally` remains conservative on reachable exits.
- Regression-first tests cover empty and nonthrowing protected regions, known
  and unknown throws, true/false filters, ordered exception hierarchies,
  rethrow, `finally`, and generated selected analyzer projection. Effects passed
  168/168, Analyzer passed 255/255, Architecture passed 176/176, the container
  contract passed, and `git diff --check` passed. The registered mutation is
  pinned but its campaign remains blocked by the unrelated pre-existing
  nonunique generated ContractFor mutation target.

### SP-AUDIT-017 - Publication-set path hashing is not injective (fixed)

- [x] Fixed by `b92f58309` (`fix: frame publication set identities`).
- Publication-set IDs now hash a versioned domain, path count, and ordinally
  sorted sequence of big-endian length-prefixed strict UTF-8 path bytes. The
  existing acquisition and marker validation paths share this sole identity.
- Regression-first tests reproduce the exact newline-delimiter collision and
  require the second partial-overlap set to fail without changing existing
  marker bytes or creating new markers. Additional controls cover empty,
  single, multiple, reordered, carriage-return, newline, and Unicode
  separator-like paths. Focused Worker tests passed 4/4, Architecture passed
  176/176, the container contract passed, and `git diff --check` passed.

### SP-AUDIT-016 - Property and event accessor calls escape SP0027 analysis (fixed)

- [x] Fixed by `af95a3889` (`fix: analyze accessor contract calls`).
- Requires discovery now represents property/indexer getters and setters and
  event add/remove accessors with exact receiver and parameter-ordinal argument
  bindings. Target-aware deduplication prevents duplicate getter diagnostics;
  compound/increment setters and conditional access remain explicitly
  nonreplayable and fail closed when exact assigned values or execution are not
  available.
- Regression-first tests cover static/instance getters and setters, indexer
  arguments and assigned values, event add/remove, compound/increment access,
  valid calls, generated code, direct source accessor contracts, exact
  candidate shapes, and conditional-access abstention. The primary focused
  matrix passed 7/7, the final static/conditional regression passed, Analyzer
  passed 252/252, Architecture passed 176/176, and `git diff --check` passed.

### SP-AUDIT-009 - Container evidence path guards are case-insensitive (fixed)

- [x] Fixed by `174d9a7d1` (`fix: enforce ordinal contained paths`).
- A shared canonical ordinal child-path authority now owns repository-contained
  evidence, output, baseline, generator, and cleanup boundaries. Root equality,
  case-distinct and prefix siblings, and traversal escapes fail closed; exact,
  canonicalized, and absolute children remain supported.
- Regression-first fixtures exercise every boundary shape and assert all
  affected consumers use the shared authority. A trusted mutation restores the
  case-insensitive comparison. Architecture passed 176/176; container contract,
  release closure, deterministic generators, release-configuration fixtures,
  and `git diff --check` passed. The full mutation campaign remains blocked by
  an unrelated pre-existing nonunique target in
  `generated-contract-for-final-compilation-validation`.

### SP-AUDIT-005 - Initializer call sites escape SP0027 analysis (fixed)

- [x] Fixed by `0c946f892` (`fix: analyze contract calls in initializers`).
- Contracts-mode analysis now inventories field and auto-property initializer
  operation roots, excludes locals and generated trees, selects the relevant
  static or instance constructor deterministically, and reuses the existing
  Requires binding and concrete replay without broadening effect analysis.
- Regression-first coverage includes invalid instance/static fields and
  auto-properties, a valid initializer, multiple constructor overloads with no
  duplicate diagnostics, and a generated-tree control. The focused tests passed
  2/2, the full Analyzer suite passed 245/245, the full Architecture suite
  passed 175/175, and `git diff --check` passed.

### SP-AUDIT-003 - Pilot qualification is not bound to fresh candidate outputs (fixed)

- [x] Fixed by `e739b3681` (`fix: bind pilots to fresh candidate evidence`).
- Pilot qualification now requires a clean checkout and exactly six candidate
  packages whose filenames, nuspec IDs, versions, and repository commits match
  HEAD. It records each package's size and SHA-256 and isolates NuGet, DOTNET
  home, verifier cache, logs, and configuration under a run-private directory.
- Every pilot deletes prior request, result, compiler-manifest, and SARIF files,
  requires a fresh complete four-file evidence set, and records the exact path,
  kind, size, and SHA-256 of each file. The schema-2 report and qualification
  receipt preserve those package and per-pilot identities.
- Regression-first fixtures cover changed package bytes, stale commits,
  missing/extra packages, wrong IDs and versions, incomplete/stale evidence,
  ambient same-version cache collisions, and an exact valid six-package,
  five-pilot report. Validation passed the focused fixtures, Architecture
  (175/175), the release-publication package tests (18/18), and `git diff
  --check`. The real five-pilot restore was not run because it requires external
  dependency access.

### SP-AUDIT-107 - The TCB inventory can self-authorize changes (fixed)

- [x] Fixed by `bc90c1ab7` (`fix: derive complete release authority closure`).
- A constrained independent traversal now derives the release authority from
  workflow and container entrypoints, statically invoked PowerShell and shell
  scripts, local actions, catalogs, and package manifests. Its canonical
  84-path set must exactly match the catalog and every leaf must occur exactly
  once in the TCB; missing roots/leaves and untracked paths fail closed.
- Regression-first disposable fixtures cover deletion, movement, and byte
  changes for all formerly missing authorities, digest and changed-file
  selection, newly invoked leaves, uninvoked decoys, duplicate inventory,
  cycles, and the canonical closure. Architecture passed 174/174 and the
  container contract passed.

### SP-AUDIT-065 - Release workflow authority is checked as raw substrings (fixed)

- [x] Fixed by `a6956e4b2` (`fix: bind release workflow authority`).
- The release contract now owns canonical identities for the two publishing
  jobs. A constrained structural workflow parser rejects duplicate job keys and
  YAML alias/merge substitutions, isolates the exact job blocks, and binds
  their guards, environments, needs, permissions/OIDC, variable and secret
  flow, artifact/login/build/push commands, and step order by SHA-256. No raw
  whole-workflow substring is accepted as release authority.
- Regression-first fixtures reject comment and dead-job decoys, wrong
  environment/guard/secret/OIDC/needs, reordered or missing steps, duplicates,
  and aliases while preserving the exact workflow. Architecture passed 172/172
  and release-publication tests passed 18/18.

### SP-AUDIT-054 - Protected release tags may have unreviewed bypass actors (fixed)

- [x] Fixed by `5e2cfe916` (`fix: bind release ruleset bypass authority`).
- The release contract now owns the exact deletion/update rules and an explicit
  empty bypass-actor set. Validation rejects rule parameters and requires the
  bypass field, canonical actor shape, type, numeric identity, mode, ordinal
  uniqueness, and exact catalog equality before producing evidence.
- Regression-first mocked-GitHub fixtures cover user, team, app, repository
  role, always/pull-request/unknown modes, actor type/case/ID/field errors,
  duplicates, missing bypass data, and extra/missing/parameterized rules. The
  no-bypass control passed, Architecture passed 172/172, and release-publication
  tests passed 18/18.

### SP-AUDIT-053 - Release tag policy does not bind the effective allowed ref set (fixed)

- [x] Fixed by `72b5d934a` (`fix: bind effective release tag policies`).
- Release configuration now requires exactly one active tag ruleset with an
  exact, case-sensitive, duplicate-free include/exclude set and rejects any
  include/exclude conflict. Each publishing environment must expose the exact
  typed deployment-ref set; wildcard, branch, extra, missing, duplicate, and
  case-different policies fail. Variable and secret checks remain independent
  least-privilege presence checks.
- Local mocked-GitHub fixtures cover the canonical contract plus all authority
  mutations, including a second active ruleset. The fixture gate passed,
  Architecture passed 172/172, and release-publication tests passed 18/18.

### SP-AUDIT-192 - Exception text can masquerade as a mutation assertion (fixed)

- [x] Fixed by `b90adfc36` (`fix: authenticate mutation assertion failures`).
- Mutation kills now require one exact structured TRX ErrorInfo with the
  supported NUnit assertion grammar and a nonempty stack containing only
  adapter/test frames. Generic or custom exception/error/failure/fault headers,
  infrastructure markers, missing or extra fields, and mixed failures are
  rejected. A canonical assertion-provenance digest binds test/execution IDs,
  message, and stack through child results, resume/shard reuse, and independent
  final catalog validation.
- Regression-first fixtures cover custom ProbeFailure and qualified types,
  exception/error/stack variants, missing structured stack, benign context,
  provenance removal/change, real scalar/collection/multiple assertions, and
  mixed failures. Mutation evidence fixtures passed, all affected scripts
  parsed, and Architecture passed 171/171.

### SP-AUDIT-178 - Batched baselines can falsely certify mutation kills (fixed)

- [x] Fixed by `5c10dd8de` (`fix: isolate mutation baselines by invocation`).
- Mutation baselines now run once per exact project, filter, and configuration
  invocation identity, with reuse only for byte-identical identities. Each
  mutant result binds the baseline invocation digest, passing selected-test
  ledger, contained TRX path, and TRX SHA-256; saved baselines, resume state,
  final validation, and parallel merging independently revalidate them.
- Regression-first planner and evidence fixtures cover ordered/shared-state
  contamination, identical and differing filters/projects/configurations,
  deterministic parallel order, baseline failure/timeout/missing TRX, stale
  resume data, receipt/hash/ledger mismatch, and valid kills. All mutation
  fixtures passed, PowerShell parsing passed, and Architecture passed 171/171.

### SP-AUDIT-126 - Ill-formed UTF-16 is non-injective across verifier boundaries (fixed)

- [x] Fixed by `57c372cd4` (`fix: reject ill-formed UTF-16 inputs`).
- One shared UTF-16 authority now rejects lone surrogates in source,
  generated/additional text, IR construction, and semantic hashing before any
  lossy UTF-8 or JSON boundary. Roslyn string literals containing malformed
  UTF-16 map to the existing typed UnsupportedExpression abstention; valid
  surrogate pairs and U+FFFD remain distinct throughout fingerprints, claim
  identities, and artifact round trips.
- Regression-first coverage includes lone high/low surrogates, valid pairs,
  replacement characters, source/generated text, diagnostics, fingerprints,
  direct IR construction, JSON round trips, and compiler-term abstention.
  Analyzer passed 243/243, Frontend 64/64, Worker 436/436, and Architecture
  171/171.

### SP-AUDIT-111 - Publication can overwrite compiler outputs created later (fixed)

- [x] Fixed by `69803b6fe` (`fix: reserve compiler outputs from publication`).
- Publication invalidation now receives the complete compiler-owned output set
  and rejects lexical or existing-file identity collisions involving any
  request, result, manifest, SARIF, or ownership-marker path before acquiring
  publication ownership. The reserved set includes final/intermediate and
  reference assemblies, PDB and documentation outputs, generated compiler
  inputs, and runtime/dependency manifests.
- Regression-first coverage exhaustively tests four publication members
  against ten compiler outputs, plus packaged clean and incremental collision
  cases that preserve prior assembly bytes and create no marker. BuildTask tests
  passed 12/12 and Architecture passed 171/171.

### SP-AUDIT-131 - Public package metadata is not contract-validated (fixed)

- [x] Fixed by `00429629f` (`fix: validate public package metadata`).
- The authoritative first-party package contract now owns exact case-sensitive
  `authors`, `projectUrl`, `description`, and `tags` values for all three
  packages. The shared nuspec validator requires each main-package field
  exactly once as attribute-free nonempty text and permits symbol-package
  omission only for canonical NuGet output; any present symbol value must
  match. Every release authority already invokes this shared validator.
- Regression-first fixtures cover missing, changed, duplicated, recased, and
  alternate XML forms for every field and all package identities, plus
  symbol/main mismatches. Focused tests passed 47/47, Architecture 171/171,
  and the release-evidence and offline-plan package controls both passed.

### SP-AUDIT-094 - Package licenses are not bound to release evidence (fixed)

- [x] Fixed by `301aa8357` (`fix: bind package licenses to release evidence`).
- The first-party package contract now owns the exact case-sensitive license
  expression for every package. The shared nuspec authority requires exact
  main-package declarations, permits symbol-package omission only when NuGet's
  canonical symbol output omits the field, rejects any conflicting declaration,
  and derives and validates both SPDX declared and concluded licenses. Evidence
  generation, final validation, and plan-only publication each recheck it.
- Regression-first fixtures reject wrong valid licenses, missing or file-form
  main declarations, case/spelling changes, symbol mismatches, and missing or
  inconsistent SBOM fields. Focused tests passed 25/25, Architecture 149/149,
  and both release-evidence and offline-plan package controls passed locally.

### SP-AUDIT-067 - SBOM package dependencies are fabricated, not derived (fixed)

- [x] Fixed by `f68cc2669` (`fix: derive SBOM dependencies from packages`).
- One shared release dependency authority now parses and canonicalizes the
  exact dependency groups in all three main and symbol package nuspecs,
  enforces the supported framework and exact-version graph, and derives the
  SPDX `DEPENDS_ON` relationships from those authenticated bytes. Evidence
  generation, immutable-artifact validation, and plan-only publication each
  invoke that authority independently.
- Regression-first fixtures reject fabricated, missing, extra, wrong-version,
  reversed, wrong-framework, duplicate, and symbol/main mismatch graphs while
  retaining the canonical packages and SBOM. Focused tests passed 14/14,
  Architecture passed 138/138, and the release-evidence and offline-plan
  package controls both passed without network or publication operations.

### SP-AUDIT-008 - Primary-constructor callable ownership is incomplete (fixed)

- [x] Fixed by `e37714bc5` (`fix: inventory primary constructor callables`).
- One shared compiler-symbol inventory now maps each C# primary-constructor
  type declaration to exactly one synthesized constructor. The contracts-only
  analyzer path replays only the primary base initializer, never admitting the
  type declaration as an effect/postcondition body, while compiler collection
  records the selected synthesized callable and claims exactly once and keeps
  unsupported lowering fail closed.
- Regression-first class and record fixtures prove invalid base arguments emit
  SP0027 and both constructors enter the manifest; valid, explicit-constructor,
  and generated-tree controls preserve ownership behavior. Focused tests passed
  6/6, the full Analyzer suite passed 232/232, and Architecture passed 124/124.

### SP-AUDIT-057 - Build success is not bound to the current verification result (fixed)

- [x] Fixed by `43fcff1f4` (`fix: bind builds to current verifier results`).
- The verifier now requires every runtime-selection property to resolve to the
  exact package-owned tools, worker, launcher, protocol, and BuildTasks closure.
  After an exit-zero launch, a BuildTask independently requires and parses the
  current request, result, and compiler manifest, binds their paths and
  SHA-256 identities, and requires protocol 11 with a Complete run. Supported
  non-verifying build transitions invalidate stable proof output rather than
  preserving an earlier success.
- Regression-first coverage rejects resultless, malformed, stale-request, and
  runtime-override builds while retaining authentic packaged verification and
  design-time behavior. Focused tests passed 10/10, BuildTask tests 11/11,
  Architecture 124/124, and the full Package suite 181/181 with one expected
  unsupported-host skip; the container contract also passed.

### SP-AUDIT-046 - Container contract does not bind the effective Docker authority (fixed)

- [x] Fixed by `b3444f92d` (`fix: bind effective container authority`).
- The container gate now parses a constrained authority grammar: every
  catalog-owned image argument must be declared exactly once globally, the
  complete ordered `FROM` graph must consume those arguments exactly, and all
  Compose services must inherit one structurally exact image/build/platform
  authority without overrides. The unnecessary unpinned external Dockerfile
  frontend directive was removed, so Docker's bundled parser is the only
  frontend authority.
- Regression-first coverage rejects ten duplicate, redeclared, unused,
  alternate, comment-decoy, frontend, and service-override mutations while
  preserving the canonical control. The focused suite passed 11/11, full
  Architecture passed 124/124, the container contract passed, and the tooling
  image rebuilt successfully.

### SP-AUDIT-028 - Qualification evidence blesses arbitrary package files (fixed)

- [x] Fixed by `d6b69fd55` (`fix: authenticate release qualification evidence`).
- Qualification now requires a clean exact-HEAD checkout at an annotated tag,
  invokes the strict six-package release validator, and binds acceptance,
  coverage, full mutation, package-consumer, and five-pilot receipts to the
  candidate commit and their immutable evidence bytes. Package-consumer and
  pilot receipts also bind the exact six candidate package names, sizes, and
  SHA-256 hashes.
- Regression-first fixtures reject malformed, failed, stale, incomplete,
  duplicate, and invalid-digest gate evidence; architecture assertions keep
  every required workflow gate and the strict release authority connected.
  The focused qualification suite passed 5/5, the full Architecture suite
  passed 113/113, and the canonical container contract gate passed.

### SP-AUDIT-015 - Release evidence does not authenticate package payloads (fixed)

- [x] Fixed by `76aedafd8` (`fix: authenticate package payload closure`).
- Evidence generation now derives each main package's exact managed/native
  payload closure from the package specifications and first-party inventory,
  compares first-party bytes and assembly identities with Release outputs,
  verifies pinned Z3 hashes/sizes, and emits per-entry ownership, identity,
  size, and SHA-256 evidence. Final validation and publication revalidate the
  archive entries against that immutable manifest without requiring build
  outputs in publish jobs.
- Regression-first local fixtures reject a renamed foreign DLL, unexpected
  SharpProof DLL, altered first-party/native bytes, alternate-path duplicates,
  and missing managed/native entries while preserving the exact valid closure.
  The seven-case suite, deterministic evidence/final validation control,
  publisher plan control, Architecture 110/110, and container contract passed.

### SP-AUDIT-014 - Empty symbol packages pass release validation (fixed)

- [x] Fixed by `199ca46ab` (`fix: authenticate symbol package payloads`).
- One shared local validator now pairs every symbol package with its main
  package, requires the exact first-party PDB entry set, rejects duplicate or
  malformed archive entries, parses portable PDB metadata, matches each PDB ID
  to its assembly CodeView ID, and binds Source Link to the exact repository
  commit. Package-source, evidence, final-artifact, and publication-plan paths
  all invoke that authority.
- Regression-first fixtures reject missing, foreign, wrong-commit, duplicate,
  and malformed symbol payloads; a static authority test prevents any release
  consumer from dropping validation. The five mutation cases, authority test,
  Architecture 110/110, and container contract validation passed locally with
  no external NuGet or publication operation.

### SP-AUDIT-013 - Mutation qualification trusts a forged summary (fixed)

- [x] Fixed by `fe816fd3c` (`fix: validate reused mutation evidence`).
- Completed mutation evidence is now reused only after the authoritative
  catalog validator binds it to exact clean HEAD, all canonical mutation rows,
  assertion-backed outcomes, selected-test ledgers, and existing SHA-256-bound
  log/TRX receipts. Newly produced rows now record both receipt digests.
- Regression-first disposable-repository fixtures reject empty and duplicate
  arrays, wrong or missing identities, missing paths/digests, and dirty tracked
  state without launching the campaign; a complete valid receipt remains
  reusable. The behavioral fixture, full Architecture 110/110, and container
  contract validation passed in the canonical container. The 136-case campaign
  was intentionally not rerun for this pre-candidate implementation commit.

### SP-AUDIT-006 - Publication lock symlinks mutate an unowned target (fixed)

- [x] Fixed by `cf209d40b` (`fix: reject publication metadata symlinks`).
- Publication locks and ownership markers now open with no-follow and
  close-on-exec semantics, validate the opened descriptor as a regular file
  with `fstat`, and read marker bytes only through that validated descriptor.
  Validation failures dispose the descriptor without touching symlink targets.
- Regression-first coverage proves missing and existing lock-symlink targets
  remain uncreated/byte-identical, exact-content marker symlinks are rejected,
  directory locks fail, and ordinary regular locks still work. Focused Package
  and Worker tests passed 1/1 each, full Worker passed 436/436, and Architecture
  passed 110/110 in the canonical container. The full Package suite could not
  run because the offline cache lacked
  `microsoft.netframework.referenceassemblies` 1.0.3.

### SP-AUDIT-033 - Definitely null lifted operators report impossible exceptions (fixed)

- [x] Fixed by `2bfbbd23d` (`fix: skip absent lifted arithmetic hazards`).
- Arithmetic hazard classification now consults managed nullable-presence facts
  before applying the underlying lifted operator. A definitely absent operand
  skips division, remainder, and checked-overflow hazards; present and unknown
  operands retain the existing conservative exception behavior.
- Regression-first coverage includes binary, unary, increment, compound,
  divide/remainder, signed/unsigned, checked/unchecked, null-left/right/both,
  present/unknown, conversion, and DoesNotThrow projection controls. Full
  Effects passed 167/167, Analyzer passed 227/227, and Architecture passed
  110/110 in the canonical container.

### SP-AUDIT-023 - Empty nullable boxing reports an allocation (fixed)

- [x] Fixed by `f5ce546c5` (`fix: classify nullable boxing allocation`).
- Boxing classification now uses abstract nullable-presence facts. Definitely
  empty nullable values allocate nothing, definitely present nullable values
  allocate a managed box, and unknown nullable values retain an incomplete
  allocation possibility without a definite allocation witness. Ordinary
  value-type boxing remains a managed allocation.
- Regression-first coverage includes empty, present, parameter, lifted-empty,
  lifted-present, and ordinary boxing at both the Effects and ZeroAllocations
  analyzer layers. Full Effects passed 166/166, Analyzer passed 226/226, and
  Architecture passed 110/110 in the canonical container.

### SP-AUDIT-018 - Definitely safe casts report impossible exceptions (fixed)

- [x] Fixed by `d39bde755` (`fix: classify proven safe conversions`).
- Conversion effects now use abstract nullness and nullable-presence facts, plus
  exact runtime types preserved by immediate boxing/reference conversions, to
  remove only proven-impossible cast and unwrap exceptions. Unknown,
  incompatible, and null-to-nonnullable conversions remain conservative.
- Regression-first coverage includes null-to-nullable unboxing, null reference
  casts, present nullable unwraps, compatible boxed/reference values, unknown
  and incompatible values, nonnullable null unboxing, and DoesNotThrow analyzer
  projection. Full Effects passed 165/165, Analyzer passed 225/225, and
  Architecture passed 110/110 in the canonical container.

### SP-AUDIT-097 - Semantic strings evade the meta-analyzer through patterns (fixed)

- [x] Fixed by `e22c653d7` (`fix: inspect semantic string patterns`).
- SPMETA004 now owns constant patterns and classic switch-case labels using
  Roslyn semantic constant values and the existing closed semantic-string
  prefix filter. Switch expressions, nested patterns, and switch statements
  produce one diagnostic per controlling semantic literal.
- Regression-first coverage includes `is`, switch-statement, switch-expression,
  and nested exact-one cases plus ordinary string, null, default/discard, and
  relational controls. All four semantic cases failed before the fix; afterward
  focused tests passed 5/5, full Meta Analyzers passed 84/84, and Architecture
  passed 110/110 in the canonical container.

### SP-AUDIT-106 - Interpolated C# synthesis bypasses SPMETA009 (fixed)

- [x] Fixed by `963009746` (`fix: inspect interpolated expression text`).
- SPMETA009 now owns interpolated-string operations containing a real hole and
  inspects their literal text parts for semantic C# expression fragments.
  Interpolation values, alignment, and format expressions are not treated as
  generated syntax, and existing concatenation detection remains unchanged.
- Regression-first coverage includes plain, constant, formatted, aligned,
  escaped, and concatenated expression construction plus ordinary formatting,
  alignment, value-only, and format-fragment decoys. Five interpolated cases
  failed before the fix; afterward focused tests passed 7/7, full Meta Analyzers
  passed 79/79, and Architecture passed 110/110 in the canonical container.

### SP-AUDIT-098 - Mutable static properties evade analyzer-state policy (fixed)

- [x] Fixed by `a6b1b9745` (`fix: reject mutable static member storage`).
- SPMETA002 now covers compiler-backed mutable static storage introduced by
  settable auto-properties and field-like events in critical analyzer,
  frontend, and verifier namespaces, while preserving the existing field rule.
  Get-only properties, computed accessors, custom events, instance members, and
  noncritical namespaces remain admissible.
- Regression-first tests prove mutable static property/event rejection and the
  complete control matrix. The diagnostic wording was updated in the
  authoritative catalog and regenerated. Focused tests passed 2/2, full Meta
  Analyzers passed 72/72, and Architecture passed 110/110 in the canonical
  container.

### SP-AUDIT-071 - Unicode-escaped Contract identifiers evade advisory activation (fixed)

- [x] Fixed by `a4f5e0d0a` (`fix: activate escaped contract identifiers`).
- The cheap activation prefilter now recognizes potential C# Unicode identifier
  escapes and delegates those trees to the existing syntax-token scan, which
  compares compiler-decoded identifier `ValueText`. Comment and string decoys
  still do not activate analysis.
- Regression-first coverage includes literal, `\u`, and `\U` spellings,
  Unicode comment/string decoys, and no-contract fast-path controls. Before the
  fix both escaped cases created zero sessions and emitted no SP0027 while all
  eight controls passed. Afterward the focused suite passed 10/10, full Analyzer
  passed 224/224, and Architecture passed 110/110 in the canonical container.

### SP-AUDIT-078 - Behavioral declaration changes bypass changed-TCB coverage (fixed)

- [x] Fixed by `7e0ed5c0d` (`fix: cover semantic declaration changes`).
- Changed TCB source lines without sequence points now fail closed as uncovered
  semantic changes unless the line is independently clear as blank,
  comment-only, or brace-only. This failure is enforced independently of the
  configured percentage floor.
- Regression-first disposable Git and Cobertura fixtures cover constants,
  initializers, attributes, modifiers, signatures, expression bodies,
  generated declarations, comments, and braces. Seven semantic cases failed
  before the fix while both nonsemantic controls passed; afterward the focused
  matrix passed 9/9 and full Architecture passed 110/110 in the canonical
  container.

### SP-AUDIT-011 - Dirty source can be certified as the clean HEAD (fixed)

- [x] Fixed by `8fddf37b1` (`fix: reject dirty exact-commit inputs`).
- The container entrypoint now rejects tracked, staged, and relevant untracked
  source changes before creating the disposable overlay for every command that
  emits or validates exact-commit artifacts: acceptance, mutation, pack,
  pilots, release-tag, release-baseline, release-plan, release-qualification,
  and release-publish. Ordinary development commands remain dirty-tree capable.
- Regression-first disposable Git fixtures cover tracked, staged, and untracked
  production changes, every guarded command, clean source, dirty development
  builds, ignored artifacts, and admitted release inputs. The focused matrix
  passed 13/13 and the full Architecture suite passed 101/101 in the canonical
  container.

### SP-AUDIT-060 - Managed flow loses post-catch execution state (fixed)

- [x] Fixed by `cf26c6044` (`fix: retain effects after handled exceptions`).
- For methods with structured exception handling, absence of regular-edge
  abstract-flow state no longer proves an operation unreachable. Roslyn CFG
  reachability remains the outer filter and scalar facts remain available for
  exception discharge.
- Regression-first coverage proves a normally completing catch preserves a
  later static write and DivideByZeroException, retains a no-throw control, and
  projects the reachable purity violation through SP0002. Full Effects passed
  164/164, Analyzer passed 219/219, and Architecture passed 88/88 in the
  canonical container.

### SP-AUDIT-101 - Mutation-bearing expressions are re-evaluated from post-state (fixed)

- [x] Fixed by `c767348df` (`fix: preserve mutation-bearing flow effects`).
- Abstract flow now treats mutation-bearing branch predicates and stored values
  conservatively instead of re-evaluating them from their post-mutation state.
  Stable expressions retain their existing precise branch refinement.
- Regression-first coverage includes assignment, initializer, increment,
  ref-call, nested-condition, evaluation-order, true/false-edge, and stable
  controls, plus analyzer projection proving the reachable write reports
  SP0002. The focused regressions passed; full Effects passed 163/163, Analyzer
  passed 218/218, and Architecture passed 88/88 in the canonical container.

### SP-AUDIT-162 - Lowered-body hydration omits producer body invariants (fixed)

- [x] Fixed by `6abd80862` (`fix: validate lowered body invariants`).
- Hydration now rejects reachable cycles, more than 64 reachable blocks,
  missing bodies for successful postcondition callables, and value-returning
  paths without an exactly typed return value. Unreachable graph rows remain
  outside the executable-body budget.
- Regression-first coverage includes reachable cycles, the exact 64/65-block
  boundary, an unreachable-cycle control, required and legitimately bodyless
  callable shapes, missing returns, wrong-type returns, and an honest control.
  The focused suite passed 39/39; full Worker passed 435/435 and Architecture
  passed 88/88 in the canonical container.

### SP-AUDIT-158 - Acceptance skip switches still certify Passed (fixed)

- [x] Fixed by `3e04a6722` (`fix: mark partial acceptance incomplete`).
- Acceptance runs using `-SkipBuild` or `-SkipTests` now emit top-level
  `status=incomplete` and an explicit non-qualifying completion message. Only a
  full run retains `status=passed` and the qualifying success message.
- Regression-first disposable harnesses cover each skip combination and the
  full-run control, asserting evidence status, output, and exit behavior. The
  focused suite passed 4/4 and the full canonical-container Architecture suite
  passed 88/88.

### SP-AUDIT-223 - Changed-line coverage fallback checks only the tip commit (fixed)

- [x] Fixed by `c7de68f88` (`fix: require coverage comparison authority`).
- Enforced changed-TCB coverage now requires an explicit durable comparison
  authority, resolves it to one exact commit, and records that commit. The
  container command and CI workflow no longer guess `HEAD^`.
- Regression-first fixtures cover an earlier trusted change hidden by an
  unrelated tip, explicit named/commit authorities, missing and unusable refs,
  one-commit repositories, merge commits, working-tree mode, and report-only
  local use. Focused coverage tests passed 12/12 and the full canonical-
  container Architecture suite passed 84/84.

### SP-AUDIT-207 - Module initializer effects are omitted (fixed)

- [x] Fixed by `37e52be93` (`fix: include module initializer effects`).
- Effect sessions now discover trusted framework-attributed source module
  initializers, analyze their call graph once, and join their effects before
  ordinary entry points without recursively reapplying initialization to the
  initializer itself.
- Regression coverage includes module-initializer writes, synchronization and
  throws, body-witness suppression after a throwing initializer, type-scoped
  static-initializer controls, and initializer-callee reentry/deduplication.
  The full canonical-container Effects and Architecture suites passed 162/162
  and 78/78.

### SP-AUDIT-141 - Captured primary-constructor reads lose receiver ownership (fixed)

- [x] Fixed by `db08657dc` (`fix: retain primary constructor ownership`).
- Captured primary-constructor parameter reads now map to receiver state, and
  positional record properties retain their receiver-storage semantics.
  Constructor-time forwarding and ordinary parameters remain local.
- Regression-first coverage includes classes, record classes, structs,
  methods, accessors, forwarded captured values, constructor-only uses, and
  ordinary parameter controls with exact effect projections. The full
  canonical-container Effects and Architecture suites passed 158/158 and 78/78.

### SP-AUDIT-072 - String concatenation omits implicit ToString effects (fixed)

- [x] Fixed by `db8f97770` (`fix: include string formatting effects`).
- Built-in string concatenation now resolves statically known source
  `ToString()` calls, preserves their writes, capabilities, and escaping
  exceptions, and treats open dispatch or unresolved metadata formatting as
  incomplete rather than effect-free.
- Regression-first coverage includes sealed reference and value-type source
  formatting, writes/throws/capabilities, string/null/no-op cases, open virtual
  dispatch, primitives, nullable values, interpolation, and allocation. The
  full canonical-container Effects and Architecture suites passed 156/156 and
  78/78.

### SP-AUDIT-002 - Using disposal effects are omitted (fixed)

- [x] Fixed by `f97ac46e9` (`fix: include using disposal effects`).
- Effect analysis now resolves synchronous disposal from using statements and
  declarations, joins the concrete source/interface call summary, models
  conditional null disposal, and leaves unknown interface dispatch fail-closed.
- Regression-first coverage includes implicit statement/declaration disposal,
  explicit-call parity, static writes, synchronization capability, escaping
  exceptions, no-op disposal, null resources, and interface dispatch. The full
  canonical-container Effects and Architecture suites passed 153/153 and 78/78.

### SP-AUDIT-175 - Changed-TCB path identity is not exact on Linux (fixed)

- [x] Fixed by `55f014b7a` (`fix: preserve ordinal coverage paths`).
- Coverage source maps, TCB sets, changed files, and changed-line maps now use
  ordinal Linux path identity. Git paths are decoded from NUL-delimited output,
  and hunk parsing operates on each decoded path independently.
- Regressions reproduce case-distinct path collapse and a quoted Unicode/tab
  path false pass, while retaining ordinary and Windows-separator controls.
  The focused canonical-container coverage-script suite passed 6/6.

### SP-AUDIT-001 - Coalesce assignment omits observable writes (fixed)

- [x] Fixed by `d963c96f5` (`fix: retain coalesce assignment writes`).
- The effect scanner now handles `ICoalesceAssignmentOperation` directly,
  resolves Roslyn flow captures back to the original l-value, and retains the
  conditional target write and setter effects. Ambiguous capture mappings
  remain fail-closed.
- Regressions cover receiver, static, and argument-owned fields; property and
  indexer setters; and definitely-null, definitely-non-null, and unknown local
  targets with exact write-region assertions. The full canonical-container
  Effects suite passed 151/151.

### SP-AUDIT-025 - Cancellation policy certifies non-equivalent control flow (fixed)

- [x] Fixed by `5a5379e7a` (`fix: bind cancellation response shape`).
- Cancellation certification now requires the exact awaited local `Respond`
  invocation, an exact `WorkerResultAssembler.Create` response argument, and
  the `runStatus` parameter bound directly to `WorkerRunStatus.Canceled`.
- Regressions reject conditional returns, conditional response creation,
  conditional status expressions, alternate overloads, and wrapper/decoy
  calls while retaining the canonical multi-argument and `ConfigureAwait`
  shape. The full canonical-container Meta.Analyzers suite passed 70/70.

### SP-AUDIT-109 - Value-type instance calls omit type initialization (fixed)

- [x] Fixed by `e669122f5` (`fix: include value-type instance initialization`).
- Type-initialization triggering now includes non-static value-type members,
  while static-constructor self-entry and reference-type instance behavior stay
  unchanged.
- Regressions cover explicit struct static initialization with allocation,
  static write, and throw; default-receiver method/property calls; no-cctor
  structs; reference instances; and static controls. The full canonical-
  container Effects suite passed 149/149.

### SP-AUDIT-210 - Mutation ledgers compare identities case-insensitively (fixed)

- [x] Fixed by `12577c87d` (`fix: compare mutation ledgers ordinally`).
- Expected methods, global and per-method ledgers, uniqueness, ordering, and
  baseline comparison now share explicit ordinal case-sensitive semantics.
- Regression fixtures reject case-only parameter, display, class, and method
  drift; retain case-distinct rows and method keys; and accept unchanged exact-
  case evidence. The complete canonical-container fixture passed twice.

### SP-AUDIT-031 - Field-like event accessor claims are not discovered (fixed)

- [x] Fixed by `4352f3a78` (`fix: discover field-like event accessors`).
- Compiler discovery now maps every field-like event variable declarator to its
  synthesized add/remove accessors while retaining normal deduplication.
- Regression coverage includes two events in one declaration, explicit event
  accessors, property/method controls, an unselected event, and exact callable,
  claim, and effect-evidence counts. The full focused class passed 42/42 in the
  canonical container.

### SP-AUDIT-244 - Dirty-tree coverage loses merge-base semantics (fixed)

- [x] Fixed by `b2e7b48a6` (`fix: preserve merge-base coverage semantics`).
- Working-tree coverage now resolves the exact merge base before diffing the
  index/worktree; clean coverage retains triple-dot comparison semantics.
- Disposable diverged-history regressions cover identical base/feature TCB
  edits and base-only edits under unrelated working-tree dirtiness. The full
  canonical-container ArchitectureTest suite passed 74/74.

## P0 active bugs

Release blockers: false proofs, missing verifier obligations, destructive supported behavior, verifier bypasses, or release authority accepting invalid candidate bytes.

### SP-AUDIT-040 - Compiler variable semantics are not bound to symbol and type identity (P0)

- [ ] `CompilerLoweredArtifact.DecodeBody` checks that every program-parameter
  binding targets a distinct canonical parameter with the same IR type, but it
  does not bind that target to the source parameter's compiler ordinal.
  `ValidateVariables` similarly permits two same-typed `PreState` rows to swap
  their `CurrentStateVariable` sources because it checks type and injectivity,
  not the compiler-owned `pre:N` association.
- Supported impact: a self-consistent lowered artifact can make symbolic
  execution interpret the body parameter `left` as canonical parameter
  `right`, and can independently reinterpret `Old(left)` as the snapshot of
  `right`. Either substitution can turn an invalid postcondition into a proof
  candidate despite the compiler's real variable mapping.
- Reproduction: temporary canonical-container worker tests lowered methods
  with two used `int` parameters, then separately swapped the two
  `ParameterBindings.Target` indices and the two pre-state
  `CurrentStateVariable` indices. Both expected
  `CompilerManifestArtifactJson.DecodeCallables` to fail closed; both returned
  normally. The existing binding corruption control uses an `int` and a `bool`,
  so its swap is rejected only by type and does not discriminate ordinal
  ownership.
- Required closure: add compiler-owned parameter ordinal (or an equally
  authoritative symbol identity) to every binding and pre-state association,
  validate one-to-one matches against the canonical inventory, and reject rows
  the collector could not emit. Add same-type binding/pre-state swaps,
  arbitrary-local sources, unused parameters, generic names, valid mixed-type,
  canonical round-trip, counterexample replay, and proof-outcome mutations. If
  the wire shape changes, perform the required compiler-artifact schema bump
  rather than silently defaulting the field.
- Consolidated cases: SP-AUDIT-099.
- Unified closure: Persist authoritative parameter ordinal, source type, pre-state source, and scalar domain; independently derive and validate their exact relationship.

### SP-AUDIT-050 - Coverage and TCB universes are self-defined (P0)

- [ ] `Test-SharpProofCoverage.ps1` builds each project's coverable-line
  denominator exclusively from sequence points present in caller-supplied
  Cobertura files. It requires at least one path per configured project, but
  never proves report assembly identity, expected source-file membership, or
  the complete sequence-point universe produced by the current binaries.
- Certifier impact: a fabricated or truncated coverage report can omit nearly
  all production code and certify 100% project/aggregate coverage. When
  `ComparisonRef` has no changed TCB lines, the changed-TCB lane also reports
  100%, allowing a release coverage receipt with no meaningful test execution.
- Reproduction: the audit generated one Cobertura report containing exactly
  one `hits=1` line for one `.cs` file in each of the 22 baseline projects and
  ran the canonical coverage validator with `ComparisonRef=HEAD`. It reported
  `coveredLines=22`, `coverableLines=22`, every project at `100.0`, changed TCB
  at `100.0`, and overall `passed=true`. The forged report was removed.
- Required closure: derive the expected production source/sequence-point
  universe from exact current PDBs (or a separately authenticated canonical
  inventory), require exact assembly/module and source-document identity, and
  reject missing/duplicate/foreign reports and omitted sequence points before
  calculating percentages. Bind the retained summary to report hashes and the
  exact commit. Add one-line/truncated/missing-project/wrong-assembly/foreign-
  source/duplicate-report fixtures, canonical merged reports, and a certifier
  mutation restoring report-defined denominators.
- Consolidated cases: SP-AUDIT-120, SP-AUDIT-148, SP-AUDIT-149, SP-AUDIT-150.
- Unified closure: Independently derive the complete production project/source/parse-option/generator universe and make coverage, complexity, generated exclusions, and TCB consume it.

### SP-AUDIT-058 - Summary provenance is unauthenticated or incompletely projected (P0)

- [ ] `CompilerLoweredArtifact.ValidSummaryEvidence` validates source and
  specification-pack provenance as only a syntactically valid SHA-256 plus an
  origin-shaped identity. It never binds a specification-pack digest to the
  catalog content identified by `pack@version`, nor a source-summary digest to
  the summarized callee/body. Implementation-IL evidence need only match some
  module in the compilation reference closure, not the callee's owning module.
- Supported impact: a lowered relation can retain arbitrary or relabeled
  provenance while entering worker/Z3 reasoning unchanged. The proof core can
  therefore attribute a result to an audited pack/source/module that did not
  authorize those relation bytes.
- Reproduction: a focused worker regression produced a valid proven
  `dotnet.scalar@1` summary, changed only its evidence digest to 64 lowercase
  `b` characters, and required `DecodeCallables` to reject it. The decoder
  returned successfully. The temporary mutation was removed.
- Required closure: recompute or look up the exact specification-pack content
  digest from the catalog; bind source summaries to canonical callee/body
  identity; bind IL summaries to the actual resolved member and owning module,
  including dependency evidence. Add wrong-pack digest/identity, wrong source
  body/callee, unused-reference module substitution, transitive dependency,
  and exact-valid controls plus provenance mutations.
- Consolidated cases: SP-AUDIT-239.
- Unified closure: Authenticate every source, IL, specification, and transitive summary dependency at hydration and project the materially used closure into proof evidence.

### SP-AUDIT-080 - Compiler feature labels do not bind discovered proof scope (P0)

- [ ] The compiler artifact records a global `WorkerFeatureSet`, but envelope
  validation checks only that the enum value is defined. It never proves that
  the callable selections, claim kinds, clauses, effect evidence, and lowered
  bodies are the complete output of discovery for that declared feature set.
- Supported impact: a canonical artifact can claim the `All` profile while
  containing only effects-selected callables and claims. Contract clauses that
  should have been discovered and verified can be absent, yet the worker has no
  independent profile/scope authority with which to reject the omission.
- Reproduction: a focused compiler-artifact test captured an effects-only
  source containing both `[DoesNotThrow]` and a false `Contract.Ensures`, changed
  only `artifact.Features` from `Effects` to `All`, and expected canonical
  serialization to reject it. Serialization succeeded. The temporary test was
  removed.
- Required closure: cryptographically/canonically bind requested feature scope
  to discovery output and independently cross-check global features against
  per-callable selections, selection reasons, claim kinds, and lowered
  evidence. Add effects/contracts/all, mixed annotations, omitted and extra
  claims, duplicate selections, generated companions, serialization/cache
  round trips, and real strict-worker controls plus a trusted mutation that
  removes scope parity.

### SP-AUDIT-173 - Call instantiation is not bound to parameter order and free-variable roles (P0)

- [ ] The compiler instantiates a relational summary from the original receiver
  and ordered arguments, but hydration authenticates only identity, types,
  freshness, and relation shape.
- Soundness impact: swapping two same-typed Boolean argument term indices while
  retaining the original relation survived canonical round-trip and hydration;
  worker reasoning can therefore apply a relation to different actuals.
- Required closure: independently reconstruct or authenticate the exact
  receiver/ordered-argument instantiation before admitting a prepared summary
  call. Add permutations, duplicates, receiver, generic, same/different-type,
  and binding-removal mutation controls.
- Consolidated cases: SP-AUDIT-100, SP-AUDIT-208.
- Unified closure: Order actuals by Roslyn parameter ordinal and authenticate receiver, ordered arguments, result, existential roles, and relation during hydration.

### SP-AUDIT-243 - Compiler effect verdicts are only self-sealed (P0)

- [ ] Compiler effect evidence hashes its mutable outcome, reason, certainty,
  constraint, witness, and replay fields, but worker hydration never compares
  those fields with an independently derived compiler result. Hydration checks
  only the locally valid digest plus the manifest claim ID and contract kind.
- Soundness impact: an honestly produced `Refuted` `ZeroAllocations` result for
  an unconditional allocation can be changed to
  `Proven/None/CompleteMayEffectSummary`, have its witness and replay cleared,
  be sealed normally, and pass hydration. The worker copies that stored verdict,
  creates a proof-core label from the refreshed digest, and can report the false
  effect claim as proven.
- Required closure: bind every compiler effect outcome, reason, certainty,
  constraint, witness/replay, and source/tree origin to an independently
  derivable compiler authority during hydration; another self-hash is not an
  authority. Add honest `Refuted -> Proven` and `Unknown -> Proven` transitions,
  constraint changes, same-kind evidence swaps, valid unchanged evidence,
  worker/launcher result classification, and authority-check-removal controls.

## P1 active bugs

Material supported-surface defects: incorrect verdicts or diagnostics, missing required qualification, or workflows that produce the wrong result.

### SP-AUDIT-004 - Release qualification matrix is incomplete (P1)

- [ ] The tag-only `release-qualification` job in
  `.github/workflows/package-consumers.yml` runs acceptance, mutation,
  coverage, package consumers, publication planning, and qualification
  binding, but never runs `tooling pilots`. Qualification evidence also does
  not require or hash a pilot report.
- Certifier impact: the private-preview and public publication jobs depend on
  `release-qualification`, so a tag can reach a publication environment with
  no five-pilot result for those exact package bytes, contrary to the checked-in
  completion contract.
- Required closure: run the corrected pilot certifier against the downloaded
  exact-SHA package artifact in the tag job, retain/hash its report in
  qualification evidence, and make publication reject missing, stale, or
  package-mismatched pilot evidence.
- Consolidated cases: SP-AUDIT-082, SP-AUDIT-133, SP-AUDIT-145, SP-AUDIT-161, SP-AUDIT-171.
- Unified closure: Generate one authoritative qualification matrix and exact-commit receipt set covering pilots, Debug, release configuration, portable OS consumers, repeated cancellation, and minimum SDK.

### SP-AUDIT-024 - Normal-completion sequencing is modeled inconsistently (P1)

- [ ] `OperationEffectScanner.ScanCall` joins the resolved callee summary even
  when managed flow proves that an instance receiver is null. Runtime argument
  evaluation is followed by `NullReferenceException`; the target method never
  begins executing. More generally, call children are joined without
  left-to-right completion: a definitely throwing earlier argument does not
  suppress later argument or callee effects.
- Supported impact: a caller assigned `null` to a local and invoked a source
  instance method whose body increments static state. The caller received the
  callee's static write in a complete projection, although execution always
  throws before that write. This can reject valid no-write, pure, allocation,
  capability, or exact-throw claims.
- Reproduction: a temporary canonical-container Effects regression expected
  an empty write set plus `NullReferenceException`; the write set was nonempty.
  A second regression invoked `Target(Fail(), Mutate())`, where `Fail` always
  throws and both the later argument and target mutate static state; those
  impossible writes were likewise retained.
- Adjacent reproduction: a definitely-null `lock` retained the static write
  from its body even though `Monitor.Enter` throws first. An object creation
  whose constructor definitely throws likewise retained the static write from
  its object initializer, which runs only after successful construction. A
  combined focused regression reported both methods as writing static state.
- Constructor member initializers have the same defect on a separate path:
  `ScanConstructorMemberInitializers` sorts members by metadata name and joins
  every initializer independently. A first initializer that definitely threw
  `InvalidOperationException` did not suppress the later initializer's static
  write, even though construction can never reach it.
- Required closure: model receiver, argument, implicit runtime boundary,
  constructor, initializer, and protected-body evaluation in language order,
  requiring normal completion before advancing. Suppress callee effects for a
  definitely-null true instance receiver, retain both paths for unknown
  nullness, and do not apply that rule to reduced extension receivers where
  null is an ordinary argument. Add source/external methods, property
  accessors, first/middle/last argument failures, lock entry, object/collection
  initialization, array initialization, argument side effects, unknown
  receivers, conditional access, reduced extensions, and a mutation probe.
- Consolidated cases: SP-AUDIT-030.
- Unified closure: Model source-order normal completion once; join later receiver, argument, conditional-access, lock, constructor, and initializer effects only when prior evaluation may complete.

### SP-AUDIT-056 - Final ContractFor reconciliation is provenance-incomplete (P1)

- [ ] Final-compilation ContractFor validation filters syntax trees through
  `AnalyzerGeneratedCodePolicy`, which recognizes provider classification,
  conventional generated suffixes, or an auto-generated header. A peer source
  generator is permitted to add an ordinary `.cs` hint without that header;
  the incremental ContractFor generator cannot see the peer's output, and the
  final analyzer silently excludes it.
- Supported impact: malformed or overlapping generated ContractFor companions
  can participate in the final compilation and runtime binding without any
  SPCF diagnostic solely because their generator chose an ordinary filename.
- Reproduction: the existing generated-companion regression was changed only
  from `AddSource("GeneratedContracts.g.cs", ...)` to the legal hint
  `GeneratedContracts.cs`. The malformed empty companion previously produced
  SPCF0004; the focused canonical-container run returned no diagnostics. The
  temporary filename change was removed.
- Required closure: identify post-generator trees from authoritative final-
  compilation provenance rather than filename/header heuristics, while keeping
  handwritten generated-code suppression separate. Add ordinary, `.g.cs`,
  header, provider-classified, peer-generator ordering, profile-off,
  overlap/malformed, and handwritten controls plus a mutation restoring the
  heuristic-only filter.
- Consolidated cases: SP-AUDIT-064, SP-AUDIT-068, SP-AUDIT-127.
- Unified closure: After all generators, reconcile every logical target/companion once in every non-off profile, independent of filename, partial ownership, and generator order.

### SP-AUDIT-086 - Unboxed struct copies retain boxed-argument ownership (P1)

- [ ] Region classification strips every built-in conversion without
  distinguishing representation-preserving reference conversions from
  unboxing. A local initialized by `(Value)boxed` therefore inherits the
  reference parameter's region even though unboxing creates a by-value struct
  copy.
- Supported impact: mutating a field of the local copy is reported as a write
  to caller-owned parameter state, so a genuinely observable-pure method is
  falsely rejected. This contradicts the existing by-value struct-copy
  semantics elsewhere in the scanner.
- Reproduction: a focused Effects test unboxed an `object` argument into a
  struct local and assigned `copy.Number = 1`. The write set was nonempty and
  `IsObservablePure` was false instead of an empty write set and true. The
  temporary test was removed.
- Required closure: classify conversion ownership by exact conversion kind;
  unboxing and other value-producing copies must introduce local/value
  ownership while reference-preserving conversions retain aliases. Add boxed
  argument, boxed field, interface boxing, nullable unboxing, failed-cast
  exceptions, reference casts, `ref` unbox controls, and by-value/ref struct
  parity plus a mutation restoring conversion-blind alias propagation.

### SP-AUDIT-088 - Compiler effect and callable reason mapping is incomplete (P1)

- [ ] Compiler capture can emit an unknown effect claim with reason
  `ResourceLimit` and certainty `IncompleteMayEffectSummary`, but the compiler
  artifact effect codec's closed reason list omits `ResourceLimit` and the
  protocol certainty predicate accepts that certainty only for
  `EffectSummaryIncomplete`.
- Supported impact: an ordinary supported method whose acyclic effect analysis
  exceeds its operation budget is serialized by the collector, then rejected
  by worker hydration as `compiler_manifest.lowered_ir` instead of producing
  the documented typed Unknown/ResourceLimit result.
- Reproduction: a focused Worker test changed otherwise valid compiler effect
  evidence to the exact producer tuple Unknown/ResourceLimit/
  IncompleteMayEffectSummary, resealed it, and expected hydration. The decoder
  threw `InvalidDataException: Compiler effect-claim evidence is invalid.` The
  temporary test was removed; existing analyzer budget tests establish the
  producer side of the tuple.
- Required closure: give producer projection, artifact codec, protocol model,
  hydration, and result assembly one generated reason/certainty catalog. Add
  exact and boundary resource-budget capture through worker response, adjacent
  unsupported-body controls, malformed tuple rejection, and mutations removing
  each permitted producer tuple.
- Consolidated cases: SP-AUDIT-151.
- Unified closure: Generate one producer/codec/hydrator/callable mapping for every supported compiler/effect Unknown reason and certainty tuple.

### SP-AUDIT-200 - Effect replay is not bound to full tree identity (P1)

- [ ] Compiler effect replay binds events only to syntax-tree ordinal, text hash,
  and span, although compilation snapshots also bind path and parse settings.
- Certifier impact: an event can be resealed against a byte-identical tree with
  different preprocessor symbols or generated/path identity, including a tree
  where the operation is inactive, and still hydrate/replay.
- Required closure: bind replay origins to a canonical digest of the complete
  selected syntax-tree snapshot. Add identical-text/different-path, symbols,
  language/features, generated identity, ordinary exact, and binding-removal
  controls.

### SP-AUDIT-241 - Constructed generic function pointers lose ref modifiers (P1)

- [ ] Generic contract canonicalization recreates function-pointer types with ref
  kinds and calling conventions but omits parameter ref custom modifiers such as
  the compiler-owned `RequiresLocationAttribute` on `ref readonly` parameters.
- Supported impact: an exact constructed generic
  `delegate*<ref readonly T, void>` contract specializes to `T=int` but binding
  fails `UnsupportedExpression`; the equivalent plain function-pointer generic
  succeeds.
- Required closure: preserve return and parameter custom modifiers during
  reconstruction, or avoid rebuilding when the symbol factory cannot preserve
  them. Add constructed/unconstructed ref-readonly, plain ref/in/out, return
  modifiers, calling conventions, ordinary generic, and modifier-loss controls.

### SP-AUDIT-242 - Pilot reports fabricate zero false-positive review counts (P1)

- [ ] `Test-SharpProofPilots.ps1` unconditionally writes
  `falsePositiveReports = 0`, but the command accepts no review ledger or other
  evidence from which that count could be derived.
- Certifier impact: an unreviewed pilot run is represented identically to a
  completed review that found no false positives, contradicting the required
  five reviewed pilot reports and allowing incomplete qualification to appear
  complete.
- Required closure: define strict review evidence bound to pilot ID, candidate
  commit, package hashes, and diagnostic or claim identity; derive counts from
  reviewed dispositions; represent missing or incomplete review as
  `Unreviewed`, never zero; bind the review receipt into qualification. Add one
  false-positive, honest zero, missing, incomplete, contradictory, stale-byte,
  and review-binding-removal controls.

## P2 active bugs

Fail-closed reliability and evidence-integrity defects: lifecycle, provenance, canonicality, resource, or reporting failures without a demonstrated false proof or invalid release.

### SP-AUDIT-007 - SBOM release identity is not fully bound (P2)

- [ ] `New-SharpProofReleaseEvidence.ps1 -SbomPath` and
  `Test-SharpProofReleaseArtifacts.ps1` validate the SPDX graph and package
  hashes but never validate the commit-bearing `documentNamespace` or its
  commit-derived creation metadata.
- Certifier impact: release evidence can contain exact packages and an exact
  release manifest while the SBOM asserts a different source revision. The
  resulting SBOM hash is then blessed by `SharpProof.release.json` and
  `SHA256SUMS`, so the final validator cannot distinguish the stale provenance.
- Reproduction: in a clean detached checkout matching the existing package
  commit, the audit changed the SBOM namespace suffix to forty zeroes, reran
  release-evidence generation with that SBOM, and then ran the final artifact
  validator. Both commands exited zero and reported immutable artifacts valid.
- Required closure: require the exact deterministic namespace
  `.../sbom/<version>/<package repository commit>`, validate the commit-derived
  creation record, and preferably compare a supplied SBOM with a regenerated
  canonical document. Add stale-commit, stale-timestamp, malformed-namespace,
  and canonical round-trip certifier fixtures.
- Consolidated cases: SP-AUDIT-185.
- Unified closure: Derive and require exact SBOM name, namespace, version, commit, and creation identity from one release authority.

### SP-AUDIT-010 - Filtered worker tests depend on Z3 test order (P2)

- [ ] The canonical `tooling test -Target SharpProof.Worker.Test` path does
  not install the contract-pinned native Z3 resolver for the worker test
  assembly. `SharpProof.Smt.Test` has an assembly-level
  `ContainerNativeLibrarySetup`, but the worker tests instantiate
  `IrSmtBackend` directly and rely on some unrelated earlier test to install a
  process-wide resolver.
- Developer and certifier impact: selecting the proof-kernel worker tests in
  the documented disposable container command failed eight cases with
  `DllNotFoundException: libz3`, while 73 non-Z3 cases in the same filter
  passed. A full run can hide the defect through test order, and a focused
  mutation/regression run can fail before exercising its mutant.
- Reproduction: `docker compose run --rm tooling test -Target
  SharpProof.Worker.Test -TestFilter
  "FullyQualifiedName~CompilerManifestArtifactTests|FullyQualifiedName~WorkerTcbEdgeCaseTests|FullyQualifiedName~AcyclicBlockPredicateExecutorTests|FullyQualifiedName~VerificationCacheTests"`
  produced 73 passes and eight native-load failures. The equivalent SMT test
  assembly passed 20/20 because it owns explicit resolver setup.
- Required closure: install and verify the exact container Z3 resolver in a
  worker-test assembly setup that runs for every filter and ordering, then add
  an isolated single-test invocation to the container command fixtures. Keep
  ambient `LD_LIBRARY_PATH` and system Z3 outside the trust boundary.

### SP-AUDIT-022 - ContractFor generator resolves profiles differently (P2)

- [ ] `AnalyzerConfiguration` and the compiler collector choose the first
  configured profile alias in their authoritative key order and disable
  analysis on an invalid value. `ContractForValidatorGenerator.IsProfileOff`
  instead scans all three aliases and returns off if any one equals `off`; it
  treats every invalid value as enabled.
- Supported impact: a custom host that supplies raw `advisory` plus build-
  property `off` runs analyzer/collector semantics but silently suppresses all
  ContractFor validation. Supplying an invalid profile disables the analyzer
  and collector with a configuration diagnostic while the generator continues
  and emits unrelated SPCF errors. One compilation therefore has no coherent
  SharpProof profile.
- Reproduction: a temporary generator regression compared default advisory
  diagnostics with conflicting `sharpproof_profile=advisory` and
  `build_property.SharpProofProfile=off`; the default emitted one SPCF error and
  the conflicting run emitted none. A second regression supplied
  `sharpproof_profile=invalid`; it expected the generator to remain disabled
  with the analyzer but received SPCF0004.
- Required closure: expose one shared profile-resolution result to analyzer,
  generator, and collector, including alias precedence, trimming, accepted
  values, provider failures, and invalid/conflicting aliases. Prefer rejecting
  conflicting nonblank aliases rather than silently selecting one. Add all
  alias permutations, invalid values, redundant equal values, provider failure,
  MSBuild-only, and mutation-discriminating parity cases.

### SP-AUDIT-029 - Publication-lock setup is not failure-atomic (P2)

- [ ] `LinuxPathIdentity.AcquirePublicationSet` constructs its entire
  `PublicationLock[]` through a LINQ pipeline before entering the cleanup
  `try/finally`. If construction of a later lock throws, every earlier
  `SafeFileHandle` is abandoned without deterministic disposal.
- Supported impact: repeated ordinary invalid publication configurations can
  exhaust file descriptors in a long-lived Core MSBuild process, eventually
  breaking unrelated builds and containment checks. Garbage collection may
  eventually run the safe-handle finalizers, but the failure path has no
  bounded cleanup and can accumulate descriptors faster than collection.
- Reproduction: a temporary canonical-container worker regression used two
  ordered output paths and made only the second derived lock path a directory,
  causing its `open(O_RDWR)` to fail. Thirty-two rejected acquisition attempts
  increased `/proc/self/fd` by exactly 32.
- Required closure: construct locks incrementally inside an ownership-aware
  `try/finally`, disposing every successfully created lock when any later
  constructor or acquisition fails. Add first/middle/last constructor failure,
  acquisition timeout, cancellation, successful lease, repeated-failure file
  descriptor, and cleanup mutation cases.
- Consolidated cases: SP-AUDIT-177.
- Unified closure: Observe cancellation before I/O, acquire incrementally under cleanup ownership, and release partial acquisitions without leaving directories or lock files.

### SP-AUDIT-034 - Compilation snapshots are not exact canonical capture images (P2)

- [ ] `CompilationFingerprint.ValidSnapshot` accepts compiler version and
  assembly-identity fields with only `HasText`; `ValidReference` does the same
  for reference identity; and the snapshot/module validators accept MVIDs with
  only `Guid.TryParseExact(value, "D")`. The collector instead emits
  `AssemblyIdentity.ToString()`, PE assembly identity display names,
  `Version.ToString()`, and lowercase `Guid.ToString("D")` values. Syntax-tree
  `LanguageVersion` is likewise accepted as arbitrary nonblank text although
  capture emits only `CSharpParseOptions.LanguageVersion.ToString()`.
- Supported impact: uppercase compiler, C# compiler, and reference-module MVID
  strings and arbitrary nonempty version text are accepted after recomputing
  `CompilationSha256`. The same compiler identity can have multiple valid
  serialized forms, while compiler and reference identity text the collector
  can never produce passes canonical deserialization, weakening deterministic
  cache/provenance identity.
- Reproduction: one temporary canonical-container worker test replaced all
  three MVID categories with uppercase D-format GUIDs; a later test set
  `CompilerVersion` to `not-a-version`. Both recomputed the compilation
  fingerprint, serialized directly, expected `JsonException`, and received no
  exception. A third probe replaced a tree language version with
  `not-a-csharp-language-version`; canonical deserialization accepted it too.
  Two adjacent probes replaced the compilation assembly identity and an
  assembly-reference identity with `not-an-assembly-identity`; both also passed
  fingerprint recomputation and canonical deserialization. A final probe
  changed `AssemblyName` while retaining the original, individually valid
  `AssemblyIdentity`; that compiler-impossible cross-field mismatch was also
  accepted.
- Required closure: validate the exact producer-canonical `System.Version`
  round trip for both version fields and lowercase D-format round trip for every
  MVID field (or generate shared canonical identity-string rules from the
  schema), and require each assembly identity to parse and round-trip through
  Roslyn's canonical display-name representation (with module identity kept as
  the exact module name), and require the compilation assembly name to equal
  the parsed assembly-identity name. Add malformed/overlong/noncanonical
  identities and versions, valid-but-mismatched name/identity pairs,
  uppercase/mixed-case, braces, compact GUID format, nil/non-nil policy,
  lowercase valid, every producer-emittable C# language-version spelling,
  arbitrary parse versions, recomputed-hash, and canonical serialization
  mutation cases.
- Consolidated cases: SP-AUDIT-081, SP-AUDIT-090, SP-AUDIT-139, SP-AUDIT-201, SP-AUDIT-212.
- Unified closure: Generate one exact capture/fingerprint predicate for canonical strings and paths, module roles/properties, additional-file identity, and empty-tree derivations.

### SP-AUDIT-041 - Proof evidence and used assumptions are not jointly bound (P2)

- [ ] Protocol validation checks proof cores, counterexample models, effect
  witnesses, and assumption-use flags only for local shape or self-consistency.
  It never ties proof labels or model variables to the compiler artifact, never
  requires an effect witness to violate the sealed claim's specific contract
  kind, and deliberately omits `Used` from its manifest-assumption comparison.
- Supported impact: a response can remain request-, input-, manifest-, budget-,
  and result-set-bound while falsely explaining why a claim was proved or
  refuted. This violates the documented canonical-proof-core contract, permits
  invented counterexample assignments, can report a state write as the reason
  a `DoesNotThrow` contract failed, and can inflate the reported use of a
  trusted boundary without corresponding solver evidence.
- Reproduction: temporary canonical-container protocol tests passed four
  fabricated payloads through `ValidateForRequest`: a proven result with sole
  core label `fabricated-proof-core`; a refuted postcondition with invented
  model variable `fabricated = 999`; and a refuted `DoesNotThrow` claim whose
  witness contained only `WritesStaticState`; plus a proven result that changed
  a declared `TrustedBoundary` assumption from unused to used and recomputed
  the summary. An existing vacuity test likewise accepts `requires:0` although
  its fixture manifest declares no precondition, so current coverage codifies
  shape-only acceptance.
- Required closure: validate claim evidence with the decoded compiler artifact,
  or replace free-form rows with typed identities. Require every proof label to
  name admitted compiler/spec/domain/summary evidence, every model variable and
  value kind to match the claim's canonical replay inventory, and every effect
  witness to contradict its exact effect contract; bind every `Used` flag to
  admitted proof/vacuity evidence. Add fabricated/wrong-ordinal core, model
  name/type/value, mismatched effect kind/capability/throw hierarchy, assumption
  use, vacuity, empty hygienic, valid controls, canonical ordering, and
  mutations restoring each shape-only path.
- Consolidated cases: SP-AUDIT-217.
- Unified closure: Share one artifact-aware mapping from proof-core labels to exact manifest assumptions, Used flags, and summary counts in producer and validator.

### SP-AUDIT-048 - Invocation lifecycle leaks temporary runtime state (P2)

- [ ] Every build allocates a GUID-scoped
  `obj/.../SharpProof/runs/<invocation>` directory for its compiler manifest.
  `_SharpProofVerifyCore` removes that directory only after `RunVerifier`.
  Configuration, manifest, launcher, worker, and other MSBuild `<Error>` paths
  before the task bypass cleanup, leaving the compiler-emitted manifest and
  ownership metadata indefinitely.
- Supported impact: repeated ordinary configuration failures accumulate one
  unique directory per build under `obj`. This is unbounded local disk growth
  on the supported container/MSBuild path and persists until the consumer
  manually cleans intermediates.
- Reproduction: a disposable isolated-feed strict consumer compiled valid
  source twice with `SharpProofVerifyPolicy=invalid`. Both builds failed at the
  documented policy validation, and the probe found two distinct directories
  under `obj/Release/net8.0/SharpProof/runs`. Syntax-error and semantic-error
  controls left zero directories and were rejected as non-reproductions. The
  disposable consumer was removed.
- Required closure: place invocation cleanup in an MSBuild finally-equivalent
  target that runs for every post-compilation success/failure/cancellation path,
  while preserving files long enough for result classification and diagnostics.
  Add every pre-launch `<Error>` condition, task launch failure, cancellation,
  successful verification, repeated-build, and cleanup-failure controls plus a
  mutation that restores success-only `RemoveDir` placement.
- Consolidated cases: SP-AUDIT-206.
- Unified closure: Own one invocation root and remove compiler and staged-worker state on prelaunch failure, success, cancellation, timeout, and bounded abandoned-root recovery.

### SP-AUDIT-049 - Release JSON decoding accepts noncanonical structures (P2)

- [ ] Release evidence and SPDX documents are parsed with PowerShell
  `ConvertFrom-Json`, then validated only through the resulting object model.
  The certifier does not reject duplicate JSON member names, require the
  generator's canonical serialization, or compare a supplied document with a
  canonical reserialization. PowerShell silently keeps the later duplicate.
- Certifier impact: one supposedly immutable validated evidence file can assert
  two conflicting package versions, repository identities, artifact graphs, or
  SBOM fields. Consumers that retain the first occurrence can make a different
  release decision from the SharpProof validator, while package bytes and all
  existing checksums remain unchanged.
- Reproduction: the audit copied the exact-HEAD release bundle and inserted a
  forged `"packageVersion":"999.0.0-forged"` immediately before the original
  `"packageVersion":"1.0.0-preview.1"` in `SharpProof.release.json`.
  `Test-SharpProofReleaseArtifacts.ps1` exited zero and certified the bundle for
  current HEAD. The disposable copy was removed.
- Required closure: parse release and SBOM JSON with a duplicate-property-
  rejecting reader, reject unmapped fields and noncanonical ordering/format,
  and validate exact byte equality with the deterministic canonical form where
  these files are authoritative. Add duplicate top-level/nested/array-row
  properties with forged-first and forged-last order, unknown properties,
  reordered/whitespace variants, canonical generated documents, and a
  certifier mutation restoring permissive `ConvertFrom-Json` parsing.
- Consolidated cases: SP-AUDIT-193, SP-AUDIT-214, SP-AUDIT-215, SP-AUDIT-229.
- Unified closure: Use one duplicate-member-rejecting, token-aware decoder enforcing raw JSON types, flat arrays, exact vocabulary/casing, and canonical serialization.

### SP-AUDIT-063 - Publication commit is not atomic or crash-durable (P2)

- [ ] `PublishOutputs` replaces the compiler manifest and request before it
  writes optional SARIF and the result commit marker. Its failure cleanup
  deletes only result and SARIF, so an ordinary late write failure preserves
  the new request/manifest beside missing or older remaining outputs.
- Supported impact: cooperative publication no longer behaves as one coherent
  four-file transaction. Verification still fails closed because the result
  commit marker is absent, but later tooling and retries observe partially
  advanced provenance instead of either the previous complete set or a fully
  invalidated set.
- Reproduction: a focused container package test created an owned four-file
  publication set, filled every member with stable bytes, then used a
  227-character SARIF basename. Lock and ownership metadata fit the filesystem
  component limit, while `AtomicFile`'s random temporary suffix made the SARIF
  write fail after manifest/request replacement. The launcher returned 3, the
  request had changed from 19 to 526 bytes, and result/SARIF were absent. The
  temporary regression was removed.
- Required closure: stage every output before committing any destination, then
  publish a generation atomically under the complete lock or restore/invalidate
  all members on failure. Add failures at each stage, pre-existing coherent
  generation, no-SARIF, retry, and cleanup controls plus a mutation that
  restores manifest/request-first publication.
- Consolidated cases: SP-AUDIT-176.
- Unified closure: Publish one generation transactionally, sync data and directories, publish the commit member last, and roll back or invalidate the whole set on failure.

### SP-AUDIT-066 - Final worker deadline applies termination grace twice (P2)

- [ ] The launcher computes its worker wait limit as project wall time plus
  almost the full termination grace, then `LinuxWorkerProcess` starts a second
  termination phase with another full grace budget. A TERM-ignoring child
  therefore receives an extra half-grace before forced kill, beyond the
  documented project-plus-one-grace final boundary.
- Supported impact: cancellation/timeout cleanup can retain the worker and hold
  the build longer than the public limit. It still terminates fail-closed, but
  the configured outer deadline is not the deadline users or package tests are
  promised.
- Reproduction: a focused Linux container test launched a direct shell child
  that ignored SIGTERM, passed `ComputeHardLimit(100, 1000)` and a 1,000 ms
  termination grace to `WaitForExit`, and required completion within the
  documented 1,100 ms plus a 200 ms scheduling allowance. Completion was
  correctly classified `TimedOut` but took 1,523 ms. The temporary regression
  was removed.
- Required closure: derive one monotonic project-plus-grace deadline and pass
  only its remaining duration through graceful and forced termination. Add
  normal exit, TERM-cooperative, TERM-ignoring, natural-exit race, cancellation,
  minimum/maximum grace, and loaded-container controls plus a mutation that
  restores independent wait and termination budgets.

### SP-AUDIT-074 - Response validation accepts an impossible cache hit (P2)

- [ ] Request-bound response validation checks the request hash, input,
  manifest, and budgets, but it is not given the expected cache or verification
  policy. It therefore validates only the response's internal
  `(CacheStatus, CacheHit)` pair, not whether that state is possible for the
  bound request.
- Supported impact: malformed worker or replay evidence can claim a cache hit
  for a cache-disabled (or otherwise non-cacheable) verification request and be
  accepted as valid. This makes cache provenance in the certified response
  unreliable even though the request hash itself is exact.
- Reproduction: a focused protocol test created a valid complete response for
  a request with `Cache.Enabled = false`, changed the summary to
  `CacheStatus=Hit` and `CacheHit=true`, and retained the exact computed request
  hash. `ValidateForRequest` returned valid. The temporary test was removed.
- Required closure: validate against the complete canonical request or pass all
  policy fields needed for cross-document invariants. Reject hits when caching
  is disabled, when verification policy forbids cache use, and for outcomes the
  cache cannot store. Add disabled/enabled, hit/miss/written/rejected,
  require-proven, failed/unknown/refuted, stale-hash, and valid-hit controls plus
  a protocol mutation that removes request/cache binding.

### SP-AUDIT-075 - Effective verifier I/O topology is incompletely validated (P2)

- [ ] Publication lock objects create each lock's parent directory before the
  complete destination set is proven structurally valid. If one destination is
  nested beneath another destination filename, preparing the child's lock
  creates the parent destination itself as a directory; ownership binding then
  rejects the set but does not undo that filesystem mutation.
- Supported impact: an ordinary invalid configuration fails as intended yet
  leaves a configured output path behind as an unowned directory. Subsequent
  corrected builds can remain blocked, and fail-closed validation has mutated
  the publication namespace it was supposed to preserve.
- Reproduction: a focused Linux host test acquired a set containing
  `result.json` and `result.json/child.json`. Acquisition threw `IOException`,
  but `result.json` existed afterward as a directory. The temporary test was
  removed.
- Required closure: validate ancestor/descendant conflicts for the complete
  canonical set before constructing locks or directories, and make all
  pre-acquisition metadata setup failure-atomic. Add both nesting orders,
  three-level paths, pre-existing parent directories, siblings, lock/marker
  residue, retry, and valid disjoint controls plus a containment mutation that
  removes the structural preflight.
- Consolidated cases: SP-AUDIT-085, SP-AUDIT-103, SP-AUDIT-205.
- Unified closure: Derive active paths once and reject duplicate or symmetric ancestor conflicts across the complete publication, input, cache, and runtime topology before mutation.

### SP-AUDIT-089 - Compiler callable state differs across producer and hydrator (P2)

- [ ] Failed callable hydration accepts any defined worker claim reason except
  `Unspecified`, although compiler lowering can emit only its small closed set
  of unsupported compiler reasons. Runtime-only reasons such as
  `MethodTimeout` therefore pass as canonical compiler evidence.
- Certifier impact: a compiler artifact for an unsupported loop was changed
  from `UnsupportedBody` to `MethodTimeout`; hydration accepted it and can
  manufacture an internally consistent TimedOut result even though no solver
  deadline elapsed.
- Reproduction: a temporary Worker test created the real unsupported-loop
  artifact, performed that one-field mutation, and expected
  `InvalidDataException`. No exception was thrown. The temporary test was
  removed.
- Required closure: validate failed callables against a generated compiler-
  producer reason catalog, separate compiler abstention from runtime outcomes,
  and table-test every allowed compiler reason and every rejected runtime
  reason. Add a mutation restoring broad enum-defined acceptance.
- Consolidated cases: SP-AUDIT-211.
- Unified closure: Generate one tagged compiler-callable state machine for diagnostics, success/failure, compiler reason sets, deep body validation, and worker projection.

### SP-AUDIT-091 - Peer-generated rejected ContractFor attributes evade validation (P2)

- [ ] Final-compilation candidate discovery first resolves the one exact
  trusted `ContractForAttribute` symbol and returns no candidates when that
  resolution is rejected or ambiguous. The incremental validator cannot see
  a peer generator's output, so a peer-generated source-defined lookalike is
  owned by neither validation path.
- Supported impact: a generator emitted a conventional `.g.cs` companion with
  a source `SharpProof.Attributes.ContractForAttribute` lookalike. Final
  contracts-profile analysis emitted no SPCF0001, although the same rejected
  handwritten attribute is required to fail closed.
- Reproduction: a temporary generated-companion analyzer test supplied the
  trusted Attributes reference, emitted the lookalike and attributed companion,
  and expected SPCF0001. The diagnostic set was empty. The test was removed.
- Required closure: discover syntactic/metadata-name candidates in the final
  compilation independently of successful trusted-symbol resolution, then
  classify each against the exact symbol and diagnose rejection. Add peer-
  generated shadow, ambiguous, missing, exact, mixed handwritten/generated,
  every profile, and non-generated filename controls plus a mutation that
  restores the early empty return.

### SP-AUDIT-095 - SBOM package URLs are not validated (P2)

- [ ] Evidence generation emits canonical NuGet purls in SPDX `externalRefs`,
  but evidence, artifact, and publication validators never inspect those rows.
- Certifier impact: a canonical component name, version, checksum, SPDX ID, and
  relationship graph can carry a purl naming an unrelated package. Package-
  manager consumers of the accepted SBOM then receive false component identity.
- Reproduction: a harmless in-memory mutation replaced SharpProof's purl with
  `pkg:nuget/Fabricated.Package@99.0.0`; every field currently consumed by the
  validators remained unchanged, and bounded source inspection found no purl
  validation path.
- Required closure: derive one exact purl from each already authenticated
  package name/version, require exactly one matching purl per first- and third-
  party component in all three release stages, and add substituted, duplicate,
  omitted, encoded, and canonical controls.

### SP-AUDIT-102 - Specification-pack configuration is not canonically sealed (P2)

- [ ] Selected pack IDs are passed only into callable lowering. Schema 12 stores
  a pack/catalog identity only in summaries that actually use a pack; the
  artifact envelope contains no configured selection set.
- Supported impact: the same call-free contract project produced byte-identical
  canonical artifacts with specification packs unset and with `dotnet.scalar`
  selected, contrary to the documented claim that selection and exact catalog
  content are sealed.
- Reproduction: a temporary worker artifact test produced both variants and
  required unequal serialization; the two JSON documents were exactly equal.
  The test was removed.
- Required closure: seal canonical selected pack IDs plus the authoritative
  catalog/version digest at the envelope, validate them before lowering, and
  bind them into the compilation/input fingerprint even when unused. Add unset,
  one/multiple, reordered/duplicate, unknown, unused/used, and digest mutation
  controls; a wire-version decision may be required.
- Consolidated cases: SP-AUDIT-195.
- Unified closure: Seal sorted selected pack IDs and exact catalog/version digest even when unused, with one identity predicate from catalog load through hydration.

### SP-AUDIT-117 - Selected auto-accessors disappear without an outcome (P2)

- [ ] Symbol validation returns early for every concrete non-extern method and
  relies on operation-block callbacks. A semicolon auto-accessor has no source
  body that enters the normal effect pipeline, and no end-of-compilation check
  reconciles selected methods with recorded outcomes.
- Supported impact: an `[EnforcePure]` auto-property setter, whose generated
  implementation writes receiver state, emitted neither a purity diagnostic
  nor typed `MissingOperationRoot` abstention.
- Reproduction: a temporary canonical-container analyzer test required any
  selected-accessor outcome and received an empty diagnostic set. The test was
  removed.
- Required closure: inventory all selected accessors and require an exact
  effect result or typed abstention. Add auto get/set/init, explicit, abstract,
  extern, suppression, outcome-recording, and mutation controls.

### SP-AUDIT-122 - Clean cannot recover publication-set metadata (P2)

- [ ] Publication-set markers persist beside every member, but the verifier
  registers no Clean hook or `FileWrites` entries for them. The failure message
  instructs users to clean before changing paths even though `dotnet clean`
  does not remove the metadata.
- Supported impact: a successful set A build followed by `dotnet clean` and a
  set B build with an alternate result path still failed with “publication
  paths partially overlap.” Recovery requires manual discovery and deletion of
  hidden implementation files.
- Reproduction: a temporary packaged-consumer test performed that exact
  build/clean/rebuild sequence in the canonical container; the second build
  exited one. The test was removed.
- Required closure: register owned marker/lock metadata for supported Clean or
  provide an explicit safe reset target that removes a complete matching set
  under its locks. Add clean/no-clean, changed member, partial failure,
  unrelated-neighbor, incremental, and mutation controls.

### SP-AUDIT-129 - Publication plans omit immutable artifact identities (P2)

- [ ] Plan-only publication validates package hashes transiently, then emits
  only package IDs, versions, paths, and filenames. It records no package hash
  or size and no release-manifest, SBOM, or checksum-file identity; nothing
  subsequently validates the plan.
- Certifier impact: different validated package bundles with the same IDs,
  versions, filenames, and embedded commit collapse to the same publication
  plan representation, so the dry-run evidence cannot identify the bytes it
  claims are ready to publish.
- Reproduction: the read-only release auditor inspected the current plan and
  producer projection: every package row has zero hash/byte properties and the
  sole repository consumer is the producer itself.
- Required closure: embed main/symbol hashes and sizes plus manifest/SBOM/
  checksum identities, validate the completed plan, and bind it into the exact-
  commit qualification receipt. Add two-bundle, stale-plan, changed-symbol,
  canonical bundle, and projection-removal mutation controls.

### SP-AUDIT-132 - Source locations lack one source-bound canonical geometry (P2)

- [ ] The compiler-artifact producer stores Roslyn's zero-based mapped line and
  column directly, while all other `WorkerSourceLocation` producers and the
  protocol contract require positive, one-based coordinates. Artifact validation
  contains a weaker diagnostic-only exception that admits zero.
- Supported impact: a physical line 5, column 16 error was recorded as 4,15; a
  first-character error was accepted as 0,0 even though
  `WorkerProtocolMetadata.IsSourceLocationValid` rejects that location.
- Reproduction: the read-only compiler auditor produced both artifacts and
  confirmed the source span remained correct while only display coordinates
  violated the shared model.
- Partial remediation (04d933623): compiler diagnostics now emit one-based
  mapped coordinates and share the protocol's positive-location/all-zero
  non-source predicate. Regression coverage includes first position, multiline,
  `#line`, generated source, exact-end zero-length spans, malformed mixed-zero
  shapes, and a conversion-removal mutation. The row remains active because
  schema 12 has no syntax-tree ordinal/hash, source text, or line-map authority
  with which hydration could recompute and authenticate mapped geometry.
- Required closure: add one to mapped source coordinates, allow the all-zero
  sentinel only for genuinely non-source diagnostics, then version the artifact
  so hydration can bind every location to its physical tree, checked source
  extent, and authenticated line map.
- Consolidated cases: SP-AUDIT-179, SP-AUDIT-220, SP-AUDIT-236.
- Unified closure: Bind every location to a physical tree and line-map identity; validate checked spans, sealed source length, and recomputed one-based mapped coordinates together.

### SP-AUDIT-140 - Relational-summary work lacks an end-to-end resource budget (P2)

- [ ] The summary builder charges fixed CFG actions but traverses, substitutes,
  rebuilds, merges, and composes arbitrarily wide term DAGs without charging
  their nodes. The separate depth check does not bound shallow width.
- Supported impact: a one-block caller can instantiate a dependency containing
  thousands of shallow unique leaves while a tiny symbolic-operation budget
  remains unexhausted, contradicting the documented 65,536-operation bound.
- Evidence: all dependency substitution and final relation construction paths
  are outside `Spend`; the limit gates only a small fixed set of block actions.
- Required closure: charge every visited/created term node across direct and
  dependency paths, or impose an equivalent total DAG-size bound. Add exact/+1,
  broad/shallow, shared-node, composition, and charge-removal controls.
- Consolidated cases: SP-AUDIT-187.
- Unified closure: Thread one catalog-owned budget through summary dependency discovery, provenance closure, term traversal, substitution, rebuild, and composition.

### SP-AUDIT-152 - Effect certainty and provenance tuples are not jointly validated (P2)

- [ ] Response validation independently accepts `EffectCertainty=VacuousEntry`
  and `Vacuity=None`; it never enforces the producer invariant that vacuous
  effect proof carries contradictory-precondition evidence and a core.
- Certifier impact: changing only a valid non-vacuous proven effect result's
  certainty to `VacuousEntry` leaves strict request-bound validation successful.
- Evidence: the closed certainty and vacuity predicates each accept the tuple;
  the real assembler emits only the coupled form.
- Required closure: add a cross-field effect-vacuity invariant and cover both
  contradictory directions, proof-core presence, non-vacuous proof, refuted/
  unknown controls, and invariant-removal mutation.
- Consolidated cases: SP-AUDIT-203, SP-AUDIT-234.
- Unified closure: Validate and produce claim kind, outcome, reason, certainty, vacuity, core, trusted provenance, and used assumption IDs from one authoritative table.

### SP-AUDIT-155 - Semantic identities and assumption kinds lack a closed producer authority (P2)

- [ ] Manifest validation enforces assumption identity/kind only per callable,
  while response summarization groups globally and rejects one ID carrying two
  kinds.
- Certifier impact: a sealed manifest with `shared` as `Precondition` in one
  callable and `UserAssume` in another validates, yet every exact response must
  fail `summary.assumption_conflict`.
- Evidence: the manifest and response validators apply different identity
  scopes to the same assumption ID domain.
- Required closure: define global versus callable-scoped assumption identity and
  enforce it consistently. Add same/different kind, same/different callable,
  generated IDs, summary reconstruction, and scope-check mutation controls.
- Consolidated cases: SP-AUDIT-202, SP-AUDIT-235.
- Unified closure: Generate exact semantic ID grammar/recomputation, a closed assumption-kind set, and exact trusted-source ownership from one producer authority.

### SP-AUDIT-165 - Z3 query ownership and accounting are incomplete (P2)

- [ ] SMT encoding creates many temporary `MkInt`, comparison, Boolean,
  conditional, and division wrapper objects without disposing them. The encoder
  owns cached roots but cannot reach those intermediate wrappers.
- Supported impact: a long-lived solver lane accumulates native references until
  managed GC/finalization, making query memory behavior depend on finalizer timing
  rather than the documented per-query lifecycle.
- Evidence: the file explicitly disposes other Z3-return wrappers and recognizes
  their native ownership, while these construction paths have no owner or
  `Dispose` scope.
- Required closure: introduce explicit expression ownership for every temporary
  and dispose after solver consumption. Add many-query native-allocation,
  no-GC-region, post-finalizer, cancellation, exception, and disposal-removal
  controls against the pinned Z3 build.
- Consolidated cases: SP-AUDIT-186.
- Unified closure: Give every Z3 query one ownership/accounting scope covering temporary ASTs, solve, model/core extraction, cancellation, exceptional exit, disposal, and final statistics.

### SP-AUDIT-166 - Corpus snapshot schema labels are not validated (P2)

- [ ] The corpus writer emits schema 3, but the loader discards comment/header
  rows and never requires one exact schema declaration.
- Certifier impact: missing, duplicated, contradictory, schema 2, and schema 999
  snapshots are accepted whenever their data rows still parse.
- Required closure: make the schema/header part of the parsed canonical format.
  Add exact-schema, missing, duplicate, conflicting, older, newer, and malformed
  header controls plus a validator-removal mutation.

### SP-AUDIT-168 - Failed gate runs preserve stale passing evidence (P2)

- [ ] The fuzz output directory is reused without invalidating `campaign.json`
  or obsolete per-seed logs before manifest validation and process launch.
- Certifier impact: an early validation or launch failure can leave a prior
  `passed: true` receipt and removed-seed evidence looking current.
- Required closure: make each campaign output transactional or start by
  removing/stale-marking prior owned evidence. Add pre-launch failure, launch
  failure, changed seed set, successful replacement, and unrelated-file controls.
- Consolidated cases: SP-AUDIT-188.
- Unified closure: Create a run-private incomplete receipt before prerequisites and atomically replace stable evidence only after every phase succeeds.

### SP-AUDIT-180 - Response validation accepts impossible elapsed times (P2)

- [ ] `ElapsedMilliseconds` is constrained only to be nonnegative and is not
  bounded by the producer's representable `TimeSpan` or supported execution
  envelope.
- Certifier impact: changing an otherwise valid request-bound response to
  `long.MaxValue` remains valid canonical evidence.
- Required closure: impose the exact producer-representable upper bound and,
  where launcher context is authoritative, the checked project-wall/grace
  envelope. Add zero, boundary, boundary+1, long-max, and overflow controls.

### SP-AUDIT-184 - Release bundle topology is not exact or self-contained (P2)

- [ ] Evidence generation and final validation enumerate packages and the one
  expected SBOM but ignore every other top-level or nested file, while CI uploads
  the whole package artifact directory.
- Certifier impact: foreign files can ship in the release artifact without a
  manifest row or `SHA256SUMS` entry.
- Required closure: require the exact regular-file set
  `{release manifest, checksums} + manifest artifacts` before upload and
  validation. Add ordinary extra, alternate SBOM, nested, directory, and exact-
  set controls.
- Consolidated cases: SP-AUDIT-224, SP-AUDIT-240.
- Unified closure: Atomically stage exactly the manifest, checksums, and seven canonical artifacts, and make every consumer use one strict topology/semantics validator.

### SP-AUDIT-189 - Acceptance restore predates its declared timeline (P2)

- [ ] The dispatcher completes and times restore before `Verify.ps1` captures
  `startedUtc`, but the receipt inserts restore as its first phase and includes
  it in total elapsed time.
- Certifier impact: the recorded phase interval is not contained by the
  receipt's declared start/completion interval.
- Required closure: give the dispatcher and receipt one timeline owner so start
  precedes restore. Add controlled zero/nonzero restore-duration and clock-
  boundary tests.

### SP-AUDIT-199 - Offline collision checks use case-sensitive filenames (P2)

- [ ] Fixture simulation probes only the exact local artifact filename, whereas
  NuGet identity and real V3 lookup normalize package ID/version casing.
- Certifier impact: the same package stored under a lowercase or otherwise
  renamed flat-container filename is missed on Linux and planned as `Push`.
- Required closure: enumerate fixture archives and compare canonical nuspec
  identity/version/role independent of filename. Add lowercase, renamed, mixed-
  case, main/symbol, duplicate, and exact-name controls.

### SP-AUDIT-226 - Compiler diagnostic namespaces are not validated (P2)

- [ ] The sole producer emits `compiler.<diagnostic-id>` and the public contract
  reserves that namespace, but artifact validation accepts any nonblank code.
- Certifier impact: a resealed compiler diagnostic can masquerade as
  `worker.infrastructure`, an analyzer diagnostic, or a bare `compiler.` code
  and is copied verbatim into the worker response.
- Required closure: require the exact ordinal `compiler.` prefix plus a canonical
  nonblank diagnostic ID. Add foreign namespace, bare prefix, case/whitespace,
  honest compiler ID, and prefix-check mutation controls.

### SP-AUDIT-230 - Publication planning does not validate SBOM semantics (P2)

- [ ] The canonical publication-plan path binds recorded file hashes but does
  not run the strict SBOM or checksum semantics validator.
- Certifier impact: replacing the SBOM with non-JSON bytes and rebinding the
  release manifest/checksum lets plan-only succeed even though the same bundle
  is rejected by the immutable-artifact validator.
- Required closure: make publication planning consume the exact strict release-
  artifact validation result before projecting actions. Add malformed/rebound
  SBOM, inconsistent checksum, valid bundle, and validation-removal fixtures.

### SP-AUDIT-233 - Rejected metadata preconditions evade SP0047 (P2)

- [ ] A readable but wrong-payload contract assembly can attach a closed
  precondition attribute to an external target; advisory activation starts, but
  call-site binding returns NotApplicable instead of accountable rejection.
- Supported impact: a call using rejected contract metadata emits neither the
  expected `SP0047` nor a semantic precondition diagnostic.
- Required closure: propagate rejected contract-API identity through metadata
  precondition binding and diagnose every attempted use. Add wrong payload,
  unreadable payload, trusted metadata, source target, mixed attributes, and
  no-contract controls.

## P3 active bugs

Precision, documentation, and developer-experience debt that does not change a supported proof or release decision.

### SP-AUDIT-035 - Compile-time-false loops are classified as diverging (P3)

- [ ] Termination classification calls `ManagedAbstractFlow.IsAcyclic` over
  reachable Roslyn CFG blocks, but follows every regular successor from each
  starting block without excluding a constant-infeasible edge. A syntactic
  loop therefore supplies a back edge even when its condition is the constant
  `false` and the body can never execute.
- Supported impact: a complete effect summary for `while (false) { }` reported
  `EffectTermination.MayDiverge` instead of `Terminates`. A valid selected
  termination claim can consequently be rejected for an exact, bounded
  control-flow shape.
- Reproduction: a temporary canonical-container Effects regression analyzed a
  method containing only `while (false) { }`; the focused assertion expected
  `Terminates` and received `MayDiverge`.
- Required closure: perform cycle detection over feasible CFG edges, at least
  folding compiler constants before following conditional successors, while
  remaining conservative for unknown conditions. Add while/for/do forms,
  false/true/unknown conditions, unreachable-body effects, nested loops, and a
  mutation that restores raw syntactic-cycle classification.

### SP-AUDIT-073 - Object-initializer writes lose fresh-object ownership (P3)

- [ ] An object creation is assigned a fresh effect region for its constructor,
  but the member initializer is scanned independently. Its implicit instance
  reference is consequently classified as an unknown/method receiver rather
  than the newly allocated object.
- Supported impact: an otherwise complete method that only initializes a field
  of a new object is reported incomplete and observably impure. Selected purity
  claims are falsely rejected, and the result is inconsistent with equivalent
  fresh array-element initialization already modeled as fresh-owned.
- Reproduction: a focused Effects test analyzed
  `new Value { Number = 1 }`. The write set was `Unknown`, projection
  completeness was false, and `IsObservablePure` was false instead of a sole
  fresh-region write and a complete pure result. The temporary test was removed.
- Required closure: analyze object/member initializers under the enclosing
  creation's fresh receiver region while still joining initializer-expression
  effects and property-setter summaries. Add field, property, nested object,
  static side effect, throwing setter, collection-initializer, struct, and fresh
  array parity controls plus a mutation restoring detached initializer scanning.

### SP-AUDIT-110 - Base property access is treated as open virtual dispatch (P3)

- [ ] Property effect scanning classifies dispatch from the accessor symbol
  alone, so every open virtual accessor is uncertain. Ordinary invocation uses
  operation-level `IsVirtual` and correctly recognizes an explicit base call as
  statically bound.
- Supported impact: `base.Value` produced an incomplete projection while the
  equivalent `base.GetValue()` was complete, rejecting an exact supported base
  property access for no runtime-dispatch reason.
- Reproduction: a temporary Effects test compared both members on the same
  derived class; the property assertion expected complete and received false,
  while the method control was complete. The test was removed.
- Required closure: pass operation-level property dispatch information into
  classification. Add base/this/interface/sealed/nonvirtual getter/setter/indexer
  controls and a mutation restoring symbol-only classification.

### SP-AUDIT-118 - Implicit empty constructors become unmodeled calls (P3)

- [ ] Source-call ownership requires a syntax reference. An implicit
  parameterless constructor has none, so object creation falls through to the
  external unknown boundary instead of the exact empty-source-constructor path.
- Supported impact: `new Empty()` for a sealed class with no members produced
  an incomplete effect projection, while fresh allocation itself is allowed
  and the equivalent explicit empty constructor is analyzable. This creates a
  systematic false unknown on an ordinary supported construction form.
- Reproduction: a temporary canonical-container Effects test required a
  complete projection for the implicit empty constructor and received false.
  The test was removed.
- Required closure: synthesize the exact implicit constructor summary, including
  the pinned base call and initializers, or typed-abstain consistently. Cover
  class/struct/record, implicit/explicit, base/member/cctor, and mutation cases.

### SP-AUDIT-125 - Archive checkouts cannot run container tooling (P3)

- [ ] Documentation supports source obtained as an archive and says Git is not
  a host prerequisite, but every finite non-dev container command unconditionally
  performs `git clone --shared` and `git rev-parse HEAD` before dispatch.
- Supported impact: a canonical source snapshot copied without `.git` failed
  before `tooling contract` with `fatal: repository ... does not exist`; build,
  test, and acceptance are equally unreachable from the advertised archive form.
- Reproduction: a disposable in-container tar copy excluded `.git`, then
  invoked the canonical entrypoint with that copy as `SHARPPROOF_REPO_ROOT`.
  No repository files or external state were changed.
- Required closure: materialize finite tasks directly from a source snapshot,
  using Git only when available for exact-commit evidence, or explicitly restore
  Git as a documented prerequisite. Add archive/Git, dirty/deleted/untracked,
  executable-bit, contract/build, and evidence-command controls.

### SP-AUDIT-135 - The documentation support-contract validator is disconnected (P3)

- [ ] `Generate-Readme.ps1 -Verify` checks package/version/support text and stale
  Windows-verifier claims, but acceptance, container dispatch, and all workflows
  omit it even though every main package embeds the README.
- Certifier impact: a forbidden `SharpProof.Verifier.Win-x64` support claim was
  rejected by the dormant validator but remained outside every release-blocking
  command graph.
- Evidence: an in-memory documentation mutation triggered the validator; exact
  caller searches across acceptance, workflows, and the dispatcher returned
  none.
- Required closure: run verification during static acceptance and release
  qualification, then bind its exact-commit outcome. Add stale-platform,
  package/version drift, disconnected-call, and clean-document controls.

### SP-AUDIT-142 - The standalone Docker build target cannot run its default command (P3)

- [ ] The Dockerfile `build` stage copies the repository to `/src` and declares
  `CMD ["build"]`, but unlike later `test` and `package` stages it does not set
  `SHARPPROOF_REPO_ROOT=/src`. The shared entrypoint defaults to the nonexistent
  `/workspace/SharpProof`.
- Supported impact: running the named immutable build image fails during its
  pre-dispatch Git snapshot instead of executing the build it declares.
- Evidence: the Dockerfile stage/root/command and entrypoint default form an
  unconditional path mismatch; Compose avoids it only by using the `dev` stage.
- Required closure: give every named stage the same explicit repository root and
  non-root execution contract. Add direct build/test/package image-command,
  working-directory, UID, and Compose parity fixtures.

### SP-AUDIT-146 - Diagnostic documentation contradicts rejected-API behavior (P3)

- [ ] `docs/diagnostic-examples.md` says a readable wrong-payload Contract API
  disables analysis without a diagnostic, while implementation and the other
  public documents emit and promise SP0047 `ContractApiIdentityRejected`.
- Supported impact: users are told to expect silence for behavior that produces
  an explicit incomplete-coverage diagnostic, obscuring the distinction from
  unreadable payloads and SP0050.
- Evidence: resolver rejection and analyzer diagnostic routing match README and
  public API text; only the diagnostic example states the opposite.
- Required closure: add a wrong-hash readable-assembly behavioral test and bind
  the documentation validator to the result. Forbid the stale silent-analysis
  claim while retaining the unreadable-payload distinction.

### SP-AUDIT-181 - Resource and concurrency documentation drifts from catalogs (P3)

- [ ] Container development documentation promises eight deterministic
  mutation lanes, while the acceptance contract, implementation, and
  architecture gate all own the value four.
- Supported impact: operators size hosts and interpret campaign duration using
  a nonexistent default concurrency level.
- Required closure: generate the documented lane count from the acceptance
  contract and reject documentation/catalog drift in either direction.
- Consolidated cases: SP-AUDIT-194.
- Unified closure: Generate CPU/memory and mutation concurrency statements from the acceptance/container catalogs and gate exact documentation parity.

### SP-AUDIT-182 - `sp check` is not the documented single build (P3)

- [ ] README and container-development docs describe one incremental build, but
  the default Debug path performs another package-test build and three
  build-capable Release pack operations.
- Supported impact: the everyday check command is materially slower and does
  more compilation than its performance/iteration contract states.
- Required closure: either reuse the initial build outputs with explicit
  no-build package phases or document and snapshot the intentional command
  graph. Add an instrumented command-plan regression.

### SP-AUDIT-209 - Typed outcome and certainty documentation drifts from schema (P3)

- [ ] The exact effect-certainty reference omits `VacuousEntry`, although the
  schema admits it and the worker emits it for effects proven from contradictory
  entry preconditions; the documentation gate does not validate this enum.
- Supported impact: an integrator can receive a valid protocol value absent from
  the purported closed reference.
- Required closure: derive the exact certainty member table and allowed tuples
  from the protocol schema, document `VacuousEntry`, and reject missing/extra or
  stale table entries.
- Consolidated cases: SP-AUDIT-225.
- Unified closure: Generate the complete public outcome/reason/certainty table from the protocol schema, including VacuousEntry and the full Unavailable domain.

### SP-AUDIT-222 - API-spec typing loses exact substituted types (P3)

- [ ] API-spec validation/instantiation admits coarse Reference variables, but
  materializes every reference null as `object` rather than the concrete
  substituted reference type.
- Supported impact: a valid `Widget` result specification `result != null`
  becomes a `Widget` versus `object` IR comparison, fails instantiation, and
  causes the worker to abandon an otherwise supported spec application.
- Required closure: derive null's concrete type from the paired operand/
  substitution or preserve concrete declarative type. Add object, string, user-
  defined reference, result/parameter, nullable comparison, and unsupported
  sequence-null controls.
- Consolidated cases: SP-AUDIT-228.
- Unified closure: Preserve and unify exact substituted IR types for null and equality operands, rejecting incompatible reference or sequence types before selection.

## Current comprehensive audit evidence

- [x] Pass A reviewed analyzer/generator/collector ownership, ContractFor
  exactness, effect-operation support, schema-12 reference provenance, and
  worker proof/result classification. Two executable soundness failures were
  admitted as SP-AUDIT-001 and SP-AUDIT-002. The temporary failing probes were
  removed after their results were recorded.
- [x] Pass B reviewed Linux path/publication containment, worker cancellation,
  exact Z3 resolution, package discovery, package consumers, pilot evidence,
  SBOM/release evidence, and tag qualification. Two certifier defects were
  admitted as SP-AUDIT-003 and SP-AUDIT-004; no supported Linux host/runtime
  failure was reproduced.
- [x] Follow-up Pass C reviewed adjacent analyzer/dataflow/compiler authority
  without reopening the first two effect defects. It admitted the executable
  initializer false negative as SP-AUDIT-005; compiler-reference ordering,
  size, stale-metadata, and canonical-shape paths had no new discriminating
  failure.
- [x] Follow-up Pass D reviewed Linux metadata paths, process containment,
  exact Z3 loading, protocol validation, container commands, and independent
  package/SBOM/release certifiers. It admitted SP-AUDIT-006 and SP-AUDIT-007.
- [x] Follow-up Pass E reviewed worker proof/result validation, compiler
  schema-12 authority, analyzer call-site ownership, and adjacent supported
  callable forms. It admitted the primary-constructor false negative as
  SP-AUDIT-008. A synthesized top-level-entry call-site probe was rejected
  because that callable is not included in the documented portable call-site
  traversal surface.
- [x] Follow-up Pass F reviewed Linux publication metadata, exact Z3/process
  containment, package consumers, container execution, and qualification
  scripts without reopening the existing release findings. It admitted the
  case-sensitive evidence-path containment defect as SP-AUDIT-009, and a
  second executable probe proved SP-AUDIT-006 also accepts an exact-content
  ownership-marker symlink.
- [x] Follow-up Pass G reviewed semantic authority and the documented targeted
  container test surface. It admitted SP-AUDIT-010 after a filtered worker
  proof-kernel matrix exposed order-dependent native Z3 setup; the Effects,
  analyzer/final-compilation, summary, proof-kernel, and SMT matrices otherwise
  passed 178 cases.
- [x] Clean Pass H reviewed the remaining semantic/compiler authority after
  SP-AUDIT-010 was admitted: ContractFor final-compilation validation,
  constructed and exact-symbol companion binding, contract inventory,
  compiler-artifact canonical hydration, acyclic execution, protocol result
  assembly, and cache replay. The dedicated ContractFor and Contracts matrices
  passed 142 cases; the compiler/worker cases were included in the 100-case
  clean host/protocol matrix below. No additional finding was admitted.
- [x] Clean Pass I reviewed the Linux host and certifier surface after the last
  finding: path and publication identity, child startup/cancellation, container
  contract and exact Z3 checks, protocol/cache failure handling, BuildTasks,
  package layout and entrypoint discovery, a real packaged proof, and offline
  publication planning. The canonical container contract passed, as did 100
  worker/host/protocol cases, 64 launcher/build-task cases, three exact-package
  cases, and five publication-plan cases. No additional finding was admitted.
- [x] Follow-up Pass J reviewed newly adjacent exact-effect operation shapes
  and admitted SP-AUDIT-012 after a canonical-container regression proved that
  `foreach` silently omits compiler-selected enumerator writes. The temporary
  source test was removed after its result was recorded.
- [x] Follow-up Pass K reviewed container source materialization and cached
  release evidence. It admitted SP-AUDIT-011 after a real package contained an
  uncommitted marker while asserting clean HEAD, and SP-AUDIT-013 after an
  empty forged mutation summary satisfied the canonical mutation command.
  Temporary source and evidence probes were removed or restored.
- [x] Follow-up Pass M reviewed the remaining release bundle structure and
  admitted SP-AUDIT-014 after both evidence generation and final validation
  accepted a symbol package containing no PDB. The disposable package
  directory was deleted after the result was recorded.
- [x] Clean Pass L reviewed remaining compiler-inserted and computed-value
  effect paths. An implicit base-constructor control passed. A compound
  property alias probe exposed a raw-summary imprecision, but its only
  discriminating form required a user-defined operator that the documented
  analyzer subset rejects before proof, so it was rejected rather than
  promoted. All temporary semantic tests were removed.
- [x] Follow-up Pass O reviewed package-to-SBOM provenance relationships and
  admitted SP-AUDIT-015 after a foreign SharpProof-prefixed DLL escaped the
  third-party inventory and both release validators. The disposable package
  directory was deleted after the result was recorded.
- [x] Follow-up Pass N reviewed admitted argument and callable-boundary
  propagation and admitted SP-AUDIT-016 after definite getter and setter
  precondition violations produced no SP0027. The temporary analyzer test was
  removed after its result was recorded.
- [x] Follow-up Pass P checked the adjacent custom-event accessor surface and
  broadened SP-AUDIT-016 after definite add/remove precondition violations also
  produced no SP0027. The temporary event test was removed.
- [x] Follow-up Pass Q corrupted the catalog-owned packaged Z3 payload and
  broadened SP-AUDIT-015 after both release validators accepted it. The exact
  package-layout test rejected the corruption, documenting the current
  workflow mitigation and the non-self-contained release boundary. The
  disposable package directory was deleted.
- [x] Follow-up Pass Q also exercised publication identity framing and admitted
  SP-AUDIT-017 after two distinct newline-containing path sets with a shared
  output were accepted as one ownership set. The temporary worker test was
  removed.
- [x] Follow-up Pass R reviewed remaining conversion exception
  classification and admitted SP-AUDIT-018 after definitely null nullable
  unboxing produced two impossible exceptions in a complete effect projection.
  The temporary Effects test was removed.
- [x] Clean Pass S reviewed cache ownership, eviction, replay validation,
  protocol canonicalization, malformed-result handling, and publication-marker
  canonicality after the framing defect was separated into SP-AUDIT-017.
  Existing tests cover symlinked cache entries, locks and directories, unowned
  suffix matches, byte-bound rollback, stale manifests, scalar-model replay,
  and oversized JSON. No additional executable supported-surface failure was
  admitted.
- [x] Follow-up Pass T reviewed exception-handler reachability, cast exception
  classification, and array-store compatibility. It admitted SP-AUDIT-019 and
  SP-AUDIT-020 from focused canonical-container failures and broadened
  SP-AUDIT-018 with a second definite reference-cast reproduction. All three
  temporary Effects probes were removed.
- [x] Clean Pass U rechecked schema-12 compiler-reference capture and package
  authority adjacent to the newly admitted semantic defects. Reference module
  count/byte budgets, exact backing-metadata comparison, deterministic linked
  module ordering, stale-module rejection, package layout, native Z3 hash, and
  repository metadata have existing direct checks. No new independent
  executable certifier failure was admitted; the remaining source/package and
  release-boundary weaknesses are already separated as SP-AUDIT-003,
  SP-AUDIT-004, SP-AUDIT-007, SP-AUDIT-011, and SP-AUDIT-013 through 015.
- [x] Follow-up Pass V compared the documented expression matrix with the
  generated operation authority and admitted SP-AUDIT-021 after a constant
  ordinary interpolation was rejected before exact effect analysis. The
  temporary analyzer probe was removed.
- [x] Clean Pass W reviewed launcher exit/result reconciliation, worker
  timeout/cancellation cleanup, protocol run/failure consistency, package
  consumer provenance, and tag-artifact acquisition. No independent
  executable failure was admitted: exact workflow artifacts are commit-named,
  while the remaining local package/pilot/release binding weaknesses are
  already captured by SP-AUDIT-003, SP-AUDIT-004, SP-AUDIT-011, and
  SP-AUDIT-013 through 015.
- [x] Clean Pass X inventoried every default API specification and the enabled
  relational pack, then cross-checked their proof-bearing effects, exception,
  nullness, cardinality, and scalar-result facts against the pinned runtime
  semantics. The catalog is deliberately small and conservative; no new
  executable model mismatch was admitted.
- [x] Follow-up Pass Y reviewed compilation-global activation across analyzer,
  ContractFor generator, and compiler collector. It admitted SP-AUDIT-022 from
  two focused custom-host failures covering conflicting aliases and an invalid
  profile value. Both temporary generator probes were removed.
- [x] Follow-up Pass Z reviewed conversion allocation semantics, exceptional
  call sequencing, source type initialization, summary/SMT normal-completion
  handling, and cancellation-boundary certification. It admitted
  SP-AUDIT-023 through SP-AUDIT-025 from three focused failures. A source static
  initializer candidate was rejected because the effect analysis already
  surfaced the boundary as incomplete instead of proving through it. All
  temporary tests were removed.
- [x] Follow-up Pass AA reviewed container publication/process containment,
  pilot isolation, package/evidence binding, and tag qualification. It admitted
  SP-AUDIT-026 after the real five-pilot certifier accepted copied stale outputs
  with a no-op `dotnet`. The disposable clone and fake executable were removed;
  no host artifacts were changed.
- [x] Follow-up Pass AB tested left-to-right call completion and broadened
  SP-AUDIT-024 after a definitely throwing first argument failed to suppress a
  later argument's and the callee's static writes. The temporary Effects probe
  was removed.
- [x] Clean Pass AC reviewed request hashing, response/manifest canonical
  validation, cache-key policy inputs, cache reconstruction, counterexample
  replay, malformed result classification, and cancellation/timeout binding.
  The cache only retains replay-validated refuted postconditions and rebuilds
  request-specific response metadata; no independent executable protocol or
  cache failure was admitted.
- [x] Follow-up Pass AD reviewed release/container command preconditions and
  admitted SP-AUDIT-027 after the canonical `release-tag` certifier accepted a
  branch ref as a valid release identity. The probe used only environment
  overrides and created no repository evidence or tracked changes.
- [x] Follow-up Pass AE reviewed analyzer diagnostic ownership at executable
  root boundaries. A focused console-compilation probe confirmed that top-level
  statements receive no SP0027, but the documented portable call-site surface
  names only methods, local functions, lambdas, and anonymous methods; the
  probe was rejected as unsupported and removed. SP-AUDIT-005 remains limited
  to documented initializer execution.
- [x] Follow-up Pass AF reviewed release-evidence self-authentication and
  admitted SP-AUDIT-028 after the canonical qualification command emitted
  passed evidence for a text file, nonexistent commit, and unrelated version.
  The disposable clone and its private Compose volumes were removed.
- [x] Follow-up Pass AG reviewed nullable conversion semantics and broadened
  SP-AUDIT-018 after a focused Effects regression proved that definitely
  present nullable unwrapping reports an impossible exception in a complete
  projection. The temporary Effects test was removed.
- [x] Clean Pass AH reviewed proof-kernel result shapes, unsat-core bounds,
  counterexample replay, model-variable ownership, undefined goals, typed
  backend failures, SMT integer bounds/division, cancellation, and native
  result disposal. The canonical Verify and SMT suites passed 33 cases; no new
  proof-acceptance or fail-closed result defect was admitted.
- [x] Follow-up Pass AI reviewed Linux publication lock acquisition and failure
  cleanup and admitted SP-AUDIT-029 after a focused regression measured one
  leaked descriptor per failed multi-lock construction. The temporary worker
  test was removed.
- [x] Clean Pass AJ reviewed ContractFor type/member exactness and direct
  attribute-scope ownership after the profile-resolution defect was separated
  into SP-AUDIT-022. Every consumed effect/control attribute explicitly opts
  out of CLR attribute inheritance; the validator and runtime binder agree on
  receiver placement, ordinary-member identity, nested/open generic
  specialization, constraints, nullability, defaults, ref/scoped kinds,
  custom modifiers, function pointers, and generated/final-compilation
  companions. The canonical ContractFor generator and Contracts suites passed
  151 cases; no additional executable supported-surface failure was admitted.
- [x] Clean Pass AK reviewed cache ownership, byte-bound rollback, lock and
  reparse rejection, replay validation, stale-schema handling, and adjacent
  package-closure authority without reopening SP-AUDIT-003, SP-AUDIT-007,
  SP-AUDIT-014, SP-AUDIT-015, or SP-AUDIT-028. The canonical cache-focused
  matrix passed 29 cases. A combined package layout/symbol/evidence probe
  exceeded its 120-second audit bound and was stopped cleanly, so it was not
  counted as new closure evidence; static review found no independent defect
  outside the existing package and release rows.
- [x] Follow-up Pass AL compared every effect-discovery operation catalog row
  with CFG reachability and scanner dispatch. It admitted SP-AUDIT-030 after a
  three-shape control proved only definitely-null conditional access retained
  the skipped callee write; constant conditional and definitely-nonnull
  coalesce controls were clean. The temporary Effects test was removed.
- [x] Follow-up Pass AM reviewed compiler-to-worker callable ownership for
  declared, synthesized, nested, generated, property/event, top-level, partial,
  and primary-constructor forms. It admitted SP-AUDIT-031 and SP-AUDIT-032
  after Roslyn symbol assertions proved the selected accessor/constructor
  contracts existed while manifest discovery returned zero. Both temporary
  worker tests were removed.
- [x] Follow-up Pass AN reviewed implicit normal-completion sequencing and
  lifted integral hazards. It broadened SP-AUDIT-024 after null lock entry and
  a definitely throwing constructor both failed to suppress later writes, and
  admitted SP-AUDIT-033 after null-left and null-right lifted division both
  retained impossible exceptions. The temporary Effects probes were removed.
- [x] Follow-up Pass AO reviewed hash-consistent compiler-manifest canonical
  shapes and admitted SP-AUDIT-034 after uppercase compiler and reference MVIDs
  survived direct serialization, fingerprint recomputation, and canonical
  deserialization. The temporary worker test was removed.
- [x] Follow-up Pass AP tested definitely-null lifted arithmetic across checked
  addition, checked conversion, increment, and compound division. It broadened
  SP-AUDIT-033 after addition, increment, and compound division retained
  impossible hazards; the nullable conversion control was clean. The temporary
  Effects test was removed.
- [x] Clean Pass AQ reviewed relational-summary signature/environment
  validation, substitution, fresh-variable ownership, normal-completion
  definedness, compiler lowering, and worker assumption encoding. A disposable
  divide-by-zero instantiation faulted rather than admitting a relation, a
  valid instantiation evaluated exactly, and independent instances had
  disjoint fresh variables. The temporary Summaries test was removed; no new
  proof-authority defect was admitted.
- [x] Follow-up Pass AR inventoried exact effect-region ownership for value
  copies, ref-like and unsupported operation shapes, then tested termination
  feasibility. It admitted SP-AUDIT-035 after a compile-time-false while loop
  was classified as diverging. The temporary Effects test was removed.
- [x] Follow-up Pass AS audited package/SBOM hash, identity, relationship, and
  license binding. It admitted SP-AUDIT-036 after license-corrupted current-HEAD
  evidence passed both canonical release certifiers. The disposable package
  copy and probe script were removed.
- [x] Follow-up Pass AT reviewed generated-source ownership after the prior
  string-literal correction. It admitted SP-AUDIT-037 after an explanatory
  leading comment suppressed a real SP0027 violation. The temporary analyzer
  test was removed.
- [x] Follow-up Pass AU revisited compiler-identity producer canonicality. It
  broadened SP-AUDIT-034 after arbitrary nonempty compiler-version text
  survived fingerprint recomputation and canonical deserialization. The
  temporary worker test was removed.
- [x] Follow-up Pass AV independently audited final third-party inventory
  authority. It admitted SP-AUDIT-038 after fabricated component identity and
  version data, made self-consistent only inside the evidence pair, passed the
  canonical final release validator. The disposable bundle and probe script
  were removed.
- [x] Follow-up Pass AW audited worker-response/result binding and admitted
  SP-AUDIT-039 after a fully fabricated worker/spec version summary passed
  request-bound validation. The temporary protocol test was removed.
- [x] Clean Pass AX reviewed Linux path canonicalization, file identity,
  publication lock ordering and partial acquisition, persistent marker
  ownership, atomic output replacement, parent-death and signal cancellation,
  and exact Z3 contract loading. No new executable trusted-container failure
  was found beyond SP-AUDIT-006, SP-AUDIT-017, and SP-AUDIT-029.
- [x] Clean Pass AY systematically compared every declared exact
  effect-discovery operation with scanner dispatch, CFG reachability, and
  implicit runtime semantics. No new independent omission was found beyond
  SP-AUDIT-001, SP-AUDIT-002, and SP-AUDIT-012; the remaining catalog entries
  are structural/compile-time nodes or reach dedicated field, property, array,
  call, allocation, operator, lock, loop, conversion, and throw handlers.
- [x] Follow-up Pass AZ audited compiler-lowered graph ownership and admitted
  SP-AUDIT-040 after both a same-typed parameter-target swap and a same-typed
  pre-state/current-state swap survived callable decoding. The existing
  mixed-type corruption control rejects only because the IR types differ. The
  temporary worker tests were removed.
- [x] Clean Pass BA reviewed callable discovery and subset ownership across
  ordinary/explicit-interface methods, constructors, property/indexer/event
  accessors, operators/conversions, destructors, local functions, lambdas,
  anonymous methods, top-level code, generated trees, and companion targets.
  The compiler inventory discovers the additional callable forms and the
  documented subset deliberately types unsupported kinds; no independent
  omission beyond SP-AUDIT-005, SP-AUDIT-008, SP-AUDIT-016, SP-AUDIT-031, and
  SP-AUDIT-032 was admitted.
- [x] Follow-up Pass BB audited result claim/assumption ownership, vacuity,
  proof-core shape, summary counts, and request binding. It admitted
  SP-AUDIT-041 after fabricated proof, counterexample-model, and mismatched
  effect-witness payloads survived the strict request-bound validator. The
  temporary protocol tests were removed.
- [x] Clean Pass BC reviewed cache path prevalidation, owned-entry naming,
  lock lifetime, byte-bound rollback, eviction continuation, payload hashing,
  manifest binding, counterexample model decoding, assumption replay, and
  cache-hit response reconstruction. No independent defect was found; unlike
  SP-AUDIT-006, the cache validates its derived lock path before opening it,
  and refuted claims are replayed against decoded compiler evidence.
- [x] Clean Pass BD reviewed call-site receiver/argument mapping for direct and
  reduced extension calls, named and omitted optional arguments, ref/in alias
  treatment, constructors, property/accessor calls, and unsupported param-array
  expansion. The mapping uses Roslyn parameter ordinals and shifts only the
  reduced receiver; no new executable failure was admitted beyond the existing
  call-site ownership rows.
- [x] Clean Pass BE reviewed closed-control attribute ownership and allowed
  exception inheritance across methods, accessors, local functions, lambdas,
  containing types, and assemblies. Scope validation, deduplication, malformed
  reason handling, and derived-exception matching remain fail-closed; no new
  supported-surface defect was admitted.
- [x] Follow-up Pass BF audited partial-method selection, effect-contract
  inheritance, normalized symbol identity, and executable-body ownership. It
  admitted SP-AUDIT-042 after one definition-level purity contract and one
  implementation body produced duplicate SP0002 diagnostics. The temporary
  analyzer test was removed.
- [x] Follow-up Pass BG audited worker run-status, failure-reason, callable
  coverage, claim outcome, summary, and strict request-binding consistency. It
  admitted SP-AUDIT-043 after all-proven responses remained valid when labeled
  `TimedOut` or `Canceled`. The temporary protocol test was removed.
- [x] Clean Pass BH reviewed Linux canonicalization and `lstat` ancestry,
  regular-file deletion and hardlink protection, mount-type rejection,
  publication metadata namespaces, ordered `flock` acquisition, marker
  rollback, worker parent-death setup, termination deadlines, and exact Z3
  hash/load ownership. No independent trusted-container defect was admitted
  beyond SP-AUDIT-006, SP-AUDIT-017, and SP-AUDIT-029; hostile concurrent path
  replacement remains an explicit preview boundary.
- [x] Follow-up Pass BI audited release artifact count, identity, filename,
  role, checksum, SBOM, and final-validation binding. It admitted SP-AUDIT-044
  after all three main/symbol roles could be inverted while retaining accepted
  self-consistent evidence. The disposable bundle and probe script were
  removed.
- [x] Rejected Pass BJ tested whether two same-identity summarized calls could
  exchange their relation/result closures while remaining inside the supported
  compiler-lowered surface. Both an order-sensitive `int` fixture and a checked
  `long` fixture abstained before producing two summary descriptors, so the
  hypothesized corruption path was not executable and no row was added. The
  temporary worker test was removed.
- [x] Follow-up Pass BK audited NuGet archive entry identity, first-party
  payload ownership, evidence regeneration, and final release validation. It
  broadened SP-AUDIT-015 after an exact duplicate first-party assembly path
  carrying different bytes was accepted and re-certified. The disposable
  package copy and probe script were removed.
- [x] Clean Pass BL cross-checked canonical container commands, CI workflow
  sequencing, exact-SHA package handoff, release planning, qualification,
  attestation, and publication ownership. It admitted no independent defect
  beyond SP-AUDIT-003, SP-AUDIT-004, SP-AUDIT-013, SP-AUDIT-027,
  SP-AUDIT-028, and SP-AUDIT-044. The review also corrected SP-AUDIT-044 to
  record that publication planning independently rejects role-swapped file
  extensions, leaving the standalone final validator defective but mitigated.
- [x] Follow-up Pass BM audited Portable IR slot canonicality, equality
  partitions, table construction, hydration, and round-trip validation. It
  admitted SP-AUDIT-045 after an unused operation row survived canonical
  serialization/deserialization and callable decoding. The temporary worker
  test was removed.
- [x] Clean Pass BN audited generated-code authority across compiler provider
  results, attributes on methods and associated members, leading trivia,
  filename fallbacks, and empty/reference-free trees. An explicit
  `GeneratedKind.NotGenerated` result intentionally overrides fallbacks and is
  already regression-tested; no independent defect was admitted beyond
  SP-AUDIT-037.
- [x] Clean Pass BO tested CFG feasibility for a compile-time constant
  conditional whose dead branch throws, and POSIX normalization for equivalent
  single- and double-leading-slash local paths. The dead effect was excluded
  and both path spellings resolved to the same canonical path, so neither
  candidate produced a supported-surface failure. Both temporary tests were
  removed.
- [x] Clean Pass BP tested a compile-time-selected switch with a throwing dead
  arm. Managed flow excluded the impossible arm while retaining a complete
  summary, so the constant-switch candidate did not reproduce. The temporary
  Effects test was removed.
- [x] Clean Pass BQ reviewed the generic forward solver's round-based joins,
  cyclic-block widening, convergence limit, graph validation, and worklist
  permutation checks together with proof-kernel backend-status, unsat-core,
  exact-model, and counterexample-replay validation. The remaining permissive
  shapes either canonicalize harmless backend redundancy or fail closed; no
  independent proof-soundness defect was admitted.
- [x] Clean Pass BR reviewed worker runtime-closure staging, cache-key inputs,
  cache entry ownership and refutation replay, direct-child startup,
  parent-death installation, timeout cleanup, and native Z3 ownership. No new
  trusted-container failure was found beyond SP-AUDIT-006, SP-AUDIT-010,
  SP-AUDIT-029, and SP-AUDIT-039.
- [x] Follow-up Pass BS audited compiler-implicit execution ordering around
  constructor member initialization. It broadened SP-AUDIT-024 after a
  definitely throwing first initializer failed to suppress a later
  initializer's static write. The temporary Effects regression was removed.
- [x] Rejected Pass BT tested whether a warning level above the command-line
  convention represented compiler-impossible snapshot evidence. Roslyn's
  current `CSharpCompilationOptions.WithWarningLevel` accepts the value, so the
  premise was false and no canonicality row was added. The temporary worker
  test was removed.
- [x] Follow-up Pass BU audited syntax-tree parse-option provenance and
  broadened SP-AUDIT-034 after an arbitrary non-language-version string passed
  fingerprint recomputation and canonical deserialization. The temporary
  worker test was removed.
- [x] Follow-up Pass BV audited compiler and reference assembly-identity
  provenance and broadened SP-AUDIT-034 after arbitrary non-identity text in
  either field passed fingerprint recomputation and canonical deserialization.
  Both temporary worker regressions were removed.
- [x] Follow-up Pass BW tested cross-field compiler identity consistency and
  broadened SP-AUDIT-034 after a changed assembly name paired with the original
  valid assembly identity passed fingerprint recomputation and canonical
  deserialization. The temporary worker regression was removed.
- [x] Clean Pass BX reviewed Linux publication path canonicalization, local
  filesystem identification, lock/marker ownership, cache locking and eviction,
  container-contract validation, exact native-Z3 resolution, bounded protocol
  JSON reads, manifest/result set validation, and cache replay binding. No new
  supported-container defect was found beyond SP-AUDIT-006, SP-AUDIT-010,
  SP-AUDIT-017, SP-AUDIT-029, SP-AUDIT-039, SP-AUDIT-041, and SP-AUDIT-043.
- [x] Rejected Pass BY exercised calls in a synthesized top-level entry point
  and compiler-generated collection-initializer `Add` invocations. Both probes
  produced no SP0027, but neither callable shape belongs to the documented
  portable call-site traversal/replay surface. The worker separately records
  the top-level callable as `UnsupportedCallable`; collection `Add` replay is
  not among the finite supported prefix forms. Neither probe was promoted, and
  both temporary analyzer tests were removed.
- [x] Clean Pass BZ reviewed API-spec identity, instantiation, term validation,
  and table canonicalization; relational-summary construction, validation, and
  substitution; SMT encoding, cancellation, model/core disposal, and proof
  kernel result checks; and worker lane retirement and result assembly. No new
  supported-surface defect was admitted beyond the existing semantic and
  worker rows.
- [x] Follow-up Pass CA audited the container toolchain certifier and admitted
  SP-AUDIT-046 after a second effective Dockerfile SDK-image argument passed
  the canonical `tooling contract` gate while the reviewed pin remained only
  as a decoy. The temporary Dockerfile mutation was removed.
- [x] Follow-up Pass CB audited release SBOM graph authority and admitted
  SP-AUDIT-047 after the final certifier accepted a fabricated cross-package
  containment relationship and duplicate package SPDX identifiers in fully
  rehashed exact-HEAD bundles. Both disposable bundles were removed.
- [x] Follow-up Pass CC audited compiler-manifest and per-invocation lifecycle.
  Syntax and semantic compiler-error probes left no run directories and were
  rejected. A valid compilation followed by an invalid verifier policy admitted
  SP-AUDIT-048 after two builds leaked two unique invocation directories. The
  disposable consumer was removed.
- [x] Follow-up Pass CD audited release JSON canonicality and admitted
  SP-AUDIT-049 after the final validator accepted contradictory duplicate
  `packageVersion` properties in the exact-HEAD manifest. The disposable bundle
  was removed.
- [x] Rejected Pass CE tested request/cache-policy consistency by changing a
  response to `CacheStatus=Hit` for a cache-disabled request. Existing generated
  protocol rules rejected the impossible response, so no row was added. The
  temporary protocol test was removed.
- [x] Clean Pass CF reviewed final-compilation option capture, syntax-tree and
  additional-file hashing, reference metadata/image parity, linked-module
  ordering and budgets, diagnostic fingerprinting, callable lowering admission,
  manifest/callable parity, and cache input identity. No independent supported-
  surface defect was admitted beyond SP-AUDIT-034, SP-AUDIT-040, SP-AUDIT-045,
  and the existing reference/resource coverage.
- [x] Clean Pass CG reviewed isolated package-source mapping, actual SDK
  selection, analyzer-item discovery, framework consumer builds, and focused
  real verifier execution. The consumer path clears ambient sources, uses a
  private package cache, checks actual requested SDK selection, and executes
  the intended package tests; no independent bypass was admitted beyond the
  existing package-payload/evidence rows.
- [x] Follow-up Pass CH audited coverage-report authority and admitted
  SP-AUDIT-050 after a forged 22-line Cobertura report passed every project,
  aggregate, and changed-TCB floor at 100%. The forged report and probe were
  removed.
- [x] Clean Pass CI reviewed managed exceptional control flow across exact and
  base catches, constant and nonconstant filters, rethrows, sibling handlers,
  throwing and nonreturning finally blocks, unknown external throws, and
  nested callable boundaries. Existing focused tests exercise the escaping-
  exception decisions; no independent defect was admitted beyond
  SP-AUDIT-019 and the existing effect-flow rows.
- [x] Clean Pass CJ reviewed dependency-audit and trusted-mutation evidence
  accounting. The canonical dependency command invokes NuGet directly,
  requires the exact solution-project and approved-source sets, and rejects
  reported problems or vulnerabilities. Its supplied-report mode is not used
  by release qualification. Mutation result/TRX validation remains covered by
  SP-AUDIT-013's existing exact-evidence weakness; no nonduplicative finding
  was admitted.
- [x] Follow-up Pass CK audited analyzer call discovery and managed exception
  ownership. The documented call-shape inventory yielded no new omission
  beyond SP-AUDIT-005, SP-AUDIT-008, and SP-AUDIT-016. A focused nested-catch
  probe admitted SP-AUDIT-051 after a rethrow owned by the inner catch
  fabricated escape of the outer caught exception. The probe was removed.
- [x] Follow-up Pass CL audited release receipt and environment-configuration
  authority. Receipt/package gaps remained covered by SP-AUDIT-003,
  SP-AUDIT-004, SP-AUDIT-028, and SP-AUDIT-049. The canonical configuration
  validator admitted SP-AUDIT-052 for its empty-set binder failure and
  SP-AUDIT-053 after wildcard environment authorization passed a mocked exact
  API check. All mocks, generated evidence, and temporary contract edits were
  removed.
- [x] Follow-up Pass CM used six independent read-only auditors across
  analyzer/ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/CI authority. Focused canonical-
  container probes admitted SP-AUDIT-054 through SP-AUDIT-062: release-tag
  bypass actors and exclusions, generated-tree validation, resultless launcher
  success, unauthenticated summary provenance, cancellation/finally handling,
  catch-state flow, by-reference ownership, and compound-conversion effects.
  Every temporary test, mock, and evidence directory was removed; the six
  auditors made no repository edits.
- [x] Follow-up Pass CN tested the strongest remaining independent candidates
  from that wave. It admitted SP-AUDIT-063 after a deterministic late SARIF
  failure left a mixed publication generation, SP-AUDIT-064 after the effects
  profile skipped a recognized peer-generated companion, SP-AUDIT-065 after
  unrelated release environments hidden behind comment tokens passed the
  canonical configuration validator, SP-AUDIT-066 after the composed worker
  deadline exceeded the documented project-plus-one-grace boundary, and
  SP-AUDIT-067 after all three release stages certified a package dependency
  graph that disagreed with the package bytes. A peer-generator stale-
  diagnostic probe passed its exact final-compilation control and was rejected;
  Roslyn CFG exposure also disproved the proposed user-defined truth-operator
  omission. All temporary source, workflow, contract, mock, timing-test, and
  package mutation changes were removed.
- [x] Follow-up Pass CO used six independent read-only auditors across
  analyzer/ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/container authority. Focused
  canonical-container probes admitted SP-AUDIT-068 through SP-AUDIT-074:
  mixed generated partials, unused nested control attributes, never-executed
  lambdas, Unicode-escaped activation, implicit string-conversion effects,
  fresh object-initializer ownership, and request/cache-state binding. A
  coalescing-conversion hypothesis was rejected because the current scanner
  retained the demonstrated static write and conservatively returned unknown
  throws/incomplete evidence. Every temporary test was removed; auditors made
  no repository edits.
- [x] Follow-up Pass CP exercised the strongest remaining nonduplicative host,
  worker, compiler, coverage, and container-certifier candidates. It admitted
  SP-AUDIT-075 through SP-AUDIT-081 after nested publication setup left an
  output directory, path text corrupted warning identity, a floating Dockerfile
  frontend passed the contract gate, an authentic changed-TCB report skipped a
  behavioral constant, backend renewal failure became a timeout, feature scope
  failed to bind discovery, and a linked module occupied the manifest slot.
  The authentic coverage probe used 44 existing reports and was read-only;
  every temporary constant, Dockerfile mutation, and regression was restored.
  Duplicate launcher/publication/workflow candidates remained covered by
  SP-AUDIT-057, SP-AUDIT-063, and SP-AUDIT-065. Catalog-only pilot uniqueness
  and active-SDK claims were not admitted without an executable reproduction.
- [x] Follow-up Pass CQ used six independent read-only auditors for analyzer/
  ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/CI authority. Focused canonical-
  container probes admitted SP-AUDIT-082 through SP-AUDIT-092: the tag workflow
  omitted Debug qualification, Compose projects shared a mutable image tag,
  strict diagnostics bypassed the MSBuild error channel, cache/output topology
  was asymmetric, unboxing retained caller ownership, selected nested callables
  disappeared, compiler resource-limit evidence was rejected, runtime reasons
  masqueraded as compiler lowering, trailing separators changed fingerprints,
  peer-generated rejected ContractFor attributes escaped, and companion bodies
  were analyzed as implementations. Every temporary regression was removed and
  the auditors made no repository edits. A lock-nested collection-initializer
  hypothesis was rejected because the scanner already returned conservative
  unknown write/throw evidence; worker/protocol pass 2 found only duplicates of
  SP-AUDIT-041, SP-AUDIT-074, and SP-AUDIT-079. Pilot-row uniqueness, exact SDK
  selection, and package-license binding were not admitted without executable
  certifier reproductions.
- [x] Follow-up Pass CR used the same six disjoint read-only areas and admitted
  SP-AUDIT-093 through SP-AUDIT-103 after focused local or exact in-memory
  probes demonstrated duplicate/vacuous pilot qualification, unbound package
  licenses and SBOM purls, incomplete cancellation-catch certification,
  pattern-based semantic-string and static-property meta-policy bypasses,
  mutable compiler integer domains, swappable relational-summary roles,
  mutation-bearing branch re-evaluation, omitted unused specification-pack
  selection, and inactive cache topology validation. Every temporary test and
  disposable package mutation was removed; auditors made no repository edits.
  The pass rejected source-only runtime-snapshot cleanup and bind-mount
  ownership hypotheses without canonical-host executions, a conservative
  lock-nested collection-initializer path, unsupported compound-operator cases,
  and duplicates of the existing proof/core/cache/provenance rows.
- [x] Follow-up Pass CS repeated the six bounded read-only areas and admitted
  SP-AUDIT-104 through SP-AUDIT-113 after exact in-memory or canonical-container
  probes demonstrated executable cancellation arguments, aliased/indexer cache
  writes, interpolated source synthesis, omitted release-certifier TCB leaves,
  left-mutation checked-overflow loss, value-type instance initialization loss,
  base-property dispatch imprecision, compiler-output publication replacement,
  `NAME_MAX` metadata overflow, and noncanonical nested body ordering. Every
  temporary test was removed and all auditors remained read-only. The pass did
  not admit unexecuted runtime-snapshot cleanup, API-spec provenance, or
  65-block hydration hypotheses; those require a discriminating supported-
  surface fixture rather than source inference alone. Adjacent proof, cache,
  workflow, and generated-compilation ideas deduplicated to existing rows.
- [x] Follow-up Pass CT repeated the six disjoint local-correctness areas and
  admitted SP-AUDIT-114 through SP-AUDIT-122 after exact in-memory or canonical-
  container probes demonstrated foreign ContractFor diagnostic locations,
  methodless rejected controls, mutation-bearing initializer re-evaluation,
  missing auto-accessor outcomes, implicit-constructor imprecision,
  contradictory SBOM checksums, self-lowering coverage policy, nontransactional
  cache publication, and unrecoverable publication-set metadata. Every
  temporary test was removed and auditors made no repository edits. A real
  apostrophe-path packaged build passed and was rejected as a hypothesis;
  Linux host ownership changes were not admitted without an executable UID/GID
  fixture. Other candidates deduplicated to existing provenance, proof, cache,
  package, and publication rows.
- [x] Follow-up Pass CU repeated the six read-only partitions and admitted
  SP-AUDIT-123 through SP-AUDIT-131 after canonical-container or exact local-
  certifier evidence demonstrated ignored lowered cache caps, multitarget SARIF
  overlap, archive-tooling Git dependence, lossy UTF-16 artifacts, peer-
  generated target drift, partial-property body mismatch, unbound publication
  plans, mismatched SBOM subjects, and unchecked public package metadata. All
  temporary probes were removed and the auditors edited no files. A net9
  `System.Threading.Lock` no-throw probe already abstained and was rejected;
  an apostrophe-path packaged build also remained green. Remaining aggregation,
  resource, atomic-file, diagnostic, and schema ideas deduplicated to existing
  rows or lacked an executable supported-surface discriminator.
- [x] Follow-up Pass CV repeated six bounded read-only partitions and admitted
  SP-AUDIT-132 through SP-AUDIT-137. Compiler-location probes demonstrated the
  zero/one-based model split; exact release command graphs exposed three
  disconnected or unbound authorities; canonical MSBuild evaluation reproduced
  stale private analyzer/collector paths; and a focused packaged build reproduced
  whitespace-only SARIF usage failure. A disposable Git directory-to-file
  snapshot probe remained green and was rejected. Analyzer, Effects, worker/SMT,
  and remaining host hypotheses were clean, fail-closed, or duplicates.
- [x] Follow-up Pass CW expanded to 20 simultaneous, disjoint, read-only
  partitions while retaining one coordinator as the only register writer. It
  admitted SP-AUDIT-138 through SP-AUDIT-165 from executable analyzer/compiler/
  effect probes and exact local protocol, container, acceptance, TCB, SBOM, CI,
  and release-certifier predicates. Focused canonical-container tests reproduced
  the captured primary-constructor receiver-read omission; source auditors had
  already executed the conditional-call and lowered-graph discriminators. The
  coordinator rejected the disproved snapshot-transition report, duplicated
  mutation/cache/host issues, fail-closed precision losses, unsupported-host
  requests, and an unexecuted ContractFor generic-wrapper suspicion. All
  temporary probes were removed and no auditor edited repository files.
- [x] Follow-up Pass CX repeated the same 20 disjoint read-only partitions and
  admitted SP-AUDIT-166 through SP-AUDIT-187. Exact compiler round trips
  demonstrated missing return values and reordered summary arguments; the
  ContractFor owner probe reproduced generic-layer misalignment; the pinned Z3
  probe measured uncharged model extraction; and local acceptance, build, TCB,
  mutation, protocol, publication, SBOM, CI, and documentation predicates
  exposed the remaining certifier and lifecycle defects. Effects, analyzer,
  package, compiler-provenance, and worker challenge passes were clean or
  deduplicated. A persistent-volume UID-change hypothesis and a dormant receipt
  helper were rejected without supported executable callers. Auditors made no
  repository edits and no temporary probe remains.
- [x] Follow-up Pass CY again used 20 disjoint, read-only partitions and admitted
  SP-AUDIT-188 through SP-AUDIT-207. Offline ContractFor probes reproduced both
  `ref readonly` failures; exact local predicates demonstrated Git-quoted TCB
  loss, mutation exception-text misclassification, release scalar coercion,
  offline publication-state drift, and package runtime-asset leakage. Source
  reviews identified stale outer-gate receipts, generated-partial suppression,
  syntax-tree replay rebinding, module-reference/assumption/certainty gaps,
  publication marker poisoning, canceled staging residue, and omitted module
  initialization. CI, BuildTasks, container, worker, and SMT challenge passes
  were otherwise clean or duplicates. One benign compiler lane was blocked by
  an automated classifier and reassigned; no security work was attempted, no
  auditor edited repository files, and no temporary probe remains.
- [x] Follow-up Pass CZ repeated 20 disjoint read-only partitions and admitted
  SP-AUDIT-208 through SP-AUDIT-222. A local Roslyn probe demonstrated honest
  named-argument misordering; offline compiler checks established function-
  pointer convention equivalence; exact local predicates reproduced case-folded
  mutation ledgers, SBOM shape/vocabulary drift, and self-invalidating plan
  output. Analyzer, provenance, protocol, worker, docs, and summary/spec reviews
  exposed the remaining parenthesis, branch-shape, empty-tree, claim/coverage,
  span, assumption-use, and concrete-null mismatches. Acceptance, BuildTasks, CI,
  container, coverage, package, Effects, Linux host, and SMT passes were clean or
  duplicates. No auditor edited repository files and no temporary probe remains.
- [x] Follow-up Pass DA repeated 20 disjoint read-only partitions and admitted
  SP-AUDIT-223 through SP-AUDIT-236. Exact local predicates exposed tip-only
  coverage fallback, non-self-contained release evidence, permissive SBOM and
  publication-plan validation, and unbound compiler/protocol identities. The
  pinned Z3 probe reproduced an ill-formed UTF-16 false proof; ContractFor,
  analyzer, specification, worker, and documentation reviews exposed the signed-
  zero, implicit-base, rejected-metadata, concrete-type, trust-use, and public-
  semantics gaps. Acceptance, BuildTasks, CI, container, compiler lowering,
  Effects, Linux host, mutation, and package passes were otherwise clean or
  duplicates. No auditor edited repository files and no temporary probe remains.
- [x] Follow-up Pass DB repeated 20 disjoint read-only partitions and admitted
  SP-AUDIT-237 through SP-AUDIT-241. Local producer/validator comparisons exposed
  unbound run-failure classification, missing summary dependency provenance, and
  empty retained release archives; Effects and ContractFor reviews isolated the
  definite-null throw and constructed function-pointer specialization failures.
  SP-AUDIT-235 was broadened to the callable and assumption ID families sharing
  its root cause. Acceptance, analyzer, BuildTasks, CI, compiler lowering and
  provenance, container, coverage, Linux host, mutation, package, public docs,
  release planning, SMT, and worker challenge passes were otherwise clean or
  duplicates. No auditor edited repository files and no temporary probe remains.

- [x] Register normalization reviewed every active row across six read-only
  subsystem partitions. Same-authority reproductions were consolidated under
  stable survivor IDs, three unsupported-language cases were removed from the
  preview backlog, and the remaining root causes were ranked with the P0-P3
  rubric above. Merged IDs remain explicit aliases so historical audit evidence
  and future regression names remain traceable. Only the coordinator edited this
  file; the reviewers made no repository changes and performed no security work.

- [x] Post-normalization Pass DD challenged the consolidated register with six
  disjoint, bounded, read-only subsystem reviews. Analyzer/ContractFor,
  Effects, compiler provenance, worker/SMT/protocol, and Linux host/build
  candidates were clean, unsupported, or already covered by the surviving root
  causes. The release-evidence review admitted SP-AUDIT-242 because pilot output
  fabricates a zero false-positive review count without accepting any review
  evidence. Only the coordinator edited this file; no tests, network actions,
  security work, or temporary probes were used.

- [x] Pass DE used six disjoint, bounded, read-only reviewers and admitted
  SP-AUDIT-243 after the worker/protocol review traced a false effect proof from
  compiler evidence through hydration and result assembly. Two independent DF
  reviewers then confirmed that no compiler-derived check rejects the ordinary
  digest-refreshed verdict. Analyzer/ContractFor, Effects, compiler provenance,
  Linux host/build, and release/CI findings were clean or duplicates.
- [x] Pass DF added ten disjoint, bounded, read-only reviewers and admitted
  SP-AUDIT-244 after coverage and acceptance reviewers independently confirmed
  the dirty-tree merge-base loss. A proposed oblivious-generic ContractFor row
  was rejected because the public contract requires exact compiler nullability.
  The other acceptance, BuildTasks, CI, lowering, container, coverage, mutation,
  package, ContractFor, and SMT partitions were clean or duplicates. Only the
  coordinator edited this register; no network, credential, permission,
  cybersecurity, adversarial, or hacking work was performed.

## Previously closed bounded audit

- [x] Pass A: audit analyzer/generator/collector parity, final-compilation
  ContractFor behavior, compiler-artifact schema 12 provenance, and mutation
  discrimination for the consolidated semantic authority. The bounded review
  accepted no defect; 99 analyzer/core/collector, 51 ContractFor, and 32
  schema-12 worker cases passed in the canonical container.
- [x] Pass B: audit Linux host containment/publication, cancellation and exact
  Z3 loading, the three-package graph, packaged consumers, and release-evidence
  commit binding. The bounded review accepted no defect; 21 host/worker,
  16 package/release-authority, and 2 exact native-boundary cases passed in the
  canonical container.
- [x] Close every accepted supported-surface reproduction with a focused
  regression and, for trusted-boundary behavior, a discriminating mutation.
  Neither bounded pass admitted a supported-surface or certifier defect, so no
  production fix, new regression, wire change, or mutation entry was required.

Only an executable failure in the documented container-supported surface, or
a demonstrated certifier defect that can admit invalid release evidence, may
add a row. The audit stops after these two passes; unsupported roadmap features
and speculative hostile-host races do not reopen the preview.

## Closed architecture and supported behavior

- [x] The canonical Linux amd64 container is the only full-verifier host.
  Native Windows/Visual Studio execution and Windows runtime primitives are
  removed from the supported verifier.
- [x] `SharpProof.Analyzer` and `SharpProof.ContractForGenerator` are thin
  entry assemblies over one `SharpProof.Analyzer.Core` implementation; the
  package exposes exactly one analyzer, one generator, and one collector.
- [x] Generated companions are validated on the final compilation without
  duplicating handwritten generator diagnostics.
- [x] Compiler artifact schema 12 binds canonical module order, image sizes,
  MVIDs, hashes, metadata identity, warning policy, and realized diagnostics;
  per-module, closure-byte, and module-count limits are enforced.
- [x] Publication locks and ownership markers cover request, result, compiler
  manifest, and optional SARIF paths as one canonical set; partial overlap,
  unowned destination files, and recognized network filesystems fail closed.
  SP-AUDIT-006 records the remaining derived-metadata symlink hole.
- [x] The launcher owns one direct Linux worker child with a bounded startup
  barrier, parent-death signal, cancellation deadline, and exact packaged Z3
  resolver.
- [x] The release graph is exactly `SharpProof.Attributes`, `SharpProof`, and
  `SharpProof.Verifier`; interface, schema, generated-output, TCB, coverage,
  mutation-catalog, documentation, and package inventories are drift-gated.
- [x] CI and local repository commands execute in the pinned container; Docker
  owns CPU and memory isolation.

## External qualification and publication

After the final source commit, the release procedure must generate evidence at
that exact commit: Debug and Release gates, coverage, all 136 trusted
mutations, local packages and isolated consumers, five pilots, SBOM/package
validation, and publication plan-only. Any subsequent tracked change
invalidates that evidence and requires regeneration.

NuGet credentials, protected release tags, private/public environments, and
external publication are owner-controlled release operations. They are not
locally controlled code debt and require separate authorization. This audit
does not authenticate, publish, promote, or tag.

## Explicit preview boundaries

Native host execution, ARM64 verifier containers, Rider integration,
shared/network publication, hostile concurrent host filesystem mutation,
loops, mutable-heap reasoning, virtual dispatch, and general source-callee
verification remain explicit roadmap items rather than release blockers.
