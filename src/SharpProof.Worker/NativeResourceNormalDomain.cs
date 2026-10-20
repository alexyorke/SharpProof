using System.Numerics;

namespace SharpProof.Worker;

// The validated compiler envelope binds this IR to the checked resource program.
// Rank arithmetic supplies progress in the unbounded counter extension; this
// checker separately rejects operations which could prevent normal completion.
// Allocation success is an explicit part of the domain, not a claim about OOM.
internal static class NativeResourceNormalDomain
{
    internal const string ModelId = WorkerResourceModels.NormalDomain;

    internal static string? Check(ResourceProgram program, CompilerTotalCallablePreparation owner, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        var remaining = ResourceExpression.MaximumNodes;
        if (owner.IsBodyAbstraction || !owner.CallPreconditions.IsEmpty)
        { return "ResourceNormalDomainCallableUnsupported"; }
        foreach (var clause in owner.Clauses)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return "ResourceNormalDomainBudgetExceeded"; }
            if (clause.Kind == CompilerContractKind.Requires &&
                (clause.Value is not IrBooleanTerm { Value: true } || clause.Safe is not IrBooleanTerm { Value: true }))
            { return "ResourceNormalDomainPreconditionsUnsupported"; }
        }
        var ir = owner.Program;
        var nonnegative = new HashSet<IrVarId>();
        foreach (var parameter in owner.Parameters)
        {
            if (ir.Factory.GetTypeInfo(ir.Factory.GetVariableInfo(parameter.Entry).Type).Kind == IrTypeKind.Integer)
            { nonnegative.Add(parameter.Entry); }
        }
        var regions = new Stack<ResourceProgram>();
        regions.Push(program);
        while (regions.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return "ResourceNormalDomainBudgetExceeded"; }
            var region = regions.Pop();
            if (region.Kind is not (ResourceProgramKind.Zero or ResourceProgramKind.AllocateArray or ResourceProgramKind.Sequence or ResourceProgramKind.Choice or ResourceProgramKind.Repeat or ResourceProgramKind.Activation) ||
                region.Kind == ResourceProgramKind.Repeat && region.RepeatKind == ResourceRepeatKind.TailRecurrence)
            { return "ResourceNormalDomainRegionUnsupported"; }
            foreach (var child in region.Children)
            { regions.Push(child); }
        }
        // Only immutable single-definition snapshots supply constants. A value
        // assigned on a loop backedge must never become a global constant.
        var definitions = new Dictionary<IrVarId, IrTerm>();
        var repeated = new HashSet<IrVarId>();
        foreach (var block in ir.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return "ResourceNormalDomainBudgetExceeded"; }
                if (instruction is IrAssignInstruction assignment && !definitions.TryAdd(assignment.Target, assignment.Value))
                { repeated.Add(assignment.Target); }
            }
        }
        foreach (var variable in repeated)
        { definitions.Remove(variable); }
        var pending = new Stack<IrBlockId>();
        var visited = new HashSet<IrBlockId>();
        pending.Push(ir.Entry);
        var returned = false;
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var id = pending.Pop();
            if (!visited.Add(id))
            { continue; }
            var block = ir.GetBlock(id);
            if (remaining < 0)
            { return "ResourceNormalDomainBudgetExceeded"; }
            if (AllocationSuccessContinuation(ir, block, ref remaining, cancellation) is { } continuation)
            {
                // This closed compiler-emitted choice models only the array
                // size-limit failure excluded by the declared ideal allocation.
                pending.Push(continuation);
                continue;
            }
            foreach (var instruction in block.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return "ResourceNormalDomainBudgetExceeded"; }
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        if (!Safe(assign.Value, ir.Factory, definitions, ref remaining, cancellation))
                        { return "ResourceNormalDomainTermUnsupported"; }
                        break;
                    case IrWriteInstruction { Region: IrWriteRegion.Local, IsStore: false }:
                        break;
                    case IrAssumeInstruction { Condition: IrBooleanTerm { Value: true } }:
                    case IrAssertInstruction { Condition: IrBooleanTerm { Value: true } }:
                        break;
                    case IrAllocationInstruction { Length: { } extent, InitialValues.IsEmpty: true } allocation
                        when Nonnegative(extent, ir.Factory, definitions, nonnegative, ref remaining, cancellation) &&
                            ir.Factory.GetTypeInfo(allocation.AllocatedType) is { Kind: IrTypeKind.Sequence, ElementType: { } element } &&
                            ir.Factory.GetTypeInfo(element).Kind is IrTypeKind.Integer or IrTypeKind.Boolean:
                        break;
                    case IrBranchInstruction branch:
                        if (!Safe(branch.Condition, ir.Factory, definitions, ref remaining, cancellation))
                        { return "ResourceNormalDomainTermUnsupported"; }
                        if (Boolean(branch.Condition, ir.Factory, definitions, nonnegative, ref remaining, cancellation) is { } constant)
                        { pending.Push(constant ? branch.WhenTrue : branch.WhenFalse); }
                        else
                        {
                            pending.Push(branch.WhenTrue);
                            pending.Push(branch.WhenFalse);
                        }
                        break;
                    case IrGotoInstruction jump:
                        pending.Push(jump.Target);
                        break;
                    case IrReturnInstruction exit:
                        if (exit.Value != null && !Safe(exit.Value, ir.Factory, definitions, ref remaining, cancellation))
                        { return "ResourceNormalDomainTermUnsupported"; }
                        returned = true;
                        break;
                    default:
                        return "ResourceNormalDomainInstructionUnsupported:" + instruction.Kind;
                }
            }
        }
        return remaining < 0 ? "ResourceNormalDomainBudgetExceeded" : returned ? null : "ResourceNormalDomainReturnUnproved";
    }

    private static IrBlockId? AllocationSuccessContinuation(IrProgram ir, IrBasicBlock block, ref int remaining, CancellationToken cancellation)
    {
        ir = ArgumentNullGuard.NotNull(ir, nameof(ir));
        block = ArgumentNullGuard.NotNull(block, nameof(block));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0)
        { return null; }
        if (block.Instructions.Length != 2 || block.Instructions[0] is not IrHavocInstruction
            { HavocKind: IrHavocKind.Variables, Origin: IrHavocOrigin.Approximation, Variables.Length: 1 } havoc ||
            block.Instructions[1] is not IrBranchInstruction { Condition: IrVariableTerm choice } branch ||
            choice.Variable != havoc.Variables[0] || choice.Type != ir.Factory.BooleanType ||
            ir.Factory.GetOperationInfo(havoc.Operation).Description is not { } description ||
            ir.Factory.GetString(description) != IrOpaqueCallSite.Describe(IrOpaqueCallEffects.Throws, "array:beyond-maximum-length"))
        { return null; }
        var failure = ir.GetBlock(branch.WhenTrue);
        if (failure.Instructions.Length != 1 || failure.Instructions[0] is not IrThrowInstruction
            { ExceptionKind: IrExceptionKind.Unknown } thrown || thrown.Operation != branch.Operation)
        { return null; }
        foreach (var instruction in ir.GetBlock(branch.WhenFalse).Instructions)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return null; }
            if (instruction is IrAllocationInstruction { Length: not null } allocation &&
                SameSourceSite(ir.Factory, allocation.Operation, branch.Operation) && ir.Factory.GetTypeInfo(allocation.AllocatedType) is
                { Kind: IrTypeKind.Sequence, ElementType: { } element } && ir.Factory.GetTypeInfo(element).Kind is IrTypeKind.Integer or IrTypeKind.Boolean)
            { return branch.WhenFalse; }
        }
        return null;
    }

    private static bool SameSourceSite(IrFactory factory, OperationId left, OperationId right)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(left.IsDefault, nameof(left));
        RepositoryParameterGuard.ThrowIfInvalid(right.IsDefault, nameof(right));
        if (left == right)
        { return true; }
        var first = factory.GetOperationInfo(left);
        var second = factory.GetOperationInfo(right);
        return first.SourceSpan is { } span && second.SourceSpan is { } other &&
            span.Document == other.Document && span.Start == other.Start && span.Length == other.Length &&
            span.Line == other.Line && span.Column == other.Column &&
            first.Description is { } description && second.Description == description;
    }

    private static bool Safe(IrTerm term, IrFactory factory, Dictionary<IrVarId, IrTerm> definitions, ref int remaining, CancellationToken cancellation)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        definitions = ArgumentNullGuard.NotNull(definitions, nameof(definitions));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0)
        { return false; }
        switch (term)
        {
            case IrBooleanTerm:
            case IrIntegerTerm:
            case IrNullTerm:
            case IrVariableTerm:
                return true;
            case IrUnaryTerm unary:
                return unary.Operator is IrUnaryOperator.Not or IrUnaryOperator.Negate && Safe(unary.Operand, factory, definitions, ref remaining, cancellation);
            case IrCastTerm cast:
                return factory.GetTypeInfo(cast.Type).Kind is IrTypeKind.Integer or IrTypeKind.Boolean &&
                    factory.GetTypeInfo(cast.Type).Kind == factory.GetTypeInfo(cast.Operand.Type).Kind && Safe(cast.Operand, factory, definitions, ref remaining, cancellation);
            case IrConditionalTerm conditional:
                return Safe(conditional.Condition, factory, definitions, ref remaining, cancellation) && Safe(conditional.WhenTrue, factory, definitions, ref remaining, cancellation) &&
                    Safe(conditional.WhenFalse, factory, definitions, ref remaining, cancellation);
            case IrBinaryTerm binary:
                var admitted = binary.Operator is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply or
                    IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse or IrBinaryOperator.Equal or IrBinaryOperator.NotEqual or
                    IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual or IrBinaryOperator.GreaterThan or IrBinaryOperator.GreaterThanOrEqual or
                    IrBinaryOperator.BitwiseAnd or IrBinaryOperator.BitwiseOr or IrBinaryOperator.BitwiseXor or IrBinaryOperator.ShiftLeft or IrBinaryOperator.ShiftRight;
                if (binary.Operator is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
                { admitted = Number(binary.Right, factory, definitions, ref remaining, cancellation) is { } divisor && divisor > 0; }
                return admitted && Safe(binary.Left, factory, definitions, ref remaining, cancellation) && Safe(binary.Right, factory, definitions, ref remaining, cancellation);
            default:
                return false;
        }
    }

    private static BigInteger? Number(IrTerm term, IrFactory factory, Dictionary<IrVarId, IrTerm> definitions,
        ref int remaining, CancellationToken cancellation, int depth = 0)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        definitions = ArgumentNullGuard.NotNull(definitions, nameof(definitions));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || depth >= 32)
        { return null; }
        if (term is IrIntegerTerm literal)
        { return literal.Integer.NumericValue; }
        if (term is IrVariableTerm variable && definitions.TryGetValue(variable.Variable, out var value))
        { return Number(value, factory, definitions, ref remaining, cancellation, depth + 1); }
        if (term is IrCastTerm cast && factory.GetTypeInfo(cast.Type) is { Kind: IrTypeKind.Integer, Width: 8 or 16 or 32 or 64 } target &&
            Number(cast.Operand, factory, definitions, ref remaining, cancellation, depth + 1) is { } number)
        {
            var minimum = target.Signed ? -(BigInteger.One << (target.Width - 1)) : BigInteger.Zero;
            var maximum = (BigInteger.One << (target.Width - (target.Signed ? 1 : 0))) - 1;
            if (number >= minimum && number <= maximum)
            { return number; }
        }
        return null;
    }

    private static bool Nonnegative(IrTerm term, IrFactory factory, Dictionary<IrVarId, IrTerm> definitions, HashSet<IrVarId> nonnegative,
        ref int remaining, CancellationToken cancellation, int depth = 0)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        definitions = ArgumentNullGuard.NotNull(definitions, nameof(definitions));
        nonnegative = ArgumentNullGuard.NotNull(nonnegative, nameof(nonnegative));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || depth >= 32)
        { return false; }
        if (Number(term, factory, definitions, ref remaining, cancellation) is { } value)
        { return value >= 0; }
        return term is IrVariableTerm variable && (nonnegative.Contains(variable.Variable) ||
            definitions.TryGetValue(variable.Variable, out var definition) && Nonnegative(definition, factory, definitions, nonnegative, ref remaining, cancellation, depth + 1));
    }

    private static bool? Boolean(IrTerm term, IrFactory factory, Dictionary<IrVarId, IrTerm> definitions, HashSet<IrVarId> nonnegative,
        ref int remaining, CancellationToken cancellation, int depth = 0)
    {
        term = ArgumentNullGuard.NotNull(term, nameof(term));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        definitions = ArgumentNullGuard.NotNull(definitions, nameof(definitions));
        nonnegative = ArgumentNullGuard.NotNull(nonnegative, nameof(nonnegative));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || depth >= 32)
        { return null; }
        if (term is IrBooleanTerm literal)
        { return literal.Value; }
        if (term is IrVariableTerm variable && definitions.TryGetValue(variable.Variable, out var value))
        { return Boolean(value, factory, definitions, nonnegative, ref remaining, cancellation, depth + 1); }
        if (term is IrUnaryTerm { Operator: IrUnaryOperator.Not } negate)
        { return !Boolean(negate.Operand, factory, definitions, nonnegative, ref remaining, cancellation, depth + 1); }
        if (term is not IrBinaryTerm binary)
        { return null; }
        if (binary.Operator is IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse)
        {
            var left = Boolean(binary.Left, factory, definitions, nonnegative, ref remaining, cancellation, depth + 1);
            var right = Boolean(binary.Right, factory, definitions, nonnegative, ref remaining, cancellation, depth + 1);
            return binary.Operator == IrBinaryOperator.AndAlso
                ? left == false || right == false ? false : left == true && right == true ? true : null
                : left == true || right == true ? true : left == false && right == false ? false : null;
        }
        if (Number(binary.Right, factory, definitions, ref remaining, cancellation) is { } limit && limit <= 0 &&
            Nonnegative(binary.Left, factory, definitions, nonnegative, ref remaining, cancellation))
        {
            if (binary.Operator == IrBinaryOperator.LessThan)
            { return false; }
            if (binary.Operator == IrBinaryOperator.GreaterThanOrEqual)
            { return true; }
        }
        if (Number(binary.Left, factory, definitions, ref remaining, cancellation) is not { } first ||
            Number(binary.Right, factory, definitions, ref remaining, cancellation) is not { } second)
        { return null; }
        return binary.Operator switch
        {
            IrBinaryOperator.Equal => first == second,
            IrBinaryOperator.NotEqual => first != second,
            IrBinaryOperator.LessThan => first < second,
            IrBinaryOperator.LessThanOrEqual => first <= second,
            IrBinaryOperator.GreaterThan => first > second,
            IrBinaryOperator.GreaterThanOrEqual => first >= second,
            _ => null
        };
    }
}
