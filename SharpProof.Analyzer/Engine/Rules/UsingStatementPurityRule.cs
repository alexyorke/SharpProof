using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class UsingStatementPurityRule : IPurityRule
    {

        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Using, OperationKind.UsingDeclaration);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            SyntaxNode? impureSyntaxNode = null;
            IOperation? resourceOperation = null;
            IOperation? bodyOperation = null;

            if (operation is IUsingOperation usingOperation)
            {
                resourceOperation = usingOperation.Resources;
                bodyOperation = usingOperation.Body;
                impureSyntaxNode = usingOperation.Syntax;
                PurityAnalysisEngine.LogDebug($"UsingStatementPurityRule: Analyzing Using Statement");
            }
            else if (operation is IUsingDeclarationOperation usingDeclarationOperation)
            {
                resourceOperation = usingDeclarationOperation.DeclarationGroup;
                impureSyntaxNode = usingDeclarationOperation.Syntax;
                PurityAnalysisEngine.LogDebug($"UsingStatementPurityRule: Analyzing Using Declaration");
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"UsingStatementPurityRule: Unexpected operation kind {operation.Kind}. Assuming pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            bool isAwaitUsing = IsAwaitUsingOperation(operation);

            if (resourceOperation != null)
            {
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking Resource operation {resourceOperation.Kind}");
                PurityAnalysisEngine.PurityAnalysisResult resourceResult = PurityAnalysisEngine.PurityAnalysisResult.Pure;

                if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource is VariableDeclarationGroup. Checking initializers.");
                    foreach (var declaration in declarationGroup.Declarations)
                    {
                        foreach (var declarator in declaration.Declarators)
                        {
                            var initVal = declarator.Initializer?.Value;
                            if (initVal != null)
                            {
                                PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Checking initializer for {declarator.Symbol.Name}: {initVal.Syntax}");
                                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initVal, context, currentState);
                                if (!initializerResult.IsPure)
                                {
                                    PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Initializer for {declarator.Symbol.Name} is IMPURE.");
                                    resourceResult = initializerResult;
                                    break;
                                }
                                PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Initializer for {declarator.Symbol.Name} is Pure.");
                            }
                            else
                            {

                                PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: No initializer for {declarator.Symbol.Name}. Assuming pure acquisition.");
                            }
                        }
                        if (!resourceResult.IsPure) break;
                    }
                }
                else if (resourceOperation is IVariableDeclarationOperation variableDeclaration)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource is VariableDeclaration. Checking initializers.");
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        var initVal = declarator.Initializer?.Value;
                        if (initVal != null)
                        {
                            PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Checking initializer for {declarator.Symbol.Name}: {initVal.Syntax}");
                            var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initVal, context, currentState);
                            if (!initializerResult.IsPure)
                            {
                                PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Initializer for {declarator.Symbol.Name} is IMPURE.");
                                resourceResult = initializerResult;
                                break;
                            }
                            PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: Initializer for {declarator.Symbol.Name} is Pure.");
                        }
                        else
                        {
                            PurityAnalysisEngine.LogDebug($"  UsingStatementPurityRule: No initializer for {declarator.Symbol.Name}. Assuming pure acquisition.");
                        }
                    }
                }
                else if (resourceOperation is ILocalReferenceOperation localReferenceOperation)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource is a reference to existing local '{localReferenceOperation.Local.Name}'. Resource acquisition is pure; implicit Dispose will be checked.");
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource is an expression {resourceOperation.Kind}. Checking expression directly.");
                    resourceResult = PurityAnalysisEngine.CheckSingleOperation(resourceOperation, context, currentState);
                }


                if (!resourceResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource operation is IMPURE.");
                    return resourceResult;
                }
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Resource operation is Pure.");
            }


            if (bodyOperation != null)
            {
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking Body operation {bodyOperation.Kind}");
                var bodyResult = PurityAnalysisEngine.CheckSingleOperation(bodyOperation, context, currentState);
                if (!bodyResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Body operation is IMPURE.");
                    return bodyResult;
                }
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Body operation is Pure.");
            }



            List<ILocalSymbol> declaredLocals = FindDeclaredLocals(resourceOperation);

            foreach (var local in declaredLocals)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var localWasReassigned = WasLocalReassignedBeforeUsing(local, operation, context.SemanticModel, context.CancellationToken);
                var disposeReceiverType = ResolveDisposeReceiverType(local, operation, context.SemanticModel, currentState, isAwaitUsing, context.CancellationToken);
                if (disposeReceiverType == null)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Local '{local.Name}' has no resolvable Dispose receiver type. Skipping Dispose check.");
                    continue;
                }

                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking implicit Dispose() for local '{local.Name}' of type {disposeReceiverType.Name}");


                IMethodSymbol? disposeMethod = FindDisposalMethod(disposeReceiverType, context.SemanticModel.Compilation, isAwaitUsing);

                if (disposeMethod == null)
                {

                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Could not find Dispose or DisposeAsync method for type {disposeReceiverType.Name}. Assuming impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(impureSyntaxNode ?? operation.Syntax);
                }

                if (localWasReassigned &&
                    (disposeReceiverType.TypeKind == TypeKind.Interface || IsOverridableDispatchTarget(disposeMethod)))
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Local '{local.Name}' was reassigned before using and has unstable Dispose dispatch. Treating implicit Dispose dispatch as impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        impureSyntaxNode ?? operation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(UsingStatementPurityRule),
                            operation,
                            syntaxNode: impureSyntaxNode ?? operation.Syntax,
                            symbol: disposeMethod,
                            catalogSource: "unstable_using_resource"));
                }



                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking callee purity of {disposeMethod.ToDisplayString()}");
                var disposeResult = PurityAnalysisEngine.GetCalleePurity(disposeMethod, context);

                if (!disposeResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Implicit Dispose() call on '{local.Name}' ({disposeMethod.Name}) is IMPURE.");

                    return disposeResult.WithCallee(disposeMethod, impureSyntaxNode ?? operation.Syntax);
                }
                else
                {

                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Implicit Dispose() call on '{local.Name}' ({disposeMethod.Name}) was analyzed as Pure.");

                }

                if (isAwaitUsing)
                {
                    var awaitPatternResult = AwaitPurityRule.CheckAwaitablePatternMembers(
                        disposeMethod.ReturnType,
                        impureSyntaxNode ?? operation.Syntax,
                        context);
                    if (!awaitPatternResult.IsPure)
                    {
                        return awaitPatternResult;
                    }
                }
            }

            if (declaredLocals.Count == 0)
            {
                var expressionDisposeReceiverType = ResolveExpressionDisposeReceiverType(resourceOperation);
                if (expressionDisposeReceiverType != null)
                {
                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking implicit Dispose() for expression resource of type {expressionDisposeReceiverType.Name}");

                    IMethodSymbol? disposeMethod = FindDisposalMethod(expressionDisposeReceiverType, context.SemanticModel.Compilation, isAwaitUsing);

                    if (disposeMethod == null)
                    {
                        PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Could not find Dispose or DisposeAsync method for expression resource type {expressionDisposeReceiverType.Name}. Skipping Dispose check.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Checking callee purity of {disposeMethod.ToDisplayString()}");
                    var disposeResult = PurityAnalysisEngine.GetCalleePurity(disposeMethod, context);

                    if (!disposeResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Implicit Dispose() call on expression resource ({disposeMethod.Name}) is IMPURE.");
                        return disposeResult.WithCallee(disposeMethod, impureSyntaxNode ?? operation.Syntax);
                    }

                    PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Implicit Dispose() call on expression resource ({disposeMethod.Name}) was analyzed as Pure.");

                    if (isAwaitUsing)
                    {
                        var awaitPatternResult = AwaitPurityRule.CheckAwaitablePatternMembers(
                            disposeMethod.ReturnType,
                            impureSyntaxNode ?? operation.Syntax,
                            context);
                        if (!awaitPatternResult.IsPure)
                        {
                            return awaitPatternResult;
                        }
                    }
                }
            }


            PurityAnalysisEngine.LogDebug($"UsingStatementPurityRule: Resource, Body (if applicable), and Dispose() calls are all pure. Result: Pure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private List<ILocalSymbol> FindDeclaredLocals(IOperation? resourceOperation)
        {
            var locals = new List<ILocalSymbol>();
            if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            {
                foreach (var declaration in declarationGroup.Declarations)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        locals.Add(declarator.Symbol);
                    }
                }
            }
            else if (resourceOperation is IVariableDeclaratorOperation declaratorOperation)
            {
                locals.Add(declaratorOperation.Symbol);
            }

            var unwrappedResourceOperation = PurityAnalysisEngine.SkipImplicitConversions(resourceOperation);
            if (unwrappedResourceOperation is ILocalReferenceOperation localReferenceOperation)
            {
                locals.Add(localReferenceOperation.Local);
            }
            return locals;
        }

        private ITypeSymbol? ResolveDisposeReceiverType(ILocalSymbol local, IOperation usingOperation, SemanticModel semanticModel, PurityAnalysisEngine.PurityAnalysisState currentState, bool isAwaitUsing, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasDeclaratorInitializer(local, cancellationToken) &&
                currentState.LocalConcreteTypes.TryGetValue(local, out var concreteType) &&
                FindDisposalMethod(concreteType, semanticModel.Compilation, isAwaitUsing) != null)
            {
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Local '{local.Name}' Dispose receiver resolved from tracked concrete type {concreteType.Name}.");
                return concreteType;
            }

            var initializerType = TryGetStableObjectCreationInitializerType(local, usingOperation, semanticModel, cancellationToken);
            if (initializerType != null && FindDisposalMethod(initializerType, semanticModel.Compilation, isAwaitUsing) != null)
            {
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Local '{local.Name}' Dispose receiver resolved from initializer type {initializerType.Name}.");
                return initializerType;
            }

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
                if (operand == null || ReferenceEquals(operand, current))
                {
                    break;
                }

                current = operand;
            }

            return current;
        }

        private ITypeSymbol? TryGetStableObjectCreationInitializerType(ILocalSymbol local, IOperation usingOperation, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaratorSyntax = local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null)
            {
                return null;
            }

            if (HasAssignmentToLocalBetweenDeclarationAndUsing(local, usingOperation, declaratorSyntax, semanticModel, cancellationToken))
            {
                PurityAnalysisEngine.LogDebug($" UsingStatementPurityRule: Local '{local.Name}' is reassigned before using; using declared type for Dispose resolution.");
                return null;
            }

            var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
            var unwrappedInitializer = UnwrapConversionsForDisposeReceiver(initializerOperation);
            return unwrappedInitializer is IObjectCreationOperation objectCreationOperation
                ? objectCreationOperation.Type
                : null;
        }

        private static bool HasDeclaratorInitializer(ILocalSymbol local, CancellationToken cancellationToken)
        {
            return local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .Any(declarator => declarator.Initializer != null);
        }

        private bool WasLocalReassignedBeforeUsing(ILocalSymbol local, IOperation usingOperation, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaratorSyntax = local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            return declaratorSyntax != null &&
                HasAssignmentToLocalBetweenDeclarationAndUsing(local, usingOperation, declaratorSyntax, semanticModel, cancellationToken);
        }

        private bool HasAssignmentToLocalBetweenDeclarationAndUsing(
            ILocalSymbol local,
            IOperation usingOperation,
            VariableDeclaratorSyntax declaratorSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootOperation = usingOperation;
            while (rootOperation.Parent != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rootOperation = rootOperation.Parent;
            }

            var declarationStart = declaratorSyntax.SpanStart;
            var usingStart = usingOperation.Syntax.SpanStart;
            foreach (var operation in rootOperation.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.Syntax.SpanStart <= declarationStart || operation.Syntax.SpanStart >= usingStart)
                {
                    continue;
                }

                switch (operation)
                {
                    case ISimpleAssignmentOperation assignment when IsLocalTarget(assignment.Target, local, semanticModel, cancellationToken):
                    case ICompoundAssignmentOperation compoundAssignment when IsLocalTarget(compoundAssignment.Target, local, semanticModel, cancellationToken):
                    case IIncrementOrDecrementOperation incrementOrDecrement when IsLocalTarget(incrementOrDecrement.Target, local, semanticModel, cancellationToken):
                    case IDeconstructionAssignmentOperation deconstructionAssignment when ContainsLocalAssignmentTarget(deconstructionAssignment.Target, local, semanticModel, cancellationToken):
                    case IInvocationOperation invocationOperation when HasByRefLocalArgument(invocationOperation, local, semanticModel, cancellationToken):
                        return true;
                }
            }

            return false;
        }

        private bool ContainsLocalAssignmentTarget(IOperation? targetOperation, ILocalSymbol local, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
            if (IsLocalTarget(unwrappedTarget, local, semanticModel, cancellationToken))
            {
                return true;
            }

            if (unwrappedTarget is ITupleOperation tupleOperation)
            {
                foreach (var element in tupleOperation.Elements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ContainsLocalAssignmentTarget(element, local, semanticModel, cancellationToken))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasByRefLocalArgument(IInvocationOperation invocationOperation, ILocalSymbol local, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                    IsLocalTarget(argument.Value, local, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLocalTarget(IOperation? targetOperation, ILocalSymbol local, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
            if (unwrappedTarget is not ILocalReferenceOperation localReferenceOperation)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(localReferenceOperation.Local, local) ||
                IsRefLocalAliasForLocal(
                    localReferenceOperation.Local,
                    local,
                    semanticModel,
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                    cancellationToken);
        }

        private bool IsRefLocalAliasForLocal(
            ILocalSymbol possibleAlias,
            ILocalSymbol targetLocal,
            SemanticModel semanticModel,
            HashSet<ISymbol> visited,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (possibleAlias.RefKind == RefKind.None || !visited.Add(possibleAlias))
            {
                return false;
            }

            foreach (var syntaxReference in possibleAlias.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (syntaxReference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declaratorSyntax ||
                    declaratorSyntax.Initializer?.Value == null)
                {
                    continue;
                }

                var initializerSyntax = declaratorSyntax.Initializer.Value;
                if (initializerSyntax is RefExpressionSyntax refExpressionSyntax)
                {
                    initializerSyntax = refExpressionSyntax.Expression;
                }

                var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
                var unwrappedInitializer = PurityAnalysisEngine.SkipImplicitConversions(initializerOperation);
                if (unwrappedInitializer is not ILocalReferenceOperation initializerLocalReference)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(initializerLocalReference.Local, targetLocal) ||
                    IsRefLocalAliasForLocal(initializerLocalReference.Local, targetLocal, semanticModel, visited, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }


        private bool ImplementsIDisposable(ITypeSymbol typeSymbol, Compilation compilation)
        {
            INamedTypeSymbol? disposableInterface = compilation.GetTypeByMetadataName("System.IDisposable");
            if (disposableInterface == null)
            {
                PurityAnalysisEngine.LogDebug($" Error: Could not find System.IDisposable in compilation.");
                return false;
            }


            return typeSymbol.Equals(disposableInterface, SymbolEqualityComparer.Default) ||
                  typeSymbol.AllInterfaces.Contains(disposableInterface, SymbolEqualityComparer.Default);

        }

        private static bool IsAwaitUsingOperation(IOperation operation)
        {
            return operation.Syntax switch
            {
                UsingStatementSyntax usingStatementSyntax => usingStatementSyntax.AwaitKeyword.RawKind != 0,
                LocalDeclarationStatementSyntax localDeclarationStatementSyntax => localDeclarationStatementSyntax.AwaitKeyword.RawKind != 0,
                _ => false
            };
        }

        private IMethodSymbol? FindDisposalMethod(ITypeSymbol typeSymbol, Compilation compilation, bool isAwaitUsing)
        {
            return isAwaitUsing
                ? FindDisposeAsyncMethod(typeSymbol, compilation) ?? FindDisposeMethod(typeSymbol, compilation)
                : FindDisposeMethod(typeSymbol, compilation) ?? FindDisposeAsyncMethod(typeSymbol, compilation);
        }

        private IMethodSymbol? FindDisposeMethod(ITypeSymbol typeSymbol, Compilation compilation)
        {
            INamedTypeSymbol? disposableInterface = compilation.GetTypeByMetadataName("System.IDisposable");
            if (disposableInterface != null)
            {
                IMethodSymbol? interfaceDisposeMethod = disposableInterface.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault();
                if (interfaceDisposeMethod != null)
                {
                    if (typeSymbol.Equals(disposableInterface, SymbolEqualityComparer.Default) ||
                        typeSymbol.TypeKind == TypeKind.Interface && typeSymbol.AllInterfaces.Contains(disposableInterface, SymbolEqualityComparer.Default))
                    {
                        return interfaceDisposeMethod;
                    }

                    var implementation = typeSymbol.FindImplementationForInterfaceMember(interfaceDisposeMethod) as IMethodSymbol;
                    if (implementation != null)
                    {
                        return implementation;
                    }
                }
            }

            return typeSymbol.GetMembers("Dispose")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    !method.IsStatic &&
                    method.Parameters.Length == 0 &&
                    method.ReturnsVoid);
        }

        private IMethodSymbol? FindDisposeAsyncMethod(ITypeSymbol typeSymbol, Compilation compilation)
        {
            INamedTypeSymbol? asyncDisposableInterface = compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (asyncDisposableInterface != null)
            {
                IMethodSymbol? interfaceDisposeAsyncMethod = asyncDisposableInterface.GetMembers("DisposeAsync").OfType<IMethodSymbol>().FirstOrDefault();
                if (interfaceDisposeAsyncMethod != null)
                {
                    if (typeSymbol.Equals(asyncDisposableInterface, SymbolEqualityComparer.Default) ||
                        typeSymbol.TypeKind == TypeKind.Interface && typeSymbol.AllInterfaces.Contains(asyncDisposableInterface, SymbolEqualityComparer.Default))
                    {
                        return interfaceDisposeAsyncMethod;
                    }

                    var implementation = typeSymbol.FindImplementationForInterfaceMember(interfaceDisposeAsyncMethod) as IMethodSymbol;
                    if (implementation != null)
                    {
                        return implementation;
                    }
                }
            }

            return typeSymbol.GetMembers("DisposeAsync")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    !method.IsStatic &&
                    method.Parameters.Length == 0);
        }

        private static bool IsOverridableDispatchTarget(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsStatic || methodSymbol.ContainingType?.IsSealed == true)
            {
                return false;
            }

            return methodSymbol.IsVirtual ||
                methodSymbol.IsAbstract ||
                methodSymbol.IsOverride && methodSymbol.IsSealed == false;
        }
    }
}
