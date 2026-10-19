# HM inference prototype

The standalone `SharpProof.HmInference.slnx` contains a rank-1 Hindley-Milner
inference core, a C# generic-signature adapter, a Roslyn analyzer (`HM0001`),
a CLI, an IDE code-fix package, samples, and executable regression checks. It is separate from the
production analyzer and main solution.

## Run

From the repository root, use the host wrapper to run verification in the
canonical Linux amd64 tooling container:

```powershell
./eng/scripts/Invoke-SharpProof.ps1 dev src/SharpProof.HmInference/verify.sh
```

The script restores and builds in a private container workspace, runs regression
checks and C#/HM samples, and exports evidence under `artifacts/hm-inference/`.
Compiler outputs remain private. The portable analyzer and code-fix assemblies
use netstandard2.0 and Roslyn 4.14; the reusable core and CLI target net10.0. Dependency versions
and lock files follow repository conventions. The verification also packs the
analyzer and runs an isolated net8.0 consumer build with a fresh package cache.

Normal repository analyzers stay enabled; the build must pass with zero warnings
and errors.

Run layout validation with `docker compose run --rm tooling layout`. For a host
Git worktree, use `./eng/scripts/Invoke-SharpProof.ps1 layout` instead; that host
orchestrator mounts Git metadata read-only and runs the check in Linux.

The built CLI accepts:

| Arguments | Behavior |
| --- | --- |
| `<source.cs> [...]` | Analyze supplied files together as a library |
| `--samples` | Analyze bundled C# programs |
| `--hm-samples` | Display core HM types and expected type errors |
| `--self-test` | Run C#, HM, runtime and differential checks |

For ad hoc files, build in a private container workspace and invoke
`dotnet <output>/SharpProof.HmInference.dll <source.cs> [...]`. This uses runtime
reference assemblies, not an MSBuild project loader. Original compilation errors
stop inference with exit code 1; missing files give exit code 2. Unsupported
methods report a reason.

## HM core

The core implements rank-1 inference for variables, lambda abstraction,
application and nonrecursive let, with explicit quantified schemes, fresh
instantiation at lookup, free-variable calculation relative to the substituted
environment, and occurs-checked unification. Substitution state belongs to each
inference run. Lambda parameters and recursive references are monomorphic while
solving; let bindings are generalized afterward. Captures are never generalized.

The public expression API also supports tuples, homogeneous lists, literals,
conditionals and simultaneous recursive groups. The prelude provides integer
arithmetic, list operations, pair and fix. Callers can supply well-formed schemes,
including schemes for data constructors. Assumptions are copied together to
preserve aliases without mutating caller state.

```csharp
var id = new HmLambda("x", new HmVariable("x"));
var expression = new HmLet("id", id, new HmTuple(
    new HmApply(new HmVariable("id"), new HmLiteral(HmLiteralKind.Number)),
    new HmApply(new HmVariable("id"), new HmLiteral(HmLiteralKind.Bool))));
var type = HmCore.PrincipalType(expression, cancellationToken); // (int, bool)
```

HmLiteral carries a type, not a runtime value. This API infers types; it is not
an interpreter or ML source parser. Recursive map infers
`forall A B. (A -> B) -> List<A> -> List<B>` (parentheses abbreviated).
Polymorphic recursion, higher-rank types, subtyping, effects and type classes
are outside this rank-1 calculus.

The reference for the core rules is Damas and Milner,
[Principal type-schemes for functional programs (1982)](https://www.cs.cmu.edu/~crary/819-f09/DamasMilner82.pdf).
Tests are implementation evidence, not a formal proof of soundness/completeness
or certification against a complete ML language implementation.

## C# adapter

Parameter type leaves become inference variables; type-constructor structure is
preserved. C# locals stay monomorphic: assigning ordinary delegate locals
polymorphic-let types would produce types C# cannot express. Explicit generic
method/local-function declarations supply schemes instantiated per call.

| Feature | Behavior |
| --- | --- |
| Primitive and nominal parameter types | Generalize unconstrained leaves |
| Func, tuples, named tuples, generic containers | Preserve shape and tuple names |
| Nullable reference/value types | Preserve annotations; synthesize required class/struct constraints |
| Task/ValueTask async methods and await | Infer result type through awaits; retain async modifiers and selected awaiter definitions |
| Null/default, coalescing, suppression and conditional access | Support typed contexts; conservatively retain types for conversions and conditional receivers |
| Arrays, jagged/rectangular arrays | Preserve rank; infer element types through indexing and Length/Rank |
| Implicit array construction | Unify initializer elements |
| Explicit array construction | Preserve declared element types |
| Synchronous lambdas | Infer inputs/results; retain explicit annotations |
| Locals and nested blocks | Share types by symbol; retain explicit/constant local types, uninitialized declarations and conversions |
| Mutable locals/parameters, tuple/field/property/array/indexer stores | Keep monotypes; retain receiver types and assignment conversions |
| Compound assignments and increments | Retain original built-in operand types; skip custom operators |
| Fallthrough branches, while/do/for, array foreach | Collect return constraints and keep block/loop locals scoped |
| Break/continue and traditional switch | Track each exit separately; consume it at the correct loop/switch |
| Tuple array foreach deconstruction | Bind nested tuple elements, discards and explicit types |
| Checked/unchecked blocks and throws | Preserve overflow contexts and treat throws as nonreturning exits |
| Tuple/custom deconstruction | Infer nested declarations, mixed assignments and discards; retain conversions and assignment-expression tuple results |
| Explicit/target-typed construction and initializers | Preserve constructor/indexer/Add selection; pin initializer source types and support nested members |
| String interpolation and nameof | Retain interpolation hole types; compiler-check nonexecuted nameof operands |
| Conditionals, return branches and guard returns | Unify results |
| Constant/discard switch expressions | Preserve governing type; unify result arms |
| Self-recursive methods | Solve monomorphic recursive references |
| Resolved method and generic calls | Import signatures; instantiate generic parameters per call |
| Named/reordered arguments and omitted optional defaults | Bind by Roslyn parameter identity rather than source position |
| Extension calls and array-based params calls | Infer receiver/expanded element types; preserve selected definitions |
| Local functions | Import schemes; compiler-check retained bodies/captures |
| Fields, properties and instance calls | Retain receiver/member types conservatively |
| Built-in arithmetic, comparisons, logic, bitwise/unary operators | Retain operand types and C# promotions |
| Casts, typed default, checked/unchecked | Retain concrete types |

Result variables not inferable from arguments retain corresponding declared
return information. For example, `Func<int,int> Capture(int x) => y => x` can
suggest `Func<int,T> Capture<T>(T x)` without an uninferable extra parameter.

Every candidate is substituted into the original compilation and checked for
compiler errors, including supplied callers and generic constraints. Statically
resolved method and constructor definitions are compared across all supplied
files. Calls outside the analyzed method must also retain their instantiated
parameter and return types. This conservatively rejects changes to argument
conversions, result boxing and caller overload selection. Indexer and collection
Add definitions inside the method are checked too. Nullable shapes supply class/
struct constraints. Calls and constructed signature types supply class/class?, struct,
unmanaged, notnull, new(), interface, base-class and dependent constraints; Roslyn
checks their combined legality.
Escaped identifiers are retained in signatures.

Instance methods, existing generic methods and containing generic types are
supported. Existing type parameters are rigid; their constraints are retained.
New names avoid identifiers already in the declaration. Attributes and
ref/out/in/params modifiers are preserved; optional parameter types remain
concrete to retain their defaults. Ref arguments at call sites remain unsupported.

The adapter supports void/async void roots, Task/ValueTask async and anonymous
lambdas, Action and custom delegates, and static/instance method-group conversions.
Each lambda has its own async return context. Explicit parameter/return types stay
concrete; variance and reduced extension method groups retain their target types.
Supplied method groups are rechecked for selected overloads after rewriting.

Sync and async iterator roots infer element types from yield return, retain
non-generic yield conversions, and support yield break and finalizer flow.
Foreach supports arrays, generic interfaces and custom instance patterns, including
nested/inherited enumerators. Await foreach supports configured and cancelable
sources. Protocol members and conversions are rechecked after rewriting.
Generic instance calls preserve receiver substitutions and explicit type arguments.

The adapter skips custom operators, expanded non-array params
collections, unsupported patterns and other unimplemented syntax. It does not
infer mutually recursive C# method groups or fully simplify arbitrary combinations
of generic constraints. See [the completion ledger](LIMITATIONS.md).

Tuple and custom deconstruction support declarations, reassignment, mixed targets,
discards, member/indexer stores and nested instance/extension Deconstruct methods.
Generic receivers retain base/interface substitutions. Assignment expressions
return their converted target tuples. Selected implicit methods and conversions are
rechecked across supplied files, including sync/async foreach deconstruction.
Existing body warnings are matched by ID and body-relative source location;
warnings at new sites reject a candidate even if that warning ID already exists.
Ref constructors, anonymous objects, record with
expressions, FormattableString and custom interpolation handlers
are also unsupported. A nameof member lookup may reject a candidate even though
its operand is not executed, because the lookup still has to compile.

Loop analysis accumulates constraints over monomorphic bindings; it does not
generalize at iteration boundaries or prove termination. Roslyn validates return
completeness, reachability and definite assignment after rewriting. Labels, named
goto and goto case/default are supported, including pending finally/disposal.
Unreachable syntax still contributes typing constraints. Boolean conditions retain
the source type of user-defined conversions and true operators.
Ref foreach, generalized extension enumeration and type-parameter enumerator
patterns remain unsupported. Ref delegates,
attributed/modified lambdas and expression trees remain unsupported.
Switch statements support constant labels and guards,
not declaration/type patterns. Try/catch/finally supports exception locals, filters,
rethrow and pending loop/return exits. Using statements/declarations, await using
and lock retain concrete resource/receiver types to preserve disposal conversions,
pattern/interface selection and Monitor versus System.Threading.Lock lowering.
Ordinary void
method calls can appear as expression statements. The standalone HM core remains
pure; these imperative extensions belong to the C# adapter only.

Break exits are consumed by the nearest loop or switch; continue exits propagate
through switches to the nearest loop. Throws contribute no return constraint;
an all-throw body retains declared result information when producing a C# signature.
The suite exercises nested exit ownership, tuple iteration, thrown exceptions and
checked overflow versus unchecked wrapping at runtime.

Imported unconstrained `T?` signatures retain their C# annotation semantics through
calls, tuples, arrays, containers and await results. They do not require a reference
or value constraint: an annotated `T` instantiated with `int` is still `int`, while
instantiation with `string` can produce a nullable reference. Explicit and constrained
instantiations normalize accordingly, including repeated annotations. Passing an
annotated result to a `notnull` requirement can force the original non-nullable
value domain; the compiler checks the resulting constraints and warnings.
Generated signatures and constraints follow the method's nullable annotation
context, including files with annotations disabled.
Nullable wrappers remain distinct from their underlying types. For example,
`int? Identity(int? x) => x` can become `T? Identity<T>(T? x) where T : struct`;
nullable reference identities use `where T : class`. Existing constraints and
nullable annotations are preserved when applying the IDE fix. Null comparisons,
conditional member access and assignment conversions use conservative original types.
Conditional array access preserves element inference and nullable lifting for
value/reference elements, nullable elements, nested arrays and multiple dimensions.
Null-coalescing assignments share their input/result types for identity and
nullable-lifting conversions; widening and boxing endpoints stay concrete.
Candidates introducing new compiler warnings (including nullable warnings) are
rejected; existing warnings do not become evidence of correctness.

Resource-local receiver checks tolerate differences between declared nullable
annotations and the non-null flow state at a member access, retaining the
declared wrapper and concrete disposal type.

Call arguments distinguish conversions from HM equality. Derived-to-base and
interface conversions carry inferred type arguments through generic bases,
implemented interfaces and arrays. Representable subtype relationships become
C# constraints. Covariant array/interface/delegate conversions retain reference
constraints, and boxing into non-nullable `object` requires `notnull`. Nullable
boxing preserves `T?` with a value constraint. Numeric, lifted numeric and
user-defined conversions keep their original endpoints. Tuple element and
expanded array-params arguments follow the same rules, including awaited nullable
values. IDE-fix runtime checks compare results, evaluation order, operator effects,
boxing and reference identity with the original code.

Async methods returning `Task<T>` or `ValueTask<T>` infer the body result and then
wrap it in the original task type. Non-generic Task/ValueTask bodies may fall
through or use `return;`. Standard generic awaits preserve their result variable;
custom instance, inherited and extension awaiters also propagate result variables
through their selected GetAwaiter and GetResult signatures, importing extension
constraints. Unsupported receiver shapes remain concrete. Awaiter/GetResult/
IsCompleted definitions are rechecked across all supplied source files; changed
overloads or instance members shadowing extensions reject the candidate. Tests apply actual IDE
fixes, compile with no warnings, and execute nullable values/references and new
Task/ValueTask types through completed/suspended awaits, cancellation and faults.
Custom task-like types use their `AsyncMethodBuilder` attribute, including a
method-level override and builders inside non-generic containers. Generic builder
constraints flow to the suggested signature. The builder's `Task` property can
keep a result concrete even when the body alone would generalize it. Generic and
non-generic task-like lambdas preserve the surrounding method's builder state.
As in C#, a builder attribute on `async void` is ignored. Compiler-required
builder signatures remain concrete even when their task-like type is not yet used.

Custom-builder candidates also undergo full compiler emission: some builder
failures are not reported by normal binding diagnostics. Explicit type usages in
the same generic scope validate builder constraints that the compiler can omit
for implicit builder constructions. Changes to source-defined compiler-required
builder methods are checked too. Runtime tests apply IDE fixes and compare the
original and fixed builder traces across completed and suspended awaits, normal
and critical awaiters, method overrides, finalizers, faults and cancellation.
Declared widening/boxing return conversions are retained rather than narrowing
the result type, including within tuple returns.

## IDE installation and fixes

Verification exports `SharpProof.HmInference.*.nupkg` under
`artifacts/hm-inference/packages/`. Install from that local NuGet source into a
C# project using `PackageReference` with `PrivateAssets="all"`. The package
contains only the analyzer and code-fix assemblies under `analyzers/dotnet/cs`;
Roslyn and MEF dependencies are supplied by the IDE host. Use an IDE supporting
Roslyn 4.14 or newer. The package has not been published to a public feed.

HM0001 offers "Generalize method signature (API change)". The fix preserves the
body, documentation, attributes, defaults and modifiers. It recomputes inference
before offering or applying changes. Document, project and solution Fix All
revalidate each candidate against the updated solution, skipping suggestions
that cease to be valid after another edit (for example, conflicting overloads).

Public APIs, binary consumers, reflection and absent external callers still
need review. Compiler validation does not establish behavioral equivalence.
Recompilation per candidate targets small experiments; solution-scale cost has
not yet been measured. An automated AdhocWorkspace test checks application and
Fix All; a fresh consumer build checks loading of the packaged analyzer. Manual
interactive IDE acceptance remains open.
## Validation scope

The suite checks C# signatures, analyzer agreement, rejected/skipped cases, and
runtime execution of generalized methods at multiple concrete types. Core cases
cover let-polymorphism, shadowing, captures, substituted environments, fresh
instantiation, occurs checks, recursive map, mutual recursion, lists, tuples,
custom assumptions and cancellation.

An independent reference Algorithm W uses immutable terms, integer variables and
explicit substitutions. Differential checking exhaustively covers all **9,130
closed terms of size at most seven** in this bounded grammar: int/bool type
literals, variables, lambdas, applications and nonrecursive lets. Each constructor
counts as one node. This is exhaustive for that grammar/bound, not all possible
expressions. Recursive groups, assumptions, lists and conditionals have separate
targeted checks.

Evidence is exported as tests.txt, samples.txt and hm-samples.txt under
`artifacts/hm-inference/`. The branch started from fetched master
`e97c6c56d58fba9f5c5c20ac59f4c7f44ff4424a`.
