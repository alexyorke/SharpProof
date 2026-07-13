### New Rules

Rule ID | Category | Severity | Notes
-------|--------|--------|-----
SP0024 | Usage | Error | Reports malformed SharpProof contract arguments such as empty `[Ensures]` conditions, undefined `[ExpectedComplexity]` values, and unknown `[AllowedCapabilities]` bits.
SP0025 | Configuration | Warning | Reports invalid `sharpproof_*` analyzer option values that would otherwise fall back to defaults silently.
SP0026 | Usage | Warning | Reports SharpProof-looking attribute names whose type identity is not in `SharpProof.Attributes` or an opt-in source-stub namespace.
SP0027 | Contracts | Warning | Reports calls that do not prove a callee `[Requires]` precondition.
SP0028 | Contracts | Warning | Reports `[Requires]` preconditions that could not be parsed, lowered, or proven within the supported bounded proof surface.
SP0029 | Usage | Error | Reports `[Requires]` attributes applied to non-method-like declarations; its code fix moves property/indexer attributes to the getter.
SP0030 | ExceptionFlow | Warning | Reports escaping exceptions that violate `[DoesNotThrow]` or `[AllowedExceptions]` contracts.
SP0031 | Usage | Error | Reports `[DoesNotThrow]` and `[AllowedExceptions]` outside method-like declarations and getter-bearing property/indexer aliases.
SP0032 | Configuration | Warning | Reports malformed, empty, unsupported, partially ignored, or stale SharpProof analyzer AdditionalFiles, including exact effect-summary identity and artifact-source mismatches.
SP0033 | ExceptionFlow | Info | Opt-in unknown runtime-hazard candidate with stable proof, reason, trigger, explain, and baseline evidence; enabled by the `unknowns` runtime-hazard modes.
SP0034 | Suggestions | Info | Opt-in high-confidence `[ZeroAllocations]` suggestion with stable evidence and a code fix.
SP0035 | Suggestions | Info | Opt-in high-confidence `[AllowedCapabilities]` suggestion with an inferred exact capability set and a code fix.
SP0036 | Suggestions | Info | Opt-in high-confidence `[ExpectedComplexity]` suggestion with an inferred bounded complexity class and a code fix.
SP0037 | Suggestions | Info | Opt-in inferred `[DoesNotThrow]` or `[AllowedExceptions]` suggestion with confidence metadata and a code fix.
SP0038 | Suggestions | Info | Opt-in high-confidence simple `[Ensures]` suggestion with a code fix.
SP0039 | Suggestions | Info | Opt-in high-confidence guard-derived `[Requires]` suggestion with a code fix.
SP0040 | Review | Info | Opt-in structured report for applied and overridden purity trust shortcuts, including exact symbol, source, value, and override disposition.
SP0041 | Nullability | Warning | Reports a reachable normal return that violates a non-null nullable return contract.
SP0042 | Nullability | Warning | Reports a reachable normal completion that violates a nullable parameter postcondition.
SP0043 | Nullability | Warning | Reports a reachable normal completion that violates a member-not-null contract.
SP0044 | Nullability | Warning | Reports a null-forgiving operator whose operand is proven null.
SP0045 | Nullability | Info | Reports a null-forgiving operator whose operand is already proven non-null.
SP0046 | Nullability | Info | Reports a nullable contract proved by every relevant completion path.
SP0047 | Nullability | Info | Opt-in report for nullable verification that ended unsupported or unknown.
SP0048 | AsyncCorrectness | Warning | Reports direct awaits of nullable results produced by null-conditional access.
SP0049 | AsyncCorrectness | Warning | Reports Task values interpolated or concatenated as text without awaiting their results.
SP0050 | AsyncCorrectness | Warning | Reports TaskCompletionSource construction that omits a proven RunContinuationsAsynchronously option.
SP0051 | AsyncCorrectness | Warning | Reports async void methods that are not event-handler shaped.
SP0052 | AsyncCorrectness | Warning | Reports Task.Result, Task.Wait, and GetAwaiter().GetResult() inside async methods.
SP0053 | AsyncCorrectness | Warning | Reports non-async Task-returning methods that return null.
SP0054 | AsyncCorrectness | Warning | Reports Task values used directly as disposable resources.
SP0055 | AsyncCorrectness | Info | Reports public async argument validation whose exceptions are captured by the returned task instead of thrown at invocation time.
SP0056 | CollectionSafety | Warning | Reports direct mutation of an ordinary mutable collection inside a foreach over the same symbol.
SP0057 | Correctness | Warning | Reports for-loop iteration variables captured by escaping anonymous functions.
SP0058 | Design | Info | Reports structs with writable instance state.
SP0059 | ResourceLifetime | Warning | Reports definitely allocated disposable fields whose containing type lacks the matching disposal interface.
SP0060 | ResourceLifetime | Warning | Reports HttpClient construction inside loops.
SP0061 | Concurrency | Warning | Reports captured locals or fields mutated without visible synchronization in known parallel callbacks.
SP0062 | Concurrency | Info | Reports LINQ/interface enumeration over concurrent collections where snapshot semantics are not guaranteed.
SP0063 | Performance | Info | Reports boxing conversions inside loops.
SP0064 | Nullability | Warning | Reports immediate dereference of results from known default-returning lookup and LINQ APIs.
SP0065 | Performance | Info | Reports immediate in-memory LINQ processing after IQueryable materialization.
SP0066 | Correctness | Warning | Reports direct state mutation inside deferred LINQ lambdas.
SP0067 | Compatibility | Info | Reports source-only helper calls inside IQueryable expressions that may not be provider-translatable.
SP0068 | Serialization | Info | Reports source-declared reference cycles serialized by System.Text.Json without explicit cycle handling.
SP0069 | Serialization | Warning | Reports JsonIgnore attributes from a different serializer than the active serialization call.
SP0070 | Usage | Info | Reports Required attributes on non-nullable value members that cannot represent omitted input.
SP0071 | Correctness | Warning | Reports unchecked multiplication used directly as an array or stack allocation length.
SP0072 | Review | Info | Reports broad pragma and attribute suppressions without reviewable scope or justification.
SP0073 | Nullability | Info | Reports explicit nullable-disable directives.
SP0074 | Correctness | Warning | Reports suspicious comparisons and arithmetic whose local or parameter operands are identical.
SP0075 | ResourceLifetime | Warning | Reports services resolved from IServiceProvider and then disposed by consuming code.
SP0076 | Correctness | Warning | Reports deferred LINQ queries that are constructed and then discarded without enumeration.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
-------|------------|------------|------------|------------|-----
SP0015 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
SP0016 | Capabilities | Warning | Capability | Warning | Normalize capability diagnostics to the public `Capabilities` taxonomy.
