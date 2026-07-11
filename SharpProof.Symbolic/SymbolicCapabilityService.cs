using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProofCapability = SharpProof.Symbolic.SymbolicCapability;

namespace SharpProof.Symbolic;

internal sealed class SymbolicCapabilityService
{
    private static readonly SymbolDisplayFormat CapabilitySymbolDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
        SymbolDisplayMemberOptions.IncludeContainingType |
        SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions:
        SymbolDisplayParameterOptions.IncludeName |
        SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public SymbolicCapabilityResult Query(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        if (target == null) throw new ArgumentNullException(nameof(target));

        options ??= SymbolicQueryOptions.Default;

        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return QueryFile(
                    source.FilePath!,
                    target,
                    options.References,
                    source.CompilationProfile,
                    cancellationToken);
            case SymbolicSourceInputKind.Text:
                return QuerySource(
                    source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                    target,
                    options.References,
                    source.CompilationProfile,
                    cancellationToken);
            case SymbolicSourceInputKind.SyntaxTree:
                return QuerySyntaxTree(source.SyntaxTree!, source.Compilation!, target, cancellationToken);
            case SymbolicSourceInputKind.Node:
                return QueryNode(source.Node!, source.SemanticModel!, target, cancellationToken);
            default:
                throw new NotSupportedException("Capability source kind is not supported.");
        }
    }

    private SymbolicCapabilityResult QueryFile(
        string filePath,
        SymbolicQueryTarget target,
        IEnumerable<MetadataReference>? references,
        SymbolicSourceCompilationProfile? compilationProfile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath)) throw new FileNotFoundException("Source file does not exist.", filePath);

        return QuerySource(
            File.ReadAllText(filePath),
            Path.GetFullPath(filePath),
            target,
            references,
            compilationProfile,
            cancellationToken);
    }

    private SymbolicCapabilityResult QuerySource(
        string sourceText,
        string filePath,
        SymbolicQueryTarget target,
        IEnumerable<MetadataReference>? references,
        SymbolicSourceCompilationProfile? compilationProfile,
        CancellationToken cancellationToken)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            "SharpProof.Symbolic.Capabilities.cs",
            "SharpProof.Symbolic.Capabilities",
            references,
            cancellationToken,
            compilationProfile);
        return QuerySyntaxTree(syntaxTree, compilation, target, cancellationToken);
    }

    private SymbolicCapabilityResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var resolvedTarget = ResolveTarget(syntaxTree, semanticModel, target, cancellationToken);
        var session = new AnalysisSession(compilation, cancellationToken);
        var summary = session.Analyze(resolvedTarget.Declaration, resolvedTarget.SemanticModel);
        return CreateResult(resolvedTarget, summary, cancellationToken);
    }

    private SymbolicCapabilityResult QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        if (target.Kind != SymbolicQueryTargetKind.Node)
            throw new NotSupportedException("Capability node queries require a node target.");

        var resolvedTarget = ResolveNodeTarget(node, semanticModel, cancellationToken);
        var session = new AnalysisSession(semanticModel.Compilation, cancellationToken);
        var summary = session.Analyze(resolvedTarget.Declaration, resolvedTarget.SemanticModel);
        return CreateResult(resolvedTarget, summary, cancellationToken);
    }

    private static SymbolicCapabilityResult CreateResult(
        ResolvedCapabilityTarget target,
        CapabilitySummary summary,
        CancellationToken cancellationToken)
    {
        var syntaxTree = target.Declaration.SyntaxTree;
        var sourceSpan =
            SymbolicSourceLocation.GetNodeSourceSpan(syntaxTree, target.Declaration.Span, cancellationToken);
        var sites = summary.Sites
            .Select(site =>
            {
                var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
                    syntaxTree,
                    site.SpanStart,
                    cancellationToken,
                    true);
                return new SymbolicCapabilitySite(
                    site.Capabilities,
                    FormatCapabilities(site.Capabilities),
                    site.SiteKind,
                    site.OperationKind,
                    site.OperationText,
                    site.SymbolDisplayName,
                    site.IsTransitive,
                    site.IsUnknown,
                    site.UnknownReason,
                    site.SpanStart,
                    site.SpanLength,
                    lineColumn.Line,
                    lineColumn.Column);
            })
            .ToArray();

        return new SymbolicCapabilityResult(
            syntaxTree.FilePath ?? string.Empty,
            target.MethodName,
            target.MethodDisplayName,
            target.DeclarationKind,
            target.Declaration.SpanStart,
            target.Declaration.Span.End,
            sourceSpan.StartLine,
            sourceSpan.StartColumn,
            sourceSpan.EndLine,
            sourceSpan.EndColumn,
            summary.Capabilities,
            FormatCapabilities(summary.Capabilities),
            sites,
            summary.UnknownReasons.OrderBy(static reason => reason.ToString(), StringComparer.Ordinal).ToArray());
    }

    private static ResolvedCapabilityTarget ResolveTarget(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        var root = syntaxTree.GetRoot(cancellationToken);
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                {
                    var position = SymbolicSourceLocation.GetPosition(
                        syntaxTree,
                        target.LineNumber!.Value,
                        target.ColumnNumber ?? 1,
                        cancellationToken);
                    return ResolvePositionTarget(root, syntaxTree, semanticModel, position, cancellationToken);
                }

            case SymbolicQueryTargetKind.Position:
                return ResolvePositionTarget(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.PositionOffset!.Value,
                    cancellationToken);

            case SymbolicQueryTargetKind.Line:
                return ResolveLineTarget(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.LineNumber!.Value,
                    cancellationToken);

            default:
                throw new NotSupportedException(
                    "Capability queries support point, position, line, or node targets only.");
        }
    }

    private static ResolvedCapabilityTarget ResolvePositionTarget(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int position,
        CancellationToken cancellationToken)
    {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var token = root.FindToken(position);
        if (token.Parent == null)
            throw new ArgumentException("Could not resolve a method-like body at the requested position.",
                nameof(position));

        return ResolveContainingMethodLike(token.Parent, semanticModel, cancellationToken);
    }

    private static ResolvedCapabilityTarget ResolveLineTarget(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int line,
        CancellationToken cancellationToken)
    {
        var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
        var declaration = root
            .DescendantNodes(static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
            .Where(static candidate => IsMethodLikeDeclaration(candidate))
            .Where(candidate => candidate.Span.OverlapsWith(lineSpan))
            .OrderBy(candidate => candidate.Span.Length)
            .ThenBy(candidate => candidate.SpanStart)
            .FirstOrDefault();

        if (declaration == null)
        {
            var token = root.FindToken(lineSpan.Start);
            if (token.Parent == null)
                throw new ArgumentException("Could not resolve a method-like body on the requested line.",
                    nameof(line));

            return ResolveContainingMethodLike(token.Parent, semanticModel, cancellationToken);
        }

        return ResolveMethodLikeDeclaration(declaration, semanticModel, cancellationToken);
    }

    private static ResolvedCapabilityTarget ResolveNodeTarget(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsMethodLikeDeclaration(node)
            ? ResolveMethodLikeDeclaration(node, semanticModel, cancellationToken)
            : ResolveContainingMethodLike(node, semanticModel, cancellationToken);
    }

    private static ResolvedCapabilityTarget ResolveContainingMethodLike(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMethodLikeDeclaration(ancestor))
                return ResolveMethodLikeDeclaration(ancestor, semanticModel, cancellationToken);
        }

        throw new ArgumentException("Could not resolve a containing method-like body for the requested target.",
            nameof(node));
    }

    private static ResolvedCapabilityTarget ResolveMethodLikeDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var methodName = TryGetDeclaredSymbol(declaration, semanticModel, cancellationToken)?.Name ?? string.Empty;
        var methodDisplayName =
            TryGetDeclaredSymbol(declaration, semanticModel, cancellationToken)?.ToDisplayString() ?? methodName;
        return new ResolvedCapabilityTarget(
            declaration,
            semanticModel,
            methodName,
            methodDisplayName,
            GetDeclarationKind(declaration));
    }

    private static bool IsMethodLikeDeclaration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax ||
               node is ConstructorDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is PropertyDeclarationSyntax ||
               node is IndexerDeclarationSyntax ||
               node is LocalFunctionStatementSyntax ||
               node is OperatorDeclarationSyntax ||
               node is ConversionOperatorDeclarationSyntax;
    }

    private static string GetDeclarationKind(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax => nameof(MethodDeclarationSyntax),
            ConstructorDeclarationSyntax => nameof(ConstructorDeclarationSyntax),
            AccessorDeclarationSyntax => nameof(AccessorDeclarationSyntax),
            PropertyDeclarationSyntax => nameof(PropertyDeclarationSyntax),
            IndexerDeclarationSyntax => nameof(IndexerDeclarationSyntax),
            LocalFunctionStatementSyntax => nameof(LocalFunctionStatementSyntax),
            OperatorDeclarationSyntax => nameof(OperatorDeclarationSyntax),
            ConversionOperatorDeclarationSyntax => nameof(ConversionOperatorDeclarationSyntax),
            _ => declaration.GetType().Name
        };
    }

    private static ISymbol? TryGetDeclaredSymbol(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return declaration switch
        {
            MethodDeclarationSyntax methodDeclaration => semanticModel.GetDeclaredSymbol(methodDeclaration,
                cancellationToken),
            ConstructorDeclarationSyntax constructorDeclaration => semanticModel.GetDeclaredSymbol(
                constructorDeclaration, cancellationToken),
            AccessorDeclarationSyntax accessorDeclaration => semanticModel.GetDeclaredSymbol(accessorDeclaration,
                cancellationToken),
            PropertyDeclarationSyntax propertyDeclaration => semanticModel.GetDeclaredSymbol(propertyDeclaration,
                cancellationToken),
            IndexerDeclarationSyntax indexerDeclaration => semanticModel.GetDeclaredSymbol(indexerDeclaration,
                cancellationToken),
            LocalFunctionStatementSyntax localFunctionStatement => semanticModel.GetDeclaredSymbol(
                localFunctionStatement, cancellationToken),
            OperatorDeclarationSyntax operatorDeclaration => semanticModel.GetDeclaredSymbol(operatorDeclaration,
                cancellationToken),
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => semanticModel.GetDeclaredSymbol(
                conversionOperatorDeclaration, cancellationToken),
            _ => null
        };
    }

    private static string FormatCapabilities(SharpProofCapability capabilities)
    {
        capabilities = NormalizeCapabilities(capabilities);
        if (capabilities == SharpProofCapability.None) return "None";

        var values = Enum.GetValues(typeof(SharpProofCapability))
            .Cast<SharpProofCapability>()
            .Where(value => value != SharpProofCapability.None && capabilities.HasFlag(value))
            .Select(static value => value.ToString())
            .ToArray();
        return values.Length == 0 ? "None" : string.Join(", ", values);
    }

    private static SharpProofCapability NormalizeCapabilities(SharpProofCapability capabilities)
    {
        if ((capabilities & (SharpProofCapability.FileRead |
                             SharpProofCapability.FileWrite |
                             SharpProofCapability.Network |
                             SharpProofCapability.Console |
                             SharpProofCapability.Registry)) != 0)
            capabilities |= SharpProofCapability.IO;

        return capabilities;
    }

    private static bool HasMethodBody(SyntaxNode methodNode)
    {
        return methodNode switch
        {
            MethodDeclarationSyntax methodDeclaration =>
                methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null,
            ConstructorDeclarationSyntax constructorDeclaration =>
                constructorDeclaration.Body != null || constructorDeclaration.ExpressionBody != null,
            OperatorDeclarationSyntax operatorDeclaration =>
                operatorDeclaration.Body != null || operatorDeclaration.ExpressionBody != null,
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration =>
                conversionOperatorDeclaration.Body != null || conversionOperatorDeclaration.ExpressionBody != null,
            AccessorDeclarationSyntax accessorDeclaration =>
                accessorDeclaration.Body != null || accessorDeclaration.ExpressionBody != null,
            PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.ExpressionBody != null,
            IndexerDeclarationSyntax indexerDeclaration => indexerDeclaration.ExpressionBody != null,
            LocalFunctionStatementSyntax localFunction =>
                localFunction.Body != null || localFunction.ExpressionBody != null,
            _ => false
        };
    }

    private sealed class AnalysisSession
    {
        private readonly HashSet<IMethodSymbol> _activeMethods =
            new(SymbolEqualityComparer.Default);

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;

        private readonly Dictionary<IMethodSymbol, CapabilitySummary> _methodCache =
            new(SymbolEqualityComparer.Default);

        public AnalysisSession(Compilation compilation, CancellationToken cancellationToken)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _cancellationToken = cancellationToken;
        }

        public CapabilitySummary Analyze(SyntaxNode declaration, SemanticModel semanticModel)
        {
            var declaredMethodSymbol = TryGetMethodSymbol(declaration, semanticModel, _cancellationToken);
            if (declaredMethodSymbol != null &&
                _methodCache.TryGetValue(declaredMethodSymbol, out var cachedSummary))
                return cachedSummary;

            if (declaredMethodSymbol != null &&
                !_activeMethods.Add(declaredMethodSymbol))
                return CapabilitySummary.Unknown(SymbolicCapabilityUnknownReason.RecursiveSourceCycle);

            try
            {
                var rootOperation =
                    MethodBodyOperationResolver.GetMethodBodyRootOperation(declaration, semanticModel,
                        _cancellationToken, true);
                if (rootOperation == null)
                {
                    var unsupported = CapabilitySummary.Unknown(SymbolicCapabilityUnknownReason.UnsupportedTarget);
                    if (declaredMethodSymbol != null) _methodCache[declaredMethodSymbol] = unsupported;

                    return unsupported;
                }

                var sites = new List<CapabilitySiteData>();
                var unknownReasons = new HashSet<SymbolicCapabilityUnknownReason>();
                foreach (var operation in rootOperation.DescendantsAndSelf())
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!IsVisibleOperation(operation, declaration)) continue;

                    foreach (var site in AnalyzeOperation(operation, semanticModel))
                    {
                        sites.Add(site);
                        if (site.IsUnknown) unknownReasons.Add(site.UnknownReason);
                    }
                }

                var summary = CapabilitySummary.FromSites(sites, unknownReasons);
                if (declaredMethodSymbol != null) _methodCache[declaredMethodSymbol] = summary;

                return summary;
            }
            catch (OperationCanceledException)
            {
                return CapabilitySummary.Unknown(SymbolicCapabilityUnknownReason.CancellationRequested);
            }
            finally
            {
                if (declaredMethodSymbol != null) _activeMethods.Remove(declaredMethodSymbol);
            }
        }

        private IEnumerable<CapabilitySiteData> AnalyzeOperation(IOperation operation, SemanticModel semanticModel)
        {
            switch (operation)
            {
                case ILockOperation:
                    yield return CapabilitySiteData.Proven(
                        SharpProofCapability.Synchronization,
                        operation,
                        "lock",
                        string.Empty);
                    yield break;

                case IDynamicMemberReferenceOperation dynamicMemberReferenceOperation
                    when dynamicMemberReferenceOperation.Parent is IDynamicInvocationOperation
                        or IDynamicIndexerAccessOperation:
                    yield break;

                case IDynamicInvocationOperation:
                case IDynamicIndexerAccessOperation:
                case IDynamicMemberReferenceOperation:
                case IDynamicObjectCreationOperation:
                    yield return CapabilitySiteData.Unknown(
                        operation,
                        "dynamic",
                        SymbolicCapabilityUnknownReason.DynamicDispatch,
                        string.Empty);
                    yield break;

                case IInvocationOperation invocation:
                    foreach (var site in AnalyzeSymbolUsage(invocation.TargetMethod, invocation, "invocation",
                                 invocation.TargetMethod)) yield return site;
                    yield break;

                case IObjectCreationOperation objectCreationOperation:
                    foreach (var site in AnalyzeSymbolUsage(
                                 objectCreationOperation.Constructor,
                                 objectCreationOperation,
                                 "object_creation",
                                 objectCreationOperation.Constructor ?? (ISymbol?)objectCreationOperation.Type))
                        yield return site;
                    yield break;

                case IPropertyReferenceOperation propertyReferenceOperation:
                    foreach (var site in AnalyzePropertyUsage(propertyReferenceOperation)) yield return site;
                    yield break;

                case IFieldReferenceOperation fieldReferenceOperation:
                    foreach (var site in AnalyzeFieldUsage(fieldReferenceOperation.Field, fieldReferenceOperation))
                        yield return site;
                    yield break;

                default:
                    yield break;
            }
        }

        private IEnumerable<CapabilitySiteData> AnalyzePropertyUsage(
            IPropertyReferenceOperation propertyReferenceOperation)
        {
            var accessor = propertyReferenceOperation.Property.GetMethod ??
                           propertyReferenceOperation.Property.SetMethod;
            foreach (var site in AnalyzeSymbolUsage(accessor, propertyReferenceOperation, "property_access",
                         propertyReferenceOperation.Property)) yield return site;
        }

        private IEnumerable<CapabilitySiteData> AnalyzeFieldUsage(IFieldSymbol fieldSymbol,
            IFieldReferenceOperation fieldReferenceOperation)
        {
            if (TryClassifySymbolCapabilities(fieldSymbol, out var capabilities))
            {
                if (capabilities != SharpProofCapability.None)
                    yield return CapabilitySiteData.Proven(
                        capabilities,
                        fieldReferenceOperation,
                        "field_access",
                        fieldSymbol.ToDisplayString(CapabilitySymbolDisplayFormat));

                yield break;
            }

            if (ShouldTreatMetadataSymbolAsUnknown(fieldSymbol))
                yield return CapabilitySiteData.Unknown(
                    fieldReferenceOperation,
                    "field_access",
                    SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable,
                    fieldSymbol.ToDisplayString(CapabilitySymbolDisplayFormat));
        }

        private IEnumerable<CapabilitySiteData> AnalyzeSymbolUsage(
            IMethodSymbol? methodSymbol,
            IOperation operation,
            string siteKind,
            ISymbol? displaySymbol)
        {
            if (methodSymbol == null)
            {
                yield return CapabilitySiteData.Unknown(
                    operation,
                    siteKind,
                    SymbolicCapabilityUnknownReason.DynamicDispatch,
                    displaySymbol?.ToDisplayString(CapabilitySymbolDisplayFormat) ?? string.Empty);
                yield break;
            }

            if (SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(methodSymbol, operation))
            {
                yield return CapabilitySiteData.Unknown(
                    operation,
                    siteKind,
                    SymbolicCapabilityUnknownReason.DynamicDispatch,
                    displaySymbol?.ToDisplayString(CapabilitySymbolDisplayFormat) ??
                    methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat));
                yield break;
            }

            if (TryAnalyzeSourceMethod(methodSymbol, operation, siteKind, out var sourceSites))
            {
                foreach (var site in sourceSites) yield return site;

                yield break;
            }

            if (TryClassifySymbolCapabilities(methodSymbol, out var capabilities))
            {
                if (capabilities != SharpProofCapability.None)
                    yield return CapabilitySiteData.Proven(
                        capabilities,
                        operation,
                        siteKind,
                        displaySymbol?.ToDisplayString(CapabilitySymbolDisplayFormat) ??
                        methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat));

                yield break;
            }

            if (ShouldTreatMetadataSymbolAsUnknown(methodSymbol))
                yield return CapabilitySiteData.Unknown(
                    operation,
                    siteKind,
                    SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable,
                    displaySymbol?.ToDisplayString(CapabilitySymbolDisplayFormat) ??
                    methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat));
        }

        private bool TryAnalyzeSourceMethod(
            IMethodSymbol methodSymbol,
            IOperation operation,
            string siteKind,
            out ImmutableArray<CapabilitySiteData> sites)
        {
            sites = ImmutableArray<CapabilitySiteData>.Empty;
            var sourceMethod = ResolveSourceImplementation(methodSymbol.OriginalDefinition);
            if (!IsSourceMethod(sourceMethod)) return false;

            if (!TryResolveSourceDeclaration(sourceMethod, out var declaration, out var semanticModel))
            {
                sites = ImmutableArray.Create(
                    CapabilitySiteData.Unknown(
                        operation,
                        siteKind,
                        SymbolicCapabilityUnknownReason.ExternalSourceBoundary,
                        methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat)));
                return true;
            }

            var calleeSummary = Analyze(declaration, semanticModel);
            var builder = ImmutableArray.CreateBuilder<CapabilitySiteData>();
            if (calleeSummary.Capabilities != SharpProofCapability.None)
                builder.Add(CapabilitySiteData.Proven(
                    calleeSummary.Capabilities,
                    operation,
                    siteKind,
                    methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat),
                    true));

            if (calleeSummary.UnknownReasons.Length != 0)
                builder.Add(CapabilitySiteData.Unknown(
                    operation,
                    siteKind,
                    calleeSummary.UnknownReasons[0],
                    methodSymbol.ToDisplayString(CapabilitySymbolDisplayFormat),
                    true));

            sites = builder.ToImmutable();
            return true;
        }

        private static IMethodSymbol ResolveSourceImplementation(IMethodSymbol methodSymbol)
        {
            return methodSymbol.PartialImplementationPart ??
                   methodSymbol.PartialDefinitionPart?.PartialImplementationPart ??
                   methodSymbol;
        }

        private bool TryResolveSourceDeclaration(
            IMethodSymbol methodSymbol,
            out SyntaxNode declaration,
            out SemanticModel semanticModel)
        {
            declaration = null!;
            semanticModel = null!;
            SyntaxNode? fallbackDeclaration = null;
            SemanticModel? fallbackSemanticModel = null;

            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(_cancellationToken);
                if (!IsMethodLikeDeclaration(syntax)) continue;

                var candidateSemanticModel = _compilation.GetSemanticModel(syntax.SyntaxTree);
                if (HasMethodBody(syntax))
                {
                    declaration = syntax;
                    semanticModel = candidateSemanticModel;
                    return true;
                }

                fallbackDeclaration ??= syntax;
                fallbackSemanticModel ??= candidateSemanticModel;
            }

            if (fallbackDeclaration != null &&
                fallbackSemanticModel != null)
            {
                declaration = fallbackDeclaration;
                semanticModel = fallbackSemanticModel;
                return true;
            }

            return false;
        }

        private static bool IsVisibleOperation(IOperation operation, SyntaxNode declaration)
        {
            for (var node = operation.Syntax; node != null && node != declaration; node = node.Parent)
                if (CSharpSyntaxFacts.IsNestedCallableBoundary(node))
                    return false;

            return true;
        }

        private static IMethodSymbol? TryGetMethodSymbol(
            SyntaxNode declaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbol = TryGetDeclaredSymbol(declaration, semanticModel, cancellationToken);
            return symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        }

        private static bool IsSourceMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Locations.Any(static location => location.IsInSource);
        }

        private static bool TryClassifySymbolCapabilities(ISymbol symbol, out SharpProofCapability capabilities)
        {
            capabilities = SharpProofCapability.None;
            var originalSymbol = symbol.OriginalDefinition;
            if (IsNativeInteropSymbol(originalSymbol)) capabilities |= SharpProofCapability.NativeInterop;

            var namespaceName = originalSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var typeName = originalSymbol.ContainingType?.OriginalDefinition.ToDisplayString() ?? string.Empty;
            var memberName = originalSymbol.Name;
            capabilities |= ClassifyKnownSymbolFamily(namespaceName, typeName, memberName, originalSymbol);
            capabilities = NormalizeCapabilities(capabilities);
            return capabilities != SharpProofCapability.None ||
                   IsKnownCapabilityNeutralSymbol(namespaceName, typeName, memberName);
        }

        private static bool ShouldTreatMetadataSymbolAsUnknown(ISymbol symbol)
        {
            var originalSymbol = symbol.OriginalDefinition;
            if (originalSymbol.Locations.Any(static location => location.IsInSource)) return false;

            var namespaceName = originalSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(namespaceName)) return true;

            return namespaceName.StartsWith("System", StringComparison.Ordinal) ||
                   namespaceName.StartsWith("Microsoft", StringComparison.Ordinal);
        }

        private static SharpProofCapability ClassifyKnownSymbolFamily(
            string namespaceName,
            string typeName,
            string memberName,
            ISymbol symbol)
        {
            if (typeName == "System.Console") return SharpProofCapability.Console;

            if (typeName == "System.Environment" ||
                typeName == "System.AppContext")
                return IsClockMember(memberName)
                    ? SharpProofCapability.Clock
                    : SharpProofCapability.Environment;

            if (typeName == "System.Guid" &&
                string.Equals(memberName, "NewGuid", StringComparison.Ordinal))
                return SharpProofCapability.Randomness;

            if (typeName == "System.DateTime" ||
                typeName == "System.DateTimeOffset" ||
                typeName == "System.Diagnostics.Stopwatch")
                return IsClockMember(memberName) ? SharpProofCapability.Clock : SharpProofCapability.None;

            if (typeName == "System.Random" ||
                typeName == "System.Security.Cryptography.RandomNumberGenerator")
                return SharpProofCapability.Randomness;

            if (typeName.StartsWith("System.Net.", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Net", StringComparison.Ordinal) ||
                namespaceName.StartsWith("System.Net", StringComparison.Ordinal))
                return SharpProofCapability.Network;

            if (typeName == "System.Diagnostics.Process" ||
                typeName == "System.Diagnostics.ProcessStartInfo")
                return SharpProofCapability.Process;

            if (typeName == "Microsoft.Win32.Registry" ||
                typeName == "Microsoft.Win32.RegistryKey")
                return SharpProofCapability.Registry;

            if (namespaceName.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                typeName == "System.Type" ||
                typeName == "System.Activator" ||
                typeName == "System.Delegate")
                return ClassifyReflectionCapability(typeName, memberName, symbol);

            if (namespaceName.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Runtime.Loader.AssemblyLoadContext", StringComparison.Ordinal))
                return SharpProofCapability.NativeInterop;

            if (typeName == "System.Threading.Monitor" ||
                typeName == "System.Threading.Mutex" ||
                typeName == "System.Threading.Semaphore" ||
                typeName == "System.Threading.SemaphoreSlim" ||
                typeName == "System.Threading.Interlocked" ||
                typeName == "System.Threading.EventWaitHandle" ||
                typeName == "System.Threading.AutoResetEvent" ||
                typeName == "System.Threading.ManualResetEvent" ||
                typeName == "System.Threading.ManualResetEventSlim")
                return SharpProofCapability.Synchronization;

            return ClassifyIoCapability(typeName, memberName);
        }

        private static SharpProofCapability ClassifyReflectionCapability(
            string typeName,
            string memberName,
            ISymbol symbol)
        {
            if (typeName == "System.Delegate" &&
                string.Equals(memberName, "DynamicInvoke", StringComparison.Ordinal))
                return SharpProofCapability.Reflection;

            if (typeName == "System.Type" &&
                (string.Equals(memberName, "GetType", StringComparison.Ordinal) ||
                 string.Equals(memberName, "GetTypeFromHandle", StringComparison.Ordinal)))
                return SharpProofCapability.Reflection;

            return symbol.ContainingNamespace?.ToDisplayString()
                       .StartsWith("System.Reflection", StringComparison.Ordinal) == true ||
                   typeName == "System.Activator"
                ? SharpProofCapability.Reflection
                : SharpProofCapability.None;
        }

        private static SharpProofCapability ClassifyIoCapability(string typeName, string memberName)
        {
            if (typeName == "System.IO.Path") return SharpProofCapability.None;

            if (typeName == "System.IO.File" ||
                typeName == "System.IO.FileInfo" ||
                typeName == "System.IO.Directory" ||
                typeName == "System.IO.DirectoryInfo" ||
                typeName == "System.IO.DriveInfo" ||
                typeName == "System.IO.FileSystemWatcher" ||
                typeName == "System.IO.FileStream")
                return ClassifyFileLikeMember(memberName);

            if (typeName.StartsWith("System.IO.Stream", StringComparison.Ordinal) ||
                typeName == "System.IO.StreamReader" ||
                typeName == "System.IO.StreamWriter" ||
                typeName == "System.IO.BinaryReader" ||
                typeName == "System.IO.BinaryWriter" ||
                typeName == "System.IO.TextReader" ||
                typeName == "System.IO.TextWriter" ||
                typeName.StartsWith("System.IO.Pipes.", StringComparison.Ordinal))
                return ClassifyGenericIoMember(memberName);

            return SharpProofCapability.None;
        }

        private static SharpProofCapability ClassifyFileLikeMember(string memberName)
        {
            if (ContainsAny(memberName, "Read", "OpenRead", "ReadAll", "ReadLines", "Exists", "Enumerate", "Get",
                    "Length", "AvailableFreeSpace", "TotalSize")) return SharpProofCapability.FileRead;

            if (ContainsAny(memberName, "Write", "Append", "Create", "Delete", "Move", "Set", "Replace", "Copy",
                    "Encrypt", "Decrypt")) return SharpProofCapability.FileWrite;

            if (ContainsAny(memberName, "Open", "CreateText"))
                return SharpProofCapability.FileRead | SharpProofCapability.FileWrite;

            return SharpProofCapability.None;
        }

        private static SharpProofCapability ClassifyGenericIoMember(string memberName)
        {
            if (ContainsAny(memberName, "Read", "CopyTo")) return SharpProofCapability.IO;

            if (ContainsAny(memberName, "Write", "Flush", "SetLength")) return SharpProofCapability.IO;

            return SharpProofCapability.None;
        }

        private static bool IsClockMember(string memberName)
        {
            return string.Equals(memberName, "Now", StringComparison.Ordinal) ||
                   string.Equals(memberName, "UtcNow", StringComparison.Ordinal) ||
                   string.Equals(memberName, "Today", StringComparison.Ordinal) ||
                   string.Equals(memberName, "TickCount", StringComparison.Ordinal) ||
                   string.Equals(memberName, "TickCount64", StringComparison.Ordinal) ||
                   string.Equals(memberName, "GetTimestamp", StringComparison.Ordinal);
        }

        private static bool IsKnownCapabilityNeutralSymbol(string namespaceName, string typeName, string memberName)
        {
            if (namespaceName.StartsWith("System", StringComparison.Ordinal))
            {
                if (typeName == "System.IO.Path") return true;

                if (typeName == "System.Math" ||
                    typeName == "System.String" ||
                    typeName.StartsWith("System.MemoryExtensions", StringComparison.Ordinal) ||
                    typeName.StartsWith("System.Convert", StringComparison.Ordinal))
                    return true;
            }

            return string.Equals(memberName, "ToString", StringComparison.Ordinal);
        }

        private static bool IsNativeInteropSymbol(ISymbol symbol)
        {
            if (symbol is IMethodSymbol methodSymbol)
                foreach (var attribute in methodSymbol.GetAttributes())
                {
                    var attributeName = attribute.AttributeClass?.Name;
                    if (string.Equals(attributeName, "DllImportAttribute", StringComparison.Ordinal) ||
                        string.Equals(attributeName, "LibraryImportAttribute", StringComparison.Ordinal))
                        return true;
                }

            return false;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    private sealed class ResolvedCapabilityTarget
    {
        public ResolvedCapabilityTarget(
            SyntaxNode declaration,
            SemanticModel semanticModel,
            string methodName,
            string methodDisplayName,
            string declarationKind)
        {
            Declaration = declaration;
            SemanticModel = semanticModel;
            MethodName = methodName;
            MethodDisplayName = methodDisplayName;
            DeclarationKind = declarationKind;
        }

        public SyntaxNode Declaration { get; }

        public SemanticModel SemanticModel { get; }

        public string MethodName { get; }

        public string MethodDisplayName { get; }

        public string DeclarationKind { get; }
    }

    private sealed class CapabilitySummary
    {
        public CapabilitySummary(
            SharpProofCapability capabilities,
            ImmutableArray<CapabilitySiteData> sites,
            ImmutableArray<SymbolicCapabilityUnknownReason> unknownReasons)
        {
            Capabilities = capabilities;
            Sites = sites;
            UnknownReasons = unknownReasons;
        }

        public SharpProofCapability Capabilities { get; }

        public ImmutableArray<CapabilitySiteData> Sites { get; }

        public ImmutableArray<SymbolicCapabilityUnknownReason> UnknownReasons { get; }

        public static CapabilitySummary FromSites(
            IReadOnlyList<CapabilitySiteData> sites,
            IReadOnlyCollection<SymbolicCapabilityUnknownReason> unknownReasons)
        {
            var capabilities = NormalizeCapabilities(
                sites.Where(static site => !site.IsUnknown)
                    .Aggregate(SharpProofCapability.None, static (current, site) => current | site.Capabilities));
            var distinctSites = sites
                .GroupBy(static site => site.Identity, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToImmutableArray();
            return new CapabilitySummary(
                capabilities,
                distinctSites,
                unknownReasons.OrderBy(static reason => reason.ToString(), StringComparer.Ordinal).ToImmutableArray());
        }

        public static CapabilitySummary Unknown(SymbolicCapabilityUnknownReason unknownReason)
        {
            return new CapabilitySummary(
                SharpProofCapability.None,
                ImmutableArray<CapabilitySiteData>.Empty,
                ImmutableArray.Create(unknownReason));
        }
    }

    private sealed class CapabilitySiteData
    {
        public CapabilitySiteData(
            SharpProofCapability capabilities,
            IOperation operation,
            string siteKind,
            string symbolDisplayName,
            bool isTransitive,
            bool isUnknown,
            SymbolicCapabilityUnknownReason unknownReason)
        {
            Capabilities = NormalizeCapabilities(capabilities);
            SiteKind = siteKind;
            OperationKind = operation.Kind.ToString();
            OperationText = operation.Syntax.ToString();
            SymbolDisplayName = symbolDisplayName;
            IsTransitive = isTransitive;
            IsUnknown = isUnknown;
            UnknownReason = unknownReason;
            SpanStart = operation.Syntax.SpanStart;
            SpanLength = operation.Syntax.Span.Length;
            Identity =
                operation.Syntax.SpanStart + "|" +
                operation.Syntax.Span.Length + "|" +
                siteKind + "|" +
                Capabilities + "|" +
                unknownReason + "|" +
                symbolDisplayName;
        }

        public SharpProofCapability Capabilities { get; }

        public string SiteKind { get; }

        public string OperationKind { get; }

        public string OperationText { get; }

        public string SymbolDisplayName { get; }

        public bool IsTransitive { get; }

        public bool IsUnknown { get; }

        public SymbolicCapabilityUnknownReason UnknownReason { get; }

        public int SpanStart { get; }

        public int SpanLength { get; }

        public string Identity { get; }

        public static CapabilitySiteData Proven(
            SharpProofCapability capabilities,
            IOperation operation,
            string siteKind,
            string symbolDisplayName,
            bool isTransitive = false)
        {
            return new CapabilitySiteData(
                capabilities,
                operation,
                siteKind,
                symbolDisplayName,
                isTransitive,
                false,
                SymbolicCapabilityUnknownReason.None);
        }

        public static CapabilitySiteData Unknown(
            IOperation operation,
            string siteKind,
            SymbolicCapabilityUnknownReason unknownReason,
            string symbolDisplayName,
            bool isTransitive = false)
        {
            return new CapabilitySiteData(
                SharpProofCapability.None,
                operation,
                siteKind,
                symbolDisplayName,
                isTransitive,
                true,
                unknownReason);
        }
    }
}
