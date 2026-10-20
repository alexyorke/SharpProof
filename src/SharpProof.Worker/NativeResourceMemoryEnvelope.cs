namespace SharpProof.Worker;

// The cumulative allocation kernel supplies the byte bound separately. This
// admission establishes closed primitive-array ownership for its transfer to
// auxiliary/retained upper bounds; each reference use has a local owned origin.
internal static class NativeResourceMemoryEnvelope
{
    internal const string ModelId = "primitive-array-cumulative-envelope-v1";

    internal static bool Admits(CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        var program = owner.Program;
        var factory = program.Factory;
        if (owner.IsBodyAbstraction || !owner.EffectsCompleteAtEntry || owner.HasReceiver ||
            factory.Semantics != IrExecutionSemantics.Total || program.Blocks.Length > ResourceMemoryTrace.MaximumEvents / 4)
        { return false; }
        foreach (var parameter in owner.Parameters)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Scalar(factory, factory.GetVariableInfo(parameter.Entry).Type))
            { return false; }
        }
        var remaining = ResourceExpression.MaximumNodes;
        foreach (var block in program.Blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            var references = new HashSet<IrVarId>();
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return false; }
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        if (allocation.Length == null || !allocation.InitialValues.IsEmpty ||
                            !PrimitiveArray(factory, allocation.AllocatedType))
                        { return false; }
                        break;
                    case IrAssignInstruction assignment:
                        var targetType = factory.GetVariableInfo(assignment.Target).Type;
                        if (!(Scalar(factory, targetType) || Reference(factory, targetType)) ||
                            Reference(factory, targetType) != Reference(factory, assignment.Value.Type))
                        { return false; }
                        break;
                    case IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                    case IrBranchInstruction:
                    case IrGotoInstruction:
                    case IrReturnInstruction:
                    case IrThrowInstruction:
                    case IrExceptionalExitInstruction:
                    case IrAssumeInstruction:
                    case IrAssertInstruction:
                        break;
                    case IrHavocInstruction { HavocKind: IrHavocKind.Variables } havoc:
                        foreach (var variable in havoc.Variables)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (!Scalar(factory, factory.GetVariableInfo(variable).Type))
                            { return false; }
                        }
                        break;
                    default:
                        return false;
                }
                foreach (var term in IrInstructionFacts.ReadTerms(instruction))
                {
                    if (!Closed(term, factory, references, ref remaining, cancellation))
                    { return false; }
                }
                if (instruction is IrAllocationInstruction { Target: { } allocated })
                { references.Add(allocated); }
                else if (instruction is IrAssignInstruction assigned && Reference(factory, assigned.Value.Type))
                { references.Add(assigned.Target); }
            }
        }
        return true;
    }

    private static bool Closed(IrTerm root, IrFactory factory, HashSet<IrVarId> references,
        ref int remaining, CancellationToken cancellation)
    {
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        references = ArgumentNullGuard.NotNull(references, nameof(references));
        var pending = new Stack<IrTerm>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return false; }
            var term = pending.Pop();
            switch (term)
            {
                case IrVariableTerm variable when Scalar(factory, variable.Type) || Reference(factory, variable.Type) && references.Contains(variable.Variable):
                case IrIntegerTerm:
                case IrBooleanTerm:
                case IrNullTerm:
                    break;
                case IrUnaryTerm when Scalar(factory, term.Type):
                case IrBinaryTerm when Scalar(factory, term.Type):
                case IrConditionalTerm:
                case IrLengthTerm:
                    break;
                case IrSequenceAccessTerm access when Scalar(factory, access.Type) && PrimitiveArray(factory, access.Sequence.Type):
                    break;
                case IrCastTerm cast when !Reference(factory, cast.Type) || Reference(factory, cast.Operand.Type):
                    break;
                default:
                    return false;
            }
            IrTraversal.PushChildren(term, pending);
        }
        return true;
    }

    private static bool PrimitiveArray(IrFactory factory, IrTypeId type)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        return factory.GetTypeInfo(type) is { Kind: IrTypeKind.Sequence, ElementType: { } element } &&
            factory.GetTypeInfo(element) is { Kind: IrTypeKind.Boolean } or
            { Kind: IrTypeKind.Integer, Width: 8 or 16 or 32 or 64 };
    }

    private static bool Scalar(IrFactory factory, IrTypeId type)
    { factory = ArgumentNullGuard.NotNull(factory, nameof(factory)); return factory.GetTypeInfo(type).Kind is IrTypeKind.Integer or IrTypeKind.Boolean; }

    private static bool Reference(IrFactory factory, IrTypeId type)
    { factory = ArgumentNullGuard.NotNull(factory, nameof(factory)); return factory.GetTypeInfo(type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String; }
}
