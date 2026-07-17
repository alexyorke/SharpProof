using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class RuleAnalysisHelper
{
    internal static IEnumerable<IOperation> EnumerateReachableAlternatives(
        IOperation operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (operation)
        {
            case IConditionalOperation conditional
                when TryGetConstantCondition(conditional, out var conditionValue):
                var selected = conditionValue ? conditional.WhenTrue : conditional.WhenFalse;
                if (selected != null)
                    foreach (var alternative in EnumerateReachableAlternatives(selected, cancellationToken))
                        yield return alternative;
                yield break;
            case IConditionalOperation conditional:
                if (conditional.WhenTrue != null)
                    foreach (var alternative in EnumerateReachableAlternatives(
                                 conditional.WhenTrue,
                                 cancellationToken))
                        yield return alternative;
                if (conditional.WhenFalse != null)
                    foreach (var alternative in EnumerateReachableAlternatives(
                                 conditional.WhenFalse,
                                 cancellationToken))
                        yield return alternative;
                yield break;
            case ICoalesceOperation coalesce:
                foreach (var alternative in EnumerateReachableAlternatives(coalesce.Value, cancellationToken))
                    yield return alternative;
                foreach (var alternative in EnumerateReachableAlternatives(coalesce.WhenNull, cancellationToken))
                    yield return alternative;
                yield break;
            default:
                yield return operation;
                yield break;
        }
    }

    internal static IEnumerable<IOperation> EnumerateRefLocalInitializerOperations(
        ILocalSymbol localSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (localSymbol.RefKind == RefKind.None) yield break;

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declaratorSyntax ||
                declaratorSyntax.Initializer?.Value == null)
                continue;

            var initializerSyntax = declaratorSyntax.Initializer.Value;
            if (initializerSyntax is RefExpressionSyntax refExpressionSyntax)
                initializerSyntax = refExpressionSyntax.Expression;

            var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
            if (PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is { } unwrappedInitializer)
                yield return unwrappedInitializer;
        }
    }

    internal static bool TryGetStableLocalInitializer(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializerSyntax,
        out IOperation initializerOperation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!visitedLocals.Add(localSymbol))
        {
            initializerSyntax = null!;
            initializerOperation = null!;
            return false;
        }

        var declaratorSyntax = GetVariableDeclaratorSyntax(localSymbol, cancellationToken);
        initializerSyntax = declaratorSyntax?.Initializer?.Value!;
        if (declaratorSyntax == null ||
            initializerSyntax == null ||
            HasAssignmentToLocalBetweenDeclarationAndObservation(
                localSymbol,
                observationSyntax,
                declaratorSyntax,
                semanticModel,
                cancellationToken) ||
            PurityAnalysisEngine.SkipImplicitConversions(
                semanticModel.GetOperation(initializerSyntax, cancellationToken)) is not { } operation)
        {
            initializerOperation = null!;
            return false;
        }

        initializerOperation = operation;
        return true;
    }

    internal static VariableDeclaratorSyntax? GetVariableDeclaratorSyntax(
        ILocalSymbol localSymbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckInstanceAndArguments(
        IOperation? instance,
        IEnumerable<IArgumentOperation> arguments,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (instance != null)
        {
            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(instance, context, currentState);
            if (!instanceResult.IsPure) return instanceResult;
        }

        foreach (var argument in arguments)
        {
            if (argument.Value is not { } value)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(argument.Syntax);

            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(value, context, currentState);
            if (!argumentResult.IsPure) return argumentResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    internal static bool IsWriteOnlyAssignmentTarget(IOperation operation)
    {
        for (var current = operation; current.Parent != null; current = current.Parent)
        {
            if (current.Parent is ISimpleAssignmentOperation simpleAssignment &&
                ReferenceEquals(simpleAssignment.Target, current))
                return true;

            if (current.Parent is IDeconstructionAssignmentOperation deconstructionAssignment &&
                ReferenceEquals(deconstructionAssignment.Target, current))
                return true;

            if (current.Parent is ITupleOperation or IDeclarationExpressionOperation or IConversionOperation)
                continue;

            return false;
        }

        return false;
    }

    internal static bool IsThisOrImplicitInstance(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation == null ||
               unwrappedOperation is IInstanceReferenceOperation
               {
                   ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
               };
    }

    internal static bool IsSourceAutoPropertyAccessor(
        IPropertySymbol propertySymbol,
        bool getter,
        CancellationToken cancellationToken)
    {
        var accessorMethod = getter ? propertySymbol.GetMethod : propertySymbol.SetMethod;
        if (accessorMethod == null ||
            accessorMethod.IsAbstract ||
            propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            return false;

        foreach (var syntaxReference in propertySymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax
                {
                    AccessorList: { } accessorList
                })
                continue;

            var accessor = accessorList.Accessors.FirstOrDefault(candidate =>
                getter
                    ? candidate.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                    : candidate.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration) ||
                      candidate.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration));
            if (accessor is { Body: null, ExpressionBody: null }) return true;
        }

        return false;
    }

    internal static bool IsFreshLocalArrayInitialization(IOperation operation)
    {
        var current = operation.Parent;

        if (current is IConversionOperation conversionOperation) current = conversionOperation.Parent;

        if (current is IVariableInitializerOperation variableInitializer &&
            variableInitializer.Parent is IVariableDeclaratorOperation variableDeclarator &&
            variableDeclarator.Symbol.Type is IArrayTypeSymbol)
            return true;

        if (current is IAssignmentOperation assignmentOperation &&
            assignmentOperation.Target is ILocalReferenceOperation localReference &&
            localReference.Type is IArrayTypeSymbol)
            return true;

        return false;
    }

    internal static bool IsStaticAbstractInterfaceMethod(
        IMethodSymbol methodSymbol,
        MethodKind methodKind)
    {
        return methodSymbol.IsStatic &&
               methodSymbol.IsAbstract &&
               methodSymbol.MethodKind == methodKind &&
               methodSymbol.ContainingType?.TypeKind == TypeKind.Interface;
    }

    internal static bool HasAssignmentToLocalBetweenDeclarationAndObservation(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SyntaxNode declarationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var start = declarationSyntax.Span.End;
        var end = observationSyntax.SpanStart;
        if (end <= start) return false;

        var observationModel = CompilationSyntaxAccess.GetSemanticModel(semanticModel, observationSyntax);
        var blockOperation = observationModel.GetOperation(containingBlock, cancellationToken);
        if (blockOperation == null) return false;

        foreach (var operation in blockOperation.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Syntax.SpanStart < start || operation.Syntax.SpanStart >= end) continue;

            switch (operation)
            {
                case ISimpleAssignmentOperation assignment
                    when IsLocalTarget(assignment.Target, localSymbol, observationModel, cancellationToken):
                case ICompoundAssignmentOperation compoundAssignment when IsLocalTarget(compoundAssignment.Target,
                    localSymbol, observationModel, cancellationToken):
                case IIncrementOrDecrementOperation incrementOrDecrement when IsLocalTarget(incrementOrDecrement.Target,
                    localSymbol, observationModel, cancellationToken):
                case IDeconstructionAssignmentOperation deconstructionAssignment
                    when ContainsLocalAssignmentTarget(deconstructionAssignment.Target, localSymbol, observationModel,
                        cancellationToken):
                case IInvocationOperation invocationOperation when HasByRefLocalArgument(invocationOperation,
                    localSymbol, observationModel, cancellationToken):
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsLocalAssignmentTarget(
        IOperation? targetOperation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
        if (IsLocalTarget(unwrappedTarget, local, semanticModel, cancellationToken)) return true;

        if (unwrappedTarget is ITupleOperation tupleOperation)
            foreach (var element in tupleOperation.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ContainsLocalAssignmentTarget(element, local, semanticModel, cancellationToken)) return true;
            }

        return false;
    }

    private static bool HasByRefLocalArgument(
        IInvocationOperation invocationOperation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocationOperation.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                IsLocalTarget(argument.Value, local, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsLocalTarget(
        IOperation? targetOperation,
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
        if (unwrappedTarget is not ILocalReferenceOperation localReferenceOperation) return false;

        return SymbolEq.AreEqual(localReferenceOperation.Local, local) ||
               IsRefLocalAliasForLocal(
                   localReferenceOperation.Local,
                   local,
                   semanticModel,
                   new HashSet<ISymbol>(SymbolEq.Default),
                   cancellationToken);
    }

    private static bool IsRefLocalAliasForLocal(
        ILocalSymbol possibleAlias,
        ILocalSymbol targetLocal,
        SemanticModel semanticModel,
        HashSet<ISymbol> visited,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (possibleAlias.RefKind == RefKind.None || !visited.Add(possibleAlias)) return false;

        foreach (var unwrappedInitializer in EnumerateRefLocalInitializerOperations(
                     possibleAlias,
                     semanticModel,
                     cancellationToken))
        {
            if (unwrappedInitializer is not ILocalReferenceOperation initializerLocalReference) continue;

            if (SymbolEq.AreEqual(initializerLocalReference.Local, targetLocal) ||
                IsRefLocalAliasForLocal(initializerLocalReference.Local, targetLocal, semanticModel, visited,
                    cancellationToken))
                return true;
        }

        return false;
    }

    internal static bool TryGetConstantCondition(IConditionalOperation conditionalOperation, out bool conditionValue)
    {
        var constantValue = conditionalOperation.Condition.ConstantValue;
        if (constantValue.HasValue && constantValue.Value is bool boolValue)
        {
            conditionValue = boolValue;
            return true;
        }

        conditionValue = false;
        return false;
    }

    internal static bool ConstructorStoresParameterMatching(
        IMethodSymbol constructor,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<IOperation, bool> targetMatches)
    {
        foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var constructorSyntax = syntaxReference.GetSyntax(cancellationToken);
            var constructorModel = semanticModel.Compilation.GetSemanticModel(constructorSyntax.SyntaxTree);
            foreach (var assignment in constructorSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (constructorModel.GetOperation(assignment, cancellationToken) is not ISimpleAssignmentOperation
                    assignmentOperation) continue;

                if (PurityAnalysisEngine.SkipImplicitConversions(assignmentOperation.Value) is not
                        IParameterReferenceOperation parameterReference ||
                    !SymbolEq.AreEqual(parameterReference.Parameter, parameter))
                    continue;

                if (targetMatches(assignmentOperation.Target)) return true;
            }
        }

        return false;
    }

    internal static bool IsSemanticallyPureSpanLikeSliceInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.MethodKind != MethodKind.Ordinary ||
            targetMethod.Name != "Slice" ||
            targetMethod.IsStatic)
            return false;

        var containingType = targetMethod.ContainingType?.OriginalDefinition.ToDisplayString();
        if (containingType is not ("System.Span<T>" or "System.ReadOnlySpan<T>" or "System.Memory<T>"
            or "System.ReadOnlyMemory<T>")) return false;

        if (targetMethod.Parameters.Length is not (1 or 2)) return false;

        return targetMethod.Parameters.All(parameter =>
            parameter.RefKind == RefKind.None &&
            parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    internal static bool IsFreshMutableEscapingReferenceType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind == TypeKind.Delegate ||
            namedType.IsValueType ||
            namedType.SpecialType == SpecialType.System_String ||
            namedType.DeclaringSyntaxReferences.Length == 0)
            return false;

        return namedType.GetMembers().Any(member =>
            member switch
            {
                IFieldSymbol field => !field.IsStatic &&
                                      !field.IsReadOnly &&
                                      field.DeclaredAccessibility != Accessibility.Private,
                IPropertySymbol property => !property.IsStatic &&
                                            property.SetMethod != null &&
                                            !property.SetMethod.IsInitOnly &&
                                            property.SetMethod.DeclaredAccessibility != Accessibility.Private,
                _ => false
            });
    }
}
