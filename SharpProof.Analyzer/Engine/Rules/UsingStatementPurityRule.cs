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

            bool isAwaitUsing = IsAwaitUsingOperation(operation);
            var disposalSyntax = impureSyntaxNode ?? operation.Syntax;

            if (resourceOperation != null)
            {
                PurityAnalysisEngine.PurityAnalysisResult resourceResult = PurityAnalysisEngine.PurityAnalysisResult.Pure;

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


                if (!resourceResult.IsPure)
                {
                    return resourceResult;
                }
            }


            if (bodyOperation != null)
            {
                var bodyResult = PurityAnalysisEngine.CheckSingleOperation(bodyOperation, context, currentState);
                if (!bodyResult.IsPure)
                {
                    return bodyResult;
                }
            }



            List<ILocalSymbol> declaredLocals = FindDeclaredLocals(resourceOperation);

            foreach (var local in declaredLocals)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var localWasReassigned = WasLocalReassignedBeforeUsing(local, operation, context.SemanticModel, context.CancellationToken);
                var disposeReceiverType = ResolveDisposeReceiverType(local, operation, context.SemanticModel, currentState, isAwaitUsing, context.CancellationToken);
                if (disposeReceiverType == null)
                {
                    continue;
                }



                IMethodSymbol? disposeMethod = FindDisposalMethod(disposeReceiverType, context.SemanticModel.Compilation, isAwaitUsing);

                if (disposeMethod == null)
                {

                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(disposalSyntax);
                }

                if (localWasReassigned &&
                    (disposeReceiverType.TypeKind == TypeKind.Interface || IsOverridableDispatchTarget(disposeMethod)))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        disposalSyntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(UsingStatementPurityRule),
                            operation,
                            syntaxNode: disposalSyntax,
                            symbol: disposeMethod,
                            catalogSource: "unstable_using_resource"));
                }

                var disposeResult = CheckImplicitDisposeCallee(
                    disposeMethod,
                    disposalSyntax,
                    context,
                    isAwaitUsing,
                    $"'{local.Name}'");
                if (!disposeResult.IsPure)
                {
                    return disposeResult;
                }
            }

            if (declaredLocals.Count == 0)
            {
                var expressionDisposeReceiverType = ResolveExpressionDisposeReceiverType(resourceOperation);
                if (expressionDisposeReceiverType != null)
                {

                    IMethodSymbol? disposeMethod = FindDisposalMethod(expressionDisposeReceiverType, context.SemanticModel.Compilation, isAwaitUsing);

                    if (disposeMethod == null)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    var disposeResult = CheckImplicitDisposeCallee(
                        disposeMethod,
                        disposalSyntax,
                        context,
                        isAwaitUsing,
                        "expression resource");
                    if (!disposeResult.IsPure)
                    {
                        return disposeResult;
                    }
                }
            }


            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckImplicitDisposeCallee(
            IMethodSymbol disposeMethod,
            SyntaxNode syntaxNode,
            PurityAnalysisContext context,
            bool isAwaitUsing,
            string resourceDescription)
        {
            var disposeResult = PurityAnalysisEngine.GetCalleePurity(disposeMethod, context);
            if (!disposeResult.IsPure)
            {
                return disposeResult.WithCallee(disposeMethod, syntaxNode);
            }

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
                if (initVal == null)
                {
                    continue;
                }

                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initVal, context, currentState);
                if (!initializerResult.IsPure)
                {
                    return initializerResult;
                }

            }

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
                return concreteType;
            }

            var initializerType = TryGetStableObjectCreationInitializerType(local, usingOperation, semanticModel, cancellationToken);
            if (initializerType != null && FindDisposalMethod(initializerType, semanticModel.Compilation, isAwaitUsing) != null)
            {
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

            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax, declaratorSyntax, semanticModel, cancellationToken))
            {
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
                RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax, declaratorSyntax, semanticModel, cancellationToken);
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
