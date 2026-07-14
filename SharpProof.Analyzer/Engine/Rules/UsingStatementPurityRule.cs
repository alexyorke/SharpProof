using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class UsingStatementPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds =>
        ImmutableArray.Create(OperationKind.Using, OperationKind.UsingDeclaration);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        SyntaxNode? impureSyntaxNode = null;
        IOperation? resourceOperation = null;
        IOperation? bodyOperation = null;

        if (operation is IUsingOperation usingOperation)
        {
            resourceOperation = usingOperation.Resources;
            bodyOperation = usingOperation.Body;
            impureSyntaxNode = usingOperation.Syntax;
        }
        else if (operation is IUsingDeclarationOperation usingDeclarationOperation)
        {
            resourceOperation = usingDeclarationOperation.DeclarationGroup;
            impureSyntaxNode = usingDeclarationOperation.Syntax;
        }
        else
        {
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        var isAwaitUsing = IsAwaitUsingOperation(operation);
        var disposalSyntax = impureSyntaxNode ?? operation.Syntax;

        if (resourceOperation != null)
        {
            var resourceResult = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            {
                resourceResult = CheckDeclaratorInitializers(
                    declarationGroup.Declarations.SelectMany(static declaration => declaration.Declarators),
                    context,
                    currentState);
            }
            else if (resourceOperation is IVariableDeclarationOperation variableDeclaration)
            {
                resourceResult = CheckDeclaratorInitializers(variableDeclaration.Declarators, context, currentState);
            }
            else if (resourceOperation is ILocalReferenceOperation localReferenceOperation)
            {
            }
            else
            {
                resourceResult = PurityAnalysisEngine.CheckSingleOperation(resourceOperation, context, currentState);
            }


            if (!resourceResult.IsPure) return resourceResult;
        }


        if (bodyOperation != null)
        {
            var bodyResult = PurityAnalysisEngine.CheckSingleOperation(bodyOperation, context, currentState);
            if (!bodyResult.IsPure) return bodyResult;
        }


        var declaredLocals = FindDeclaredLocals(resourceOperation);

        foreach (var local in declaredLocals)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var localWasReassigned =
                WasLocalReassignedBeforeUsing(local, operation, context.SemanticModel, context.CancellationToken);
            var disposeReceiverType = ResolveDisposeReceiverType(local, operation, context.SemanticModel, currentState,
                isAwaitUsing, context.CancellationToken);
            if (disposeReceiverType == null) continue;


            var disposeMethod =
                DisposalMemberClassifier.FindDisposalMethod(
                    disposeReceiverType,
                    context.SemanticModel.Compilation,
                    isAwaitUsing);

            if (disposeMethod == null) return MissingDisposalEvidence(operation, disposalSyntax, disposeReceiverType);

            if (localWasReassigned &&
                (disposeReceiverType.TypeKind == TypeKind.Interface || IsOverridableDispatchTarget(disposeMethod)))
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    disposalSyntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(UsingStatementPurityRule),
                        operation,
                        disposalSyntax,
                        disposeMethod,
                        "unstable_using_resource"));

            var disposeResult = CheckImplicitDisposeCallee(
                disposeMethod,
                disposalSyntax,
                context,
                isAwaitUsing,
                $"'{local.Name}'");
            if (!disposeResult.IsPure) return disposeResult;
        }

        if (declaredLocals.Count == 0)
        {
            var expressionDisposeReceiverType = ResolveExpressionDisposeReceiverType(resourceOperation);
            if (expressionDisposeReceiverType != null)
            {
                var disposeMethod = DisposalMemberClassifier.FindDisposalMethod(
                    expressionDisposeReceiverType,
                    context.SemanticModel.Compilation,
                    isAwaitUsing);

                if (disposeMethod == null)
                    return MissingDisposalEvidence(operation, disposalSyntax, expressionDisposeReceiverType);

                var disposeResult = CheckImplicitDisposeCallee(
                    disposeMethod,
                    disposalSyntax,
                    context,
                    isAwaitUsing,
                    "expression resource");
                if (!disposeResult.IsPure) return disposeResult;
            }
        }


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult MissingDisposalEvidence(
        IOperation operation,
        SyntaxNode syntax,
        ITypeSymbol receiverType)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "unknown_external_call",
                nameof(UsingStatementPurityRule),
                operation,
                syntax,
                receiverType,
                "missing_disposal_member"));
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckImplicitDisposeCallee(
        IMethodSymbol disposeMethod,
        SyntaxNode syntaxNode,
        PurityAnalysisContext context,
        bool isAwaitUsing,
        string resourceDescription)
    {
        var disposeResult = PurityCalleeResolver.GetCalleePurityAtUse(disposeMethod, syntaxNode, context);
        if (!disposeResult.IsPure) return disposeResult;

        return isAwaitUsing
            ? AwaitPurityRule.CheckAwaitablePatternMembers(disposeMethod.ReturnType, syntaxNode, context)
            : PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDeclaratorInitializers(
        IEnumerable<IVariableDeclaratorOperation> declarators,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        foreach (var declarator in declarators)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var initVal = declarator.Initializer?.Value;
            if (initVal == null) continue;

            var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initVal, context, currentState);
            if (!initializerResult.IsPure) return initializerResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private List<ILocalSymbol> FindDeclaredLocals(IOperation? resourceOperation)
    {
        var locals = new List<ILocalSymbol>();
        if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            foreach (var declaration in declarationGroup.Declarations)
                foreach (var declarator in declaration.Declarators)
                    locals.Add(declarator.Symbol);
        else if (resourceOperation is IVariableDeclaratorOperation declaratorOperation)
            locals.Add(declaratorOperation.Symbol);

        var unwrappedResourceOperation = PurityAnalysisEngine.SkipImplicitConversions(resourceOperation);
        if (unwrappedResourceOperation is ILocalReferenceOperation localReferenceOperation)
            locals.Add(localReferenceOperation.Local);
        return locals;
    }

    private ITypeSymbol? ResolveDisposeReceiverType(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, PurityAnalysisEngine.PurityAnalysisState currentState, bool isAwaitUsing,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasDeclaratorInitializer(local, cancellationToken) &&
            currentState.LocalConcreteTypes.TryGetValue(local, out var concreteType) &&
            DisposalMemberClassifier.FindDisposalMethod(concreteType, semanticModel.Compilation, isAwaitUsing) != null)
            return concreteType;

        var initializerType =
            TryGetStableObjectCreationInitializerType(local, usingOperation, semanticModel, cancellationToken);
        if (initializerType != null &&
            DisposalMemberClassifier.FindDisposalMethod(initializerType, semanticModel.Compilation, isAwaitUsing) != null)
            return initializerType;

        return local.Type;
    }

    private ITypeSymbol? ResolveExpressionDisposeReceiverType(IOperation? resourceOperation)
    {
        var unwrappedResource = UnwrapConversionsForDisposeReceiver(resourceOperation);
        return unwrappedResource is IObjectCreationOperation objectCreationOperation
            ? objectCreationOperation.Type
            : unwrappedResource?.Type ?? resourceOperation?.Type;
    }

    private IOperation? UnwrapConversionsForDisposeReceiver(IOperation? operation)
    {
        var current = PurityAnalysisEngine.SkipImplicitConversions(operation);
        while (current is IConversionOperation conversion)
        {
            var operand = PurityAnalysisEngine.SkipImplicitConversions(conversion.Operand);
            if (operand == null || ReferenceEquals(operand, current)) break;

            current = operand;
        }

        return current;
    }

    private ITypeSymbol? TryGetStableObjectCreationInitializerType(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var declaratorSyntax = GetDeclaratorSyntax(local, cancellationToken);
        var initializerSyntax = declaratorSyntax?.Initializer?.Value;
        if (declaratorSyntax == null || initializerSyntax == null) return null;

        if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax,
                declaratorSyntax, semanticModel, cancellationToken)) return null;

        var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
        var unwrappedInitializer = UnwrapConversionsForDisposeReceiver(initializerOperation);
        return unwrappedInitializer is IObjectCreationOperation objectCreationOperation
            ? objectCreationOperation.Type
            : null;
    }

    private static bool HasDeclaratorInitializer(ILocalSymbol local, CancellationToken cancellationToken)
    {
        return GetDeclaratorSyntax(local, cancellationToken)?.Initializer != null;
    }

    private bool WasLocalReassignedBeforeUsing(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var declaratorSyntax = GetDeclaratorSyntax(local, cancellationToken);
        return declaratorSyntax != null &&
               RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax,
                   declaratorSyntax, semanticModel, cancellationToken);
    }

    private static VariableDeclaratorSyntax? GetDeclaratorSyntax(ILocalSymbol local,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
    }


    private static bool IsAwaitUsingOperation(IOperation operation)
    {
        return operation.Syntax switch
        {
            UsingStatementSyntax usingStatementSyntax => usingStatementSyntax.AwaitKeyword.RawKind != 0,
            LocalDeclarationStatementSyntax localDeclarationStatementSyntax => localDeclarationStatementSyntax
                .AwaitKeyword.RawKind != 0,
            _ => false
        };
    }

    private static bool IsOverridableDispatchTarget(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.IsStatic || methodSymbol.ContainingType?.IsSealed == true) return false;

        return methodSymbol.IsVirtual ||
               methodSymbol.IsAbstract ||
               (methodSymbol.IsOverride && !methodSymbol.IsSealed);
    }
}
