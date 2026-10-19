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
| Nullable types, null/default/coalescing/conditional access | Open |
| Generic constraints and name allocation | Existing constraints retained and names reserved; synthesis remains open |
| Async/await and iterators | Open |
| Try/catch/finally, using/lock and goto | Open |
| Declaration/type/property/list patterns | Open |
| Custom enumerables and Deconstruct | Open |
| Field/property/indexer stores and object/collection initializers | Verified: conservative receiver/value types, overload/conversion tests and runtime probe; anonymous objects and record with expressions remain open |
| Void/custom delegates and method groups | Open |
| User operators/conversions and non-array params collections | Open |
| Nested generic, pointer/function-pointer and ref-like types | Open |
| Mutually recursive C# groups | Open |
| Solution-scale cost | Open: no representative end-to-end performance baseline |
| API/behavior compatibility | Supplied static calls retain instantiated signatures; separate-file runtime regressions cover conversions/boxing/overloads. Dynamic, reflection, binary and absent external consumers still need a compatibility policy |

The pure HM core's rank-1 restriction and C# local monomorphism are properties of
their respective type systems, not omissions that can be removed by making locals
unsoundly polymorphic. An ML parser/interpreter is not required for a C# analyzer.
