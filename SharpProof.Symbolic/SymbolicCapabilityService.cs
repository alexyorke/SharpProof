using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProofCapability = SharpProof.Attributes.SharpProofCapability;

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
        return SymbolicSourceInputDispatcher.Execute(
            source,
            target,
            options,
            "SharpProof.Symbolic.Capabilities.cs",
            "SharpProof.Symbolic.Capabilities",
            "Capability source kind is not supported.",
            QuerySyntaxTree,
            QueryNode,
            cancellationToken);
    }

    private SymbolicCapabilityResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var resolvedTarget = SymbolicMethodLikeTargetResolver.Resolve(
            syntaxTree,
            semanticModel,
            target,
            "Capability queries support point, position, line, or node targets only.",
            IsMethodLikeDeclaration,
            ResolveMethodLikeDeclaration,
            cancellationToken);
        return ExecuteAnalysis(resolvedTarget, compilation, cancellationToken);
    }

    private SymbolicCapabilityResult QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        if (target.Kind != SymbolicQueryTargetKind.Node)
            throw new NotSupportedException("Capability node queries require a node target.");

        var resolvedTarget = SymbolicMethodLikeTargetResolver.ResolveNode(
            node,
            semanticModel,
            IsMethodLikeDeclaration,
            ResolveMethodLikeDeclaration,
            cancellationToken);
        return ExecuteAnalysis(resolvedTarget, semanticModel.Compilation, cancellationToken);
    }

    private static SymbolicCapabilityResult ExecuteAnalysis(
        ResolvedCapabilityTarget target,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var summary = new AnalysisSession(compilation, cancellationToken)
            .Analyze(target.Declaration, target.SemanticModel);
        return CreateResult(target, summary, cancellationToken);
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
                    SymbolicCapabilityFacts.Format(site.Capabilities),
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
            SymbolicCapabilityFacts.Format(summary.Capabilities),
            sites,
            summary.UnknownReasons.OrderBy(static reason => reason.ToString(), StringComparer.Ordinal).ToArray());
    }

    private static ResolvedCapabilityTarget ResolveMethodLikeDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = TryGetDeclaredSymbol(declaration, semanticModel, cancellationToken);
        var methodName = symbol?.Name ?? string.Empty;
        var methodDisplayName = symbol?.ToDisplayString() ?? methodName;
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
            return SymbolicMethodSourceResolver.TryResolve(
                _compilation,
                methodSymbol,
                IsMethodLikeDeclaration,
                true,
                _cancellationToken,
                out declaration,
                out _,
                out semanticModel);
        }

        private static bool IsVisibleOperation(IOperation operation, SyntaxNode declaration)
        {
            for (var node = operation.Syntax; node != null && node != declaration; node = node.Parent)
                if (CSharpSyntaxFacts.IsNestedLocalCallableBoundary(node))
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
            return SymbolicMethodSourceResolver.IsBackedBySource(methodSymbol);
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
            capabilities = SymbolicCapabilityFacts.Normalize(capabilities);
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

            if (typeName == "System.Diagnostics.Stopwatch")
                return IsStopwatchClockMember(memberName)
                    ? SharpProofCapability.Clock
                    : SharpProofCapability.None;

            if (typeName == "System.DateTime" ||
                typeName == "System.DateTimeOffset")
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
            if (FileReadWriteMembers.Contains(memberName))
                return SharpProofCapability.FileRead | SharpProofCapability.FileWrite;

            if (FileReadMembers.Contains(memberName)) return SharpProofCapability.FileRead;

            if (FileWriteMembers.Contains(memberName)) return SharpProofCapability.FileWrite;

            return SharpProofCapability.None;
        }

        private static SharpProofCapability ClassifyGenericIoMember(string memberName)
        {
            return GenericIoMembers.Contains(memberName)
                ? SharpProofCapability.IO
                : SharpProofCapability.None;
        }

        private static readonly ImmutableHashSet<string> FileReadMembers =
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "ReadAllBytes",
                "ReadAllBytesAsync",
                "ReadAllLines",
                "ReadAllLinesAsync",
                "ReadAllText",
                "ReadAllTextAsync",
                "ReadLines",
                "OpenRead",
                "Exists",
                "EnumerateDirectories",
                "EnumerateFiles",
                "EnumerateFileSystemEntries",
                "GetAttributes",
                "GetCreationTime",
                "GetCreationTimeUtc",
                "GetCurrentDirectory",
                "GetDirectories",
                "GetDirectoryRoot",
                "GetFiles",
                "GetFileSystemEntries",
                "GetLastAccessTime",
                "GetLastAccessTimeUtc",
                "GetLastWriteTime",
                "GetLastWriteTimeUtc",
                "GetLogicalDrives",
                "GetParent",
                "Length",
                "AvailableFreeSpace",
                "TotalFreeSpace",
                "TotalSize",
                "CreationTime",
                "CreationTimeUtc",
                "LastAccessTime",
                "LastAccessTimeUtc",
                "LastWriteTime",
                "LastWriteTimeUtc",
                "Refresh");

        private static readonly ImmutableHashSet<string> FileWriteMembers =
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "WriteAllBytes",
                "WriteAllBytesAsync",
                "WriteAllLines",
                "WriteAllLinesAsync",
                "WriteAllText",
                "WriteAllTextAsync",
                "AppendAllLines",
                "AppendAllLinesAsync",
                "AppendAllText",
                "AppendAllTextAsync",
                "AppendText",
                "Create",
                "CreateDirectory",
                "CreateSubdirectory",
                "CreateText",
                "Delete",
                "Move",
                "MoveTo",
                "SetAttributes",
                "SetCreationTime",
                "SetCreationTimeUtc",
                "SetCurrentDirectory",
                "SetLastAccessTime",
                "SetLastAccessTimeUtc",
                "SetLastWriteTime",
                "SetLastWriteTimeUtc",
                "Replace",
                "Copy",
                "CopyTo",
                "Encrypt",
                "Decrypt");

        private static readonly ImmutableHashSet<string> FileReadWriteMembers =
            ImmutableHashSet.Create(StringComparer.Ordinal, "Open", "OpenHandle", "OpenText");

        private static readonly ImmutableHashSet<string> GenericIoMembers =
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "Read",
                "ReadAsync",
                "ReadByte",
                "ReadBlock",
                "ReadBlockAsync",
                "ReadLine",
                "ReadLineAsync",
                "ReadToEnd",
                "ReadToEndAsync",
                "BeginRead",
                "EndRead",
                "CopyTo",
                "CopyToAsync",
                "Write",
                "WriteAsync",
                "WriteByte",
                "WriteLine",
                "WriteLineAsync",
                "BeginWrite",
                "EndWrite",
                "Flush",
                "FlushAsync",
                "SetLength");

        private static bool IsClockMember(string memberName)
        {
            return string.Equals(memberName, "Now", StringComparison.Ordinal) ||
                   string.Equals(memberName, "UtcNow", StringComparison.Ordinal) ||
                   string.Equals(memberName, "Today", StringComparison.Ordinal) ||
                   string.Equals(memberName, "TickCount", StringComparison.Ordinal) ||
                   string.Equals(memberName, "TickCount64", StringComparison.Ordinal) ||
                   string.Equals(memberName, "GetTimestamp", StringComparison.Ordinal);
        }

        private static bool IsStopwatchClockMember(string memberName)
        {
            return memberName is
                "Elapsed" or
                "ElapsedMilliseconds" or
                "ElapsedTicks" or
                "Frequency" or
                "GetElapsedTime" or
                "GetTimestamp" or
                "IsHighResolution" or
                "QueryPerformanceCounter" or
                "QueryPerformanceFrequency" or
                "Restart" or
                "Start" or
                "StartNew" or
                "Stop";
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

            return typeName == "System.Object" &&
                   string.Equals(memberName, "ToString", StringComparison.Ordinal);
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
            var capabilities = SymbolicCapabilityFacts.Normalize(
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
            Capabilities = SymbolicCapabilityFacts.Normalize(capabilities);
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
