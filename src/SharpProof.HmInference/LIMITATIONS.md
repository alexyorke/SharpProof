# Completion ledger

The target is a production C# analyzer with an IDE package and automatic code
fixes, not a claim that a finite test suite proves universal correctness. Items
below stay open until the implementation and its relevant verification exist.
The README describes today's supported subset; it is not the completion spec.

| Requirement | Current evidence / remaining work |
| --- | --- |
| Portable IDE analyzer | Implemented: netstandard2.0 analyzer, packaged-consumer load test; interactive IDE acceptance remains open |
| IDE code fix | Verified: MEF discovery and application/trivia/stale-input/cancellation tests |
| Safe Fix All | Implemented: sequential document/project/solution provider; conflict regression checks |
| NuGet installation | Verified: package contents and fresh-cache net8.0 consumer build |
| Normal repository checks | Verified: default build with repository analyzers enabled, zero warnings/errors |
| Complete project loading | Open: CLI compiles loose files with runtime references |
| Instance and generic root methods | Implemented: declaration regression checks |
| Attributes, optional/ref/out/in/params root parameters | Implemented: declaration regression checks |
| Nullable types, null/default/coalescing/conditional access | Verified: reference/value wrappers, class/struct constraints, conditional array elements/lifting, coalescing assignments with preserved conversions and retained annotations on existing type parameters; imported unconstrained T? schemes preserve annotation semantics across value/reference domains, composed containers/awaits and explicit/constrained instantiations |
| Generic constraints and name allocation | Existing constraints retained and names reserved; class/class?/struct/unmanaged/notnull/new(), interface, base and dependent constraints imported from calls and constructed signature types; constraint simplification and unsupported type shapes remain open; new compiler warnings reject candidates |
| Task/ValueTask async roots and await | Verified: generic/non-generic roots, nullable results, generic custom instance/inherited/extension awaiters and awaiter bindings rechecked across supplied files; IDE-fix runtime checks cover suspension, cancellation and faults |
| Void and async void roots; async/anonymous lambdas | Implemented: delegate Invoke signatures, per-lambda async contexts and explicit return types; runtime code-fix checks cover captures, callbacks, suspension, faults and cancellation |
| Sync/async iterators | Implemented: generic/non-generic enumerable and enumerator roots, yield return/break, preserved yield conversions and finalizers; verified applied-code-fix runtime checks cover laziness, early disposal, finalization, suspension, cancellation and faults |
| Sync/async foreach | Implemented: arrays, generic interfaces, nested and inherited enumerator patterns, configured/cancelable async enumeration and tuple deconstruction; selected protocol members/conversions rechecked. Custom implicit awaitable types stay concrete to preserve GetAwaiter overloads. Generalized implicit custom awaiters in foreach/extension enumerators, ref foreach and type-parameter enumerator patterns remain open |
| Custom async builders | Open |
| Generic instance calls and explicit method type arguments | Implemented: receiver/base/interface substitutions, method constraints and retained explicit arguments |
| Try/catch/finally, using/await using and lock | Implemented: filtered catches, rethrow, finalizer flow and concrete lifetime receivers; IDE-fix runtime checks preserve disposal pattern selection. Structural disposal constraint generalization remains open |
| Goto, labels and C# return completeness | Verified: named/case/default jumps, infinite-loop returns and unreachable typing; Roslyn checks candidate reachability/definite assignment. Applied IDE-fix runtime checks preserve routing and pending finally/disposal |
| Explicit, constant and uninitialized locals | Verified: declared types and initializer/assignment conversions retained; runtime checks cover widening and boxing identity |
| Boolean condition conversions | Verified: primitive Boolean inference and retained user-defined conversion/true-operator types, including loops, conditional expressions, guards and filters |
| Declaration/type/property/list patterns | Open |
| Custom Deconstruct methods | Verified: instance, generic extension, base/interface projections, nested/mixed targets, assignment results and sync/async foreach; member/indexer stores and conversions retained. Implicit bindings/conversions are rechecked across supplied files; IDE-fix runtime checks compare evaluation/setter order |
| Field/property/indexer stores and object/collection initializers | Verified: conservative receiver/value types, overload/conversion tests and runtime probe; anonymous objects and record with expressions remain open |
| Void/custom delegates and method groups | Implemented: Action and generic/non-generic delegates, static/instance method groups, conservative variance/extension conversions and cross-file binding checks; ref delegates, attributed/modified lambdas and expression trees remain open |
| User operators/conversions and non-array params collections | Open |
| Nested generic, pointer/function-pointer and ref-like types | Open |
| Mutually recursive C# groups | Open |
| Solution-scale cost | Open: no representative end-to-end performance baseline |
| API/behavior compatibility | Supplied static calls retain instantiated signatures; separate-file runtime regressions cover conversions/boxing/overloads. Dynamic, reflection, binary and absent external consumers still need a compatibility policy |

The pure HM core's rank-1 restriction and C# local monomorphism are properties of
their respective type systems, not omissions that can be removed by making locals
unsoundly polymorphic. An ML parser/interpreter is not required for a C# analyzer.
