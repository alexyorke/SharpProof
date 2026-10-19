# HM inference prototype

The standalone `SharpProof.HmInference.slnx` contains a rank-1 Hindley-Milner
inference core, a C# generic-signature adapter, a Roslyn analyzer (`HM0001`),
a CLI, samples, and executable regression checks. It is separate from the
production analyzer and main solution.

## Run

From the repository root, in the canonical Linux amd64 tooling container:

```sh
docker compose run --rm tooling dev src/SharpProof.HmInference/verify.sh
```

The script restores and builds in a private container workspace, runs regression
checks and C#/HM samples, and exports evidence under `artifacts/hm-inference/`.
Compiler outputs remain private. It retains the pinned SDK and Arcade imports
and adds no NuGet dependencies. As in the initial prototype, the exploratory
build disables repository style analyzers; this is not production CI acceptance.

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
    new HmApply(new HmVariable("id"), new HmLiteral(HmLiteralKind.Int)),
    new HmApply(new HmVariable("id"), new HmLiteral(HmLiteralKind.Bool))));
var type = HmCore.PrincipalType(expression); // (int, bool)
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
| Arrays, jagged/rectangular arrays | Preserve rank; infer element types through indexing and Length/Rank |
| Implicit array construction | Unify initializer elements |
| Explicit array construction | Preserve declared element types |
| Synchronous lambdas | Infer inputs/results; retain explicit annotations |
| Immutable locals and nested blocks | Share types by symbol and respect scope |
| Mutable locals/parameters and array-element stores | Keep one monotype per symbol; preserve assignment conversions |
| Compound assignments and increments | Retain original built-in operand types; skip custom operators |
| Fallthrough branches, while/do/for, array foreach | Collect return constraints and keep block/loop locals scoped |
| Break/continue and traditional switch | Track each exit separately; consume it at the correct loop/switch |
| Tuple array foreach deconstruction | Bind nested tuple elements, discards and explicit types |
| Checked/unchecked blocks and throws | Preserve overflow contexts and treat throws as nonreturning exits |
| Tuple deconstruction declarations | Infer nested tuple bindings and discards; retain explicit element types |
| Explicit/target-typed object construction | Bind constructor arguments and preserve the selected constructor |
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
compiler errors, including supplied callers and generic constraints. Selected
method definitions inside the body are compared before/after to reject overload
rebinding. Selected constructors are checked too. Roslyn validates C# constraints;
the engine does not invent them. Escaped source identifiers are retained in signatures.

The adapter explicitly skips async, ref/out, nullable
annotations, user-defined operators, expanded non-array params collections, null and
coalescing, existing generic root methods, instance root methods, attributes,
unsupported patterns and other unimplemented syntax. It does not infer mutually
recursive C# method groups or synthesize generic constraints. Imported signatures
and retained annotations can make inference conservative.

Tuple deconstruction supports declarations, reassignment and mixed targets for
local/parameter identifiers and discards. Custom Deconstruct methods (including
nested ones) remain unsupported. Object
initializers, ref constructors, FormattableString and custom interpolation handlers
are also unsupported. A nameof member lookup may reject a candidate even though
its operand is not executed, because the lookup still has to compile.

Loop analysis accumulates constraints over monomorphic bindings; it does not
generalize at iteration boundaries or prove termination. Loops conservatively
fall through, so methods whose only return is inside an infinite loop are skipped.
Goto, foreach over custom enumerables, property/field stores and void delegate
calls remain unsupported. Switch statements support constant labels and guards,
not declaration/type patterns. Try/catch/finally and rethrow are unsupported.
Ordinary void
method calls can appear as expression statements. The standalone HM core remains
pure; these imperative extensions belong to the C# adapter only.

Break exits are consumed by the nearest loop or switch; continue exits propagate
through switches to the nearest loop. Throws contribute no return constraint;
an all-throw body retains declared result information when producing a C# signature.
The suite exercises nested exit ownership, tuple iteration, thrown exceptions and
checked overflow versus unchecked wrapping at runtime.

Candidates are checked independently. Applying multiple suggestions requires
another compile. Public APIs, binary consumers, reflection and external callers
need review. No source is rewritten and no automatic fix is offered. The analyzer
is exercised through Compilation.WithAnalyzers in the SDK-targeted CLI; it is
not a portable IDE NuGet package. Recompilation per candidate targets small
experiments, not large-solution performance.

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
