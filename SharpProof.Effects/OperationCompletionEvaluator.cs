using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class OperationCompletionEvaluator
{
    private readonly ManagedFlowResult? _abstractFlow;
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
        Func<IInvocationOperation, bool> isImplicitLockEnterWithNullValue,
        ManagedFlowResult? abstractFlow = null)
    {
        _abstractFlow = abstractFlow;
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
            IInvocationOperation invocation when
                invocation.TargetMethod.Name == "<Clone>$" &&
                invocation.Syntax.ToString().IndexOf(
                    "with",
                    StringComparison.Ordinal) >= 0 &&
                GetRecordCopyConstructor(invocation.TargetMethod) is { } copyConstructor =>
                CanCompleteInvocation(
                    copyConstructor,
                    instance: null,
                    invocation),
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
            ICoalesceOperation coalesce =>
                CanCompleteCoalesce(coalesce),
            ITryOperation @try => CanCompleteTry(@try),
            ISwitchExpressionOperation switchExpression =>
                CanCompleteSwitchExpression(switchExpression),
            ISwitchExpressionArmOperation arm =>
                CanCompletePatternEvaluation(
                    arm.Pattern,
                    IsPatternInputDefinitelyNonNull(arm.Pattern)) &&
                (arm.Guard == null || CanCompleteNormally(arm.Guard)) &&
                CanCompleteNormally(arm.Value),
            IPropertySubpatternOperation propertySubpattern =>
                CanCompleteNormally(propertySubpattern.Member) &&
                CanCompletePatternEvaluation(propertySubpattern.Pattern),
            IPatternOperation pattern =>
                CanCompletePatternEvaluation(
                    pattern,
                    IsPatternInputDefinitelyNonNull(pattern)),
            IBlockOperation or IExpressionStatementOperation or
                IReturnOperation or IVariableDeclarationGroupOperation or
                IVariableDeclarationOperation or IVariableDeclaratorOperation or
                IVariableInitializerOperation or IObjectOrCollectionInitializerOperation =>
                ChildrenCanComplete(operation),
            ILabeledOperation labeled =>
                ChildrenCanComplete(labeled),
            _ => true
        };
    }

    private bool CanCompleteSwitchExpression(
        ISwitchExpressionOperation switchExpression)
    {
        if (!CanCompleteNormally(switchExpression.Value))
        {
            return false;
        }

        return SwitchExpressionFacts.GetReachableArms(
                switchExpression,
                CanCompleteNormally,
                _isProvenNonNull(
                    switchExpression.Value,
                    switchExpression))
            .Any(CanCompleteNormally);
    }

    private bool CanCompleteTry(ITryOperation @try)
    {
        if (@try.Finally != null && !CanCompleteNormally(@try.Finally))
        {
            return false;
        }

        return CanCompleteNormally(@try.Body) ||
            @try.Catches.Any(catchClause =>
                (catchClause.Filter == null ||
                 CanCompleteNormally(catchClause.Filter)) &&
                CanCompleteNormally(catchClause.Handler));
    }

    private bool CanCompletePatternEvaluation(
        IPatternOperation pattern,
        bool inputDefinitelyNonNull = false)
    {
        if (pattern is INegatedPatternOperation negated)
        {
            return CanCompletePatternEvaluation(
                negated.Pattern,
                inputDefinitelyNonNull);
        }
        if (pattern is IBinaryPatternOperation binary)
        {
            if (!CanCompletePatternEvaluation(
                    binary.LeftPattern,
                    inputDefinitelyNonNull))
            {
                return false;
            }
            var leftSelection =
                SwitchExpressionFacts.GetPatternSelectionForUnknownValue(
                    binary.LeftPattern,
                    binary.LeftPattern.InputType,
                    inputDefinitelyNonNull);
            var rightIsRequired =
                binary.OperatorKind == BinaryOperatorKind.And &&
                leftSelection == SwitchExpressionSelection.Always ||
                binary.OperatorKind == BinaryOperatorKind.Or &&
                leftSelection == SwitchExpressionSelection.Never;
            return !rightIsRequired || CanCompletePatternEvaluation(
                binary.RightPattern,
                inputDefinitelyNonNull);
        }
        if (pattern is IListPatternOperation listPattern &&
            !CanCompleteListPattern(
                listPattern,
                inputDefinitelyNonNull))
        {
            return false;
        }
        if (pattern is not IRecursivePatternOperation recursive ||
            pattern.InputType?.IsValueType != true &&
                !inputDefinitelyNonNull ||
            !SymbolEqualityComparer.Default.Equals(
                recursive.MatchedType,
                pattern.InputType))
        {
            return true;
        }
        if (recursive.DeconstructSymbol is IMethodSymbol deconstruct &&
            !CanMethodCompleteNormally(deconstruct))
        {
            return false;
        }
        foreach (var subpattern in recursive.DeconstructionSubpatterns)
        {
            if (!CanCompletePatternEvaluation(subpattern) &&
                SwitchExpressionFacts.IsTotalPattern(
                    subpattern,
                    subpattern.InputType))
            {
                return false;
            }
            if (!SwitchExpressionFacts.IsTotalPattern(
                    subpattern,
                    subpattern.InputType))
            {
                return true;
            }
        }
        foreach (var subpattern in recursive.PropertySubpatterns)
        {
            if (!CanCompleteNormally(subpattern.Member))
            {
                return false;
            }
            if (!CanCompletePatternEvaluation(subpattern.Pattern) &&
                SwitchExpressionFacts.IsTotalPattern(
                    subpattern.Pattern,
                    subpattern.Pattern.InputType))
            {
                return false;
            }
            if (!SwitchExpressionFacts.IsTotalPattern(
                    subpattern.Pattern,
                    subpattern.Pattern.InputType))
            {
                return true;
            }
        }
        return true;
    }

    private bool CanCompleteListPattern(
        IListPatternOperation pattern,
        bool inputDefinitelyNonNull)
    {
        if (pattern.InputType?.IsValueType != true &&
            !inputDefinitelyNonNull)
        {
            return true;
        }
        if (!CanListPatternMemberCompleteNormally(pattern.LengthSymbol))
        {
            return false;
        }
        var requiredLength = pattern.Patterns.Count(
            static item => item is not ISlicePatternOperation);
        var hasSlice = pattern.Patterns.Any(
            static item => item is ISlicePatternOperation);
        if (!TryGetGoverningListLength(pattern, out var length))
        {
            if (requiredLength != 0 || !hasSlice ||
                pattern.Patterns.Length != 1 ||
                pattern.Patterns[0] is not ISlicePatternOperation
                { Pattern: { } slicePattern } totalSlice)
            {
                return true;
            }
            return CanListPatternMemberCompleteNormally(
                    totalSlice.SliceSymbol) &&
                CanCompletePatternEvaluation(
                    slicePattern,
                    IsListPatternMemberResultDefinitelyNonNull(
                        totalSlice.SliceSymbol));
        }

        if (hasSlice ? length < requiredLength : length != requiredLength)
        {
            return true;
        }

        foreach (var item in pattern.Patterns)
        {
            if (item is ISlicePatternOperation slice)
            {
                if (slice.Pattern == null)
                {
                    continue;
                }
                if (!CanListPatternMemberCompleteNormally(slice.SliceSymbol) ||
                    !CanCompletePatternEvaluation(
                        slice.Pattern,
                        IsListPatternMemberResultDefinitelyNonNull(
                            slice.SliceSymbol)))
                {
                    return false;
                }
                if (!SwitchExpressionFacts.IsTotalPattern(
                        slice.Pattern,
                        slice.Pattern.InputType))
                {
                    return true;
                }
                continue;
            }

            if (!CanListPatternMemberCompleteNormally(pattern.IndexerSymbol) ||
                !CanCompletePatternEvaluation(
                    item,
                    IsListPatternMemberResultDefinitelyNonNull(
                        pattern.IndexerSymbol)))
            {
                return false;
            }
            if (!SwitchExpressionFacts.IsTotalPattern(
                    item,
                    item.InputType))
            {
                return true;
            }
        }
        return true;
    }

    private bool CanListPatternMemberCompleteNormally(ISymbol? symbol)
    {
        var method = SwitchExpressionFacts.GetCallableListPatternMember(symbol);
        return method == null ||
            CanDirectListPatternMemberCompleteNormally(method);
    }

    internal IReadOnlyList<IMethodSymbol>
        GetReachableImplicitListPatternMembers(IListPatternOperation pattern)
    {
        var methods = new List<IMethodSymbol>();
        var governingValue = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (governingValue != null &&
            _isProvenNull(governingValue, pattern))
        {
            return methods;
        }

        var lengthMember = SwitchExpressionFacts
            .GetCallableListPatternMember(pattern.LengthSymbol);
        if (lengthMember != null)
        {
            methods.Add(lengthMember);
            if (!CanDirectListPatternMemberCompleteNormally(lengthMember))
            {
                return methods;
            }
        }

        var requiredLength = pattern.Patterns.Count(
            static item => item is not ISlicePatternOperation);
        var hasSlice = pattern.Patterns.Any(
            static item => item is ISlicePatternOperation);
        var hasKnownLength = TryGetGoverningListLength(pattern, out var length);
        if (hasKnownLength &&
            (hasSlice ? length < requiredLength : length != requiredLength))
        {
            return methods;
        }

        foreach (var item in pattern.Patterns)
        {
            var member = item is ISlicePatternOperation slice
                ? slice.Pattern == null
                    ? null
                    : SwitchExpressionFacts.GetCallableListPatternMember(
                        slice.SliceSymbol)
                : SwitchExpressionFacts.GetCallableListPatternMember(
                    pattern.IndexerSymbol);
            if (member != null)
            {
                methods.Add(member);
                if (!CanDirectListPatternMemberCompleteNormally(member))
                {
                    return methods;
                }
            }

            var nestedPattern = item is ISlicePatternOperation nestedSlice
                ? nestedSlice.Pattern
                : item;
            if (nestedPattern != null &&
                !CanCompletePatternEvaluation(
                    nestedPattern,
                    IsListPatternMemberResultDefinitelyNonNull(
                        item is ISlicePatternOperation nestedSliceMember
                            ? nestedSliceMember.SliceSymbol
                            : pattern.IndexerSymbol)))
            {
                return methods;
            }
        }
        return methods;
    }

    private bool CanDirectListPatternMemberCompleteNormally(
        IMethodSymbol method)
    {
        return method.IsAbstract || method.IsVirtual && !method.IsSealed ||
            CanMethodCompleteNormally(method);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1508:Avoid dead conditional code",
        Justification = "The analyzer misreads the multi-branch nullable " +
            "assignment above the null check as unreachable.")]
    private bool IsListPatternMemberResultDefinitelyNonNull(ISymbol? symbol)
    {
        var method = SwitchExpressionFacts.GetCallableListPatternMember(symbol);
        if (method?.ReturnType.IsReferenceType != true ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
        var expression = declaration switch
        {
            MethodDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            PropertyDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            MethodDeclarationSyntax
            { Body.Statements.Count: 1 } methodDeclaration
                when methodDeclaration.Body!.Statements[0] is
                    ReturnStatementSyntax { Expression: { } body } => body,
            AccessorDeclarationSyntax
            { Body.Statements.Count: 1 } accessor
                when accessor.Body!.Statements[0] is
                    ReturnStatementSyntax { Expression: { } body } => body,
            _ => null
        };
        if (expression == null)
        {
            return false;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, expression.SyntaxTree);
        return model.GetOperation(expression) is { } operation &&
            DefiniteOperationFacts.IsDefinitelyNonNull(operation);
    }

    private bool TryGetGoverningListLength(
        IListPatternOperation pattern,
        out long length)
    {
        var value = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (value != null)
        {
            value = DefiniteOperationFacts.UnwrapHarmlessValue(value);
        }
        if (value is IArrayCreationOperation
            { DimensionSizes.Length: 1 } arrayCreation &&
            arrayCreation.DimensionSizes[0].ConstantValue is
            { HasValue: true, Value: int arrayLength })
        {
            length = arrayLength;
            return true;
        }
        if (pattern.LengthSymbol is IPropertySymbol
            { GetMethod: { } lengthGetter } &&
            (!lengthGetter.IsVirtual || lengthGetter.IsSealed) &&
            TryGetIntegralConstantReturn(lengthGetter, out length))
        {
            return true;
        }

        length = 0;
        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1508:Avoid dead conditional code",
        Justification = "The analyzer misreads the multi-branch nullable " +
            "assignment above the null check as unreachable.")]
    private bool TryGetIntegralConstantReturn(
        IMethodSymbol method,
        out long value)
    {
        value = 0;
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }
        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
        ExpressionSyntax? expression = null;
        if (declaration is PropertyDeclarationSyntax
            { ExpressionBody.Expression: { } propertyBody })
        {
            expression = propertyBody;
        }
        else if (declaration is AccessorDeclarationSyntax
        { ExpressionBody.Expression: { } accessorBody })
        {
            expression = accessorBody;
        }
        else if (declaration is AccessorDeclarationSyntax
        { Body.Statements.Count: 1 } accessor &&
                 accessor.Body!.Statements[0] is ReturnStatementSyntax
                 { Expression: { } returnBody })
        {
            expression = returnBody;
        }
        else if (declaration.DescendantNodes()
                     .OfType<ArrowExpressionClauseSyntax>()
                     .FirstOrDefault() is { Expression: { } arrowBody })
        {
            expression = arrowBody;
        }
        else if (declaration is ArrowExpressionClauseSyntax arrow)
        {
            expression = arrow.Expression;
        }
        if (expression == null)
        {
            return false;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression);
        if (!constant.HasValue || constant.Value == null)
        {
            return false;
        }
        try
        {
            value = Convert.ToInt64(
                constant.Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return value >= 0;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private bool IsPatternInputDefinitelyNonNull(IPatternOperation pattern)
    {
        IOperation? switchValue = null;
        IOperation? origin = null;
        if (pattern.Parent is ISwitchExpressionArmOperation arm &&
            arm.Parent is ISwitchExpressionOperation switchExpression)
        {
            switchValue = switchExpression.Value;
            origin = switchExpression;
        }
        else if (pattern.Parent is IPatternCaseClauseOperation clause &&
            clause.Parent is ISwitchCaseOperation @case &&
            @case.Parent is ISwitchOperation switchStatement)
        {
            switchValue = switchStatement.Value;
            origin = switchStatement;
        }

        return switchValue != null && origin != null &&
            (DefiniteOperationFacts.IsDefinitelyNonNull(switchValue) ||
             _isProvenNonNull(switchValue, origin));
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
        member = NormalizeStaticInitializationMember(member);
        if (!member.IsStatic && member is not IMethodSymbol
            { MethodKind: MethodKind.Constructor })
        {
            return false;
        }
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

    internal static bool CanAssumeStaticInitializationComplete(
        IMethodSymbol caller,
        ISymbol member)
    {
        member = NormalizeStaticInitializationMember(member);
        return SymbolEqualityComparer.Default.Equals(
                caller.ContainingType,
                member.ContainingType) &&
            (caller.MethodKind == MethodKind.StaticConstructor ||
             caller.ContainingType.StaticConstructors.Any(
                 static constructor => !constructor.IsImplicitlyDeclared));
    }

    internal static ISymbol NormalizeStaticInitializationMember(
        ISymbol member)
    {
        return member is IMethodSymbol { ReducedFrom: { } reduced }
            ? reduced
            : member;
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
            element.Indices.All(CanCompleteNormally) &&
            ArrayAccessMayComplete(element);
    }

    private bool ArrayAccessMayComplete(
        IArrayElementReferenceOperation element)
    {
        if (_abstractFlow == null ||
            element.Indices.Length != 1 ||
            !_abstractFlow.TryEvaluate(
                element,
                element.ArrayReference,
                out var array) ||
            !_abstractFlow.TryEvaluate(
                element,
                element.Indices[0],
                out var index) ||
            !array.TryGetCardinality(out var length) ||
            !index.TryGetInteger(out var interval))
        {
            return true;
        }

        if (interval.UpperBound is { } maximumIndex && maximumIndex < 0)
        {
            return false;
        }

        return length.UpperBound is not { } maximumLength ||
            maximumLength > 0 &&
            (interval.LowerBound is not { } minimumIndex ||
             minimumIndex < maximumLength);
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

    private bool CanCompleteCoalesce(ICoalesceOperation coalesce)
    {
        if (!CanCompleteNormally(coalesce.Value))
        {
            return false;
        }

        if (_isProvenNonNull(coalesce.Value, coalesce))
        {
            return true;
        }

        return !_isProvenNull(coalesce.Value, coalesce) ||
            CanCompleteNormally(coalesce.WhenNull);
    }

    private bool CanCompleteDeconstruction(
        IDeconstructionAssignmentOperation deconstruction)
    {
        if (!CanCompleteNormally(deconstruction.Value))
        {
            return false;
        }

        var phasesMayComplete = !TryGetDeconstructionInfo(
            _compilation,
            deconstruction,
            out var info) ||
            DeconstructionPhasesMayComplete(
                info,
                deconstruction.Value,
                isRoot: true,
                origin: deconstruction);
        return phasesMayComplete &&
            CanCompleteDeconstructionTarget(deconstruction.Target);
    }

    private bool CanCompleteDeconstructionTarget(IOperation target)
    {
        if (target is ITupleOperation tuple)
        {
            return tuple.Elements.All(CanCompleteDeconstructionTarget);
        }

        return CanCompleteWriteTarget(target);
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

        foreach (var nested in info.Nested.IsDefault
                     ? ImmutableArray<DeconstructionInfo>.Empty
                     : info.Nested)
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

        return info.Conversion?.MethodSymbol is not { } conversion ||
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

        info = model.GetDeconstructionInfo(syntax);
        return true;
    }

    internal bool CanCompleteCompoundValue(
        ICompoundAssignmentOperation assignment)
    {
        return CanCompleteNormally(assignment.Target) &&
            CanCompleteCompoundInConversion(assignment) &&
            CanCompleteNormally(assignment.Value) &&
            CanCompleteCompoundOperator(assignment) &&
            CanCompleteCompoundOutConversion(assignment);
    }

    internal bool CanCompleteCompoundInConversion(
        ICompoundAssignmentOperation assignment)
    {
        return CanCompleteCompoundConversion(
            assignment.InConversion.MethodSymbol,
            assignment);
    }

    internal bool CanCompleteCompoundOperator(
        ICompoundAssignmentOperation assignment)
    {

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

    internal bool CanCompleteCompoundOutConversion(
        ICompoundAssignmentOperation assignment)
    {
        return CanCompleteCompoundConversion(
            assignment.OutConversion.MethodSymbol,
            assignment);
    }

    private bool CanCompleteCompoundConversion(
        IMethodSymbol? method,
        ICompoundAssignmentOperation assignment)
    {
        return method == null ||
            CanCompleteInvocation(method, instance: null, assignment);
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
        member = NormalizeStaticInitializationMember(member);
        if (!RequiresStaticInitializationCompletion(member) ||
            CanAssumeStaticInitializationComplete(_caller, member) ||
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

        if (conversion.OperatorMethod == null &&
            conversion.Type?.IsValueType == true &&
            !ManagedAbstractValue.IsNullableType(conversion.Type) &&
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
                var truthOperator = ConditionalTruthOperatorFacts.Resolve(
                    binary);
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
