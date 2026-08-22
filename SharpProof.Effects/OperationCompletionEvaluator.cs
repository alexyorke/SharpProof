using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class OperationCompletionEvaluator
{
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly IMethodSymbol _caller;
    private readonly Compilation _compilation;
    private readonly DefiniteOperationFacts _completionFacts;
    private readonly Func<IOperation?, IOperation, bool> _isProvenNull;
    private readonly Func<IOperation?, IOperation, bool> _isProvenNonNull;
    private readonly Func<IInvocationOperation, bool> _isImplicitLockEnterWithNullValue;
    private readonly DefiniteOperationFacts _staticInitializationFacts;

    internal OperationCompletionEvaluator(
        EffectAnalysisSession session,
        IMethodSymbol caller,
        Func<IOperation?, IOperation, bool> isProvenNull,
        Func<IOperation?, IOperation, bool> isProvenNonNull,
        Func<IInvocationOperation, bool> isImplicitLockEnterWithNullValue)
    {
        _apiSpecs = session.ApiSpecs;
        _caller = caller;
        _compilation = session.Compilation;
        _completionFacts = new DefiniteOperationFacts(
            session.Compilation,
            CancellationToken.None);
        _staticInitializationFacts = new DefiniteOperationFacts(
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
                    invocation,
                    invocation.Arguments),
            IPropertyReferenceOperation property =>
                CanCompleteProperty(property),
            IFieldReferenceOperation field =>
                CanCompleteField(field),
            IArrayElementReferenceOperation element =>
                CanCompleteArrayElement(element),
            IAnonymousObjectCreationOperation or
                IDelegateCreationOperation =>
                ChildrenCanComplete(operation),
            IObjectCreationOperation creation =>
                CanCompleteConstruction(creation),
            IArrayCreationOperation array =>
                CanCompleteArrayCreation(array),
            IConditionalAccessOperation conditional =>
                CanCompleteConditionalAccess(conditional),
            IWithOperation withOperation =>
                CanCompleteWith(withOperation),
            ILockOperation @lock => CanCompleteLock(@lock),
            IFlowCaptureOperation capture =>
                CanCompleteNormally(capture.Value),
            IMethodReferenceOperation methodReference =>
                CanCompleteMethodReference(methodReference),
            IArgumentOperation argument =>
                CanCompleteNormally(argument.Value),
            ICoalesceAssignmentOperation assignment =>
                CanCompleteCoalesceAssignment(assignment),
            IDeconstructionAssignmentOperation deconstruction =>
                CanCompleteDeconstruction(deconstruction),
            ISimpleAssignmentOperation assignment =>
                CanCompleteWriteTarget(assignment.Target) &&
                CanCompleteNormally(assignment.Value),
            ICompoundAssignmentOperation assignment =>
                CanCompleteCompoundValue(assignment) &&
                CanCompleteWriteTarget(assignment.Target),
            IParenthesizedOperation parenthesized =>
                CanCompleteNormally(parenthesized.Operand),
            IConversionOperation conversion =>
                CanCompleteConversion(conversion),
            IBinaryOperation binary => CanCompleteBinary(binary),
            IUnaryOperation unary =>
                CanCompleteUnary(unary),
            IIncrementOrDecrementOperation increment =>
                CanCompleteIncrementValue(increment) &&
                CanCompleteWriteTarget(increment.Target),
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

        return StaticInitializationMayComplete(method) &&
            (method.DeclaringSyntaxReferences.Length == 0 ||
             _completionFacts.MethodCanCompleteNormally(method));
    }

    internal bool CanMethodCompleteNormally(IMethodSymbol method)
    {
        return StaticInitializationMayComplete(method) &&
            (method.DeclaringSyntaxReferences.Length == 0 ||
             _completionFacts.MethodCanCompleteNormally(method));
    }

    internal static bool RequiresStaticInitializationCompletion(
        ISymbol member)
    {
        if (member is IMethodSymbol
            { MethodKind: MethodKind.StaticConstructor })
        {
            return false;
        }

        return member is IFieldSymbol { IsStatic: true, IsConst: false } ||
            member.ContainingType?.StaticConstructors.Any(
                static constructor => !constructor.IsImplicitlyDeclared) ==
            true;
    }

    internal bool CanCompleteWithClone(IWithOperation withOperation)
    {
        if (!CanCompleteNormally(withOperation.Operand) ||
            withOperation.Operand.Type?.IsReferenceType == true &&
            _isProvenNull(withOperation.Operand, withOperation))
        {
            return false;
        }

        if (withOperation.CloneMethod is not { } clone)
        {
            return true;
        }

        var copyConstructor = GetRecordCopyConstructor(clone);
        return copyConstructor == null
            ? CanCompleteInvocation(
                clone,
                withOperation.Operand,
                withOperation)
            : CanCompleteInvocation(
                copyConstructor,
                instance: null,
                withOperation);
    }

    internal static IMethodSymbol? GetRecordCopyConstructor(
        IMethodSymbol clone)
    {
        var type = clone.ContainingType;
        return type.InstanceConstructors.FirstOrDefault(constructor =>
            constructor.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(
                constructor.Parameters[0].Type,
                type));
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
               StaticInitializationMayComplete(property.Property) &&
               (accessor.DeclaringSyntaxReferences.Length == 0 ||
                _completionFacts.MethodCanCompleteNormally(accessor));
    }

    private bool CanCompleteMethodReference(
        IMethodReferenceOperation methodReference)
    {
        return ChildrenCanComplete(methodReference) &&
            (methodReference.Method.IsStatic ||
             methodReference.Instance == null ||
             !_isProvenNull(methodReference.Instance, methodReference));
    }

    private bool CanCompleteField(IFieldReferenceOperation field)
    {
        return (field.Instance == null ||
                CanCompleteNormally(field.Instance) &&
                !_isProvenNull(field.Instance, field)) &&
            StaticInitializationMayComplete(field.Field);
    }

    private bool CanCompleteArrayElement(IArrayElementReferenceOperation element)
    {
        return CanCompleteNormally(element.ArrayReference) &&
            !_isProvenNull(element.ArrayReference, element) &&
            element.Indices.All(CanCompleteNormally);
    }

    private bool CanCompleteWriteTarget(IOperation target)
    {
        return target switch
        {
            IFieldReferenceOperation field =>
                CanCompleteField(field),
            IArrayElementReferenceOperation element =>
                CanCompleteArrayElement(element),
            IPropertyReferenceOperation property
                when property.Property.SetMethod is { } setter =>
                CanCompleteInvocation(
                    setter,
                    property.Instance,
                    property,
                    property.Arguments),
            ILocalReferenceOperation or
                IParameterReferenceOperation or
                IDiscardOperation => true,
            _ => true
        };
    }

    private bool CanCompleteCoalesceAssignment(
        ICoalesceAssignmentOperation assignment)
    {
        if (!CanCompleteNormally(assignment.Target))
        {
            return false;
        }

        if (_isProvenNonNull(assignment.Target, assignment))
        {
            return true;
        }

        return !_isProvenNull(assignment.Target, assignment) ||
            CanCompleteNormally(assignment.Value) &&
            CanCompleteWriteTarget(assignment.Target);
    }

    private bool CanCompleteDeconstruction(
        IDeconstructionAssignmentOperation deconstruction)
    {
        if (!CanCompleteNormally(deconstruction.Value))
        {
            return false;
        }

        return !TryGetDeconstructionInfo(
                _compilation,
                deconstruction,
                out var info) ||
            DeconstructionPhasesMayComplete(
                info,
                deconstruction.Value,
                isRoot: true,
                origin: deconstruction);
    }

    private bool DeconstructionPhasesMayComplete(
        Microsoft.CodeAnalysis.CSharp.DeconstructionInfo info,
        IOperation value,
        bool isRoot,
        IOperation origin)
    {
        if (info.Method is { } method)
        {
            var callable = method.ReducedFrom ?? method;
            var completes = isRoot &&
                !method.IsStatic &&
                method.ReducedFrom == null
                    ? CanCompleteInvocation(method, value, origin)
                    : CanMethodCompleteNormally(callable);
            if (!completes)
            {
                return false;
            }
        }

        foreach (var nested in info.Nested)
        {
            if (!DeconstructionPhasesMayComplete(
                    nested,
                    value,
                    isRoot: false,
                    origin: origin))
            {
                return false;
            }
        }

        return info.Conversion.MethodSymbol is not { } conversion ||
            CanMethodCompleteNormally(conversion);
    }

    private static bool TryGetDeconstructionInfo(
        Compilation compilation,
        IDeconstructionAssignmentOperation operation,
        out DeconstructionInfo info)
    {
        info = default;
        if (operation.Syntax is not AssignmentExpressionSyntax syntax)
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, syntax.SyntaxTree);
        if (model is not CSharpSemanticModel csharpModel)
        {
            return false;
        }

        info = csharpModel.GetDeconstructionInfo(syntax);
        return true;
    }

    internal bool CanCompleteCompoundValue(
        ICompoundAssignmentOperation assignment)
    {
        if (!CanCompleteNormally(assignment.Target) ||
            !CanCompleteNormally(assignment.Value))
        {
            return false;
        }

        if (assignment.OperatorKind is
                BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder &&
            assignment.Value.ConstantValue is { HasValue: true, Value: 0 })
        {
            return false;
        }

        return assignment.OperatorMethod == null ||
            CanCompleteInvocation(
                assignment.OperatorMethod,
                instance: null,
                assignment);
    }

    internal bool CanCompleteIncrementValue(
        IIncrementOrDecrementOperation increment)
    {
        return CanCompleteNormally(increment.Target) &&
            (increment.OperatorMethod == null ||
             CanCompleteInvocation(
                 increment.OperatorMethod,
                 instance: null,
                 increment));
    }

    internal bool CanCompleteConstruction(IObjectCreationOperation creation)
    {
        if (creation.Arguments.Any(argument =>
                !CanCompleteNormally(argument.Value)) ||
            creation.Constructor is not { } constructor ||
            !StaticInitializationMayComplete(constructor) ||
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

    private bool StaticInitializationMayComplete(ISymbol member)
    {
        if (!RequiresStaticInitializationCompletion(member) ||
            SymbolEqualityComparer.Default.Equals(
                _caller.ContainingType,
                member.ContainingType) ||
            member.ContainingType is not { } type ||
            !EffectMethodNodeBuilder.HasPotentialStaticInitialization(
                type,
                _apiSpecs))
        {
            return true;
        }

        foreach (var typeMember in type.GetMembers())
        {
            var isStaticInitializable = typeMember switch
            {
                IFieldSymbol field => field.IsStatic && !field.IsConst,
                IPropertySymbol property => property.IsStatic,
                IEventSymbol @event => @event.IsStatic,
                _ => false
            };
            if (!isStaticInitializable)
            {
                continue;
            }
            foreach (var reference in typeMember.DeclaringSyntaxReferences)
            {
                var expression = EffectProjections.GetInitializerExpression(
                    reference.GetSyntax());
                if (expression == null)
                {
                    continue;
                }
                var model = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, expression.SyntaxTree);
                var operation = model.GetOperation(expression);
                if (operation != null &&
                    !_staticInitializationFacts.MayCompleteNormally(operation))
                {
                    return false;
                }
            }
        }

        return type.StaticConstructors.All(constructor =>
            constructor.DeclaringSyntaxReferences.Length == 0 ||
            _completionFacts.MethodCanCompleteNormally(constructor));
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

    private bool CanCompleteWith(IWithOperation withOperation)
    {
        return CanCompleteWithClone(withOperation) &&
            CanCompleteNormally(withOperation.Initializer);
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

        if (conversion.Type?.IsValueType == true &&
            conversion.Operand.ConstantValue is
            { HasValue: true, Value: null })
        {
            return false;
        }

        return conversion.OperatorMethod == null ||
            CanCompleteInvocation(
                conversion.OperatorMethod,
                instance: null,
                conversion);
    }

    private bool CanCompleteBinary(IBinaryOperation binary)
    {
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or
            BinaryOperatorKind.ConditionalOr)
        {
            if (!CanCompleteNormally(binary.LeftOperand))
            {
                return false;
            }

            if (binary.OperatorMethod == null &&
                binary.LeftOperand.ConstantValue is
                { HasValue: true, Value: bool left })
            {
                var shortCircuits = binary.OperatorKind ==
                    BinaryOperatorKind.ConditionalAnd
                        ? !left
                        : left;
                return shortCircuits ||
                    CanCompleteNormally(binary.RightOperand);
            }

            if (binary.OperatorMethod != null)
            {
                var truthOperatorName = binary.OperatorKind ==
                    BinaryOperatorKind.ConditionalAnd
                        ? "op_False"
                        : "op_True";
                var truthOperator = binary.OperatorMethod.ContainingType
                    .GetMembers(truthOperatorName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                return truthOperator == null ||
                    CanMethodCompleteNormally(truthOperator);
            }

            return true;
        }

        if (!ChildrenCanComplete(binary))
        {
            return false;
        }

        if (binary.OperatorKind is
                BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder &&
            binary.RightOperand.ConstantValue is { HasValue: true, Value: 0 })
        {
            return false;
        }

        return binary.OperatorMethod == null ||
            CanCompleteInvocation(
                binary.OperatorMethod,
                instance: null,
                binary);
    }

    private bool CanCompleteUnary(IUnaryOperation unary)
    {
        return CanCompleteNormally(unary.Operand) &&
            (unary.OperatorMethod == null ||
             CanCompleteInvocation(
                 unary.OperatorMethod,
                 instance: null,
                 unary));
    }

    private bool CanCompleteConditional(IConditionalOperation conditional)
    {
        if (!CanCompleteNormally(conditional.Condition))
        {
            return false;
        }

        if (conditional.Condition.ConstantValue is
            { HasValue: true, Value: bool condition })
        {
            return CanCompleteNormally(
                condition
                    ? conditional.WhenTrue
                    : conditional.WhenFalse);
        }

        return CanCompleteNormally(conditional.WhenTrue) ||
            CanCompleteNormally(conditional.WhenFalse);
    }

    private bool ChildrenCanComplete(IOperation operation)
    {
        return operation.ChildOperations.All(CanCompleteNormally);
    }
}
