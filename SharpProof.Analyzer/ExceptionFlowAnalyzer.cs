using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using ExceptionSources = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionSources;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static readonly AsyncLocal<SharpProofAttributeIdentityPolicy?> CurrentAttributePolicy = new();

    internal static SharpProofAttributeIdentityPolicy ActiveAttributePolicy =>
        CurrentAttributePolicy.Value ?? RequiresContractHelpers.OfficialAttributePolicy;

    internal static IDisposable UseAttributePolicy(SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var previous = CurrentAttributePolicy.Value;
        CurrentAttributePolicy.Value = attributePolicy ?? RequiresContractHelpers.OfficialAttributePolicy;
        return new AttributePolicyScope(previous);
    }

    public static void AnalyzeSymbolForExceptions(
        MethodBodyAnalysisContext context,
        AnalyzerConfiguration config,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        CompilationPurityService purityService,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var runtimeHazardMode = AnalyzerConfiguration.GetRuntimeHazardMode(
            context.Options,
            context.Node.SyntaxTree,
            config.RuntimeHazardMode);
        var reportMethodSummaries = AnalyzerConfiguration.GetReportExceptions(
                                        context.Options,
                                        context.Node.SyntaxTree,
                                        config.ReportExceptions) ||
                                    AnalyzerConfiguration.RuntimeHazardReportsMethodSummaries(runtimeHazardMode);
        var reportCheckedExceptionSites = AnalyzerConfiguration.GetCheckedExceptions(
                                              context.Options,
                                              context.Node.SyntaxTree,
                                              config.CheckedExceptions) ||
                                          AnalyzerConfiguration.RuntimeHazardReportsSites(runtimeHazardMode);
        var reportUnknownRuntimeHazards =
            AnalyzerConfiguration.RuntimeHazardReportsUnknownCandidates(runtimeHazardMode);

        var methodSymbol = context.MethodSymbol;

        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return;

        var exceptionContracts = CollectExceptionContracts(methodSymbol, context.SemanticModel, attributePolicy,
            context.CancellationToken);
        var hasValidExceptionContracts = exceptionContracts.Any(static contract => contract.InvalidArguments.IsDefaultOrEmpty);
        if (!reportMethodSummaries &&
            !reportCheckedExceptionSites &&
            !reportUnknownRuntimeHazards &&
            exceptionContracts.Length == 0)
            return;

        ExceptionFlowQuery.MethodExceptionQueryResult? queryResult = null;
        var unknownRuntimeHazards = ImmutableArray<SymbolicRuntimeHazard>.Empty;
        if (reportMethodSummaries ||
            reportCheckedExceptionSites ||
            reportUnknownRuntimeHazards ||
            hasValidExceptionContracts)
            using (UseAttributePolicy(attributePolicy))
            {
                if (reportMethodSummaries || reportCheckedExceptionSites || hasValidExceptionContracts)
                    queryResult = context.State.GetOrCreateSymbolicQueryResult(
                        "exception-flow",
                        () => ExceptionFlowQuery.AnalyzeMethod(
                            context.Node,
                            context.SemanticModel,
                            context.CancellationToken,
                            methodSymbol,
                            exceptionSummaryCatalog,
                            purityService.SmtAnalysis,
                            attributePolicy));

                if (reportUnknownRuntimeHazards)
                    unknownRuntimeHazards = context.State.GetOrCreateSymbolicQueryResult(
                        "unknown-runtime-hazards",
                        () => new CachedUnknownRuntimeHazards(
                            ExceptionFlowQuery.CollectUnknownRuntimeHazardCandidates(
                                context.Node,
                                context.SemanticModel,
                                context.CancellationToken,
                                purityService.SmtAnalysis))).Hazards;
            }

        AnalyzeExceptionContracts(context, methodSymbol, exceptionContracts, queryResult, baseline);

        if (reportUnknownRuntimeHazards)
            AnalyzeUnknownRuntimeHazardCandidates(
                context,
                methodSymbol,
                unknownRuntimeHazards,
                baseline);

        if (queryResult == null) return;

        if (reportCheckedExceptionSites)
            AnalyzeUncaughtExceptionSites(context, methodSymbol, queryResult.SiteEntries, baseline);

        if (!reportMethodSummaries || queryResult.ExceptionEvidence.Count == 0) return;

        var diagnosticLocation = GetIdentifierLocation(context.Node);
        if (diagnosticLocation == null) return;

        var sortedTypes = queryResult.ExceptionEvidence.Types;
        var exceptionList = string.Join(", ", sortedTypes);
        var properties = BaselineDiagnosticProperties.Add(
            CreateExceptionProperties(queryResult.ExceptionEvidence),
            methodSymbol,
            context.Node.SyntaxTree,
            "ExceptionSummary",
            evidenceKey: CreateExceptionEvidenceKey("summary", queryResult.ExceptionEvidence));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            diagnosticLocation,
            "runtime hazards",
            "may_throw");

        var diagnostic = Diagnostic.Create(
            SharpProofDiagnostics.ExceptionSummaryRule,
            diagnosticLocation,
            null,
            properties, methodSymbol.Name, exceptionList);
        if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeUnknownRuntimeHazardCandidates(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ImmutableArray<SymbolicRuntimeHazard> hazards,
        DiagnosticBaseline baseline)
    {
        foreach (var hazard in hazards)
        {
            var site = FindRuntimeHazardSiteNode(context.Node, hazard);
            var location = GetExceptionSiteLocation(site);
            if (location == null) continue;

            var displayReason = hazard.GetDisplayStatusReason();
            if (string.IsNullOrWhiteSpace(displayReason)) displayReason = hazard.Proof.Reason;

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ExceptionTypesProperty, hazard.ExceptionType)
                .Add(SharpProofDiagnostics.ExceptionCategoriesProperty, hazard.Category)
                .Add(SharpProofDiagnostics.ExceptionSourcesProperty,
                    ExceptionSources.UnknownRuntimeHazardCandidate)
                .Add(SharpProofDiagnostics.RuntimeHazardKindProperty, hazard.Kind.ToString())
                .Add(SharpProofDiagnostics.RuntimeHazardStatusProperty, hazard.Status.ToString())
                .Add(SharpProofDiagnostics.RuntimeHazardStatusReasonProperty, hazard.StatusReason)
                .Add(SharpProofDiagnostics.RuntimeHazardTriggerProperty, hazard.TriggerCondition)
                .Add(SharpProofDiagnostics.RuntimeHazardProofBackendProperty, hazard.Proof.Backend.ToString())
                .Add(SharpProofDiagnostics.RuntimeHazardUnknownReasonProperty,
                    hazard.Proof.UnknownReason.ToString());
            properties = UnknownReasonDiagnosticProperties.Add(properties, hazard.UnknownReasonInfo);
            properties = AnalysisTruncationDiagnosticProperties.Add(properties, hazard.AnalysisTruncation);
            properties = BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                hazard.NodeKind,
                evidenceKey: CreateUnknownRuntimeHazardEvidenceKey(hazard));
            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                "runtime hazard candidate",
                hazard.Proof.Status.ToString(),
                hazard.UnknownReasonInfo.Code);

            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.UnknownRuntimeHazardRule,
                location,
                null,
                properties,
                hazard.Kind.ToString(),
                hazard.OperationText,
                displayReason);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static SyntaxNode FindRuntimeHazardSiteNode(
        SyntaxNode methodNode,
        SymbolicRuntimeHazard hazard)
    {
        return methodNode.DescendantNodesAndSelf()
                   .FirstOrDefault(node =>
                       node.SpanStart == hazard.SpanStart &&
                       node.Span.End == hazard.SpanEnd)
               ?? methodNode;
    }

    private static string CreateUnknownRuntimeHazardEvidenceKey(SymbolicRuntimeHazard hazard)
    {
        return hazard.SpanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               hazard.SpanEnd.ToString(CultureInfo.InvariantCulture) +
               "|" +
               hazard.Kind +
               "|" +
               hazard.Category +
               "|" +
               hazard.StatusReason +
               "|" +
               hazard.Proof.UnknownReason +
               "|" +
               hazard.TriggerCondition;
    }

    private static void AnalyzeUncaughtExceptionSites(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ImmutableArray<ExceptionFlowQuery.UncaughtExceptionSiteEntry> siteEntries,
        DiagnosticBaseline baseline)
    {
        foreach (var siteGroup in siteEntries.GroupBy(entry => CreateExceptionSiteKey(entry.Site),
                     StringComparer.Ordinal))
        {
            var firstEntry = siteGroup.First();
            var siteEvidence = new ExceptionFlowQuery.ExceptionEvidenceSet();
            string? exceptionSymbol = null;
            foreach (var siteEntry in siteGroup)
            {
                siteEvidence.Add(siteEntry.Exception);
                exceptionSymbol ??= siteEntry.ExceptionSymbol;
            }

            if (siteEvidence.Count == 0) continue;

            var siteLocation = GetExceptionSiteLocation(firstEntry.Site);
            if (siteLocation == null) continue;

            var sortedTypes = siteEvidence.Types;
            var exceptionList = string.Join(", ", sortedTypes);
            var operationDisplay = GetExceptionSiteDisplay(firstEntry.Site, firstEntry.Method);
            var properties = CreateExceptionProperties(siteEvidence);
            if (!string.IsNullOrWhiteSpace(exceptionSymbol))
                properties = properties.Add(SharpProofDiagnostics.ExceptionSymbolProperty, exceptionSymbol);

            properties = BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                firstEntry.Site.Kind().ToString(),
                evidenceKey: CreateExceptionEvidenceKey(CreateExceptionSiteKey(firstEntry.Site), siteEvidence));
            properties = ExplainDiagnosticProperties.Add(
                properties,
                siteLocation,
                "runtime hazards",
                "hazard",
                siteEvidence.FormatCategories());

            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.UncaughtExceptionSiteRule,
                siteLocation,
                null,
                properties, operationDisplay, exceptionList);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static ImmutableDictionary<string, string?> CreateExceptionProperties(
        ExceptionFlowQuery.ExceptionEvidenceSet exceptionEvidence)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.ExceptionTypesProperty, string.Join(";", exceptionEvidence.Types))
            .Add(SharpProofDiagnostics.ExceptionCategoriesProperty, exceptionEvidence.FormatCategories())
            .Add(SharpProofDiagnostics.ExceptionSourcesProperty, exceptionEvidence.FormatSources());
        var formattedEdges = exceptionEvidence.FormatEdges();
        if (!string.IsNullOrWhiteSpace(formattedEdges))
            properties = properties.Add(SharpProofDiagnostics.ExceptionEdgesProperty, formattedEdges);

        return properties;
    }

    private static string CreateExceptionEvidenceKey(
        string scope,
        ExceptionFlowQuery.ExceptionEvidenceSet exceptionEvidence)
    {
        return scope +
               "|" +
               string.Join(";", exceptionEvidence.Types) +
               "|" +
               exceptionEvidence.FormatCategories() +
               "|" +
               exceptionEvidence.FormatSources() +
               "|" +
               exceptionEvidence.FormatEdges();
    }

    private static string CreateExceptionSiteKey(SyntaxNode node)
    {
        return node.SpanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               node.Span.End.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<TNode> GetRelevantDescendants<TNode>(SyntaxNode methodNode)
        where TNode : SyntaxNode
    {
        return methodNode
            .DescendantNodes(candidate =>
                ReferenceEquals(candidate, methodNode) || !ExecutionVisibility.IsNestedCallableBoundary(candidate))
            .OfType<TNode>();
    }

    private static IEnumerable<TNode> GetRelevantDescendantsAndSelf<TNode>(SyntaxNode methodNode)
        where TNode : SyntaxNode
    {
        return methodNode
            .DescendantNodesAndSelf(candidate =>
                ReferenceEquals(candidate, methodNode) || !ExecutionVisibility.IsNestedCallableBoundary(candidate))
            .OfType<TNode>();
    }

    internal static IEnumerable<MethodCallCandidate> GetCalleeCallSites(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invocation in GetInvocationNodes(methodNode))
        {
            var knownExactLocals = GetKnownExactLocalTypesBefore(invocation, semanticModel, cancellationToken);
            if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
            {
                foreach (var invokedMethod in ResolveInvocationTargets(invocationOperation, knownExactLocals))
                {
                    if (invokedMethod.MethodKind == MethodKind.DelegateInvoke) continue;

                    if (seen.Add(CreateMethodCallSiteKey(invocation, invokedMethod)))
                        yield return new MethodCallCandidate(invocation, invokedMethod);
                }

                if (TryCreateDynamicDispatchCandidate(
                        invocation,
                        invocationOperation.TargetMethod,
                        invocationOperation,
                        invocationOperation.Instance,
                        knownExactLocals,
                        seen,
                        out var dynamicDispatchCandidate))
                    yield return dynamicDispatchCandidate;
            }
            else if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol invokedMethod &&
                     invokedMethod.MethodKind != MethodKind.DelegateInvoke &&
                     seen.Add(CreateMethodCallSiteKey(invocation, invokedMethod)))
            {
                yield return new MethodCallCandidate(invocation, invokedMethod);
            }
        }

        foreach (var creation in GetObjectCreationNodes(methodNode))
            if (semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol is IMethodSymbol constructorSymbol &&
                seen.Add(CreateMethodCallSiteKey(creation, constructorSymbol)))
                yield return new MethodCallCandidate(creation, constructorSymbol);

        foreach (var initializer in GetConstructorInitializerNodes(methodNode))
            if (TryGetConstructorInitializerTarget(initializer, semanticModel, cancellationToken,
                    out var constructorSymbol) &&
                seen.Add(CreateMethodCallSiteKey(initializer, constructorSymbol)))
                yield return new MethodCallCandidate(initializer, constructorSymbol);

        foreach (var propertyAccess in GetPropertyAccessNodes(methodNode, semanticModel, cancellationToken))
        {
            var knownExactLocals = GetKnownExactLocalTypesBefore(propertyAccess, semanticModel, cancellationToken);
            if (semanticModel.GetOperation(propertyAccess, cancellationToken) is IPropertyReferenceOperation
                propertyReferenceOperation)
            {
                foreach (var getterMethod in ResolvePropertyAccessorTargets(
                             propertyReferenceOperation,
                             false,
                             knownExactLocals))
                    if (seen.Add(CreateMethodCallSiteKey(propertyAccess, getterMethod)))
                        yield return new MethodCallCandidate(propertyAccess, getterMethod);

                if (TryCreateDynamicDispatchCandidate(
                        propertyAccess,
                        propertyReferenceOperation.Property?.GetMethod,
                        propertyReferenceOperation,
                        propertyReferenceOperation.Instance,
                        knownExactLocals,
                        seen,
                        out var dynamicDispatchCandidate))
                    yield return dynamicDispatchCandidate;
            }
            else if (semanticModel.GetSymbolInfo(propertyAccess, cancellationToken).Symbol is IPropertySymbol
                         propertySymbol &&
                     propertySymbol.GetMethod != null &&
                     seen.Add(CreateMethodCallSiteKey(propertyAccess, propertySymbol.GetMethod)))
            {
                yield return new MethodCallCandidate(propertyAccess, propertySymbol.GetMethod);
            }
        }

        foreach (var propertyWrite in GetPropertyWriteNodes(methodNode, semanticModel, cancellationToken))
        {
            var knownExactLocals = GetKnownExactLocalTypesBefore(propertyWrite, semanticModel, cancellationToken);
            if (semanticModel.GetOperation(propertyWrite, cancellationToken) is IPropertyReferenceOperation
                propertyReferenceOperation)
            {
                foreach (var setterMethod in ResolvePropertyAccessorTargets(
                             propertyReferenceOperation,
                             true,
                             knownExactLocals))
                    if (seen.Add(CreateMethodCallSiteKey(propertyWrite, setterMethod)))
                        yield return new MethodCallCandidate(propertyWrite, setterMethod);

                if (TryCreateDynamicDispatchCandidate(
                        propertyWrite,
                        propertyReferenceOperation.Property?.SetMethod,
                        propertyReferenceOperation,
                        propertyReferenceOperation.Instance,
                        knownExactLocals,
                        seen,
                        out var dynamicDispatchCandidate))
                    yield return dynamicDispatchCandidate;
            }
            else if (TryGetPropertySetterMethod(propertyWrite, semanticModel, cancellationToken,
                         out var setterMethod) &&
                     setterMethod != null &&
                     seen.Add(CreateMethodCallSiteKey(propertyWrite, setterMethod)))
            {
                yield return new MethodCallCandidate(propertyWrite, setterMethod);
            }
        }

        foreach (var usingDisposeNode in GetUsingDisposeNodes(methodNode, semanticModel, cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(usingDisposeNode)))
                yield return usingDisposeNode;

        foreach (var forEachRuntimeNode in GetForEachRuntimeMethodNodes(methodNode, semanticModel, cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(forEachRuntimeNode.CallSite, forEachRuntimeNode.Method)))
                yield return forEachRuntimeNode;

        foreach (var operatorNode in GetOperatorAndConversionNodes(methodNode, semanticModel, cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(operatorNode.CallSite, operatorNode.Method)))
                yield return operatorNode;

        foreach (var delegateInvocationNode in GetLocalDelegateTargetInvocationNodes(methodNode, semanticModel,
                     cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(delegateInvocationNode.CallSite, delegateInvocationNode.Method)))
                yield return delegateInvocationNode;

        foreach (var handlerConstructorNode in GetInterpolatedStringHandlerConstructorNodes(methodNode, semanticModel,
                     cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(handlerConstructorNode.CallSite, handlerConstructorNode.Method)))
                yield return handlerConstructorNode;
    }

    private static string CreateMethodCallSiteKey(SyntaxNode callSite, IMethodSymbol method)
    {
        return callSite.SpanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               callSite.Span.End.ToString(CultureInfo.InvariantCulture) +
               "|" +
               method.OriginalDefinition.ToDisplayString();
    }

    private static string CreateDynamicDispatchCallSiteKey(SyntaxNode callSite, IMethodSymbol method)
    {
        return CreateMethodCallSiteKey(callSite, method) + "|dynamic-dispatch";
    }

    private static bool TryCreateDynamicDispatchCandidate(
        SyntaxNode callSite,
        IMethodSymbol? method,
        IOperation operation,
        IOperation? receiver,
        IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals,
        HashSet<string> seen,
        out MethodCallCandidate candidate)
    {
        candidate = null!;
        if (method == null ||
            !IsSourceDispatchSlot(method) ||
            TryResolveExactConcreteType(receiver, knownExactLocals, out _) ||
            !SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(method, operation) ||
            !seen.Add(CreateDynamicDispatchCallSiteKey(callSite, method)))
            return false;

        candidate = new MethodCallCandidate(
            callSite,
            method.OriginalDefinition,
            isDynamicDispatch: true);
        return true;
    }

    private static bool IsSourceDispatchSlot(IMethodSymbol method)
    {
        return method.OriginalDefinition.DeclaringSyntaxReferences.Length != 0;
    }

    private static string CreateMethodCallSiteKey(MethodCallCandidate candidate)
    {
        var key = CreateMethodCallSiteKey(candidate.CallSite, candidate.Method);
        if (candidate.UsingDisposeGuard?.ResourceExpression is not { } resourceExpression) return key;

        return key +
               "|using-resource:" +
               resourceExpression.SpanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               resourceExpression.Span.End.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<IMethodSymbol> ResolveInvocationTargets(
        IInvocationOperation invocationOperation,
        IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals = null)
    {
        var invokedMethod = invocationOperation.TargetMethod;
        if (invokedMethod == null) yield break;

        if (IsBaseReference(invocationOperation.Instance))
        {
            yield return invokedMethod.OriginalDefinition;
            yield break;
        }

        if (TryResolveExactConcreteType(invocationOperation.Instance, knownExactLocals, out var exactReceiverType))
        {
            var exactTarget =
                PurityAnalysisEngine.ResolveMethodTargetForConcreteReceiver(invokedMethod, exactReceiverType);
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
        if (accessor == null) yield break;

        if (IsBaseReference(propertyReferenceOperation.Instance))
        {
            yield return accessor.OriginalDefinition;
            yield break;
        }

        if (propertyReferenceOperation.Property != null &&
            TryResolveExactConcreteType(propertyReferenceOperation.Instance, knownExactLocals,
                out var exactReceiverType))
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
            yield return constructorDeclaration.Initializer;
    }

    private static bool TryGetConstructorInitializerTarget(
        ConstructorInitializerSyntax initializer,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
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
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rootOperation =
            MethodBodyOperationResolver.GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken, false);
        if (rootOperation == null) yield break;

        foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            if (TryGetOperatorOrConversionMethod(operation, out var method))
            {
                var key = method!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                          "@" +
                          operation.Syntax.SpanStart.ToString(CultureInfo.InvariantCulture) +
                          ":" +
                          operation.Syntax.Span.End.ToString(CultureInfo.InvariantCulture);
                if (seen.Add(key)) yield return new MethodCallCandidate(operation.Syntax, method);
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
                when conversionOperation.Conversion.IsUserDefined &&
                     conversionOperation.Conversion.MethodSymbol != null:
                method = conversionOperation.Conversion.MethodSymbol;
                return true;
            default:
                return false;
        }
    }

    private static Location? GetIdentifierLocation(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
            OperatorDeclarationSyntax op => op.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.GetLocation(),
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

    private sealed class AttributePolicyScope : IDisposable
    {
        private readonly SharpProofAttributeIdentityPolicy? _previous;

        public AttributePolicyScope(SharpProofAttributeIdentityPolicy? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            CurrentAttributePolicy.Value = _previous;
        }
    }

    private sealed class CachedUnknownRuntimeHazards
    {
        public CachedUnknownRuntimeHazards(ImmutableArray<SymbolicRuntimeHazard> hazards)
        {
            Hazards = hazards;
        }

        public ImmutableArray<SymbolicRuntimeHazard> Hazards { get; }
    }

    internal sealed class MethodCallCandidate
    {
        public MethodCallCandidate(
            SyntaxNode callSite,
            IMethodSymbol method,
            UsingDisposeGuard? usingDisposeGuard = null,
            bool isDynamicDispatch = false)
        {
            CallSite = callSite;
            Method = method;
            UsingDisposeGuard = usingDisposeGuard;
            IsDynamicDispatch = isDynamicDispatch;
        }

        public SyntaxNode CallSite { get; }

        public IMethodSymbol Method { get; }

        public UsingDisposeGuard? UsingDisposeGuard { get; }

        public bool IsDynamicDispatch { get; }
    }

    internal sealed class UsingDisposeGuard
    {
        public UsingDisposeGuard(ExpressionSyntax resourceExpression)
        {
            ResourceExpression = resourceExpression;
        }

        public ExpressionSyntax ResourceExpression { get; }
    }

    private enum PathFactKind
    {
        Zero,
        Null
    }
}
