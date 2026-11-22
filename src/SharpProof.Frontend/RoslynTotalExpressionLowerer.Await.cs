namespace SharpProof.Frontend;

// `await e` in an admitted async body (RoslynTotalProgramLowerer.Async.cs).
//
// An awaited call to a source async method that the lowering admits runs its
// callee's body: the callee runs on the caller's thread up to its own first
// suspension, and the caller's code after the await runs only once the
// callee's task completes, so the callee's whole body comes first. Its value
// is the callee's `return` value, and an exception that leaves the callee
// faults its task, which the await rethrows unchanged: the same exception
// object reaches the caller's handlers, as from a synchronous call. See
// AwaitedSourceCall.
//
// Any other awaited value is an unknown call. Awaiting it runs its awaiter's
// GetAwaiter, IsCompleted, OnCompleted and GetResult, which for a task type
// can read an IValueTaskSource or post the continuation to a captured
// SynchronizationContext, and the rest of the body may run later, after
// other code changed any memory. The value, any exception and any effect are
// unknown, so the call is an opaque call that may do anything; the awaited
// value is its argument, as it escapes to the awaiter.
internal sealed partial class RoslynTotalExpressionLowerer
{
    // The program lowerer's awaited source call (InlineAwaitedSourceCall).
    internal Func<IInvocationOperation, IrBlockId, int, TotalBodyValue?>? AwaitedSourceCall { get; init; }

    private TotalBodyValue? LowerAwait(IAwaitOperation operation, IrBlockId block, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton || !_context.Target.IsAsync || operation.Type == null ||
            operation.Type.SpecialType != SpecialType.System_Void && !CSharpOperationSemantics.IsValueDomain(operation.Type))
        { return null; }
        var awaited = WithoutConfigureAwait(operation.Operation);
        if (LowerCompletedAwait(operation, awaited, block, depth) is { } completed)
        { return completed; }
        // A dispatched call that does not resolve to this body may run
        // another override; it is then an unknown call like any other.
        if (awaited is IInvocationOperation call && call.TargetMethod.IsAsync && !call.TargetMethod.DeclaringSyntaxReferences.IsEmpty)
        {
            var inlined = AwaitedSourceCall?.Invoke(call, block, depth);
            if (inlined != null || !(call.TargetMethod.IsVirtual || call.TargetMethod.IsOverride))
            { return inlined; }
        }
        if (!AllowOpaqueCalls || awaited.Type == null || !CSharpOperationSemantics.IsValueDomain(awaited.Type))
        { return null; }
        var value = LowerBodyValue(awaited, block, depth + 1);
        if (!value.Classification.IsExact)
        { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
        return EmitUnknownAwait(operation, value.Value,
            operation.Type.SpecialType == SpecialType.System_Void ? null : operation.Type, value.Continuation);
    }

    // The unknown call an await of `awaited` is: it takes the awaited value,
    // may do anything and throw anything, and yields an unknown result.
    internal TotalBodyValue EmitUnknownAwait(IOperation operation, IrTerm awaited, ITypeSymbol? result, IrBlockId block)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        awaited = RepositoryParameterGuard.NotNull(awaited, nameof(awaited));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var site = _context.Site(operation);
        var resultType = result != null ? _context.Type(result) : _factory.BooleanType;
        var member = _factory.GetOrCreateMemberSnapshot(_factory.CreateIdentity(), _factory.ObjectType,
            "opaque-call:" + AwaitMember, resultType, true, [awaited.Type]);
        _builder!.CallSnapshot(block, _context.OpaqueCallSite(operation, IrOpaqueCallEffects.All, AwaitMember), null, member, null, [awaited]);
        var throws = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, throws);
        IrTerm value = _factory.Boolean(false);
        if (result != null)
        {
            var target = _context.Temporary(resultType);
            _builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, target);
            value = _factory.Variable(target);
        }
        return ApplyRule(operation, new TotalScalarRule(value, [new(IrExceptionKind.Unknown, _factory.Variable(throws))],
            FrontendSubsetClassification.Exact), block);
    }

    internal const string AwaitMember = "await";
    private const string FromResultMember = "task-from-result";

    // A task that is complete when it is created: Task.CompletedTask,
    // ValueTask.CompletedTask, Task.FromResult(x), ValueTask.FromResult(x)
    // and new ValueTask<T>(x). Awaiting it does not suspend: its awaiter's
    // IsCompleted is true, and GetResult returns x (C# specification,
    // awaitable expressions; runtime TaskAwaiter and ValueTaskAwaiter). So the
    // await is the value x, evaluated where the operand is. Task.FromResult
    // allocates its task unless it returns a cached one: a possible allocation.
    private TotalBodyValue? LowerCompletedAwait(IAwaitOperation operation, IOperation awaited, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        switch (awaited)
        {
            case IPropertyReferenceOperation { Property: { Name: "CompletedTask", IsStatic: true } property, Instance: null }
                when TotalLoweringContext.IsTaskType(property.ContainingType) && operation.Type?.SpecialType == SpecialType.System_Void:
                return new(_factory.Boolean(false), block, FrontendSubsetClassification.Exact);
            case IInvocationOperation { TargetMethod: { Name: "FromResult", IsStatic: true, Parameters.Length: 1 } method, Arguments.Length: 1 } call
                when TotalLoweringContext.IsTaskType(method.ContainingType) && method.ContainingType.Arity == 0 &&
                    TotalLoweringContext.IsTaskType(method.ReturnType) && operation.Type?.SpecialType != SpecialType.System_Void:
                {
                    var value = LowerBodyValue(call.Arguments[0].Value, block, depth + 1);
                    if (!value.Classification.IsExact)
                    { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
                    if (method.ContainingType.Name == "Task")
                    {
                        var member = _factory.GetOrCreateMember(_factory.CreateIdentity(), _factory.ObjectType,
                            "opaque-call:" + FromResultMember, _factory.BooleanType, true);
                        _builder!.Call(value.Continuation, _context.OpaqueCallSite(operation, IrOpaqueCallEffects.Allocates, FromResultMember), null, member, null);
                    }
                    return new(_context.BridgeValue(value.Value, _context.Type(operation.Type)), value.Continuation, FrontendSubsetClassification.Exact);
                }
            case IObjectCreationOperation { Constructor: { Parameters.Length: 1 } constructor, Arguments.Length: 1, Initializer: null } creation
                when TotalLoweringContext.IsTaskType(constructor.ContainingType) && constructor.ContainingType.Name == "ValueTask" &&
                    constructor.ContainingType.Arity == 1 &&
                    SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, constructor.ContainingType.TypeArguments[0]):
                {
                    var value = LowerBodyValue(creation.Arguments[0].Value, block, depth + 1);
                    return value.Classification.IsExact
                        ? new(_context.BridgeValue(value.Value, _context.Type(operation.Type)), value.Continuation, FrontendSubsetClassification.Exact)
                        : Approximate(operation, value.Continuation, value.Classification.Abstention);
                }
            default:
                return null;
        }
    }

    // `t.ConfigureAwait(c)`, the Boolean overload with a constant `c` on one
    // of the four framework task types, only wraps t with the choice of
    // context: awaiting either runs the same task and rethrows its fault. The
    // ConfigureAwaitOptions overload can suppress that rethrow
    // (SuppressThrowing), so it, like any other overload, stays an operand
    // of an unknown await.
    internal static IOperation WithoutConfigureAwait(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IInvocationOperation
        {
            TargetMethod: { Name: "ConfigureAwait", IsStatic: false, IsExtensionMethod: false, ReducedFrom: null, Parameters.Length: 1 } configure,
            Instance: { } awaited,
            Arguments.Length: 1
        } invocation && configure.Parameters[0].Type.SpecialType == SpecialType.System_Boolean &&
            invocation.Arguments[0].Value.ConstantValue.HasValue &&
            TotalLoweringContext.IsTaskType(configure.ContainingType) ? awaited : operation;
    }
}
