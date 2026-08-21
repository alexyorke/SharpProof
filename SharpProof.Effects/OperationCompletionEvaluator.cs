namespace SharpProof.Effects;

internal sealed class OperationCompletionEvaluator
{
    private readonly DefiniteOperationFacts _completionFacts;
    private readonly Func<IOperation?, IOperation, bool> _isProvenNull;
    private readonly Func<IOperation?, IOperation, bool> _isProvenNonNull;
    private readonly Func<IInvocationOperation, bool> _isImplicitLockEnterWithNullValue;

    internal OperationCompletionEvaluator(
        EffectAnalysisSession session,
        Func<IOperation?, IOperation, bool> isProvenNull,
        Func<IOperation?, IOperation, bool> isProvenNonNull,
        Func<IInvocationOperation, bool> isImplicitLockEnterWithNullValue)
    {
        _completionFacts = new DefiniteOperationFacts(
            session.Compilation,
            CancellationToken.None);
        _isProvenNull = isProvenNull;
        _isProvenNonNull = isProvenNonNull;
        _isImplicitLockEnterWithNullValue = isImplicitLockEnterWithNullValue;
    }

    internal bool CanCompleteNormally(IOperation? operation)
    {
        if (operation == null)
        {
            return true;
        }

        return operation switch
        {
            IThrowOperation => false,
            IInvocationOperation invocation =>
                !_isImplicitLockEnterWithNullValue(invocation) &&
                CanCompleteInvocation(
                    invocation.TargetMethod,
                    invocation.Instance,
                    invocation),
            IPropertyReferenceOperation property =>
                CanCompleteProperty(property),
            IFieldReferenceOperation field =>
                CanCompleteField(field),
            IArrayElementReferenceOperation element =>
                CanCompleteArrayElement(element),
            IObjectCreationOperation creation =>
                CanCompleteConstruction(creation),
            IArrayCreationOperation array =>
                CanCompleteArrayCreation(array),
            IConditionalAccessOperation conditional =>
                CanCompleteConditionalAccess(conditional),
            ILockOperation @lock => CanCompleteLock(@lock),
            IFlowCaptureOperation capture =>
                CanCompleteNormally(capture.Value),
            IArgumentOperation argument =>
                CanCompleteNormally(argument.Value),
            IParenthesizedOperation parenthesized =>
                CanCompleteNormally(parenthesized.Operand),
            IConversionOperation conversion =>
                CanCompleteConversion(conversion),
            IBinaryOperation binary => CanCompleteBinary(binary),
            IUnaryOperation unary =>
                ChildrenCanComplete(unary),
            IIncrementOrDecrementOperation increment =>
                CanCompleteNormally(increment.Target),
            IConditionalOperation conditional =>
                CanCompleteConditional(conditional),
            IBlockOperation or IExpressionStatementOperation or
                IReturnOperation or IVariableDeclarationGroupOperation or
                IVariableDeclarationOperation or IVariableDeclaratorOperation or
                IVariableInitializerOperation or IObjectOrCollectionInitializerOperation =>
                ChildrenCanComplete(operation),
            _ => true
        };
    }

    internal bool CanCompleteInvocation(
        IMethodSymbol method,
        IOperation? instance,
        IOperation origin,
        IEnumerable<IArgumentOperation>? arguments = null)
    {
        if (instance != null &&
            (!CanCompleteNormally(instance) ||
             method.ReducedFrom == null && _isProvenNull(instance, origin)))
        {
            return false;
        }

        if (arguments != null &&
            arguments.Any(argument => !CanCompleteNormally(argument.Value)))
        {
            return false;
        }

        return method.DeclaringSyntaxReferences.Length == 0 ||
            _completionFacts.MethodCanCompleteNormally(method);
    }

    private bool CanCompleteProperty(IPropertyReferenceOperation property)
    {
        var accessor = property.Property.GetMethod;
        if (accessor == null ||
            property.Instance != null &&
            (!CanCompleteNormally(property.Instance) ||
             _isProvenNull(property.Instance, property)))
        {
            return false;
        }

        return property.Arguments.All(argument =>
                   CanCompleteNormally(argument.Value)) &&
               (accessor.DeclaringSyntaxReferences.Length == 0 ||
                _completionFacts.MethodCanCompleteNormally(accessor));
    }

    private bool CanCompleteField(IFieldReferenceOperation field)
    {
        return (field.Instance == null ||
                CanCompleteNormally(field.Instance) &&
                !_isProvenNull(field.Instance, field));
    }

    private bool CanCompleteArrayElement(IArrayElementReferenceOperation element)
    {
        return CanCompleteNormally(element.ArrayReference) &&
            !_isProvenNull(element.ArrayReference, element) &&
            element.Indices.All(CanCompleteNormally);
    }

    internal bool CanCompleteConstruction(IObjectCreationOperation creation)
    {
        if (creation.Arguments.Any(argument =>
                !CanCompleteNormally(argument.Value)) ||
            creation.Constructor is not { } constructor ||
            constructor.DeclaringSyntaxReferences.Length != 0 &&
            !_completionFacts.MethodCanCompleteNormally(constructor))
        {
            return false;
        }

        return creation.Initializer == null ||
            CanCompleteNormally(creation.Initializer);
    }

    private bool CanCompleteArrayCreation(IArrayCreationOperation array)
    {
        if (array.DimensionSizes.Any(size =>
                !CanCompleteNormally(size) ||
                size.ConstantValue is { HasValue: true, Value: int length } &&
                length < 0))
        {
            return false;
        }

        return array.Initializer == null ||
            CanCompleteNormally(array.Initializer);
    }

    private bool CanCompleteConditionalAccess(
        IConditionalAccessOperation conditional)
    {
        if (!CanCompleteNormally(conditional.Operation))
        {
            return false;
        }

        if (_isProvenNull(conditional.Operation, conditional))
        {
            return true;
        }

        return !_isProvenNonNull(conditional.Operation, conditional) ||
            CanCompleteNormally(conditional.WhenNotNull);
    }

    private bool CanCompleteLock(ILockOperation @lock)
    {
        return CanCompleteNormally(@lock.LockedValue) &&
            !_isProvenNull(@lock.LockedValue, @lock) &&
            CanCompleteNormally(@lock.Body);
    }

    private bool CanCompleteConversion(IConversionOperation conversion)
    {
        if (!CanCompleteNormally(conversion.Operand))
        {
            return false;
        }

        return !(conversion.Type?.IsValueType == true &&
                 conversion.Operand.ConstantValue is
                 { HasValue: true, Value: null });
    }

    private bool CanCompleteBinary(IBinaryOperation binary)
    {
        if (!ChildrenCanComplete(binary))
        {
            return false;
        }

        return binary.OperatorKind is not (
            BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            binary.RightOperand.ConstantValue is not { HasValue: true, Value: 0 };
    }

    private bool CanCompleteConditional(IConditionalOperation conditional)
    {
        if (!CanCompleteNormally(conditional.Condition))
        {
            return false;
        }

        return CanCompleteNormally(conditional.WhenTrue) ||
            CanCompleteNormally(conditional.WhenFalse);
    }

    private bool ChildrenCanComplete(IOperation operation)
    {
        return operation.ChildOperations.All(CanCompleteNormally);
    }
}
