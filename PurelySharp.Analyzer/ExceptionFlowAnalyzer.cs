using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        public static void AnalyzeSymbolForExceptions(
            SyntaxNodeAnalysisContext context,
            bool reportExceptions,
            bool checkedExceptions,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            CompilationPurityService purityService)
        {
            var reportMethodSummaries = Analyzer.Configuration.AnalyzerConfiguration.GetReportExceptions(
                context.Options,
                context.Node.SyntaxTree,
                reportExceptions);
            var reportCheckedExceptionSites = Analyzer.Configuration.AnalyzerConfiguration.GetCheckedExceptions(
                context.Options,
                context.Node.SyntaxTree,
                checkedExceptions);
            if (!reportMethodSummaries && !reportCheckedExceptionSites)
            {
                return;
            }

            if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol methodSymbol))
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var queryResult = ExceptionFlowQuery.AnalyzeMethod(
                context.Node,
                context.SemanticModel,
                context.CancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                purityService.SmtAnalysis);

            if (reportCheckedExceptionSites)
            {
                AnalyzeUncaughtExceptionSites(context, queryResult.SiteEntries);
            }

            if (!reportMethodSummaries || queryResult.ExceptionEvidence.Count == 0)
            {
                return;
            }

            var diagnosticLocation = GetIdentifierLocation(context.Node);
            if (diagnosticLocation == null)
            {
                return;
            }

            var sortedTypes = queryResult.ExceptionEvidence.Types;
            var exceptionList = string.Join(", ", sortedTypes);
            var properties = CreateExceptionProperties(queryResult.ExceptionEvidence);

            context.ReportDiagnostic(Diagnostic.Create(
                PurelySharpDiagnostics.ExceptionSummaryRule,
                diagnosticLocation,
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[] { methodSymbol.Name, exceptionList }));
        }

        private static void AnalyzeUncaughtExceptionSites(
            SyntaxNodeAnalysisContext context,
            ImmutableArray<ExceptionFlowQuery.UncaughtExceptionSiteEntry> siteEntries)
        {
            foreach (var siteGroup in siteEntries.GroupBy(entry => CreateExceptionSiteKey(entry.Site), StringComparer.Ordinal))
            {
                var firstEntry = siteGroup.First();
                var siteEvidence = new ExceptionFlowQuery.ExceptionEvidenceSet();
                string? exceptionSymbol = null;
                foreach (var siteEntry in siteGroup)
                {
                    siteEvidence.Add(siteEntry.Exception);
                    exceptionSymbol ??= siteEntry.ExceptionSymbol;
                }

                if (siteEvidence.Count == 0)
                {
                    continue;
                }

                var siteLocation = GetExceptionSiteLocation(firstEntry.Site);
                if (siteLocation == null)
                {
                    continue;
                }

                var sortedTypes = siteEvidence.Types;
                var exceptionList = string.Join(", ", sortedTypes);
                var operationDisplay = GetExceptionSiteDisplay(firstEntry.Site, firstEntry.Method);
                var properties = CreateExceptionProperties(siteEvidence);
                if (!string.IsNullOrWhiteSpace(exceptionSymbol))
                {
                    properties = properties.Add(PurelySharpDiagnostics.ExceptionSymbolProperty, exceptionSymbol);
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    PurelySharpDiagnostics.UncaughtExceptionSiteRule,
                    siteLocation,
                    additionalLocations: null,
                    properties: properties,
                    messageArgs: new object[] { operationDisplay, exceptionList }));
            }
        }

        private static ImmutableDictionary<string, string?> CreateExceptionProperties(
            ExceptionFlowQuery.ExceptionEvidenceSet exceptionEvidence)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(PurelySharpDiagnostics.ExceptionTypesProperty, string.Join(";", exceptionEvidence.Types))
                .Add(PurelySharpDiagnostics.ExceptionCategoriesProperty, exceptionEvidence.FormatCategories())
                .Add(PurelySharpDiagnostics.ExceptionSourcesProperty, exceptionEvidence.FormatSources());
            var formattedEdges = exceptionEvidence.FormatEdges();
            if (!string.IsNullOrWhiteSpace(formattedEdges))
            {
                properties = properties.Add(PurelySharpDiagnostics.ExceptionEdgesProperty, formattedEdges);
            }

            return properties;
        }

        private static string CreateExceptionSiteKey(SyntaxNode node)
        {
            return node.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                node.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static IEnumerable<TNode> GetRelevantDescendants<TNode>(SyntaxNode methodNode)
            where TNode : SyntaxNode
        {
            return methodNode
                .DescendantNodes(descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !ExecutionVisibility.IsNestedCallableBoundary(candidate))
                .OfType<TNode>();
        }

        private static IEnumerable<TNode> GetRelevantDescendantsAndSelf<TNode>(SyntaxNode methodNode)
            where TNode : SyntaxNode
        {
            return methodNode
                .DescendantNodesAndSelf(descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !ExecutionVisibility.IsNestedCallableBoundary(candidate))
                .OfType<TNode>();
        }

        internal static IEnumerable<MethodCallCandidate> GetCalleeCallSites(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var invocation in GetInvocationNodes(methodNode))
            {
                var knownExactLocals = GetKnownExactLocalTypesBefore(invocation, semanticModel, cancellationToken);
                if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
                {
                    foreach (var invokedMethod in ResolveInvocationTargets(invocationOperation, knownExactLocals))
                    {
                        if (invokedMethod.MethodKind == MethodKind.DelegateInvoke)
                        {
                            continue;
                        }

                        if (seen.Add(CreateMethodCallSiteKey(invocation, invokedMethod)))
                        {
                            yield return new MethodCallCandidate(invocation, invokedMethod);
                        }
                    }
                }
                else if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol invokedMethod &&
                         invokedMethod.MethodKind != MethodKind.DelegateInvoke &&
                         seen.Add(CreateMethodCallSiteKey(invocation, invokedMethod)))
                {
                    yield return new MethodCallCandidate(invocation, invokedMethod);
                }
            }

            foreach (var creation in GetObjectCreationNodes(methodNode))
            {
                if (semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol is IMethodSymbol constructorSymbol &&
                    seen.Add(CreateMethodCallSiteKey(creation, constructorSymbol)))
                {
                    yield return new MethodCallCandidate(creation, constructorSymbol);
                }
            }

            foreach (var initializer in GetConstructorInitializerNodes(methodNode))
            {
                if (TryGetConstructorInitializerTarget(initializer, semanticModel, cancellationToken, out var constructorSymbol) &&
                    seen.Add(CreateMethodCallSiteKey(initializer, constructorSymbol)))
                {
                    yield return new MethodCallCandidate(initializer, constructorSymbol);
                }
            }

            foreach (var propertyAccess in GetPropertyAccessNodes(methodNode, semanticModel, cancellationToken))
            {
                var knownExactLocals = GetKnownExactLocalTypesBefore(propertyAccess, semanticModel, cancellationToken);
                if (semanticModel.GetOperation(propertyAccess, cancellationToken) is IPropertyReferenceOperation propertyReferenceOperation)
                {
                    foreach (var getterMethod in ResolvePropertyAccessorTargets(
                                 propertyReferenceOperation,
                                 preferSetter: false,
                                 knownExactLocals))
                    {
                        if (seen.Add(CreateMethodCallSiteKey(propertyAccess, getterMethod)))
                        {
                            yield return new MethodCallCandidate(propertyAccess, getterMethod);
                        }
                    }
                }
                else if (semanticModel.GetSymbolInfo(propertyAccess, cancellationToken).Symbol is IPropertySymbol propertySymbol &&
                         propertySymbol.GetMethod != null &&
                         seen.Add(CreateMethodCallSiteKey(propertyAccess, propertySymbol.GetMethod)))
                {
                    yield return new MethodCallCandidate(propertyAccess, propertySymbol.GetMethod);
                }
            }

            foreach (var propertyWrite in GetPropertyWriteNodes(methodNode, semanticModel, cancellationToken))
            {
                var knownExactLocals = GetKnownExactLocalTypesBefore(propertyWrite, semanticModel, cancellationToken);
                if (semanticModel.GetOperation(propertyWrite, cancellationToken) is IPropertyReferenceOperation propertyReferenceOperation)
                {
                    foreach (var setterMethod in ResolvePropertyAccessorTargets(
                                 propertyReferenceOperation,
                                 preferSetter: true,
                                 knownExactLocals))
                    {
                        if (seen.Add(CreateMethodCallSiteKey(propertyWrite, setterMethod)))
                        {
                            yield return new MethodCallCandidate(propertyWrite, setterMethod);
                        }
                    }
                }
                else if (TryGetPropertySetterMethod(propertyWrite, semanticModel, cancellationToken, out var setterMethod) &&
                         setterMethod != null &&
                         seen.Add(CreateMethodCallSiteKey(propertyWrite, setterMethod)))
                {
                    yield return new MethodCallCandidate(propertyWrite, setterMethod);
                }
            }

            foreach (var usingDisposeNode in GetUsingDisposeNodes(methodNode, semanticModel, cancellationToken))
            {
                if (seen.Add(CreateMethodCallSiteKey(usingDisposeNode.CallSite, usingDisposeNode.Method)))
                {
                    yield return usingDisposeNode;
                }
            }

            foreach (var forEachRuntimeNode in GetForEachRuntimeMethodNodes(methodNode, semanticModel, cancellationToken))
            {
                if (seen.Add(CreateMethodCallSiteKey(forEachRuntimeNode.CallSite, forEachRuntimeNode.Method)))
                {
                    yield return forEachRuntimeNode;
                }
            }

            foreach (var operatorNode in GetOperatorAndConversionNodes(methodNode, semanticModel, cancellationToken))
            {
                if (seen.Add(CreateMethodCallSiteKey(operatorNode.CallSite, operatorNode.Method)))
                {
                    yield return operatorNode;
                }
            }

            foreach (var delegateInvocationNode in GetLocalDelegateTargetInvocationNodes(methodNode, semanticModel, cancellationToken))
            {
                if (seen.Add(CreateMethodCallSiteKey(delegateInvocationNode.CallSite, delegateInvocationNode.Method)))
                {
                    yield return delegateInvocationNode;
                }
            }

            foreach (var handlerConstructorNode in GetInterpolatedStringHandlerConstructorNodes(methodNode, semanticModel, cancellationToken))
            {
                if (seen.Add(CreateMethodCallSiteKey(handlerConstructorNode.CallSite, handlerConstructorNode.Method)))
                {
                    yield return handlerConstructorNode;
                }
            }
        }

        private static string CreateMethodCallSiteKey(SyntaxNode callSite, IMethodSymbol method)
        {
            return callSite.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                callSite.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "|" +
                method.OriginalDefinition.ToDisplayString();
        }

        private static IEnumerable<IMethodSymbol> ResolveInvocationTargets(
            IInvocationOperation invocationOperation,
            IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals = null)
        {
            var invokedMethod = invocationOperation.TargetMethod;
            if (invokedMethod == null)
            {
                yield break;
            }

            if (IsBaseReference(invocationOperation.Instance))
            {
                yield return invokedMethod.OriginalDefinition;
                yield break;
            }

            if (TryResolveExactConcreteType(invocationOperation.Instance, knownExactLocals, out var exactReceiverType))
            {
                var exactTarget = PurityAnalysisEngine.ResolveMethodTargetForConcreteReceiver(invokedMethod, exactReceiverType);
                if (exactTarget != null)
                {
                    yield return exactTarget.OriginalDefinition;
                    yield break;
                }
            }

            yield return invokedMethod.OriginalDefinition;
        }

        private static IEnumerable<IMethodSymbol> ResolvePropertyAccessorTargets(
            IPropertyReferenceOperation propertyReferenceOperation,
            bool preferSetter,
            IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals = null)
        {
            var accessor = preferSetter
                ? propertyReferenceOperation.Property?.SetMethod
                : propertyReferenceOperation.Property?.GetMethod;
            if (accessor == null)
            {
                yield break;
            }

            if (IsBaseReference(propertyReferenceOperation.Instance))
            {
                yield return accessor.OriginalDefinition;
                yield break;
            }

            if (TryResolveExactConcreteType(propertyReferenceOperation.Instance, knownExactLocals, out var exactReceiverType))
            {
                var exactAccessor = PurityAnalysisEngine.ResolvePropertyAccessorTargetForConcreteReceiver(
                    propertyReferenceOperation.Property,
                    exactReceiverType,
                    preferSetter);
                if (exactAccessor != null)
                {
                    yield return exactAccessor.OriginalDefinition;
                    yield break;
                }
            }

            yield return accessor.OriginalDefinition;
        }

        private static IEnumerable<InvocationExpressionSyntax> GetInvocationNodes(SyntaxNode methodNode)
        {
            return GetRelevantDescendants<InvocationExpressionSyntax>(methodNode);
        }

        private static IEnumerable<SyntaxNode> GetObjectCreationNodes(SyntaxNode methodNode)
        {
            return GetRelevantDescendants<SyntaxNode>(methodNode)
                .Where(node => node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax);
        }

        private static IEnumerable<ConstructorInitializerSyntax> GetConstructorInitializerNodes(SyntaxNode methodNode)
        {
            if (methodNode is ConstructorDeclarationSyntax constructorDeclaration &&
                constructorDeclaration.Initializer != null)
            {
                yield return constructorDeclaration.Initializer;
            }
        }

        private static bool TryGetConstructorInitializerTarget(
            ConstructorInitializerSyntax initializer,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out IMethodSymbol constructorSymbol)
        {
            if (semanticModel.GetOperation(initializer, cancellationToken) is IInvocationOperation invocationOperation &&
                invocationOperation.TargetMethod != null)
            {
                constructorSymbol = invocationOperation.TargetMethod;
                return true;
            }

            if (semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol is IMethodSymbol symbol)
            {
                constructorSymbol = symbol;
                return true;
            }

            constructorSymbol = null!;
            return false;
        }

        private static IEnumerable<MethodCallCandidate> GetOperatorAndConversionNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rootOperation = GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken);
            if (rootOperation == null)
            {
                yield break;
            }

            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                if (TryGetOperatorOrConversionMethod(operation, out var method))
                {
                    var key = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                        "@" +
                        operation.Syntax.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ":" +
                        operation.Syntax.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (seen.Add(key))
                    {
                        yield return new MethodCallCandidate(operation.Syntax, method);
                    }
                }
            }
        }

        private static bool TryGetOperatorOrConversionMethod(
            IOperation operation,
            out IMethodSymbol? method)
        {
            method = null;
            switch (operation)
            {
                case IBinaryOperation binaryOperation when binaryOperation.OperatorMethod != null:
                    method = binaryOperation.OperatorMethod;
                    return true;
                case IUnaryOperation unaryOperation when unaryOperation.OperatorMethod != null:
                    method = unaryOperation.OperatorMethod;
                    return true;
                case IConversionOperation conversionOperation
                    when conversionOperation.Conversion.IsUserDefined && conversionOperation.Conversion.MethodSymbol != null:
                    method = conversionOperation.Conversion.MethodSymbol;
                    return true;
                default:
                    return false;
            }
        }

        private static IOperation? GetMethodBodyRootOperation(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return methodNode switch
            {
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null =>
                    semanticModel.GetOperation(methodDeclaration.Body, cancellationToken),
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.Body != null =>
                    semanticModel.GetOperation(constructorDeclaration.Body, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(constructorDeclaration.ExpressionBody.Expression, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.Body != null =>
                    semanticModel.GetOperation(operatorDeclaration.Body, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(operatorDeclaration.ExpressionBody.Expression, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.Body != null =>
                    semanticModel.GetOperation(accessorDeclaration.Body, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(accessorDeclaration.ExpressionBody.Expression, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.Body != null =>
                    semanticModel.GetOperation(localFunction.Body, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null =>
                    semanticModel.GetOperation(localFunction.ExpressionBody.Expression, cancellationToken),
                _ => semanticModel.GetOperation(methodNode, cancellationToken)
            };
        }

        private static Location? GetIdentifierLocation(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax method => method.Identifier.GetLocation(),
                ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
                OperatorDeclarationSyntax op => op.OperatorToken.GetLocation(),
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
                AccessorDeclarationSyntax accessor =>
                    accessor.Parent?.Parent switch
                    {
                        PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
                        IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
                        _ => accessor.Keyword.GetLocation()
                    } ?? accessor.Keyword.GetLocation(),
                _ => node.GetLocation()
            };
        }

        private static Location? GetExceptionSiteLocation(SyntaxNode node)
        {
            return node switch
            {
                InvocationExpressionSyntax invocation => invocation.Expression.GetLocation(),
                ObjectCreationExpressionSyntax creation => creation.GetLocation(),
                ImplicitObjectCreationExpressionSyntax creation => creation.GetLocation(),
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
                IdentifierNameSyntax identifier => identifier.Identifier.GetLocation(),
                ElementAccessExpressionSyntax elementAccess => elementAccess.GetLocation(),
                _ => node.GetLocation()
            };
        }

        private static string GetExceptionSiteDisplay(SyntaxNode node, IMethodSymbol method)
        {
            var display = node.ToString();
            return string.IsNullOrWhiteSpace(display)
                ? method.OriginalDefinition.ToDisplayString()
                : display;
        }

        internal sealed class MethodCallCandidate
        {
            public MethodCallCandidate(SyntaxNode callSite, IMethodSymbol method)
            {
                CallSite = callSite;
                Method = method;
            }

            public SyntaxNode CallSite { get; }

            public IMethodSymbol Method { get; }
        }

        private enum PathFactKind
        {
            Zero,
            Null
        }
    }
}
