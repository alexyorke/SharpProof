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
- A method can pass its own `ref`, `out` or `in` parameter on to a source
  method, which then stores through the same reference. Calls to source
  methods with `in` parameters are analyzed when each `in` argument is a value,
  a local or by-value parameter of the caller passed once by reference, or a
  `readonly` field read outside constructors. Fields and array elements passed
  by `ref`, `out` or `in` still keep the caller `Unknown`.
- Calls to generic source methods with delegate parameters, such as
  `Apply<T>(Func<T, bool> f, T x)` called with a `Func<int, bool>`, are
  analyzed: the method's body is inlined as for any other non-virtual source
  call. Before, such calls kept the whole caller `Unknown`. Invoking the
  delegate is still an unknown call.
- A source call whose inlined body runs out of the lowering work budget
  becomes an unknown call, after its `Requires` is checked, instead of
  leaving the whole caller `Unknown`. Such a call may do anything, so it does
  not help purity or exception claims; claims that do not depend on the call
  can now be proved.
- An empty collection expression `[]` whose target is `List<T>`,
  `HashSet<T>` or `Dictionary<TKey, TValue>` is analyzed as `new T()`, in a
  body or a field initializer.
- Values of source `readonly struct` types are analyzed: construction,
  including target-typed `new(...)` and constructors chained with `: this(...)`,
  `default`, field and property reads, instance members and user-defined
  operators. Purity, capability and postcondition claims over them can be
  proved. A body that builds such a value keeps allocation, effect-contract
  and resource-bound claims `Unknown`, because the model allocates it and the
  CLR does not.
- Values of mutable source structs are analyzed the same way, with C#'s copy
  semantics. A store into a field or auto-property of a local or by-value
  parameter, including `+=` and `++`, changes only that variable, and so does
  a mutating member called on it: the callee works on a copy of `this` that
  the caller assigns back on normal return. A copy, element or box made
  earlier keeps the old value. Mutations through a field of an object, an
  array element, a `ref` local, a struct field of a struct, an assignment to
  `this`, or a mutating call inside a `try` block keep the method `Unknown`,
  as do stores and mutating calls on the analyzed member's own `this`, which
  is its caller's storage.
- An instance member of a modeled struct called on a local or parameter sees
  what its arguments did to that variable: C# passes the variable by
  reference, so the callee's `this` is read once the arguments have run.
  Before, it was read before them.
- A class or struct constructor chained with `: this(...)` is inlined: the
  other constructor runs on the same object, with its field initializers,
  then the rest of this one. Instance field initializers no longer count as
  effects, so `new` of a class with them can be proved pure.
- `new` can inline source base constructors in classes without a destructor. The class's
  own field initializers run before the base constructor's arguments, then
  the base class's initializers and body run before the class's body. A
  virtual call on the base constructor's receiver or a metadata base class
  other than `object` leaves the construction unknown.
- A readonly static, or a static get-only auto-property, whose type's
  initializer runs to completion at analysis time holds the value that run
  left, when it is a `bool`, an integer, an enum, `null` or a `readonly
  struct` of those, such as NodaTime's `LocalTime.Midnight`. Code the
  initializer itself runs still sees an unknown value.
- Effect claims cover methods with their own `ref` or `out` parameters of a
  scalar, reference or type-parameter type, such as `bool TryParse(string s, out int value)`.
  The argument may be a field, an element or a variable passed twice, so each
  store to one is a write to argument state that forgets memory and the other
  by-reference parameters. Before, such methods were `Unknown` for every claim
  except a postcondition on a single scalar `out` parameter.
- A type pattern that declares a `string` or array variable from an object,
  such as `o is string s`, made the whole run fail: the frontend wrote a cast
  that the manifest reader then rejected. The pattern's variable is now
  unmodeled, as `as` already was, so that method stays `Unknown`. The artifact
  encoder also leaves out any callable whose program the worker cannot read,
  instead of writing a manifest no worker accepts.
- A call into another assembly may take `out` arguments, such as
  `int.TryParse(s, out var v)` or `d.TryGetValue(k, out var v)`, when each
  names a local or parameter of the caller passed once, outside every `try`
  region: on normal return the variable holds an unknown value. A handler
  could otherwise see a value the callee stored before it threw. Before,
  such calls kept the whole method from lowering.
- More patterns are exact:
  - `x is null` and `x is not null` over a type-parameter value;
  - string constant patterns such as `s is "." or ","` (false for `null`);
  - `not` around a declaration or type pattern, such as `b is not Paragraph p`;
  - the empty property pattern `{ }`, as in `b.Parent is not { } parent`.
  A type or property pattern whose input already converts to the matched
  type is a null test. In a build that is not optimized, `is null` on a
  `T` that may be a value type boxes it, so allocation claims stay
  conservative there. NodaTime methods that check their arguments with
  `Preconditions.CheckNotNull` now lower; three in the OSS corpus are
  refuted for purity, since a `null` argument throws.
- A target-typed `switch` expression or `?:` over class values with no
  common type, such as `i switch { 0 => token, 1 => block, _ => null }`
  returning their base class, keeps the chosen reference. Before, the
  conversion of the merged value to its own type was refused, so the method
  did not lower.
- A body that reads a readonly static holding an object no code can change,
  such as `static readonly object Int64_0 = 0L;` or
  `static readonly object TrueObject = true;`, now lowers. The static's own
  initializer must store null, a constant string, a boxed constant or
  `new object()`, nothing else in its type may store into it, and its type's
  initializer must be admitted. The read is not an ambient read, so
  `value ? TrueObject : FalseObject` can be proven pure. Before, any such read
  left the method `Unknown`. The object itself is not modeled: unboxing it or
  a postcondition about it stays `Unknown`. Statics holding arrays, class
  instances or a library call's result, such as `Task.CompletedTask` or
  `Task.FromResult(0)`, still abstain.
- `s[i]` on a string, with an `int` index, faults exactly on a null string or
  an index outside `[0, s.Length)`. Before, a guarded read such as
  `s.Length > 0 ? s[0] : ' '` was `Unknown` under `DoesNotThrow`, because the
  catalog let the call throw for any index. The character's value is still
  unknown.
- Interpolated strings converted to `string` are modeled when every hole is a
  `string`, `char`, `bool`, built-in integer or reference, with no alignment
  or format clause. Before, any interpolation left the body unsupported. With
  only `string` holes the value is the same `String.Concat` as the equivalent
  `+` chain. Otherwise it is a fresh non-null string of unknown content, so
  `$"line-{n}"` can be proved not to throw. An integer hole formats through
  the ambient culture, an ambient read that keeps `EnforcePure` `Unknown` and
  that the ambient-culture assumption now also covers; a reference hole's
  `ToString` is an unknown call. `char` and `bool` holes are culture-free, so
  `$"{c}={flag}"` can be proved pure. Other hole types, alignment and format
  clauses, and `FormattableString` or handler conversions stay unsupported.
- A static initializer may store `Array.Empty<T>()`: it returns one cached
  zero-length array per `T` and runs no code of `T`, so it counts as making
  fresh values, as `new T[0]` does.
- Built-in integer conversions to `float`, `double` and `decimal`, and
  widening from `float` to `double`, give opaque values. This lets forwarding
  helpers such as Humanizer's `Seconds(this int s) => Seconds((double)s)`
  reach the callee's existing proof rules. Stores to admitted class fields
  also accept these values and record the write.
- Target-typed `default` and `default!` count as effect-free values in type
  initialization, including in constructors of cached generic objects.
- Reads of admitted readonly caches can return an opaque sealed source object
  built from constants, default values and deferred delegate arguments.
- A store of a reference or a `readonly struct` value into an array the
  method allocated and then returned, such as
  `var a = new string[1]; a[0] = "x"; return a;`, was refuted as a write to
  the caller's state. It is now `Unknown`; a store into an array the caller
  passed in is still refuted.
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
- The default API table (version 13) also specifies `List<T>.Clear`,
  `Dictionary<TKey, TValue>.Clear` and `string.CompareOrdinal(string, string)`.
  A guarded `list.Clear()` can now be proven not to throw, and
  `string.CompareOrdinal` is pure and allocates nothing. `Clear`
  writes its receiver and forgets what the body knew about the collection's
  contents. Members that consult the element or key comparer, such as
  `List<T>.Contains`, `Dictionary` `ContainsKey` and `TryGetValue`, or call
  `ToString` on elements, such as `string.Join`, stay unspecified because they
  can run user code.
- A type initializer that is a closed computation no longer blocks its
  callers. SharpProof runs such an initializer at analysis time: constants,
  validating constructors and helpers, loops, exact integer arithmetic and
  `BigInteger` bounds, with floating-point and `decimal` results only stored.
  When it finishes, it never throws and has no effect a body can observe, so
  calls and constructions in its type are inlined and the type's own members
  are admitted at entry. One that allocates is admitted only by claims that
  ignore allocation. This opens NodaTime's `Duration`, `Instant`, `Offset` and
  `LocalTime` arithmetic.
- A type initializer that fills its readonly statics with new framework
  collections counts as one that only builds fresh values, so purity and
  capability claims accept it and callers in other types inline its type's
  members. That covers `List`, `Queue`, `Stack`, `Dictionary`, `HashSet` and
  `ConcurrentDictionary` built empty, with a constant capacity, or with
  `StringComparer.Ordinal` or `OrdinalIgnoreCase`, and collection initializers
  whose keys are distinct constant integers, characters, strings, enums or
  `bool` values, such as Humanizer's ordinal tables. A comparer from source, a
  culture comparer, keys of other types and repeated `Add` keys still leave the
  claim `Unknown`, and a body that reads such a collection still reads state
  other code can change. A target-typed `new(...)` in an initializer is now
  admitted like the explicit form.
- A type initializer may store into its own type's mutable statics, such as
  `static int State = 1;` or a static constructor that fills a
  `private static Dictionary<,>` it may later replace. Nothing reads such a
  static before the initializer finishes, so purity and capability claims
  accept the store, and callers in other types inline the type's members.
  The store counts as fresh values, so allocation, effect-contract and
  resource-bound claims stay `Unknown`, and a body's read of the static is
  still an ambient read. `[ThreadStatic]` statics and static events still
  make the initializer an entry effect.
- A type initializer may construct an instance of a class derived from a
  source base class, such as a `static readonly Instance = new Derived();`
  singleton. The constructors down the chain, written `: base(arguments)` or
  implicit, must each only store into the new instance, and each base class's
  own initializer must be admitted. Before, any class whose base was not
  `object` made its constructing initializer an entry effect. A base
  constructor that stores into a virtual auto-property still does.
- A product of a widened input, such as `days * TicksPerDay` with an `int`
  `days`, is checked as the same term a postcondition writes for it. A widened
  input plus or minus a constant, such as `(days + 1) * TicksPerDay`, is
  checked as the widened input plus that constant, with the wrap of the
  narrow sum accounted for. A contract that restates such a product no longer
  runs out of solver budget.
- Iterator methods, which use `yield`, are analyzed. Calling one runs none of
  its body: the call creates and returns a new enumerator, and the body,
  argument checks included, runs only as the result is enumerated. So a claim
  on an iterator, or on code that calls one, describes that call alone:
  `[EnforcePure]` and `[DoesNotThrow]` can be proved, while `[ZeroAllocations]`
  is refuted, because every call allocates. Before, such methods were
  `Unknown`, and a call to one was an unknown call that could do anything.
  Enumerating the result is still an unknown call, and an iterator
  with `Requires`, `Ensures` or `Assume` stays `Unknown`, since its clauses
  would describe the enumeration.
- Lambdas and anonymous methods converted to a delegate are analyzed, so a
  method that returns, stores or passes one, such as NodaTime's pattern
  handler factories, no longer stays `Unknown` for that alone. Creating the
  delegate runs none of its body, so `[EnforcePure]` and `[DoesNotThrow]` can
  be proved however the body behaves. A capturing lambda allocates a new
  delegate each time, so `[ZeroAllocations]` is refuted when one is reached;
  a non-capturing one reuses a delegate the compiler caches, and a closure
  frame is allocated where a captured variable's scope begins, so those leave
  allocation claims `Unknown`. Invoking the delegate is still an unknown call.
  A lambda is refused when it, or any other lambda or local function in the
  method, writes a variable it captures, passes it by reference or calls a
  mutating member of a captured struct, since an invocation the method cannot
  see would then change the method's own variables. Expression trees, lambdas
  that call local functions and lambdas capturing a constructor's `this` stay
  `Unknown`.
- `throw new NotSupportedException()` and `throw new NotImplementedException()`,
  the bodies of stub methods, are modeled like `new InvalidOperationException()`:
  the constructor looks up its message in runtime resources, under a runtime
  lock. With `SharpProofTrustExceptionMessages` a stub, or a method that throws
  one behind a guard, can now be proved pure. Without it, the lock refutes an
  effect claim on such a method, as for the other modeled constructors. Before,
  both constructors were unknown calls, so these claims stayed `Unknown` even
  with the property.
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
  version 13.

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
