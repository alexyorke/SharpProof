using System.Numerics;

namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private static bool EmptyReturn(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        return operation is IReturnOperation { ReturnedValue: null } ||
            operation is IBlockOperation { Operations.Length: 1 } block &&
            block.Operations[0] is IReturnOperation { ReturnedValue: null };
    }

    private Cost CallableBody(IMethodSymbol method, IOperation body, int depth)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        _cancellation.ThrowIfCancellationRequested();
        // Only the positively guarded tail-recursive family gets this rule.
        // Other control shapes pass through the closed visitor, where an
        // unproved recursive cycle remains Unknown.
        if (!method.IsStatic || !method.ReturnsVoid || method.IsGenericMethod || method.ContainingType.IsGenericType ||
            !method.ContainingType.StaticConstructors.IsEmpty ||
            body is not IBlockOperation block || block.Operations.Length < 2 ||
            block.Operations[0] is not IConditionalOperation { WhenFalse: null } guard ||
            !EmptyReturn(guard.WhenTrue) ||
            guard.Condition is not IBinaryOperation { OperatorKind: BinaryOperatorKind.LessThanOrEqual, OperatorMethod: null } condition ||
            condition.LeftOperand is not IParameterReferenceOperation rank ||
            rank.Parameter.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64) ||
            IntegerConstant(condition.RightOperand) != 0 ||
            block.Operations[block.Operations.Length - 1] is not IExpressionStatementOperation { Operation: IInvocationOperation tail } ||
            !SymbolEqualityComparer.Default.Equals(tail.TargetMethod, method))
        { return Visit(body, depth); }
        var size = Size(rank);
        if (size is null)
        { return Unknown("UnprovedRecursiveProgress"); }
        Bound? activations = null;
        foreach (var argument in tail.Arguments)
        {
            if (argument.Parameter is not { } parameter)
            { return Unknown("UnsupportedRecursiveArguments"); }
            if (!SymbolEqualityComparer.Default.Equals(parameter, rank.Parameter))
            {
                if (argument.Value is not IParameterReferenceOperation forwarded ||
                    !SymbolEqualityComparer.Default.Equals(forwarded.Parameter, parameter))
                { return Unknown("UnsupportedRecursiveArguments"); }
                continue;
            }
            if (argument.Value is not IBinaryOperation { OperatorMethod: null } progress ||
                progress.LeftOperand is not IParameterReferenceOperation current ||
                !SymbolEqualityComparer.Default.Equals(current.Parameter, parameter) ||
                IntegerConstant(progress.RightOperand) is not { } amount || amount <= 0)
            { return Unknown("UnprovedRecursiveProgress"); }
            var maximum = rank.Parameter.Type.SpecialType == SpecialType.System_Int32
                ? new BigInteger(int.MaxValue) : new BigInteger(long.MaxValue);
            if (amount > maximum || !SymbolEqualityComparer.Default.Equals(progress.Type, rank.Parameter.Type))
            { return Unknown("UnprovedRecursiveProgress"); }
            activations = progress.OperatorKind switch
            {
                BinaryOperatorKind.Subtract => DivideCeiling(size, amount),
                BinaryOperatorKind.Divide when amount > 1 => GeometricCount(size, amount, descending: true),
                _ => null
            };
        }
        if (activations is null)
        { return Unknown("UnprovedRecursiveProgress"); }
        // On n>0, subtracting a representable positive constant cannot
        // underflow; dividing by k>1 also decreases the nonnegative rank. All
        // other parameters are unchanged. Prefix costs use the entry-size
        // envelope for every activation, so scaling remains an upper bound.
        if (--_remaining < 0 || depth > 128)
        { return Unknown("AnalysisBudgetExceeded"); }
        var prefix = Sequence(block.Operations.Skip(1).Take(block.Operations.Length - 2), depth + 1);
        return prefix.Scale(activations);
    }
}
