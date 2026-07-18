using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor AwaitNullConditionalRule = CreateCommonBugDescriptor(
        AwaitNullConditionalId,
        "Awaiting a null-conditional expression",
        "Awaiting null-conditional expression '{0}' can dereference a null awaitable",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "A null-conditional invocation produces a null awaitable when its receiver is null. Coalesce to a non-null awaitable or guard the receiver before awaiting.");

    public static readonly DiagnosticDescriptor TaskConvertedToStringRule = CreateCommonBugDescriptor(
        TaskConvertedToStringId,
        "Task converted to text without awaiting",
        "Task expression '{0}' is converted to text instead of awaiting its result",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "String concatenation and interpolation call ToString on Task objects; they do not use the asynchronous result.");

    public static readonly DiagnosticDescriptor TaskCompletionSourceContinuationsRule = CreateCommonBugDescriptor(
        TaskCompletionSourceContinuationsId,
        "TaskCompletionSource may run continuations synchronously",
        "TaskCompletionSource construction '{0}' does not prove RunContinuationsAsynchronously",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "TaskCompletionSource should normally include TaskCreationOptions.RunContinuationsAsynchronously to prevent completing threads from running arbitrary continuations inline.");

    public static readonly DiagnosticDescriptor AsyncVoidRule = CreateCommonBugDescriptor(
        AsyncVoidId,
        "Async void method is not an event handler",
        "Async void method '{0}' is not an event handler; return Task so callers can observe completion and exceptions",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Async void prevents callers from awaiting completion and routes exceptions through the current synchronization context. It is reserved for event-handler-shaped methods.");

    public static readonly DiagnosticDescriptor BlockingAsyncRule = CreateCommonBugDescriptor(
        BlockingAsyncId,
        "Async method blocks on asynchronous work",
        "Async method '{0}' synchronously blocks on '{1}'",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Calling Task.Result, Task.Wait, or GetAwaiter().GetResult() inside async code can deadlock or starve worker threads. Await the operation instead.");

    public static readonly DiagnosticDescriptor NullTaskReturnRule = CreateCommonBugDescriptor(
        NullTaskReturnId,
        "Task-returning method returns null",
        "Task-returning method '{0}' returns null; callers that await it will throw",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "A non-async method whose declared return type is Task or Task<T> must return a task object, not null.");

    public static readonly DiagnosticDescriptor TaskUsedAsDisposableRule = CreateCommonBugDescriptor(
        TaskUsedAsDisposableId,
        "Task used as a disposable resource",
        "Task expression '{0}' is disposed by using instead of awaiting its result",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        "Using a Task disposes the task object; it does not await the asynchronous operation or manage the resource produced by that operation.");

    public static readonly DiagnosticDescriptor AsyncValidationDeferredRule = CreateCommonBugDescriptor(
        AsyncValidationDeferredId,
        "Public async parameter validation is deferred",
        "Validation in async method '{0}' is captured by the returned task; use a synchronous wrapper when fail-fast argument validation is required",
        "AsyncCorrectness",
        DiagnosticSeverity.Info,
        "Exceptions thrown from an async Task method, including validation before its first await, are stored in the returned task. A synchronous wrapper is required when callers must observe argument errors at invocation time.");

    public static readonly DiagnosticDescriptor CollectionMutationDuringEnumerationRule = CreateCommonBugDescriptor(
        CollectionMutationDuringEnumerationId,
        "Collection mutated during enumeration",
        "Collection '{0}' is mutated by '{1}' while it is being enumerated",
        "CollectionSafety",
        DiagnosticSeverity.Warning,
        "Mutating the same ordinary mutable collection inside its foreach body invalidates the active enumerator and commonly throws InvalidOperationException.");

    public static readonly DiagnosticDescriptor CapturedLoopVariableRule = CreateCommonBugDescriptor(
        CapturedLoopVariableId,
        "For-loop variable captured by an escaping closure",
        "For-loop variable '{0}' is captured by a closure that can observe a later iteration value",
        "Correctness",
        DiagnosticSeverity.Warning,
        "For-loop iteration variables are shared across iterations. Copy the value into a local inside the loop before capturing it in a lambda that can escape the iteration.");

    public static readonly DiagnosticDescriptor MutableStructRule = CreateCommonBugDescriptor(
        MutableStructId,
        "Struct exposes mutable instance state",
        "Struct '{0}' has mutable instance state; copies can be modified independently",
        "Design",
        DiagnosticSeverity.Info,
        "Mutable value types are frequently modified through accidental copies. Prefer readonly struct, record struct with immutable members, or a class when shared mutation is intended.");

    public static readonly DiagnosticDescriptor OwnedDisposableFieldRule = CreateCommonBugDescriptor(
        OwnedDisposableFieldId,
        "Owned disposable field has no owner disposal contract",
        "Type '{0}' creates disposable field '{1}' but does not implement '{2}'",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "A field initialized or assigned from a local allocation is owned by its containing type. The owner must expose the matching deterministic disposal lifecycle.");

    public static readonly DiagnosticDescriptor HttpClientInLoopRule = CreateCommonBugDescriptor(
        HttpClientInLoopId,
        "HttpClient created repeatedly inside a loop",
        "HttpClient is created inside loop '{0}'; reuse a client or use IHttpClientFactory",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "Repeated HttpClient construction creates avoidable connection pools and can exhaust sockets under sustained load.");

    public static readonly DiagnosticDescriptor UnsynchronizedSharedMutationRule = CreateCommonBugDescriptor(
        UnsynchronizedSharedMutationId,
        "Shared state mutated by a parallel callback",
        "Shared state '{0}' is mutated in '{1}' without visible synchronization",
        "Concurrency",
        DiagnosticSeverity.Warning,
        "Captured locals and fields mutated by Task, Thread, timer, or Parallel callbacks require synchronization such as Interlocked or lock, or should be replaced with isolated state.");

    public static readonly DiagnosticDescriptor ConcurrentCollectionEnumerationRule = CreateCommonBugDescriptor(
        ConcurrentCollectionEnumerationId,
        "Concurrent collection enumerated through LINQ",
        "LINQ operator '{0}' enumerates concurrent collection '{1}' without snapshot guarantees",
        "Concurrency",
        DiagnosticSeverity.Info,
        "Concurrent collection members have documented concurrency behavior, but interface and LINQ extension methods do not necessarily provide an atomic snapshot.");

    public static readonly DiagnosticDescriptor BoxingInLoopRule = CreateCommonBugDescriptor(
        BoxingInLoopId,
        "Value boxed inside a loop",
        "Value of type '{0}' is boxed inside loop '{1}'",
        "Performance",
        DiagnosticSeverity.Info,
        "Repeated boxing allocates objects and adds GC pressure. Prefer generic APIs or strongly typed interfaces in repeated paths.");

    public static readonly DiagnosticDescriptor MaybeNullResultDereferenceRule = CreateCommonBugDescriptor(
        MaybeNullResultDereferenceId,
        "Maybe-null query result is dereferenced",
        "Result of '{0}' can be null or empty-default and is dereferenced immediately",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Default-returning lookup and LINQ APIs require a guard, a non-null fallback, or a proof that a matching element exists before dereference.");

    public static readonly DiagnosticDescriptor PrematureQueryMaterializationRule = CreateCommonBugDescriptor(
        PrematureQueryMaterializationId,
        "Queryable is materialized before further filtering",
        "'{0}' materializes IQueryable before subsequent '{1}' processing",
        "Performance",
        DiagnosticSeverity.Info,
        "Compose supported filters and projections on IQueryable before materialization so the remote provider can execute them.");

    public static readonly DiagnosticDescriptor DeferredQuerySideEffectRule = CreateCommonBugDescriptor(
        DeferredQuerySideEffectId,
        "Deferred query lambda mutates state",
        "Deferred LINQ operator '{0}' contains state mutation '{1}'",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Side effects in deferred LINQ lambdas run on every enumeration and can therefore execute zero, one, or multiple times unexpectedly.");

    public static readonly DiagnosticDescriptor QueryTranslationRiskRule = CreateCommonBugDescriptor(
        QueryTranslationRiskId,
        "Queryable expression calls source-only method",
        "Queryable operator '{0}' calls source method '{1}' that the remote provider may not translate",
        "Compatibility",
        DiagnosticSeverity.Info,
        "Remote IQueryable providers translate a bounded method set. Source-only helper calls in query predicates and selectors require provider-specific translation support.");

    public static readonly DiagnosticDescriptor SerializationCycleRiskRule = CreateCommonBugDescriptor(
        SerializationCycleRiskId,
        "Serialized source type has a reference cycle",
        "Type '{0}' contains a serializable reference cycle and is serialized without explicit cycle handling",
        "Serialization",
        DiagnosticSeverity.Info,
        "System.Text.Json rejects reachable object cycles by default. Project cyclic entities to DTOs, ignore a link, or configure deliberate reference handling.");

    public static readonly DiagnosticDescriptor SerializerAttributeMismatchRule = CreateCommonBugDescriptor(
        SerializerAttributeMismatchId,
        "Serializer ignores attribute from another JSON library",
        "Serializer '{0}' does not honor attribute '{1}' on member '{2}'",
        "Serialization",
        DiagnosticSeverity.Warning,
        "Newtonsoft.Json and System.Text.Json define similarly named attributes that are not interchangeable.");

    public static readonly DiagnosticDescriptor IneffectiveRequiredAttributeRule = CreateCommonBugDescriptor(
        IneffectiveRequiredAttributeId,
        "Required attribute cannot reject a non-nullable value type default",
        "[Required] on non-nullable value member '{0}' cannot distinguish omitted input from default({1})",
        "Usage",
        DiagnosticSeverity.Info,
        "Use a nullable value type when model binding must distinguish missing input, then validate the resulting value range separately.");

    public static readonly DiagnosticDescriptor UncheckedAllocationArithmeticRule = CreateCommonBugDescriptor(
        UncheckedAllocationArithmeticId,
        "Allocation length uses unchecked arithmetic",
        "Allocation length expression '{0}' can wrap before bounds validation",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Compute allocation sizes in a checked context so overflow is reported instead of becoming a wrapped negative or undersized length.");

    public static readonly DiagnosticDescriptor SuppressionWithoutJustificationRule = CreateCommonBugDescriptor(
        SuppressionWithoutJustificationId,
        "Diagnostic suppression lacks justification",
        "Suppression '{0}' has no reviewable diagnostic scope or justification",
        "Review",
        DiagnosticSeverity.Info,
        "Broad pragma suppressions and SuppressMessage attributes without justification hide warning debt and should be narrowed or documented.");

    public static readonly DiagnosticDescriptor NullableAnalysisDisabledRule = CreateCommonBugDescriptor(
        NullableAnalysisDisabledId,
        "Nullable analysis explicitly disabled",
        "Nullable analysis is disabled for this source region",
        "Nullability",
        DiagnosticSeverity.Info,
        "Explicit nullable-disable directives create blind spots in compile-time null-state analysis. Prefer scoped annotations and resolved warnings.");

    public static readonly DiagnosticDescriptor IdenticalOperandsRule = CreateCommonBugDescriptor(
        IdenticalOperandsId,
        "Binary operation uses the same value on both sides",
        "Operation '{0}' uses '{1}' as both operands; verify that the second operand is correct",
        "Correctness",
        DiagnosticSeverity.Warning,
        "Identical stable operands in built-in comparisons, subtraction, division, and remainder usually indicate a copied or mistyped operand. Floating-point and user-defined operators are excluded because they need not be reflexive.");

    public static readonly DiagnosticDescriptor ContainerOwnedServiceDisposedRule = CreateCommonBugDescriptor(
        ContainerOwnedServiceDisposedId,
        "Container-owned service is disposed by its consumer",
        "Service resolved by '{0}' is disposed by consuming code; the dependency-injection container owns its lifetime",
        "ResourceLifetime",
        DiagnosticSeverity.Warning,
        "Services resolved from IServiceProvider are disposed by their owning service provider or scope. Consumers should not wrap the resolved service in using or call Dispose directly.");

    public static readonly DiagnosticDescriptor UnconsumedDeferredQueryRule = CreateCommonBugDescriptor(
        UnconsumedDeferredQueryId,
        "Deferred query is never consumed",
        "Deferred query produced by '{0}' is never enumerated or materialized",
        "Correctness",
        DiagnosticSeverity.Warning,
        "LINQ query operators are deferred. Constructing a query and discarding it performs no work and does not execute its predicate or selector.");
}
