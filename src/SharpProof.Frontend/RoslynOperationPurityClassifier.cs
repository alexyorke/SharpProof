namespace SharpProof.Frontend;

// Classifies an actual compiler operation tree under the caller's method policy.
// Each lowering scope owns its reference-identity and depth-sensitive cache.
internal sealed class RoslynOperationPurityClassifier(Func<IMethodSymbol, bool> isKnownPure, int maximumDepth)
{
    private readonly Dictionary<IOperation, Dictionary<int, bool>> _purityResults =
        (isKnownPure is null ? throw new System.ArgumentNullException(nameof(isKnownPure)) : new(ReferenceComparer<IOperation>.Instance));

    internal bool IsDemonstrablyPure(IOperation operation, int depth)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (depth >= maximumDepth)
        {
            return false;
        }

        var purityResults = _purityResults;
        if (!purityResults.TryGetValue(operation, out var depths))
        {
            depths = [];
            purityResults.Add(operation, depths);
        }
        if (depths.TryGetValue(depth, out var cached))
        {
            return cached;
        }

        var result = IsDemonstrablyPureCore(operation, depth);
        depths.Add(depth, result);
        return result;
    }

    private bool IsDemonstrablyPureCore(IOperation operation, int depth)
    {

        var childDepth = depth + 1;
        if (operation.ConstantValue.HasValue)
        {
            return true;
        }

        return operation switch
        {
            ILiteralOperation or
                ILocalReferenceOperation or
                IParameterReferenceOperation or
                IInstanceReferenceOperation or
                IDefaultValueOperation or
                ITypeOfOperation or
                ISizeOfOperation => true,
            IConversionOperation conversion =>
                conversion.OperatorMethod == null &&
                IsDemonstrablyPure(conversion.Operand, childDepth),
            IUnaryOperation unary =>
                unary.OperatorMethod == null &&
                IsDemonstrablyPure(unary.Operand, childDepth),
            IBinaryOperation binary =>
                binary.OperatorMethod == null &&
                IsDemonstrablyPure(binary.LeftOperand, childDepth) &&
                IsDemonstrablyPure(binary.RightOperand, childDepth),
            IConditionalOperation conditional =>
                IsDemonstrablyPure(conditional.Condition, childDepth) &&
                IsDemonstrablyPure(conditional.WhenTrue, childDepth) &&
                conditional.WhenFalse != null &&
                IsDemonstrablyPure(conditional.WhenFalse, childDepth),
            IIsNullOperation isNull =>
                IsDemonstrablyPure(isNull.Operand, childDepth),
            IInvocationOperation invocation =>
                isKnownPure(invocation.TargetMethod) &&
                (invocation.Instance == null ||
                 IsDemonstrablyPure(invocation.Instance, childDepth)) &&
                invocation.Arguments.All(argument =>
                    argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (IsDemonstrablyPure(argument.Value, childDepth))),
            _ => false
        };
    }

}
