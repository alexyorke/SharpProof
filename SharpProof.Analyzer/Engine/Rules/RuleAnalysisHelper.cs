using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class RuleAnalysisHelper
{
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

        return SymbolEqualityComparer.Default.Equals(localReferenceOperation.Local, local) ||
               IsRefLocalAliasForLocal(
                   localReferenceOperation.Local,
                   local,
                   semanticModel,
                   new HashSet<ISymbol>(SymbolEqualityComparer.Default),
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

        foreach (var syntaxReference in possibleAlias.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declaratorSyntax ||
                declaratorSyntax.Initializer?.Value == null)
                continue;

            var initializerSyntax = declaratorSyntax.Initializer.Value;
            if (initializerSyntax is RefExpressionSyntax refExpressionSyntax)
                initializerSyntax = refExpressionSyntax.Expression;

            var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
            var unwrappedInitializer = PurityAnalysisEngine.SkipImplicitConversions(initializerOperation);
            if (unwrappedInitializer is not ILocalReferenceOperation initializerLocalReference) continue;

            if (SymbolEqualityComparer.Default.Equals(initializerLocalReference.Local, targetLocal) ||
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
                    !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter))
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
