namespace SharpProof.Worker;

internal sealed record NativeTaskIrFaultOriginCheck(ImmutableHashSet<IrExceptionKind>? ExceptionKinds,
    bool HasIrAllocations, string? UnknownReason)
{
    internal bool HasOrigins => ExceptionKinds is not null;
}

// Conservative fault candidates of owned primitive IR, not a complete runtime exception
// profile. Allocation failure, initialization, runtime helpers and traces need
// separate models; an empty set does not prove exception-free execution.
internal static class NativeTaskIrFaultOriginVerifier
{
    internal const string Model = "task-join-owned-ir-fault-origins-v1";

    internal static NativeTaskIrFaultOriginCheck Check(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        cancellation.ThrowIfCancellationRequested();
        var source = NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return Unknown(source.UnknownReason!); }
        var kinds = ImmutableHashSet.CreateBuilder<IrExceptionKind>();
        var allocations = false;
        var remaining = ResourceExpression.MaximumNodes;
        foreach (var child in projection.Children)
        {
            var program = child.Owner.Program;
            var exits = new HashSet<IrBlockId>();
            foreach (var block in program.Blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                if (block.Terminator is IrExceptionalExitInstruction)
                { exits.Add(block.Id); }
            }
            if (exits.Contains(program.Entry))
            { return Unknown("TaskIrFaultOriginsUnclassifiedExit"); }
            foreach (var block in program.Blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                if (block.Terminator is not IrThrowInstruction &&
                    IrInstructionFacts.TryGetSuccessors(block.Terminator) is { } next &&
                    (next.First is { } first && exits.Contains(first) || next.Second is { } second && exits.Contains(second)))
                { return Unknown("TaskIrFaultOriginsUnclassifiedExit"); }
                foreach (var instruction in block.Instructions)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (--remaining < 0)
                    { return Unknown("TaskIrFaultOriginsBudgetExceeded"); }
                    switch (instruction)
                    {
                        case IrHavocInstruction { Origin: not IrHavocOrigin.Input }:
                            return Unknown("TaskIrFaultOriginsApproximationUnsupported");
                        case IrAssumeInstruction { Condition: not IrBooleanTerm { Value: true } }:
                        case IrAssertInstruction { Condition: not IrBooleanTerm { Value: true } }:
                            return Unknown("TaskIrFaultOriginsControlUnsupported");
                        case IrThrowInstruction thrown:
                            if (thrown.ExceptionKind is not (IrExceptionKind.DivideByZero or IrExceptionKind.Overflow or
                                IrExceptionKind.NullReference or IrExceptionKind.IndexOutOfRange or IrExceptionKind.InvalidCast or
                                IrExceptionKind.Argument or IrExceptionKind.ArgumentOutOfRange))
                            { return Unknown("TaskIrFaultOriginsUnclassifiedThrow"); }
                            kinds.Add(thrown.ExceptionKind);
                            break;
                        case IrAllocationInstruction:
                            allocations = true;
                            kinds.Add(IrExceptionKind.Overflow);
                            break;
                    }
                    foreach (var read in IrInstructionFacts.ReadTerms(instruction))
                    {
                        var pending = ImmutableStack<IrTerm>.Empty.Push(read);
                        while (!pending.IsEmpty)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (--remaining < 0)
                            { return Unknown("TaskIrFaultOriginsBudgetExceeded"); }
                            pending = pending.Pop(out var term);
                            switch (term)
                            {
                                case IrBinaryTerm { Operator: IrBinaryOperator.Divide or IrBinaryOperator.Remainder }:
                                    kinds.Add(IrExceptionKind.DivideByZero);
                                    kinds.Add(IrExceptionKind.Overflow);
                                    break;
                                case IrLengthTerm:
                                    kinds.Add(IrExceptionKind.NullReference);
                                    break;
                                case IrSequenceAccessTerm:
                                    kinds.Add(IrExceptionKind.NullReference);
                                    kinds.Add(IrExceptionKind.IndexOutOfRange);
                                    break;
                                case IrCastTerm cast:
                                    var target = program.Factory.GetTypeInfo(cast.Type).Kind;
                                    var operand = program.Factory.GetTypeInfo(cast.Operand.Type).Kind;
                                    if (target is IrTypeKind.Integer or IrTypeKind.Boolean && operand == IrTypeKind.Reference)
                                    {
                                        kinds.Add(IrExceptionKind.NullReference);
                                        kinds.Add(IrExceptionKind.InvalidCast);
                                    }
                                    else if (target is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String &&
                                        cast.Type != program.Factory.ObjectType && cast.Type != cast.Operand.Type)
                                    { kinds.Add(IrExceptionKind.InvalidCast); }
                                    break;
                            }
                            // Source ownership already admitted the complete
                            // primitive term grammar. Other operations need no
                            // additional family beyond compiler-emitted guards.
                            pending = IrTraversal.PushChildren(term, pending);
                        }
                    }
                }
            }
        }
        return new(kinds.ToImmutable(), allocations, null);
    }

    private static NativeTaskIrFaultOriginCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, false, reason);
    }
}
