using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckLinqSourceEnumeratorPurity(
        IOperation sourceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var unwrappedSource = PurityAnalysisEngine.SkipImplicitConversions(sourceOperation) ?? sourceOperation;
        var sourceType = PurityAnalysisEngine.TryResolveKnownConcreteType(unwrappedSource, currentState,
            context.SemanticModel.Compilation, out var concreteType)
            ? concreteType
            : unwrappedSource.Type;
        if (sourceType == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        foreach (var getEnumerator in LoopPurityRule.EnumerateGetEnumeratorImplementations(sourceType))
        {
            var enumeratorPurity = PurityAnalysisEngine.GetCalleePurity(getEnumerator.OriginalDefinition, context);
            if (!enumeratorPurity.IsPure) return enumeratorPurity.WithCallee(getEnumerator, unwrappedSource.Syntax);

            foreach (var enumeratorType in EnumerateLinqReturnedEnumeratorTypes(
                         getEnumerator,
                         sourceType,
                         context.SemanticModel,
                         context.CancellationToken))
            {
                var runtimePurity = LoopPurityRule.CheckForEachEnumeratorRuntimeMemberPurity(
                    enumeratorType,
                    unwrappedSource.Syntax,
                    context);
                if (!runtimePurity.IsPure) return runtimePurity;
            }
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateLinqReturnedEnumeratorTypes(
        IMethodSymbol getEnumerator,
        ITypeSymbol sourceType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        AddConcreteLinqEnumeratorType(getEnumerator.ReturnType, seen);
        AddNestedLinqEnumeratorTypes(sourceType, seen);

        foreach (var syntaxReference in getEnumerator.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax methodDeclaration) continue;

            if (methodDeclaration.ExpressionBody?.Expression != null)
                AddConcreteLinqEnumeratorType(
                    GetLinqExpressionType(methodDeclaration.ExpressionBody.Expression, semanticModel,
                        cancellationToken),
                    seen);

            if (methodDeclaration.Body == null) continue;

            foreach (var returnStatement in methodDeclaration.Body.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (returnStatement.Expression == null) continue;

                AddConcreteLinqEnumeratorType(
                    GetLinqExpressionType(returnStatement.Expression, semanticModel, cancellationToken),
                    seen);
            }
        }

        return seen;
    }

    private static void AddConcreteLinqEnumeratorType(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> enumeratorTypes)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.TypeKind != TypeKind.Interface &&
            namedType.DeclaringSyntaxReferences.Length > 0)
            enumeratorTypes.Add(namedType.OriginalDefinition);
    }

    private static void AddNestedLinqEnumeratorTypes(
        ITypeSymbol sourceType,
        HashSet<INamedTypeSymbol> enumeratorTypes)
    {
        if (sourceType is not INamedTypeSymbol namedSourceType) return;

        foreach (var nestedType in EnumerateLinqNestedTypes(namedSourceType))
        {
            if (nestedType.DeclaringSyntaxReferences.Length == 0 ||
                !IsLinqEnumeratorType(nestedType))
                continue;

            enumeratorTypes.Add(nestedType.OriginalDefinition);
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateLinqNestedTypes(INamedTypeSymbol typeSymbol)
    {
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            yield return nestedType;
            foreach (var descendant in EnumerateLinqNestedTypes(nestedType)) yield return descendant;
        }
    }

    private static bool IsLinqEnumeratorType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces.Any(interfaceType =>
            interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_IEnumerator ||
            interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerator_T);
    }

    private static ITypeSymbol? GetLinqExpressionType(ExpressionSyntax expression, SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = semanticModel.GetOperation(expression, cancellationToken);
        while (operation is IConversionOperation conversion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation = conversion.Operand;
        }

        return operation?.Type ?? semanticModel.GetTypeInfo(expression, cancellationToken).Type;
    }
}