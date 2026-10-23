# HM inference prototype

The standalone `SharpProof.HmInference.slnx` contains a rank-1 Hindley-Milner
inference core, a C# generic-signature adapter, a Roslyn analyzer (`HM0001`),
a CLI, an IDE code-fix package, samples, and executable regression checks. It is separate from the
production analyzer and main solution.

LINQ query syntax follows Roslyn's selected call tree, including `let`, additional
`from` clauses, ordering, joins/group joins, grouping and continuations. Generated
selectors share inferred element graphs through transparent carriers. `IQueryable`
quotes and custom query patterns retain their selected operators; supplied queries
are checked again after signature edits. Typed `from` casts and numeric endpoints
stay fixed, while nullable/default joins, coalescing and awaited sources retain
their structural relationships. Scalar default writes through generic ref returns use the effective compiler diagnostics to infer a struct bound only where the proposed signature needs one. Ordinary blocks, annotated `ref T?` contracts and compatible array getters keep their broader domains. Nullable contexts, pragma suppression, per-tree severity and warning promotion retain their configured behavior. Query selectors and awaited query sources preserve independent sibling types; dependent value-domain bounds normalize to equality. Applied Fix All checks cover deferred enumeration,
early disposal, traces, reference identity, provider expression trees, independent
key/value domains, suspension, faults and cancellation. Bundled examples are in
`Samples/Queries.cs.txt`; CLI and package fixtures cover C# 8, 9 and 13.

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
| `[--unsafe] <source.cs> [...]` | Analyze supplied files together as a library; opt into unsafe compilation |
| `--project <project.csproj>` | Load the MSBuild project and its source project references |
| `--solution <solution.sln/slnx/slnf>` | Load the selected solution graph |
| `--framework <tfm> --configuration <name> --property <Name=Value>` | Set MSBuild properties for project/solution loading |
| `--no-restore` | Use existing project assets without restoring dependencies |
| `--no-build` | Use supplied generator/analyzer binaries without building tool projects |
| `--samples` | Analyze bundled C# programs |
| `--hm-samples` | Display core HM types and expected type errors |
| `--self-test` | Run C#, HM, runtime and differential checks |

For ad hoc files, build in a private container workspace and invoke
`dotnet <output>/SharpProof.HmInference.dll <source.cs> [...]`. This uses runtime
reference assemblies. Project/solution mode uses the installed MSBuild SDK selected
for the target directory and keeps project reference assemblies, language/nullable
settings, preprocessor symbols, linked/excluded files, source generators and analyzer
configuration. Dependencies are restored by default; `--no-restore` assumes current
restore assets. Declared generator/analyzer project references are built in their
SDK-selected framework and configuration before analysis. Builds are cached per
project context, and failures stop inference even when older binaries exist.
`--no-build` trusts supplied tool binaries. Raw analyzer-file references still require
the specified binary. The native MSBuild graph supplies project contexts; source
references are compiled to fresh in-memory metadata, including Visual Basic
references. Requested multi-target roots include every framework by default.
Dependencies use the SDK-selected compatible target, excluding unused frameworks.
A selected source reference error stops analysis even when old binaries exist.
Source generators run once per project with the loaded parse options, additional
files and analyzer configuration. Loading failures, initialization/execution failures
and effective generator errors stop inference; warning severity and suppression
follow project settings. Generated methods are excluded by default, with an explicit
`generated_code=false` editor setting respected. Generated output keeps its compiler
file paths and per-file editor settings.
Project mode reports configured HM0001 diagnostics for each loaded C# target
framework. A bare project/solution path is accepted too. Original compilation errors
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
| Local function captures | Infer supported bodies after the enclosing local declarations; retain declared local schemes and propagate capture constraints, async and iterator results |
| Collection expressions and non-array params | Infer elements through construction, spread enumeration and collection builders; preserve Add/protocol selection and conversions |
| Anonymous objects and with expressions | Infer internal anonymous property graphs and clone initializer constraints; preserve nominal record/struct types, conversions and selected clone members |
| Reference calls and delegates | Infer invariant ref/out/in slots, out variables/discards, ref aliases/returns and ref delegate invocation; retain lambda modifiers/attributes and compiler escape checks |
| Primary-constructor captures | Preserve declared instance storage types while generalizing unrelated method inputs |
| Primitive and nominal parameter types | Generalize unconstrained leaves; retain assembly identity and generic-parameter scopes, verify spelling through extern aliases and shadowed nested names |
| Func, tuples, named tuples, generic/nested containers | Preserve owner/inner arguments, nullable annotations and tuple names |
| Ref-like types, Span and ReadOnlySpan | Infer element types, indexing/slicing, ref stores/returns and ref iteration; synthesize allows ref struct when supported |
| Nullable reference/value types | Preserve annotations; synthesize required class/struct constraints |
| Task/ValueTask async methods and await | Infer result type through awaits; retain async modifiers and selected awaiter definitions |
| Null/default, coalescing, suppression and conditional access | Support typed contexts; conservatively retain types for conversions and conditional receivers |
| Arrays, jagged/rectangular arrays | Preserve rank; infer element types through indexing and Length/Rank |
| Implicit array construction | Unify initializer elements |
| Explicit array construction | Preserve declared element types |
| Synchronous lambdas | Infer inputs/results; retain explicit annotations |
| Locals and nested blocks | Share types by symbol; retain explicit/constant local types, uninitialized declarations and conversions |
| Mutable locals/parameters, tuple/field/property/array/indexer stores | Keep monotypes; preserve member selection and assignment conversions |
| Compound assignments and increments | Retain built-in promotions; infer generic user operators and nullable lifting |
| Fallthrough branches, while/do/for, array foreach | Collect return constraints and keep block/loop locals scoped |
| Break/continue and traditional switch | Track each exit separately; consume it at the correct loop/switch |
| Tuple array foreach deconstruction | Bind nested tuple elements, discards and explicit types |
| Checked/unchecked blocks and throws | Preserve overflow contexts and treat throws as nonreturning exits |
| Tuple/custom deconstruction | Infer nested declarations, mixed assignments and discards; retain conversions and assignment-expression tuple results |
| Explicit/target-typed construction and initializers | Preserve constructor/indexer/Add selection; pin initializer source types and support nested members |
| String interpolation and nameof | Retain interpolation hole types; compiler-check nonexecuted nameof operands |
| Conditionals, return branches and guard returns | Unify results |
| Declaration, recursive, positional and list patterns | Bind pattern variables; infer structural member/element types and retain implicit protocol selections |
| Legacy type tests and safe casts | Infer the operand; retain the tested type and reference/nullable cast target, including awaited operands |
| Pattern switches and switch expressions | Bind section/arm variables and guards; join converted results, retain numeric/user conversion endpoints and nullable lifting; fixed local result types permit independent captures |
| Self-recursive methods | Solve monomorphic recursive references |
| Resolved method and generic calls | Import signatures; instantiate generic parameters per call |
| Named/reordered arguments and omitted optional defaults | Bind by Roslyn parameter identity rather than source position |
| Extension calls and array-based params calls | Infer receiver/expanded element types; preserve selected definitions |
| Local functions | Import schemes; compiler-check retained bodies/captures |
| Fields, properties and instance calls | Retain receiver/member types conservatively |
| Dynamic signatures and operations | Retain dynamic annotations and binder operand types; infer independent generic components |
| Built-in null equality/inequality | Infer symbolic operands for null constants and reference defaults; preserve user-operator dispatch |
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
concrete to retain their defaults. Ref/out arguments retain invariant storage types; omitted readonly arguments preserve their conversions.

The adapter supports void/async void roots, Task/ValueTask async and anonymous
lambdas, Action and custom delegates, and static/instance method-group conversions.
Each lambda has its own async return context. Explicit parameter/return types stay
concrete; variance and reduced extension method groups retain their target types.
Supplied method groups are rechecked for selected overloads after rewriting.

Sync and async iterator roots infer element types from yield return, retain
non-generic yield conversions, and support yield break and finalizer flow.
Foreach supports arrays, generic interfaces and custom instance/extension patterns,
including base/interface receiver projection and imported extension constraints.
Await foreach supports configured and cancelable sources. Implicit MoveNextAsync
and DisposeAsync awaits infer through instance/inherited/extension GetAwaiter and
GetResult members in the loop's scope. MoveNextAsync results and IsCompleted
properties retain their required Boolean types. Specialized extension overloads
keep the type arguments their signatures require. Protocol members, conversions
and implicit await bindings are rechecked across all supplied files, including
when an extension protocol method itself becomes generic. Runtime IDE-fix checks
compare original/fixed traces through suspension, empty/full iteration, early
breaks, body/move/disposal failures and cancellation.
Generic instance calls preserve receiver substitutions and explicit type arguments.

The adapter skips unsupported patterns and other unimplemented
syntax. Collection expressions and expanded non-array params are inferred through
their element and construction protocols. Add calls preserve omitted defaults and
normal/expanded array/span/builder params forms, including trailing packs and
constrained/ref extension receivers. Selected methods and nested collection
construction are rechecked across supplied files. It does not
infer mutually recursive C# method groups or fully simplify arbitrary combinations
of generic constraints. See [the completion ledger](LIMITATIONS.md).

Tuple and custom deconstruction support declarations, reassignment, mixed targets,
discards, member/indexer stores and nested instance/extension Deconstruct methods.
Generic receivers retain base/interface substitutions. Assignment expressions
return their converted target tuples. Selected implicit methods and conversions are
rechecked across supplied files, including sync/async foreach deconstruction.
Existing body warnings are matched by ID and body-relative source location;
warnings at new sites reject a candidate even if that warning ID already exists.
Anonymous objects use internal structural property graphs; record/struct with
expressions retain nominal receivers and inferred initializer constraints.
FormattableString/IFormattable targets infer their boxed holes, retaining format
and alignment syntax. Custom interpolation handlers infer generic AppendFormatted
arguments, containing-type substitutions and method constraints, including forwarded
arguments/receivers and conditional append protocols. Expanded constructor packs
support arrays, spans, lists and custom collection builders; forwarded elements
share the normal conversion and Add constraints. Construction and Add selections
inside these implicit packs are checked too, including collections packed for
AppendFormatted calls. Element conversions preserve boxing and widening endpoints;
target-dependent formattable/handler elements keep their conversion targets.
Constructor and append selections
are checked across supplied files. Ordinary string holes retain their original types
to preserve formatting overloads hidden by compiler lowering. A nameof member lookup may reject a candidate even though
its operand is not executed, because the lookup still has to compile.

Loop analysis accumulates constraints over monomorphic bindings; it does not
generalize at iteration boundaries or prove termination. Roslyn validates return
completeness, reachability and definite assignment after rewriting. Labels, named
goto and goto case/default are supported, including pending finally/disposal.
Unreachable syntax still contributes typing constraints. Boolean conditions retain
the source type of user-defined conversions and true operators.
General type-parameter enumerator patterns remain unsupported.
Generic user-defined binary/unary operators, increments, compound assignments,
and conversions share containing-type substitutions with their operands/results.
Nullable lifting and checked operator selection are retained; explicit casts and
fixed call targets preserve their declared conversion endpoints. Awaited return
conversions infer through Task/ValueTask results, including nullable struct
constraints. Selected operators and conversions are rechecked across supplied
files. Applied IDE-fix tests verify effects, reference identity, new payload domains,
null short-circuiting and suspension before a lifted async conversion.
Ref delegates and attributed/modified lambdas retain their syntax and contracts.
Quoted lambdas infer through Expression<TDelegate>, including custom delegates,
nested quotes, nullable captures and compilation/invocation. Natural delegate/tree
types are resolved for conversions to Delegate, Expression and LambdaExpression;
declared conversion endpoints remain fixed. Compiler checks enforce tree-language
restrictions and supplied call bindings.
Switch statements support pattern variables and guards.
Try/catch/finally supports exception locals, filters,
rethrow and pending loop/return exits. Using statements/declarations and await using
infer IDisposable/IAsyncDisposable constraints and generic resource payloads, including
nullable resources. Explicit storage conversions and distinct disposal patterns remain
fixed; selected disposal members and implicit await bindings are rechecked across
supplied files, and emission validates lowering. Applied IDE fixes preserve null handling,
disposal on return and failure, exception precedence, and async suspension/completion.
Custom instance/extension disposal awaiters retain payload relationships and specialized
bindings; custom builder awaiter constraints propagate through implicit disposal awaits.
Pattern disposal remains supported when a source-defined disposal interface lacks its
expected member; malformed fallback contracts reject safely.
Lock retains its concrete receiver to preserve Monitor versus System.Threading.Lock lowering.
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

Nested generic types retain enclosing and inner type arguments separately. Instance
calls, nested delegates, deconstruction, awaiters and constraints substitute both
levels, including nested types without their own arguments and deeper nesting.
Rendering preserves nullable annotations and named tuple arguments at each level.
Ref-like signature types include Span/ReadOnlySpan and their nested enumerators.
Generic field/property/indexer reads and stores propagate their declared type
arguments; selected members are rechecked across supplied files. Index-from-end
and range access retain index conversion domains. Ref/ref-readonly returns and
iteration retain their modifiers and aliasing. Ref-like leaves can become type
parameters with `allows ref struct` on C# 13 and supporting runtimes; older
language versions or target runtimes keep those leaves concrete while allowing
unrelated parameters to generalize. Compiler
checks still enforce ref safety and generic type-argument restrictions. Runtime
IDE-fix tests cover span mutation, ref aliases, shared slices versus copied array
ranges, nested object identity and completed/suspended nested awaits with new
type arguments. Direct pointer/function-pointer types and stack allocation remain
open.

Ordinary return joins infer null-compatible reference/value constraints and preserve structural input/result relationships. Direct recursive calls retain the declared return structure during inference, including nullable tuple components and Task/ValueTask payloads. Untyped null-like defaults can participate in returns, null coalescing and coalescing assignment, including awaited payloads; explicit casts and typed defaults keep their declared endpoints. Scalar default literals can generalize alongside an input: C# 9+ nullable-enabled code emits an unconstrained `T?` result with a bare `T` input, preserving zero for value arguments and null for reference or nullable-value arguments. C# 8 uses a struct constraint where the annotation cannot be expressed; nullable-disabled code uses plain `T`. This covers branch joins, tuple components, callbacks, iterators and Task/ValueTask returns. Scalar default and null-like stores also generalize inferred locals, reassigned parameters, nullable references/values, container roots and array/list elements. Nullable reference/value ref parameters and data/function pointers retain their storage contracts. Natural callbacks keep capture nullability even when the compiler inferred their return before a later null store; this avoids making unrelated inputs nullable. Supplied calls that cannot infer nullable type arguments still retain concrete endpoints. An earlier copied value keeps its input shape; captured storage joins default writes across its lifetime, including local-function writes. Reassigned parameter storage receives the nullable annotation it needs. Known local/parameter ref aliases propagate default writes through chains, rebindings and conditional references, preserving earlier value copies and allowing reference/value domains in C# 9+. Ref/out slots receive matching nullable annotations; parameter-backed array/span elements, generic fields, ref properties/indexers and generic ref returns now receive slot annotations before body inference. This preserves independent fill arguments and sibling fields, tracks receiver copies/reassignments, and keeps numeric conversion endpoints. Receiver recovery also binds foreach/await foreach variables, ref iteration, tuple/custom deconstruction, mixed deconstruction assignments and variables from is/switch patterns using the same rules as body inference. Property, positional, list and slice patterns retain structural storage paths; named tuple fields and ItemN aliases share their element graphs and protocol identities. Runtime fixes verify getter/deconstructor counts, slice copies and Task/ValueTask suspension, faults and cancellation. Storage receivers declared by out var calls reuse normal argument binding, including generic/delegate calls, TryGetValue, tuple outputs, deferred captures and awaited operands; explicit output types retain their fixed boundaries. Executed fixes check direct/ref writes, call counts, dictionary misses and new reference/nullable-value domains. Call-context discovery also links implicit callback parameters back to generic sequence/delegate inputs, including nested callbacks and captured dependencies. It records parameter shapes only in the isolated probe and keeps explicit lambda types and fixed result boundaries. Applied fixes verify enumeration counts, escaping callbacks, independent sibling values and Task/ValueTask callback suspension, faults and cancellation. Execution checks cover enumeration/deconstruction counts, early-return cleanup, suspension, exact faults and cancellation, and return values captured before awaited disposal. Unresolved owners retain existing constraints. Alias owners are joined across their lifetime. Runtime fixes verify copies, mutations and awaited results. `int? value ??= default` generalizes under a struct constraint and retains an underlying zero value rather than becoming null. Layout-derived unmanaged constraints trigger signature rendering again, preserving custom async-builder result types. Converted coalescing operands use directed conversion constraints, preserving independent source/result types, boxing, collection projections and generic user-defined conversions. Numeric conversions retain their original endpoints.

Ordinary return conversions use directed bounds for boxing, interface/base conversions, generic collection projections and covariance. Fixed local/lambda return contracts retain their declared result types while captured inputs can generalize. Natural delegates stored as object and expression-tree shapes retain their closed types. Nullable boxing keeps a single nullable annotation; numeric conversion endpoints and supplied callers that cannot infer a nullable type argument remain concrete. Runtime checks cover ordinary, local, callback, iterator and async returns in original and new domains.

Fixed nullable return contracts bind each adjusted return or yield before merging flow. This keeps captured domains independent across nullable branches, tuple results and local iterators, including value and nullable-value captures.

Fixed delegate contexts infer conversions inside lambda/anonymous-method returns without changing the delegate signature. This admits value captures boxed into object results, including nested and async callbacks; expression-tree shape and ref-return invariance remain protected.

Fixed call parameters also accept representable conversions from generalized inputs, including constructors, delegate calls, explicit generic instantiations, params and array elements. Reference and omitted readonly arguments retain their original storage contracts.

Fixed declared value storage accepts representable conversions from generalized inputs without changing its declared type. This includes fields, properties, indexers, explicit locals, object initializers and typed deconstruction. Nullable annotations come from the bound storage contract; numeric/boxing endpoints, explicit ref storage and inferred-local assignments retain their required types.

Pattern variables participate in inference through `is` expressions, switch labels,
guards and switch-expression arms. Var captures share the input monotype; property
and positional patterns propagate generic container/tuple arguments, including
inherited members and extension deconstructors. Nullable empty-property patterns
preserve the underlying value type. List and slice patterns support arrays, strings
and custom indexers/slices. Declaration/type tests keep their explicit tested type,
while relational and constant tests retain their original conversion domain.
Runtime `ITuple` positional patterns retain the static input domain required by
C# fallback binding. Selected implicit property, length, indexer, slice and deconstructor members are
rechecked across supplied files. Runtime IDE-fix tests compare values and getter,
indexer, slice and deconstruction traces, including failed matches and empty lists.

Resource-local receiver checks tolerate differences between declared nullable
annotations and the non-null flow state at a member access, retaining the
declared wrapper and concrete disposal type.

Call arguments distinguish conversions from HM equality. Derived-to-base and
interface conversions carry inferred type arguments through generic bases,
implemented interfaces and arrays. Representable subtype relationships become
C# constraints. Covariant array/interface/delegate conversions retain reference
constraints, and boxing into non-nullable `object` requires `notnull`. Nullable
boxing preserves `T?` with a value constraint. Numeric and lifted numeric conversions keep their original endpoints;
user-defined conversions infer owner substitutions while preserving fixed targets. Tuple element and
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
constraints flow to the suggested signature. Extra constraints on normal/critical
awaiter parameters are projected onto inferred awaiter types, including implicit
async-foreach awaits. They can pin an await input or couple it to the result while
other inputs still generalize. Nested async lambdas retain separate builder-result
contexts. Generic builders with generic containing types are excluded by C# itself
(CS8940); regression fixtures check that boundary. The builder's `Task` property can
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

Existing hidden-by-default compiler style diagnostics promoted to errors do not
block inference. Candidate validation retains per-file diagnostic severity and
generated-code settings when replacing a syntax tree. A candidate that adds compiler
warnings or promoted style errors is rejected, including imports made unused by
the generated signature.

Data pointer shapes support dereference, indexing/stores, address-taking, updates,
arithmetic, nested pointers and pointer arrays. Generic pointer elements acquire
required unmanaged constraints, including constraints on actual struct storage.
Unused generic struct arguments remain free. Explicit pointer locals retain their
storage types. Stack allocation infers implicit elements
and preserves explicit element types and conversions; pointer and span targets are
supported. Applied IDE-fix runtime checks cover aliases, stores, element stride,
byte/struct payloads and stack allocation. Project mode uses the project's unsafe
setting; loose files
require `--unsafe`.

Function pointer signatures retain managed/unmanaged calling conventions, explicit
convention lists and parameter/return reference kinds. Inference covers indirect
calls, nested signatures, nullable values, pointer payloads, generic containing
types and method addresses with inferred/explicit type arguments and constraints.
Supplied method-address bindings are checked alongside delegate method groups.
Applied IDE fixes verify managed reference identity, ref-return storage, out and
readonly callbacks, generic callback addresses and native Cdecl calls using int
and long payloads. Explicit local storage and conversion endpoints stay fixed.
Candidates are rejected when a supplied bare method-address call cannot infer the
new C# type arguments. Typed callback variables provide their complete signatures
for inference; this call-site distinction has regression coverage.
Fixed declarations preserve their explicit pointer storage types while inferring
array elements and instance, inherited, readonly and extension pinning patterns.
Unrelated generic arguments remain free; void-pointer pinning imports unmanaged
requirements for the actual element. Specialized extension bindings keep their
concrete receiver domain. Selected pinning methods are rechecked across supplied
files, reconstructing extension type arguments inferred from the receiver. Both
fixed bodies and generalized implicit helpers undergo compiler emission checks.
Fixed-size buffers have read/store coverage through ref parameters, pointers and
managed owners. Applied IDE fixes verify storage and dispatch, null/empty sources,
compacting GC pinning, unrelated managed tags and new unmanaged element domains.
Constraint normalization removes self dependencies, merges mutually dependent
inference variables, keeps the strongest compatible base-class requirement and
relates arguments through generic bases and invariant interfaces. It repeats when
unification exposes another simplification. Cyclic groups retain their incoming
and outgoing requirements; ordinary acyclic bounds and F-bounded interfaces stay
separate. Runtime IDE-fix checks compare helper effects, values and reference
identity in original/new domains and reject invalid base/value domains. General
directional lower bounds and ambiguous multiple interface projections remain open.
Nullable constraints are collected before solving. Annotated constraint owners and
nullable dependency cycles retain their parameter wrappers while sharing the
underlying type variables. Nominal bounds intersect their outer nullability;
class/notnull requirements strengthen nullable bases, and inherited interface
bounds remove duplicate nullable/non-nullable requirements. Constraints imported
from nullable-oblivious source and metadata retain their permissive domains.
Nullable nominal constraints use normal scoped spelling, including existing extern
aliases. Runtime IDE fixes verify nulls, reference identity, helper effects,
interface-admitted structs, required-domain errors and non-nullability warnings.
Dependent bounds are rechecked after their target resolves. Sealed classes and
value types become fixed types, array/delegate bounds use conversion rules, and
object bounds retain their nullability requirements. Transitive nominal bounds
are simplified without dropping required non-null obligations. Reference-only
System.Enum domains stay fixed, preserving boxed identity. Applied IDE fixes
exercise nullable async results, concrete domains and newly admitted types.
General directional lower-bound solving and ambiguous interface projections
remain open.

When a type implements a generic interface through multiple argument shapes,
constraint projection tests each alternative on a copied substitution graph.
A uniquely compatible array, list or tuple shape selects its implementation;
failed alternatives leave inference variables unchanged. Inherited and struct
implementations have regression coverage, and applied IDE fixes verify interface
dispatch, helper effects and nullable payload domains. Genuinely ambiguous
projections remain unresolved until other constraints distinguish them.

Constructed receivers constrained by variant interfaces contribute directional
conversion bounds when their projection is unambiguous. Original generic
arguments are retained per call before following inference substitutions.
Covariance and contravariance preserve their directions through nested arrays,
interfaces and delegates. Identity cases share variables without excluding value
types; differing reference arguments acquire class bounds, keeping boxing out of
reference variance. Nullable arguments, inherited/struct receivers, mixed variance
and repeated calls have regression coverage. Applied IDE fixes compare dispatch,
reference identity, nullable and async results, and newly admitted domains.
Array receivers use the same substituted interface endpoints before classifying
element conversions. Integer and nullable-value identity domains stay available;
reference covariance receives directional bounds. Runtime fixes verify original
and new reference/value/nullable array domains without copying their arrays.

When multiple variant interface projections have different shapes, their original
substituted conversions are checked before binding any inference variables.
Exact implemented interfaces take precedence over variance alternatives. A unique
remaining projection can be selected through covariance, contravariance
and nested delegate conversions even when structural equality cannot distinguish
it. Reversed declarations, inherited and struct receivers, fixed value branches
and independent calls have regression coverage. Runtime fixes compare explicit
interface dispatch, boxed argument behavior, reference identity and async results
with new reference, value and nullable payloads. Distinct viable variance
alternatives remain unresolved rather than choosing the first declaration.

When a proposal changes a supplied call or method group's instantiated signature,
inference retries with the affected parameter components fixed to their original
types. Receiver and unaffected tuple/container components can still generalize.
The refinement set grows monotonically over the original finite parameter leaves;
each retry starts with a fresh inference session and runs all candidate checks.
Unsuccessful refinements retain the original rejection. Tests cover boxed and
reference arguments, tuples, arrays, delegates, extensions, multiple callers and
separate source files. Applied IDE fixes compare generic helper type arguments,
interface dispatch, method-group signatures and object identity while admitting
new receiver domains and unrelated tuple/tag types.

Enumerator return parameters are mapped through their actual substituted types
before resolving Current, MoveNext/MoveNextAsync and disposal members. The
declared enumerator constraints use the original concrete substitutions for
conversion classification, preserving variance while relating inferred payloads.
Class, struct, inherited, extension and async-interface patterns have regression
coverage. Applied IDE fixes compare iteration/disposal effects, suspended async
iteration and nullable payload reference identity in original and new domains.

When an enumerator remains an existing type parameter, its declared constraint
domain and selected protocol members stay fixed while unrelated collection
arguments remain inferable. Sync/async, class/interface/chained constraints,
extensions, ref Current, explicit element conversions and distinct async member
owners have regression coverage. Runtime IDE fixes verify new tag domains with
class/struct enumerators, early-break disposal and faults after async suspension.

Custom builder state-machine bounds on Start and the selected safe/unsafe await
member are checked for dependencies on the builder result. Such dependencies
retain the original result domain, including nullable wrappers, while unrelated
parameters remain inferable. Unused completion members do not restrict results.
Fixtures emit before analysis and cover method overrides, async lambdas and local
captures. Applied IDE fixes compare protocol traces, suspended completion, result
identity (including boxed structs), nulls, faults and cancellation, including new
unrelated types. Debug and Release compare against their own protocol baselines.
Capture/layout-dependent state-machine requirements still have emission guards.

Root async methods with unmanaged state-machine builder requirements inspect
emitted private storage through Roslyn metadata symbols. Only type arguments that
physically occupy parameter/local, builder or awaiter fields receive unmanaged
constraints; unused parameters and phantom arguments remain free. Active safe
and critical completion members are distinguished. Layout probes can temporarily
strengthen generic constraints when the host compiler enforces them before emit.
Applied fixes verify actual runtime reference-containing layouts and reject
managed payload calls.

For C# 10 and later, private layout probes correlate nested async lambdas,
anonymous delegates and local functions through temporary method attributes.
Markers stay out of suggestions and applied fixes. Each active state machine
contributes only its physically stored generic arguments, including multiple
independent callbacks. Runtime fixes verify callback values, field layouts,
unmanaged-domain errors and unrestricted unused parameters.

Generic local-function calls propagate physical storage requirements through
selected type substitutions, forwarding chains, method groups and recursive
calls. A finite fixed point tracks source generic symbols; composite structs
constrain only stored arguments, preserving phantom parameters. Nullable local
results retain their wrapper and infer unmanaged element constraints. Applied
fixes exercise new unmanaged payloads and reject managed payloads; CLI and
packaged analyzer consumers verify Release builds.

For C# 8 and 9, portable debug metadata correlates nested async functions using
temporary local constants. The constants generate no state-machine fields and
remain private to probes; original language options and anonymous delegate
parameter lists are preserved. Block and expression bodies, mixed builders,
repeated local names, generic owners, hidden lines and unencoded source trees
have regression coverage. Incomplete marker correlation rejects a candidate. Applied C# 8 IDE fixes compare runtime traces and new
nullable/unmanaged domains. CLI and fresh NuGet consumers exercise both language
versions. C# 7.3 retains its compiler restriction on generic unmanaged constructed
state machines.

When supplied calls fail generic argument inference, contextual null/default
literals, empty collections and target-typed construction retain their declared
types. Implicit callback inputs retain their context, including expression trees,
async lambdas and nested lambda returns. Independent parameters and inferable
callback outputs remain generic. Refinement retries are bounded by signature
components and still pass the existing compilation, warning and binding checks.
Regression coverage includes nullable Task/ValueTask results, named extension
arguments across files, IDE fixes and runtime checks of original and new domains.

Contextual refinement also follows tuple fields (including nullable tuples),
nonempty collection elements using their original collection protocol, and
conditional and switch expressions. Direct tuple arguments retain generic typed
siblings; lifted nullable and conditional forms retain their required inference
context. Async callback returns are
checked against the task result shape. Callback output inference requires a
natural return type; untyped composite returns retain the required output context,
while typed composite returns can still generalize. Callback input refinement is
retried first so inferred outputs retain their available generality.

After contextual or binding refinement, candidate retained type components are
removed one at a time and the full inference and validation pipeline is rerun.
Only successful relaxations are kept; necessary call-site types remain concrete.
Redundant aliases can be removed even when the initial refinement leaves no
generic components. This recovers generic shared nullable inputs, tuple fields
and async results when another caller argument already supplies their type.
The process terminates by strictly removing retained components and supports
cancellation between trials. It establishes a deletion-minimal retained set,
without claiming a globally principal type for C# overloads and conversions.

Method-group arguments also retain the delegate input context needed for C#
output inference. Generic, overloaded, instance and reduced extension groups,
nullable/custom delegates and groups inside collection expressions have regression
coverage. Outputs and unrelated parameters remain generic when the supplied calls
permit it. Applied IDE fixes check overloads, constructed generic arguments,
receiver evaluation, nullable async results and newly supported output domains.

Closed delegate identities and generic containing-type arguments retain their
required context for implicit callbacks and expression trees. Refinement handles
both failed inference and incompatible natural delegate returns. Validated pin
removal keeps independently inferable outputs generic. Bare managed and Cdecl
method-address arguments retain input types while allowing inferred returns,
including ref returns and nested function-pointer inputs. Typed pointer/delegate
controls retain full generality. IDE fixes verify nominal types, expression-tree
delegate types, nullable async callbacks, pointer identity, ref aliasing and new
output domains. Callers with no remaining generic component still reject.

Conditional access infers member, method, delegate and indexer types from the
non-null receiver rather than anchoring the entire expression. Receiver frames
support nested chains, extension calls, nullable structs/tuples and event reads.
Value/reference lifting retains the required constraints without duplicating an
existing nullable wrapper; array slices retain their array shape. IDE fixes check
null short-circuiting, argument/getter order, async fallback, reference identity
and new value/reference payloads. Consumer checks cover CLI and packaged analysis.

Conditional conversions use the non-null branch receiver type, including struct
boxing. Reference lifting remains nullable inside coalescing even when contextual
type symbols omit the annotation; compiler-hosted consumer checks cover this.

Null and negated-null patterns preserve symbolic input types, including nullable
values, nullable references, arrays, property patterns, switch expressions, and
awaited Task/ValueTask results. IDE code-fix runtime checks cover null and non-null
inputs and suspension before testing an awaited nullable value. Numeric and enum
constant patterns continue to retain their operand domain.

The binary `value is Type` form and `value as Type` casts infer symbolic operand
types while keeping their explicit target fixed. Regression checks cover reference,
interface, array, generic and nullable value targets, original boxed inputs, caller
conversion preservation, single evaluation, and Task/ValueTask operands. IDE fixes
are executed over both their original domain and additional value/reference types.

Built-in equality and inequality against null preserve symbolic operand types,
including nullable tuples, arrays, delegates, conditional access and awaited results.
Nullable defaults retain their type because C# does not permit comparing arbitrary
`T?` to `default` or `default(int?)` with only a struct constraint. Comparisons between two variable operands
retain their original operator domain;
user operators continue through signature and dispatch validation. Runtime fixes
check single evaluation, suspension and the distinction between built-in null tests
and overloaded equality. Reference defaults and explicit reference null casts/constants require a class
constraint when their operand becomes a bare type parameter; array/delegate shapes
retain their inherent reference identity. Dynamic operations retain their binding context as described below.

Dynamic signatures retain `dynamic` separately from `object`, including nullable,
array, tuple and task shapes. Dynamic calls, members, indexers, operators, stores,
construction, conversions, conditions and await keep their original static operand
types. Other parameters/results can still become generic. Static object-to-dynamic
arguments retain both sides of their conversion so a signature change does not
silently turn a static call into a dynamic call. Candidate validation compares
dynamic sites, operand types, explicit type arguments and statically known method
groups; changing a known dynamically invoked group rejects the suggestion.
IDE runtime checks compare original and fixed overload selection, member/indexer
effects, user conversions, missing-member exceptions and await suspension/faults.

Identity conversions preserve nested object/dynamic annotations independently of
their CLR representation. Return and imported-argument boundaries rebuild tuple,
array, task and named/nested generic shapes while sharing unaffected inferred
variables. Thus Pair<dynamic, int> can become Pair<dynamic, T> at its input and
Pair<object, T> at its output without changing the return dynamic metadata or
forcing the second field to remain int. Runtime IDE checks compare DynamicAttribute
flags exactly and verify new decimal fields, tuple values, and array/task/container
reference identity. CLI and fresh package consumers assert the full return shapes.

Foreach supports dynamic collection expressions and statically bound dynamic
elements in arrays, IEnumerable<dynamic> and IAsyncEnumerable<dynamic>. Dynamic
collection expressions retain the runtime IEnumerable conversion; synchronous
dynamic resources retain the runtime IDisposable conversion. Independent values
can become generic through loops, using statements/declarations and sync/async
iterator methods. IDE runtime checks compare interface selection against competing
patterns, iteration effects, early exit and exception cleanup. Async checks suspend
both MoveNextAsync and DisposeAsync, preserve faults/cancellation, and verify
deferred iterator execution and early disposal of inner enumerators.

Instance method suggestions also check supplied calls through unresolved dynamic
receivers. Potentially applicable accessible calls block generalization, including
conditional calls and explicit type arguments matching either the old or proposed
arity. Argument names, required/optional/params slots, reference modifiers,
accessibility and known sealed receiver types exclude incompatible targets.
User/runtime conversions retain their resulting receiver type, and nullable value
boxing uses its underlying type. A cross-file runtime regression demonstrates that
an unchecked generic rewrite changes existing binder failures into successful
calls; protected analysis preserves those failures and an existing successful call,
while an unrelated instance method still receives a working generic IDE fix.

Cross-file global aliases to closed generic types can now generalize through a
safe extern alias import in the method's document. Candidate validation and IDE
fixes share the import logic, including nullable signatures, suspended awaits,
nested generic owners and constraints. Fix All adds each import once and retains
file banners, method comments and XML documentation. Alias declarations in other
documents retain their text. Conflicting names that require alias renaming remain
unsupported. Compiler-provided caller-info arguments are compared before and
after rewriting; imports that change caller line numbers are rejected, and IDE
formatting falls back to the original expression text when needed to preserve
CallerArgumentExpression values.

Array initializers use bound element conversions, including boxing, numeric and
nullable lifting, tuples and user-defined operators. Explicit arrays retain their
written element type; implicit arrays infer a shared element scheme and recheck
the rewritten array type. Rectangular, jagged and bare declaration initializers
are supported. Dimension expressions retain their bound integer endpoints.
Null elements preserve reference requirements; default elements carry nullable
annotations that also work for new value-type instantiations. Applied IDE fixes
verify array shapes, element/conversion order, reference identity, new domains,
negative dimensions and suspended/faulted/canceled nullable async initializers.

Partial methods infer from their implementation and rewrite both signatures as
one candidate. The IDE fix updates both documents, preserving each part's
existing generic names, constraints and trivia. Definitions emit no duplicate
suggestion. Same-file and separate-file checks cover nullable/async methods,
recursion, supplied calls and method groups, callbacks, ref returns and iterators.
Project Fix All tracks later methods after a partner-file edit. Async partial
methods without an emitted state-machine attribute use a portable-PDB layout
probe when a custom builder requires unmanaged storage; temporary constraints
are mirrored to both signatures. IDE application requires editable source parts.

When partial parts differ in nullable or dynamic annotations, each part retains
its contract. Declaration-side nullable bounds participate in inference, and
dynamic/object boundaries retain their fixed leaves while independent components
can generalize. Applied fixes preserve source and emitted metadata across arrays,
tuples, nested generic owners, task payloads and existing type parameters. Runtime
checks verify dynamic attributes, nullable values, identity and suspended async
results with faults and cancellation in newly admitted generic domains.

If an inferred constraint breaks a supplied call, analysis can retain the
affected original signature components and retry. Class, constructor, base,
interface, struct and unmanaged failures preserve their original boxing and
user-defined conversions while independent parameters and tuple slots remain
generic. Callee binding is inspected separately from return conversions.
Exact-signature tests cover both caller locations, named/extension/open generic
calls, satisfied constraints and cases with no generic component left. Applied
Fix All runtime checks compare original values, conversion/helper order, boxing,
new independent domains and partial async suspension, faults and cancellation.

Nullable constraint and result warnings at supplied calls can also trigger
signature refinement. This preserves original nullable argument conversions and
return contracts while independent parameters and tuple siblings stay generic.
Original warning suppression and warnings-as-errors settings are respected;
other newly introduced warnings still reject candidates. Exact signatures and
IDE runtime checks cover nullable reference/value inputs, array identity, tuple
siblings, generic caller wrappers and async suspension, faults and cancellation.

Parenthesized assignment targets and tuple deconstruction stores preserve nullable
storage paths, including ref-return targets and contextually typed `default`
elements. Compiler warnings refine the affected tuple element independently.
Captured-storage discovery follows each tuple target through nested, conditional
and switch tuple values, including writes through known ref aliases. Callbacks
created before those writes retain their lifetime nullability; earlier ordinary
value copies and explicitly typed defaults retain their separate boundaries.
Conditional and switch tuple sources preserve each slot's nullability even when
another branch reads that slot before storing defaults. Ref-return warnings on a
whole branch constrain the affected destination while independent tuple elements
keep broader domains. Storage discovery resolves tuple slots through translated
query scopes, including nested and callback-hosted queries.
Task and ValueTask checks cover stores after suspension, earlier value copies,
reference and nullable-value domains, deferred query execution, faults and
cancellation. C# 8/9/13 CLI and packaged consumers verify these signatures.

Calls, delegate invocations, and field/property/indexer reads honor `MaybeNull` and
`NotNull` contracts,
including contracts imported from emitted metadata. Nullable results preserve
independent inputs and tuple siblings; arrays, existing generic parameters and
Task/ValueTask results retain their structural relationships. Physical nullable
value types stay intact. Ref-return reads use the contract, while ref aliases
and writes retain the storage type. Applied fixes verify default/null results,
identity, explicit struct constructors, suspension, faults and cancellation.
C# 8 uses representable class/struct bounds for newly inferred nullable results;
existing unconstrained generic return types retain their declared form there.
`NotNullIfNotNull` refines results when the named argument is demonstrably
non-null. Named arguments, multiple guards, extension calls, constants, existing
constraints, source/metadata delegates and explicit arrays/collections are covered.
Unconstrained or nullable inputs retain nullable results, preserving their wider
domains. Omitted generic defaults are distinguished from fixed non-null constants;
contextual default arguments no longer pin generic calls to their original types.
Implicit params packs retain nullable results where compiler flow cannot prove
the condition. Member reads include getter-return attributes, conditional access,
queries and awaited Task/ValueTask receivers. Assignments and ref aliases retain
the declared storage type. Conditional indexer results follow getter-return
attributes; property-level `NotNullIfNotNull` does not refine reads in the pinned compiler.
Stronger wrapper-contract forwarding and other member flow
contracts still need further work.

Value arguments honor `AllowNull` input contracts on source and metadata methods
and delegates. Nullable inputs retain their relationship to the receiver or other
arguments without widening the declared result or stored payload. Named calls,
arrays, nullable values, queries and awaited Task/ValueTask arguments are covered.
Expanded params and collection expressions retain their non-null container shape;
ordinary generic inputs gain no unnecessary C# 8 class/struct bounds. Explicit
reference arguments keep their invariant storage contract. Applied fixes verify
stored and returned identity, nullable-value domains, lazy queries, helper counts,
await suspension, exact faults and cancellation. Generic method type inference
still follows the compiler's nullable argument rules.

Reads of locals and parameters passed to `MaybeNull`, `MaybeNullWhen`, `NotNull`
and `NotNullWhen` contracts use the generalized candidate's compiler flow state
and annotation. This distinguishes an ignored/failure result from a proven-success
read without treating unconstrained `T` as newly annotated `T?`. Direct ternary
branches retain the joined compiler annotation. Copies, reassignment, negated
conditions, delegates, emitted metadata and awaited Task/ValueTask receivers have
regression coverage. Explicit reference arguments still bind their storage type.
C# 8 retains representable value bounds when a nullable output requires them;
compiler facts are rechecked until stable, with cyclic refinements rejected.
Applied fixes execute original and new reference/nullable-value domains, compare
helper counts and identities, and check suspension, exact faults and cancellation.
Member reads also honor `MemberNotNull` and `MemberNotNullWhen`, including getter
attributes, inherited/static members and source/metadata contracts. Compiler facts
use each expression span (start and length), preserving nested reads, receiver
isolation and invalidating writes;
failure-branch optionality does not widen independent fallback parameters. Query
selectors and Task/ValueTask results retain these relationships. Ordinary nullable fields
passed to parameter postconditions now participate in the same refinement while
reference binding preserves their declared storage type. Applied fixes verify
member/getter counts, receiver identity, mutations, lazy query disposal, nullable
value/reference domains, suspension, exact faults and cancellation. CLI and package
consumers cover member-state declarations in C# 9/13 and indirect field outputs in
C# 8/9/13. The pinned compiler rejects member-state declarations in C# 8 (CS8400).
Tuple-field reads consume the same compiler facts as ordinary members, with named
and `ItemN` fields normalized to one storage identity. Nested tuple outputs,
reference aliases, metadata and Task/ValueTask results have signature controls.
Storage discovery also accounts for `MaybeNull`/`MaybeNullWhen` out writes into
tuple and array slots, including parenthesized targets and known ref aliases.
The output binder admits the annotated storage independently of the read-state
refinement, preserving dictionary payload relationships and independent tuple
siblings or fallback inputs. Physical nullable values retain their representation;
fixed value-type output contracts add no unnecessary optionality. Mixed
`MaybeNull`/`NotNull` outputs retain the pinned compiler's nullable behavior.
Applied fixes execute tuple copies and ref mutations, one-time array indices,
dictionary hits/misses, reference identity, nullable-value domains, struct defaults
without constructor calls, suspended awaits, exact faults and cancellation.
CLI/package consumers check representable C# 8 bounds and broader C# 9/13 domains.
Nullable write discovery also handles `ref` parameters with `MaybeNull` or
`MaybeNullWhen` output contracts. Source/metadata methods and delegates preserve
array/tuple storage, aliases, copied values and relationships with other inputs.
`AllowNull` inputs and conditional `NotNullWhen`/`NotNull` guarantees participate
through the same compiler-checked storage and read paths. Fixed value-type and
struct-constrained references keep their original domains. Applied fixes verify
reference/nullable-value inputs, struct defaults without constructors, copied
identities, ref tuple mutations, one-time indices, faults before writes and
Task/ValueTask suspension, exact faults and cancellation. CLI/package consumers
exercise C# 8 class/struct bounds and broader C# 9/13 nullable domains.
Explicit readonly references honor `AllowNull` input contracts for `in` and
`ref readonly` parameters. Storage retains its runtime type, and unannotated inputs
gain no unnecessary nullable wrapper or class/struct bound. Source/metadata,
array elements, aliases, physical nullable values and awaited inputs have controls.
Applied fixes verify unchanged inputs, observed identities, one-time indices,
Task/ValueTask suspension, exact observer/await faults and cancellation.
Conflicting return annotations follow the pinned compiler: method/delegate
`MaybeNull` is retained alongside unconditional `NotNull`, while a proven
`NotNullIfNotNull` still refines the result. Member reads use their own precedence;
source property annotations override getter-return annotations. Emission forwards
property attributes to getter returns, so imported getters are checked separately.
Signature and execution checks cover these distinctions, null results and required
getter faults. CLI/package consumers exercise C# 8/9/13; `ref readonly` parameter
declarations are exercised in C# 13.
Direct wrapper parameters forward `DisallowNull` when generalized-candidate
compiler diagnostics show that the callee requires it. The candidate, displayed
signature, IDE fix and emitted parameter metadata retain the inferred contract.
This avoids replacing a value precondition with a blanket `notnull` type bound:
non-null nullable-value inputs remain usable. Guards, existing attributes and
suppressed warnings prevent unnecessary forwarding; user attribute syntax stays
intact. Named calls, delegates, source/metadata helpers, readonly parameters and
Task/ValueTask wrappers have regression coverage. Partial implementations emit the
contract once while both declarations retain matching generic types. Applied fixes
verify reference identity, nullable-value inputs, exact helper faults, suspension,
cancellation and reflected metadata. CLI/package consumers cover C# 8/9/13 and
C# 9/13 non-void partial declarations. Projected tuple fields, array elements,
member reads, delegate/readonly arguments and Task/ValueTask payloads infer
`notnull` only on the component that needs it. Unrelated tuple components stay
unconstrained. Effective compiler diagnostics drive these bounds; guarded and
suppressed calls avoid unnecessary restrictions. Source/metadata helpers, applied
IDE fixes and C# 8/9/13 CLI/package consumers cover these cases. Local aliases
and conditional choices now forward value preconditions to the necessary source
parameters. Compiler-flow trials remove redundant contracts, retaining unrelated
nullable inputs and non-null nullable-value callers. Default-backed projected
storage instead requires a non-nullable value domain when the original value type
proves that domain applies. These candidates rerender both parameters and results
after constraint normalization. Applied fixes verify zero-initialized structs
without invoking constructors, mutation, identity, suspension, exact faults and
cancellation. Remaining relational and optional-storage contracts still need work.
Member writes now distinguish `AllowNull` and `DisallowNull` input contracts from
read and storage types. An allowed null/default store retains the generic owner
payload, while subsequent field/property reads follow compiler flow; indexer reads
retain their compiler contract. Assignment expressions preserve the assigned value's
nullability. Required field/property/indexer writes forward value preconditions or
infer projected bounds, including tuple deconstruction and async writes. Mixed
input flags give `DisallowNull` precedence. Source property annotations and emitted
setter-parameter annotations follow their distinct compiler behavior. Ref/out
storage and physical nullable values keep their existing representation. Source,
metadata, guard, suppression, receiver-isolation and C# 8/9/13 CLI/package checks
cover these rules. Applied fixes verify null results, identity, receiver/index
counts, writes, suspension, exact setter/await faults and cancellation.
Explicit collection elements now honor `AllowNull` on their selected Add input
without making the collection owner's payload nullable. Maybe-null results,
contextual defaults, arrays and physical nullable values retain their payload
relationships. Spreads retain the enumeration element's nullability, as required
by the compiler's spread conversion. Generic call results now follow generalized
candidate flow, including the nullable result inferred through packed params
collections. Contextual defaults are inferred
from the accepting input; explicit numeric endpoints and Add/builder/protocol
selection remain checked. Regression fixtures record the pinned compiler's absent
ordinary `DisallowNull` argument warnings for unconstrained collection-expression
inputs and spreads. Explicit Add calls retain diagnostic-driven precondition forwarding.
Compiler hosts that warn on the annotated Add input trigger a fresh inference
pass with an optional collection element domain. This follows actual diagnostics
and retains unannotated owners on hosts that accept them. CLI and package fixtures
verify both host behaviors.
Source/metadata fixtures and applied IDE fixes verify null/default items, identity,
constructor/Add/helper counts, spread disposal, Task/ValueTask suspension, exact
helper/Add/await faults and cancellation. C# 13 CLI/package fixtures verify the new
signatures; earlier-language consumers retain their existing feature boundaries.
Generic invocation results now preserve nullable arguments inside constructed
return types. Generalized-candidate type information refines lists, arrays,
delegates, lazy enumerables, named tuples and deeper containers without propagating
nullable return annotations into inputs or unrelated result members. Return views are rebuilt independently of argument bindings; ref/out
storage retains its existing contract. Declared return annotations and default
markers survive the refinement. Source/metadata fixtures cover nullable-value and
fixed-type controls, container-only nullability, non-null container contracts and
awaited ValueTask results. Applied fixes verify identity, null/default payloads,
array runtime types, selective tuple members, iterator laziness/disposal, helper/factory counts, suspension,
exact faults and cancellation in original/reference/nullable-value domains.
C# 8 uses representable value-domain bounds; C# 9/13 retain unconstrained nullable
arguments. CLI/package fixtures check these language-specific signatures.
Nullable call inputs can also refine a generic receiver's payload independently
of other inputs. For example, passing a maybe-null value to `Pair<T>` can suggest
`Pair<T?>` while retaining a separate `T value` or `Task<T>` input. Refinement uses
the candidate compiler's argument diagnostics and preserves caller-pinned slots.
Source/metadata and applied-fix checks cover direct delegates, aliases, instance
methods and Task/ValueTask suspension, faults and cancellation; C# 8/9/13 CLI and
package checks enforce representable signatures. Nullable async coverage also
includes configured awaits, custom awaiters, iterators and foreach elements.
Receiver promotion currently requires a direct owner type-parameter formal and
one traceable input origin. Compound formals, other nullable argument shapes and
remaining indirect-storage flow still need work.

Solution-scale benchmark inputs, quality checks, measurements and reproduction
commands are documented in [PERFORMANCE.md](PERFORMANCE.md).
