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
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        CompilationPurityService purityService,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var runtimeHazardMode = context.Configuration.RuntimeHazardMode;
        var reportMethodSummaries = context.Configuration.ReportExceptions ||
                                    (runtimeHazardMode & RuntimeHazardMode.Summaries) != 0;
        var reportCheckedExceptionSites = context.Configuration.CheckedExceptions ||
                                          (runtimeHazardMode & RuntimeHazardMode.Sites) != 0;
        var reportUnknownRuntimeHazards =
            (runtimeHazardMode & RuntimeHazardMode.Unknowns) != 0;

        var methodSymbol = context.MethodSymbol;

        if (PurityAnalysisEngine.IsMetadataSymbol(methodSymbol)) return;

        var exceptionContracts = CollectExceptionContracts(methodSymbol, context.SemanticModel, attributePolicy,
            context.CancellationToken);
        var hasValidExceptionContracts = exceptionContracts.Any(static contract => contract.InvalidArguments.IsDefaultOrEmpty);
        if (!reportMethodSummaries &&
            !reportCheckedExceptionSites &&
            !reportUnknownRuntimeHazards &&
            exceptionContracts.Length == 0)
            return;

        ExceptionFlowEngine.ExceptionFlowResult? queryResult = null;
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
                        () => ExceptionFlowEngine.AnalyzeMethod(
                            context.Snapshot.Input,
                            context.CancellationToken,
                            exceptionSummaryCatalog,
                            purityService.SmtAnalysis,
                            attributePolicy));

                if (reportUnknownRuntimeHazards)
                {
                    var hazardResult = queryResult ?? context.State.GetOrCreateSymbolicQueryResult(
                        "unknown-runtime-hazards",
                        () => ExceptionFlowEngine.AnalyzeHazards(
                            context.Snapshot.Input,
                            context.CancellationToken,
                            purityService.SmtAnalysis));
                    unknownRuntimeHazards = hazardResult.RawHazards
                        .Where(static hazard =>
                            hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                                SymbolicRuntimeHazardStatus.Unsupported)
                        .ToImmutableArray();
                }
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
            AnalyzeUncaughtExceptionSites(context, methodSymbol, queryResult.Sites, baseline);

        if (!reportMethodSummaries || queryResult.Evidence.Count == 0) return;

        var diagnosticLocation = GetIdentifierLocation(context.Node);
        if (diagnosticLocation == null) return;

        var sortedTypes = queryResult.Evidence.Types;
        var exceptionList = string.Join(", ", sortedTypes);
        var properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            CreateExceptionProperties(queryResult.Evidence),
            methodSymbol,
            context.Node.SyntaxTree,
            "ExceptionSummary",
            null,
            CreateExceptionEvidenceKey("summary", queryResult.Evidence),
            diagnosticLocation,
            "runtime hazards",
            "may_throw");

        var diagnostic = Diagnostic.Create(
            SharpProofDiagnostics.ExceptionSummaryRule,
            diagnosticLocation,
            null,
            properties, methodSymbol.Name, exceptionList);
        AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
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
            properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                hazard.NodeKind,
                null,
                CreateUnknownRuntimeHazardEvidenceKey(hazard),
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
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
        }
    }

    internal static SyntaxNode FindRuntimeHazardSiteNode(
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
        return CreateSourceSpanKey(hazard.SpanStart, hazard.SpanEnd) +
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
        ImmutableArray<ExceptionFlowEngine.ExceptionFlowSite> siteEntries,
        DiagnosticBaseline baseline)
    {
        foreach (var siteGroup in siteEntries.GroupBy(entry => CreateExceptionSiteKey(entry.Site),
                     StringComparer.Ordinal))
        {
            var firstEntry = siteGroup.First();
            var siteEvidence = new ExceptionFlowEngine.ExceptionEvidenceProjection(siteGroup);
            var exceptionSymbol = siteGroup.Select(static site => site.ExceptionSymbol)
                .FirstOrDefault(static symbol => !string.IsNullOrWhiteSpace(symbol));

            if (siteEvidence.Count == 0) continue;

            var siteLocation = GetExceptionSiteLocation(firstEntry.Site);
            if (siteLocation == null) continue;

            var sortedTypes = siteEvidence.Types;
            var exceptionList = string.Join(", ", sortedTypes);
            var operationDisplay = GetExceptionSiteDisplay(firstEntry.Site, firstEntry.Method);
            var properties = CreateExceptionProperties(siteEvidence);
            if (!string.IsNullOrWhiteSpace(exceptionSymbol))
                properties = properties.Add(SharpProofDiagnostics.ExceptionSymbolProperty, exceptionSymbol);

            properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                firstEntry.Site.Kind().ToString(),
                null,
                CreateExceptionEvidenceKey(CreateExceptionSiteKey(firstEntry.Site), siteEvidence),
                siteLocation,
                "runtime hazards",
                "hazard",
                siteEvidence.FormatCategories());

            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.UncaughtExceptionSiteRule,
                siteLocation,
                null,
                properties, operationDisplay, exceptionList);
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
        }
    }

    private static ImmutableDictionary<string, string?> CreateExceptionProperties(
        ExceptionFlowEngine.ExceptionEvidenceProjection exceptionEvidence)
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
        ExceptionFlowEngine.ExceptionEvidenceProjection exceptionEvidence)
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
        return CreateSourceSpanKey(node);
    }

    internal static IEnumerable<TNode> GetRelevantDescendants<TNode>(SyntaxNode methodNode)
        where TNode : SyntaxNode
    {
        return CSharpSyntaxFacts
            .DescendantNodesInExecution(methodNode, includeSelf: false)
            .OfType<TNode>();
    }

    private static IEnumerable<TNode> GetRelevantDescendantsAndSelf<TNode>(SyntaxNode methodNode)
        where TNode : SyntaxNode
    {
        return CSharpSyntaxFacts
            .DescendantNodesInExecution(methodNode)
            .OfType<TNode>();
    }

    internal static IEnumerable<MethodCallCandidate> GetCalleeCallSites(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rootOperation =
            MethodBodyOperationResolver.GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken, false);
        if (rootOperation != null)
        {
            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (operation)
                {
                    case IInvocationOperation invocation:
                        foreach (var candidate in CreateInvocationCandidates(
                                     invocation, semanticModel, cancellationToken))
                            if (seen.Add(CreateMethodCallSiteKey(candidate))) yield return candidate;
                        break;
                    case IObjectCreationOperation { Constructor: { } constructor } creation:
                        var creationCandidate = new MethodCallCandidate(creation.Syntax, constructor);
                        if (seen.Add(CreateMethodCallSiteKey(creationCandidate))) yield return creationCandidate;
                        break;
                    case IPropertyReferenceOperation property:
                        foreach (var candidate in CreatePropertyCandidates(
                                     property, semanticModel, cancellationToken))
                            if (seen.Add(CreateMethodCallSiteKey(candidate))) yield return candidate;
                        break;
                    case IInterpolatedStringHandlerCreationOperation handler:
                        var handlerConstructor = FindObjectCreationConstructor(handler.HandlerCreation);
                        if (handlerConstructor != null)
                        {
                            var handlerCandidate = new MethodCallCandidate(handler.Syntax, handlerConstructor);
                            if (seen.Add(CreateMethodCallSiteKey(handlerCandidate))) yield return handlerCandidate;
                        }
                        break;
                    default:
                        if (TryGetOperatorOrConversionMethod(operation, out var method) &&
                            seen.Add(CreateMethodCallSiteKey(operation.Syntax, method!)))
                            yield return new MethodCallCandidate(operation.Syntax, method!);
                        break;
                }
            }
        }

        if (methodNode is ConstructorDeclarationSyntax { Initializer: { } initializer })
        {
            var initializedConstructor =
                (semanticModel.GetOperation(initializer, cancellationToken) as IInvocationOperation)?.TargetMethod ??
                semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol as IMethodSymbol;
            if (initializedConstructor != null)
            {
                var candidate = new MethodCallCandidate(initializer, initializedConstructor);
                if (seen.Add(CreateMethodCallSiteKey(candidate))) yield return candidate;
            }
        }

        foreach (var usingDisposeNode in GetUsingDisposeNodes(methodNode, semanticModel, cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(usingDisposeNode))) yield return usingDisposeNode;

        foreach (var forEachRuntimeNode in GetForEachRuntimeMethodNodes(methodNode, semanticModel))
            if (seen.Add(CreateMethodCallSiteKey(forEachRuntimeNode.CallSite, forEachRuntimeNode.Method)))
                yield return forEachRuntimeNode;

        foreach (var delegateInvocationNode in GetLocalDelegateTargetInvocationNodes(methodNode, semanticModel,
                     cancellationToken))
            if (seen.Add(CreateMethodCallSiteKey(delegateInvocationNode)))
                yield return delegateInvocationNode;
    }

    private static IEnumerable<MethodCallCandidate> CreateInvocationCandidates(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var method = ResolveDispatchTarget(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Syntax,
            semanticModel,
            cancellationToken,
            exactType => PurityConcreteReceiverResolver.ResolveMethodTargetForConcreteReceiver(
                invocation.TargetMethod, exactType));
        if (method.MethodKind != MethodKind.DelegateInvoke)
            yield return new MethodCallCandidate(invocation.Syntax, method);

        if (TryCreateDynamicDispatchCandidate(
                invocation.Syntax,
                invocation.TargetMethod,
                invocation,
                invocation.Instance,
                semanticModel,
                cancellationToken,
                out var dynamicDispatchCandidate))
            yield return dynamicDispatchCandidate;
    }

    private static IEnumerable<MethodCallCandidate> CreatePropertyCandidates(
        IPropertyReferenceOperation property,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var setter = property.Syntax.Parent is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression
        } assignment && ReferenceEquals(assignment.Left, property.Syntax);
        var propertySymbol = property.Property;
        var accessor = setter ? propertySymbol?.SetMethod : propertySymbol?.GetMethod;
        if (propertySymbol != null && accessor != null)
            yield return new MethodCallCandidate(
                property.Syntax,
                ResolveDispatchTarget(
                    accessor,
                    property.Instance,
                    property.Syntax,
                    semanticModel,
                    cancellationToken,
                    exactType => PurityConcreteReceiverResolver.ResolvePropertyAccessorTargetForConcreteReceiver(
                        propertySymbol, exactType, setter)));

        if (TryCreateDynamicDispatchCandidate(
                property.Syntax,
                accessor,
                property,
                property.Instance,
                semanticModel,
                cancellationToken,
                out var dynamicDispatchCandidate))
            yield return dynamicDispatchCandidate;
    }

    private static string CreateMethodCallSiteKey(SyntaxNode callSite, IMethodSymbol method)
    {
        return CreateSourceSpanKey(callSite) +
               "|" +
               method.OriginalDefinition.ToDisplayString();
    }

    private static bool TryCreateDynamicDispatchCandidate(
        SyntaxNode callSite,
        IMethodSymbol? method,
        IOperation operation,
        IOperation? receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out MethodCallCandidate candidate)
    {
        candidate = null!;
        if (method == null ||
            method.OriginalDefinition.DeclaringSyntaxReferences.Length == 0 ||
            PurityConcreteReceiverResolver.TryResolveExactConcreteType(
                receiver, callSite, semanticModel, cancellationToken, out _) ||
            !SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(method, operation))
            return false;

        candidate = new MethodCallCandidate(
            callSite,
            method.OriginalDefinition,
            isDynamicDispatch: true);
        return true;
    }

    private static string CreateMethodCallSiteKey(MethodCallCandidate candidate)
    {
        var key = CreateMethodCallSiteKey(candidate.CallSite, candidate.Method);
        if (candidate.IsDynamicDispatch) key += "|dynamic-dispatch";
        if (candidate.UsingDisposeGuard?.ResourceExpression is not { } resourceExpression) return key;

        return key +
               "|using-resource:" +
               CreateSourceSpanKey(resourceExpression);
    }

    private static IMethodSymbol ResolveDispatchTarget(
        IMethodSymbol fallbackTarget,
        IOperation? receiver,
        SyntaxNode callSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<INamedTypeSymbol, IMethodSymbol?> resolveExactTarget)
    {
        if (!SymbolicDispatchFacts.IsBaseReference(receiver) &&
            PurityConcreteReceiverResolver.TryResolveExactConcreteType(
                receiver,
                callSite,
                semanticModel,
                cancellationToken,
                out var exactReceiverType) &&
            resolveExactTarget(exactReceiverType) is { } exactTarget)
            return exactTarget.OriginalDefinition;

        return fallbackTarget.OriginalDefinition;
    }

    private static string CreateSourceSpanKey(SyntaxNode node)
    {
        return CreateSourceSpanKey(node.SpanStart, node.Span.End);
    }

    private static string CreateSourceSpanKey(int spanStart, int spanEnd)
    {
        return spanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               spanEnd.ToString(CultureInfo.InvariantCulture);
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

    private sealed class AttributePolicyScope(SharpProofAttributeIdentityPolicy? previous) : IDisposable
    {
        public void Dispose() => CurrentAttributePolicy.Value = previous;
    }

    internal sealed class MethodCallCandidate(
        SyntaxNode callSite,
        IMethodSymbol method,
        UsingDisposeGuard? usingDisposeGuard = null,
        bool isDynamicDispatch = false)
    {
        public SyntaxNode CallSite { get; } = callSite;
        public IMethodSymbol Method { get; } = method;
        public UsingDisposeGuard? UsingDisposeGuard { get; } = usingDisposeGuard;
        public bool IsDynamicDispatch { get; } = isDynamicDispatch;
    }

    internal sealed class UsingDisposeGuard(ExpressionSyntax resourceExpression)
    {
        public ExpressionSyntax ResourceExpression { get; } = resourceExpression;
    }

}
