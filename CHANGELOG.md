# Changelog

The first public version will be `1.0.0-preview.2`. No earlier version was
published, so the section below describes that release as a whole instead of
listing changes against an earlier one. Development history is in the Git log.

## Unreleased

### Packages and requirements

- One reference is the whole setup. `SharpProof` brings the contract API
  (`SharpProof.Attributes`), the analyzers, the MSBuild integration, the worker
  and the pinned Z3 solver through `SharpProof.Verifier`. The package graph is
  `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`, all at one
  version.
- Verification is strict by default: a build fails when a selected claim is
  refuted, is not proven, or depends on a declared assumption. There is one
  exception: a call-precondition claim that only runs out of time does not
  fail the build. It is counted as an unchecked call, so a passing strict
  build does not prove it. The `advisory` and `off` profiles are explicit
  opt-outs.
- Verification stays with the project that references the package. A project
  that references a verified library, or a package that depends on SharpProof,
  gets the contract API but does not inherit strict verification, the worker
  run or the SDK requirement.
- Full verification needs .NET SDK 10.0.100 or newer and runs on Windows, macOS
  and glibc Linux, each on x64 and ARM64. Windows solver payloads link the C++
  runtime statically, so no Visual C++ redistributable is needed. musl Linux is
  not supported. Analyzer-only use needs SDK 9.0.300 or newer. The application
  can target an older framework; see
  [the support matrix](docs/preview-support.md).
- Setup problems are reported as `SPB` errors that name the value or host that
  was detected; [build errors](docs/build-errors.md) lists them.
- The optional developer packages are `SharpProof.Tool` (the `dotnet sharpproof`
  command), `SharpProof.Templates` (`dotnet new sharpproof` and
  `sharpproof-workspace`) and `SharpProof.CodeFixes` (editor actions).
- The release workflow smoke-tests the packed packages on Windows and macOS and
  attests package provenance before publishing. The consumer container image
  that the templates pin is built from the same tooling image as the packages.

### Contract API

- Contracts are ordinary C# calls in a method's prologue: `Contract.Requires`,
  `Ensures`, `Assume`, `Result`, `Old` and `ValueAtReturn`. They are compiled
  out of the binary and add no runtime checks.
- `Contract.ValueAtReturn(out x)` names an out parameter's final value in a
  postcondition. A method with one `bool`, character, integer or enum out
  parameter has its postconditions verified when its body touches no memory
  and makes no call; effect claims on it stay Unknown.
- Closed attributes state common conditions: `[NonNull]`, `[Positive]` and
  `[InRange]` on parameters and returns.
- Effect contracts: `[EnforcePure]`, `[EffectContract]`, `[ZeroAllocations]`,
  `[DoesNotThrow]`, `[AllowedExceptions]` and `[AllowedCapabilities]`.
- `[ResourceUpperBound]` declares upper bounds on loop-body entries, array
  allocations, allocated elements and allocated bytes.
- `[ContractFor]` companions attach contracts to members you do not own.
  A companion member can also declare `[EnforcePure]`, `[ZeroAllocations]`,
  `[AllowedCapabilities]`, `[DoesNotThrow]` and `[AllowedExceptions]` for its
  target; `[EffectContract]` and `[ResourceUpperBound]` there are an error
  (SPCF0011). `[SharpProofTrusted]` and `[SharpProofSuppress]` record a reviewed
  assumption or a reporting choice; neither is proof.
- Attribute constructors and properties reject unknown flag bits, and
  exception-type arrays are copied on the way in and out.

### Verification

- Each claim ends as `Proven`, `Refuted` with a replayable counterexample, or
  `Unknown` with a typed reason. Code outside the modeled subset is reported
  as incomplete, never as proven.
- Call preconditions are checked at the call site, as claims of the caller:
  - calls within a project check the callee's `Requires`;
  - calls into a project reference built in the same build check the clauses
    that project exports, when they read only parameters;
  - calls into other assemblies check the callee's closed parameter attributes
    and the `Requires` of a `[ContractFor]` companion the caller declares.

  A library's own `Contract.Requires` is compiled out of its binary and is not
  checked across a package boundary. Calls that cannot be checked are counted
  in the build summary, and nothing is assumed after them.
- Purity, effect, allocation, capability and exception claims account for type
  and module initializers. For purity and capability claims, a type
  initializer that only builds fresh values counts as effect-free. Allocation,
  effect-contract and resource-bound claims still count what such an
  initializer allocates and stay `Unknown`. Any other initializer leaves the
  claim `Unknown` with a reason that names it.
- Enum values are analyzed as their underlying integer: comparisons, `&`,
  `|`, `^`, `~` and conversions, including checked conversions that
  overflow. Addition, subtraction, `++` and `--` are analyzed for enums
  over `int`, `uint`, `long` and `ulong`. Arithmetic on narrower enums and
  enum boxing are still `Unknown`.
- Nullable value types over a `bool`, an integer, an enum or a modeled
  readonly struct are analyzed: `HasValue`, `Value`, `GetValueOrDefault`,
  `??`, null tests, lifted comparisons and integer arithmetic, widening
  conversions and patterns such as `n is int v`, along with `++`, `--`,
  compound assignment, narrowing conversions, the three-valued `bool?` `&`
  and `|`, boxing, `Equals` against `null` or a boxed value, and
  `GetHashCode` for `int`, `uint` and `bool`. Reading `Value` when there is
  none, and `ToString` of a non-`bool` value, stay unknown calls, so they are
  not proven pure.
- Objects carry runtime types. Allocations and boxes in the analyzed code
  record their exact type, so `is T`, `as T`, type patterns such as
  `obj is Foo f`, unboxing, downcasts, enum boxing and `Equals(object)` are
  analyzed for them. A value whose allocation the code does not show, such
  as a parameter, keeps an unknown runtime type and is never refuted from a
  guessed one.
- A call to a virtual member is analyzed like a non-virtual one when the
  code fixes its target: a `base.` call, a sealed override or a member of a
  sealed type, or a receiver whose static type is sealed.
- `[EnforcePure]` can prove a method that calls itself, when it has no
  `Requires`: the purity check assumes the recursive call is pure, by
  induction on recursion depth. Other checks still treat the call as unknown.
- Calls to source methods with `ref` and `out` parameters are analyzed when
  each such argument is a local or parameter of the caller, passed once, and
  the call is outside any `try` block of the caller. Before, such calls kept
  the whole caller `Unknown`.
- Values of source `readonly struct` types are analyzed: construction,
  including target-typed `new(...)` and constructors chained with `: this(...)`,
  `default`, field and property reads, instance members and user-defined
  operators. Purity, capability and postcondition claims over them can be
  proved. A body that builds such a value keeps allocation, effect-contract
  and resource-bound claims `Unknown`, because the model allocates it and the
  CLR does not.
- A class or struct constructor chained with `: this(...)` is inlined: the
  other constructor runs on the same object, with its field initializers,
  then the rest of this one. Instance field initializers no longer count as
  effects, so `new` of a class with them can be proved pure.
- A store of a reference into an array the method allocated and then
  returned, such as `var a = new string[1]; a[0] = "x"; return a;`, was
  refuted as a write to the caller's state. It is now `Unknown`; a store into
  an array the caller passed in is still refuted.
- A constructor's assignments to its own readonly fields and get-only
  auto-properties are modeled, so a value read back from an object built by such
  a constructor is known. Before, immutable objects built this way could not be
  proved pure, and postconditions about their fields stayed `Unknown`.
- A method that calls another method of its own type is analyzed through that
  call even when the type has a static initializer. The initializer is
  accounted for once, at the calling method's entry.
- The default API table specifies common framework members, including calendar
  and time-of-day getters and comparisons on `DateTime`, `DateOnly`, `TimeOnly`
  and `TimeSpan`.
- A type initializer that is a closed computation no longer blocks its
  callers. SharpProof runs such an initializer at analysis time: constants,
  validating constructors and helpers, loops, exact integer arithmetic and
  `BigInteger` bounds, with floating-point and `decimal` results only stored.
  When it finishes, it never throws and has no effect a body can observe, so
  calls and constructions in its type are inlined and the type's own members
  are admitted at entry. One that allocates is admitted only by claims that
  ignore allocation. This opens NodaTime's `Duration`, `Instant`, `Offset` and
  `LocalTime` arithmetic.
- A product of a widened input, such as `days * TicksPerDay` with an `int`
  `days`, is checked as the same term a postcondition writes for it. A widened
  input plus or minus a constant, such as `(days + 1) * TicksPerDay`, is
  checked as the widened input plus that constant, with the wrap of the
  narrow sum accounted for. A contract that restates such a product no longer
  runs out of solver budget.
- A refuted contract is reported as SP0051 at the clause or call and fails the
  build under every verifier policy. SP0047 reports incomplete analysis and
  SP0048 a declared assumption; each links to its documentation.
- Results are written under `obj/<Configuration>/<TargetFramework>/SharpProof`
  as a request, a result and a compiler manifest. The worker result records
  solver work and time per verified callable.
- On Windows the project must be on a fixed local NTFS volume. Paths may
  contain a tilde; only 8.3 short names are rejected. ReFS volumes, including
  Dev Drives, are not supported.
- Protocol 49, manifest 9, cache 72, compiler artifact 61, default API table
  version 12.

### Resource bounds

- Loop-entry, array-allocation, element and byte bounds are proved as
  constants or linear functions of a named input size. Shapes the model does
  not cover are `Unknown`.
- A nightly runtime oracle generates methods and fails if a compiled method
  exceeds a bound that was proved.
- A nightly performance gate verifies a pinned corpus and fails when total
  solver work rises more than 25% above its reference.

### Developer tools

- `tooling library-build` builds the whole pinned NodaTime source as a user
  project through the packed package, with a purity claim on every method,
  and reports build time, compiler manifest size against the 32 MiB limit and
  claim outcomes. The nightly lane runs it.
- `dotnet new sharpproof` and `sharpproof-workspace` generate a starter that
  builds cleanly. The default `native` variant verifies on the build machine;
  `container` verifies in a pinned Linux image with retained evidence and a
  comparison workflow; `portable` runs the analyzers only.
- `dotnet sharpproof init` plans the same three setups for an existing project
  and writes nothing without `--apply`. `doctor` checks a project's setup,
  `explain` and `compare` read stored evidence, and `export-test` writes an
  NUnit regression for a refuted postcondition.
- The reusable consumer workflow compares each run with the run for its exact
  base commit. It reports regressions by default and can enforce them. A
  first baseline is created only by an explicit manual run.
