# SharpProof semantics

This document defines how to interpret the current verifier's results. The implementation authorities are the typed IR, compiler artifact, worker protocol validators, native VC checks, and replay code. The portable analyzer provides a separate, conservative analysis surface.

## Outcomes and evidence

| Claim outcome | Meaning |
| --- | --- |
| `Proven` | The modeled obligation holds under its recorded assumptions and entry domain |
| `Refuted` | A violating execution or effect site has been validated by concrete replay |
| `Unknown` | The claim could not be established or refuted within the supported model and limits |

An SMT satisfiable result alone is not a refutation. A solver failure, unsupported operation, invalid result, or failed replay cannot be promoted to success. Proofs are about the modeled executions and claim, not arbitrary program behavior.

Worker run status, callable coverage, and claim outcome are distinct. A complete run may contain Unknown claims. Every selected callable and claim must be accounted for by the manifest and response; missing results are protocol failures. Diagnostic silence, suppression, and a successful ordinary C# build are not proof evidence.

## Contracts and trust

Direct `Requires`, `Ensures`, and `Assume` calls form a contiguous prologue. They are compiler-bound to the verified Attributes API. A source lookalike or an unverified assembly contributes no contract evidence.

`Requires` restricts a callee's entry domain and creates obligations at modeled calls only; [Calls and loops](#calls-and-loops) lists the calls that stay unchecked. `Ensures` applies on normal return. `Result<T>` names that return value and `Old<T>` names entry state. `Assume` adds explicit user evidence; it is not independently proven.

Direct and companion clauses are alternative effective sources. Valid direct clauses take precedence for the member; a valid companion supplies clauses when there are no valid direct clauses. Invalid or misplaced clauses remain usage errors. Compiler-elided clause invocations, including their argument evaluation, are omitted from executable body semantics.

The `SHARPPROOF_CONTRACTS` symbol is unsupported. SharpProof supplies no runtime enforcement. See [public API](docs/public-api.md) for exact attribute and intrinsic rules.

Closed parameter and return attributes supply validated non-null, positive, or inclusive-range conditions. Invalid targets, unsupported types, and invalid values do not create facts.

Trusted boundaries and resolved API specifications are assumptions with provenance. `SharpProofTrusted` alone does not invent a complete effect summary. External complete effect declarations require the reviewed precondition-free certification accepted by the metadata reader. Reporting suppression changes no evidence. Assumption policy governs declared user assumptions and trusted boundaries; it does not reject every recorded precondition or API specification.

## Entry feasibility and vacuity

The worker checks entry feasibility separately from a body claim. Contradictory preconditions are recorded as vacuous entry evidence. Body facts, arbitrary user assumptions, and API results must not be reported as contradictory preconditions. Proofs that depend on user assumptions remain conditional on that declared evidence.

Postconditions constrain normal returns. A body with no modeled normal return may satisfy a postcondition vacuously; the response records `NoModeledNormalReturn`. This does not establish termination or exception freedom. Effect claims have their own obligations.

Input domains include modeled CLR constraints. An instance receiver is non-null. Closed sealed reference roots of incompatible runtime types cannot denote the same non-null object. Nullable inputs and compatible aliases remain possible.

## Typed execution model

Roslyn operations and CFG are lowered to typed Total IR. Integral operations carry their exact admitted width, signedness, conversion behavior, and checked/unchecked semantics. Normal and exceptional paths remain distinct. Unsupported numeric and expression forms abstain rather than borrowing an unrelated representation.

Built-in C# integer shifts (`<<`, `>>`, `>>>` and their compound forms) follow the CLR: the int count uses only its low five (32-bit) or six (64-bit) bits, `>>` is arithmetic for signed values, `>>>` is logical, and narrow operands are promoted to int. A C# compound shift on narrow storage narrows the result under the enclosing checked context. VB compound shifts on 8-bit or 16-bit storage instead mask the count by 7 or 15 and wrap the storage result regardless of overflow checking. See [compiled VB and C# controls](src/SharpProof.Frontend.Tests/VisualBasicNarrowCompoundShiftTests.cs). Admitted metadata IL shifts preserve stack width and distinguish arithmetic `shr` from logical `shr.un`; lifted and user-defined shifts abstain. See [metadata shift tests](src/SharpProof.Worker.Tests/MetadataIlShiftSemanticsTests.cs).

Built-in integer `&`, `|`, `^` and `~` (and their compound forms) apply to operands after C# numeric promotion, so narrow operands become int and mixed signed/unsigned operands take the common promoted type; they never overflow. Bool `&`, `|` and `^` evaluate both operands and are logical and, or and inequality. IL `and`, `or`, `xor` and `not` use the same operators. Lifted, enum and user-defined bitwise operators abstain.

Passive scalar SSA constraints preserve instruction ownership, predecessor edge guards, and source locations. Branch conditions restrict the corresponding paths. Multiple returns and exceptional exits are joined under their reach conditions.

Heap reasoning tracks modeled entry contents and ordered stores under path reach. A matching latest store determines a later read. Calls or writes outside the precise store model may forget contents, leaving approximated values. An element write does not implicitly change a field. Exact instance-field value reasoning excludes fields declared on explicit-layout types, including readonly fields.

Approximation values can support sound over-approximate proof obligations only within the checks implemented by the worker. They cannot be treated as independently executable concrete counterexamples.

## Calls and loops

Source and admissible metadata calls are resolved through compiler evidence and lowered or modeled within the available boundary. Unknown targets and recursive or opaque call paths carry conservative effects and values. An external declaration is not a source-body proof.

Natural loops use cut encodings and invariant candidates. Candidate invariants must pass establishment and preservation checks before use in a proof. Bounded witness search is separate from the proof encoding; finite unrolling cannot prove an arbitrary loop or termination. Unsupported CFG shapes or exhausted construction limits produce Unknown.

Call preconditions and body checkpoints are obligations, not unchecked assumptions. Each clause of a modeled call's `Requires`, including closed parameter attributes and companion clauses, is a `CallPrecondition` claim of the caller. The caller must establish the clause from its own entry domain on every path that reaches the call, so a caller with no `Requires` of its own must establish it for every input. A body that declares nothing is selected automatically when it makes a modeled call. A `Refuted` claim fails the build under every policy, and an `Unknown` one fails `require-proven`, except that a claim that ran out of time does not (see below). The callee's `Requires` is not assumed after the call. A call inside an inlined callee belongs to that callee when it is verified on its own, so one violation is reported once.

A modeled call is an invocation, construction or accessor call, from a body that lowers exactly, to one of:

- A non-virtual source callee in the same compilation with by-value parameters. Every clause of its `Requires` is checked.
- A callee in another assembly that is not dispatched (not virtual, abstract, an override or an interface member) and not generic, whose arguments are all by value and none is a `params` array. A non-lifted user-defined binary operator declared in another assembly is a call to its operator method with the two operands as arguments. Three kinds of clause are checked, in this order. A companion that the calling project declares for the callee with `[ContractFor]` contributes its `Requires`; its claims are marked as declared by the calling project. The callee's closed parameter attributes (`[NonNull]`, `[Positive]`, `[InRange]`) are read from the referenced image whose hash the compiler artifact records, so a reference assembly qualifies. When the callee's project is a project reference built in the same build, the `Requires` that project exported for the callee are checked too; see [contract exports](#contract-exports). A call's clauses are checked together or not at all.

A metadata clause has no source location. Its claim names the assembly, the member, the parameter and the attribute, as in `[InRange(0, 9)] on parameter 0 in Library`, and a violation is reported at the call. The attribute is the contract the referenced image declares. A different implementation loaded at run time is outside this model.

### Contract exports

A verified project writes a contract export for each target framework: `obj/<configuration>/<framework>/SharpProof/contracts-export.json`. It lists each public or internal method, constructor and accessor with a source `Contract.Requires`. Each entry holds the callee's documentation ID, its clauses as portable IR with their source locations, and a digest of the declaration and contract source. Closed attributes are not exported; they travel in the binary. A callable whose clauses read anything but its parameters, such as a field or `this`, is exported as incomplete, without clauses.

An MSBuild step inside each compilation stamps the export with the module version ID and SHA-256 of the reference and implementation assemblies that compilation produced. A compilation that does not complete the compiler capture (verification off, or the collector failed) removes the previous export instead. A referencing project passes the export of each project reference, for the framework it references, to its compiler as an additional file. It accepts the export only when the stamp names the exact image it resolved for that assembly, and the compiler artifact records the export's hash and stamp so the worker rejects a row that cites anything else.

An exported clause's claim cites the clause's file and line in the exporting project and names that project, as in `at Library.cs(4,9), exported by Library`, and a violation is reported at the call. The clause is an obligation of the caller; it is never assumed after the call.

Contract exports cover only:

- Project references built in the same build. A package, a plain assembly reference or a project built separately has no export.
- The framework the reference resolves to. Another framework's export is not consulted.
- Calls that are not dispatched.

A missing export, an export that cannot be read, or one whose stamp does not match is never checked. When the export can be read, its callees' calls are counted as unchecked. A call to an incomplete callable is counted as unchecked as well. The stamp binds the export to the reference image, which does not change when only a method body or a `Requires` changes. The export cannot go stale that way, because every compilation of the exporting project restamps or removes it.

Nothing else is checked. A passing build says nothing about these calls:

- Calls in a body that does not lower exactly or is abstracted.
- Calls in lambdas, and in local functions that a selected body does not inline. Lambdas and local functions are not selected as roots.
- Calls in top-level statements, and in property accessors and operators unless a selected caller lowers them inline.
- Calls to virtual, interface and abstract callees. A `Requires` on an interface or base method is therefore not checked at its dispatched calls.
- Calls to async and extern callees, and to callees with `ref`, `in` or `out` parameters.
- Calls to a callee whose type has a non-constant static initializer. A body with a declared claim does inline such a callee when the callee is declared in the caller's own type; a body selected only for its calls does not.
- Calls in a body selected only for its calls, once the compiler artifact reaches 24 MiB. Such bodies are left out, last callable ID first, so the artifact stays under its 32 MiB limit. A body with a declared claim is never left out, so a project its declared claims alone push past the limit still fails with SP0049.
- Calls whose claim ran out of time, with reason `MethodTimeout` or `ProjectTimeout`. The claim reports `Unknown`, but it does not fail `require-proven`: its caller's SP0047 is a warning there, as under `warn-on-unknown`, because machine load alone must not decide a strict build. Any other `Unknown` reason still fails `require-proven`, and so does a declared claim that runs out of time, including one in the same caller.
- A `Contract.Requires` in another assembly's source without an accepted contract export. This is the main public-API case: `Contract.Requires` is compiled out of the library's binary, so a package's callers cannot see the clause, let alone check it. Only the library's closed parameter attributes, or a companion the calling project declares, are checked.
- Calls into another assembly that are dispatched or generic, or take a `ref`, `in`, `out` or `params` argument; calls whose attributes cannot be read from an authenticated image or paired with the symbol's; calls to a callee whose companion does not resolve cleanly; calls to a callee whose export is incomplete or whose exported clauses cannot be rebuilt over its parameters; and calls with an argument whose representation the verifier cannot convert to its parameter's.

The build summary, the tool report and SARIF report the number of source calls whose callee declares a precondition the compiler can see and that no claim checks. The manifest's `UncheckedCallSites` holds the part known at compile time, and the reports add the calls whose claims ran out of time. The count covers every case above except a `Contract.Requires` in another assembly's source with no readable contract export, which the compiler cannot see. It also counts calls in dead code. Zero therefore means no visible call was missed; it does not mean every precondition in the program was checked. The advisory SP0027 can still report a definite violation at an unchecked call; its silence is not proof.

General virtual dispatch, recursion, async execution, and arbitrary metadata bodies are not blanket supported just because a related form can be lowered. See [coverage and limits](docs/coverage-and-limits.md).

The [language reference](docs/language-reference.md) distinguishes admitted operations, modeled call boundaries, and opaque or unsupported cases, with implementation and behavioral test references. A supported operator does not imply that every operand type, lifted form, or containing callable is admitted.

## Effects

Native effect claims are decided over the same Total program, through the exception and effect-site verifiers. They include `DoesNotThrow`, `AllowedExceptions`, `ZeroAllocations`, `EnforcePure`, `AllowedCapabilities`, and `EffectContract`.
Exception proofs also require positively admitted entry initialization. The compiler emits a native exception constraint only when the target type and source module initialization have no modeled synchronous fault. Empty initializers, built-in constants, simple static-field writes of admitted values, and the audited parameterless `System.Object` construction can satisfy this check. Bound conversions and initialization dependencies are checked; an initializer that calls unproved code, invokes a property setter or user conversion, or has an unproved dependency yields Unknown. An unused base or enclosing type is not an initialization dependency merely because of inheritance or nesting.

This exception check is separate from entry purity and allocation checks: a no-fault constant write to a mutable static still changes state. Existing opaque source-callee boundaries remain conservative. A non-null receiver does not add the effects of constructing that receiver to its instance method. Ordinary allocation follows the existing modeled-exception policy; arbitrary initializer programs and precise initialization-exception summaries remain unsupported. Missing native exception constraints cannot supply a proof, and compiler artifact schema 31 rejects the older admission meaning.

Purity, allocation, capability and effect-contract claims also require entry initialization with no observable effect. The CLR runs a type initializer at a time the program does not control: before the first access to a static member, or, for a `beforefieldinit` type, at any earlier point. So the initializer of the callable's declaring type, and of every enclosing type, counts as part of entry. No code can see a type's statics before its initializer has run, except code the initializer itself reaches through a cycle. An initializer that only fills in the type's own readonly statics, from values it makes itself, therefore changes nothing a body can observe. SharpProof admits two classes of such initializers:

- **Constants.** Static readonly fields and get-only static auto-properties are initialized from compile-time constants: integers, `bool`, `char`, floating-point numbers, enums, strings or `null`. A static constructor may be empty or only store such constants into those members. This class runs no other code and allocates nothing, so every effect claim accepts it.
- **Fresh values.** As above, but a value may also be one of these:
  - a one-dimensional array of constant length up to 65,536, with admitted elements, or an array collection expression;
  - a boxed value;
  - a lambda, or a method group of a static method;
  - `new object()`;
  - a `new` of a source class whose base is `object`, or of a source struct, through a constructor that only stores its own parameters or admitted values into the new instance's own fields and auto-properties. The class or struct has no object initializer, no chained `this(...)` constructor and no `~T()` method, and its type initializer is itself admitted.
  - a read of a readonly static field that is neither volatile nor `[ThreadStatic]`, of the type itself or of another source type whose own initializer is admitted. Such a field holds only what its initializer stored, so the read is not an ambient read, and it adds nothing beyond that initializer's class. A read of a mutable static is an ambient read and stays an entry effect.

  This class writes no state a body can observe, calls no other code and cannot throw except by running out of memory. The lambda and method-group delegate caches the compiler generates are not observable state. Fresh values do allocate, and the first call that runs the initializer pays for that. So `EnforcePure` and `AllowedCapabilities` accept this class, while `ZeroAllocations`, `EffectContract` and resource bounds do not. Compiler artifact schema 61 records the distinction as `EntryConstructsFreshValues`.

Everything else is an entry effect, and the claim stays `Unknown` with SP0047 `Advisory:EntryInitialization:TypeInitializer`. That covers any call (including collection initializers and metadata constructors), a read of a static of a metadata type or of a type whose initializer is not admitted, a throw, a cross-type cycle, a static constructor with control flow, locals or other statements, and a write to a mutable, volatile or `[ThreadStatic]` static or to a field-like event. A source callee's type initializer is admitted only by the stricter callee rule, so `CalleeTypeInitializer` is unchanged. The exception is a callee declared in the caller's own type: its body runs under the initializer the caller already answers for, exactly as if it were written in the caller, so the entry rule above covers it and the callee is inlined. A callee in a nested, enclosing or unrelated type keeps the stricter rule, because that type's initializer can run in the middle of the body. Constructed generic types are distinct types here. A module initializer is always an entry effect.

Effects distinguish receiver, argument, captured, static, and ambient reads/writes; allocation; exceptions; synchronization; nondeterminism; native code; and reflection. Capability flags are a separate closed set. Each flag is independent: permitting `Throws` does not permit `Allocates`.

Observable purity excludes modeled externally observable effects and relevant ambient reads. A local operation is not automatically externally observable; classification depends on the modeled region and escape behavior. Calls with incomplete information retain conservative may-effects.

Assumption: the ambient culture has no user-defined callbacks. When the CLR itself raises a fault, such as an out-of-range string or array index or an integer division by zero, it builds the exception message through a resource lookup that reads the ambient `CultureInfo`. A user-defined culture subtype can therefore run code at that point, as [ExceptionalFrameworkCultureTests](src/SharpProof.Specs.Tests/ExceptionalFrameworkCultureTests.cs) records. The verifier treats these runtime faults as callback-free, so a program that installs such a culture is outside this model. Faults behind cataloged calls, currently `Math.Abs(int)` and `Nullable<T>.Value`, stay conservative; see the [API specification catalog](docs/api-spec-catalog.md).

A body proof carries `CompleteMayEffectSummary`. A trusted external declaration carries `TrustedCompleteBoundary`, and remains an assumption. A replayed violation carries `DefiniteViolation` with a source site. A contradictory entry carries `VacuousEntry`. These certainties must agree with outcome, reason, witness, and vacuity; protocol validation rejects inconsistent tuples.

## Counterexample replay

Replay validates a candidate against the owned program, entry values, path, and claim. Solver-chosen values for unconstrained results or approximated reads are not arbitrary executable inputs. Undefined postconditions, unsupported replay operations, and missing source-site evidence cannot produce a definite refutation.

Heap witness decoding also checks the closed compiler type evidence. Distinct non-null references from incompatible sealed runtime types cannot be assigned one object token. Impossible aliases downgrade a candidate to Unknown; this witness filter adds no SMT premise. Compatible aliases and null remain allowed. Unused observations can still make a witness conservatively unreplayable, and reference-valued array accesses remain a current admission limit.

A call-precondition refutation is a concrete entry that reaches the call with the clause false and with no approximated value. Replay starts after entry initialization, so a type or module initializer that is not proved free of observable effects leaves the claim Unknown.

Effect witnesses identify the violating allocation, throw, write, or synchronization site. An inlined metadata site without a source position is insufficient for an emitted definite violation.

## Resources, caching, and failure

Native queries and methods have deterministic resource limits as well as wall-clock limits. Cancellation is cooperative within the worker and enforced by the launcher boundary. Completed solver work remains charged even when cancellation arrives later.

A response cache binds compiler artifact bytes, worker and specification identity, protocol, manifest, and budgets. Only validated complete eligible responses are reused. Stable semantic Unknown results may be cached; transient failures are excluded. Corruption or unavailable cache storage becomes a miss, never a changed semantic verdict.

Protocol validation and atomic publication are infrastructure boundaries. A malformed request, unowned output, or backend failure is a run failure or typed abstention, not an empty successful proof set. See [Unknown reasons](docs/unknown-reasons.md), [SMT lifecycle](docs/smt-lifecycle.md), and [preview support](docs/preview-support.md).

## Verification references

- [Architecture](docs/architecture.md): current component ownership.
- [Reading verification evidence](docs/verification-results.md): manifests, claim results, replay models, assumptions, vacuity, SARIF, and cache interpretation.
- [Analysis limits](docs/analysis-limits.md): current defaults.
- [Coverage and limits](docs/coverage-and-limits.md): supported examples and remaining boundaries.
- [Golden fixtures](tests/golden/README.md) and [corpus gates](tests/SharpProof.Gates/README.md): executable regression evidence.
