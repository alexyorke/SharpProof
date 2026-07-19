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
        SymbolicQueryContext request,
        CancellationToken cancellationToken) =>
        SymbolicMethodLikeQueryDispatcher.Execute(
            request,
            SymbolicSourceCompilationKind.Capabilities,
            "Capability source kind is not supported.",
            "Capability queries support point, position, line, or node targets only.",
            "Capability node queries require a node target.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(node),
            ResolveMethodLikeDeclaration,
            ExecuteAnalysis,
            cancellationToken);

    private static SymbolicCapabilityResult ExecuteAnalysis(
        ResolvedCapabilityTarget target,
        Compilation compilation,
        CancellationToken cancellationToken) =>
        CreateResult(
            target,
            new AnalysisSession(compilation, cancellationToken).Analyze(target.Declaration, target.SemanticModel),
            cancellationToken);

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
        var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        var methodName = symbol?.Name ?? string.Empty;
        var methodDisplayName = symbol?.ToDisplayString() ?? methodName;
        return new ResolvedCapabilityTarget(
            declaration,
            semanticModel,
            methodName,
            methodDisplayName,
            declaration.GetType().Name);
    }

    private sealed class AnalysisSession(Compilation compilation, CancellationToken cancellationToken)
    {
        private readonly HashSet<IMethodSymbol> _activeMethods =
            new(SymbolEqualityComparer.Default);

        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly Compilation _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));

        private readonly Dictionary<IMethodSymbol, CapabilitySummary> _methodCache =
            new(SymbolEqualityComparer.Default);

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

                    foreach (var site in AnalyzeOperation(operation))
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

        private IEnumerable<CapabilitySiteData> AnalyzeOperation(IOperation operation)
        {
            return operation switch
            {
                ILockOperation => new[]
                {
                    CapabilitySiteData.Proven(
                        SharpProofCapability.Synchronization, operation, "lock", string.Empty)
                },
                IDynamicMemberReferenceOperation { Parent: IDynamicInvocationOperation or IDynamicIndexerAccessOperation } =>
                    Array.Empty<CapabilitySiteData>(),
                IDynamicInvocationOperation or IDynamicIndexerAccessOperation or
                    IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation => new[]
                    {
                        CapabilitySiteData.Unknown(
                            operation, "dynamic", SymbolicCapabilityUnknownReason.DynamicDispatch, string.Empty)
                    },
                IInvocationOperation invocation => AnalyzeSymbolUsage(
                    invocation.TargetMethod, invocation, "invocation", invocation.TargetMethod),
                IObjectCreationOperation creation => AnalyzeSymbolUsage(
                    creation.Constructor,
                    creation,
                    "object_creation",
                    creation.Constructor ?? (ISymbol?)creation.Type),
                IPropertyReferenceOperation property => AnalyzePropertyUsage(property),
                IFieldReferenceOperation field => AnalyzeFieldUsage(field.Field, field),
                _ => Array.Empty<CapabilitySiteData>()
            };
        }

        private IEnumerable<CapabilitySiteData> AnalyzePropertyUsage(
            IPropertyReferenceOperation propertyReferenceOperation)
        {
            var accessor = propertyReferenceOperation.Property.GetMethod ??
                           propertyReferenceOperation.Property.SetMethod;
            return AnalyzeSymbolUsage(
                accessor, propertyReferenceOperation, "property_access", propertyReferenceOperation.Property);
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

        private static IMethodSymbol ResolveSourceImplementation(IMethodSymbol methodSymbol) =>
            methodSymbol.PartialImplementationPart ??
            methodSymbol.PartialDefinitionPart?.PartialImplementationPart ?? methodSymbol;

        private bool TryResolveSourceDeclaration(
            IMethodSymbol methodSymbol,
            out SyntaxNode declaration,
            out SemanticModel semanticModel)
        {
            return SymbolicMethodSourceResolver.TryResolve(
                _compilation,
                methodSymbol,
                static node => SymbolicMethodLikeDeclaration.IsSupported(node),
                true,
                _cancellationToken,
                out declaration,
                out _,
                out semanticModel);
        }

        private static bool IsVisibleOperation(IOperation operation, SyntaxNode declaration) =>
            !operation.Syntax.AncestorsAndSelf()
                .TakeWhile(node => node != declaration)
                .Any(CSharpSyntaxFacts.IsNestedLocalCallableBoundary);

        private static IMethodSymbol? TryGetMethodSymbol(
            SyntaxNode declaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken) =>
            SymbolicMethodLikeDeclaration.GetMethodSymbol(declaration, semanticModel, cancellationToken);

        private static bool IsSourceMethod(IMethodSymbol methodSymbol) =>
            SymbolicMethodSourceResolver.IsBackedBySource(methodSymbol);

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
            ISymbol symbol) => typeName switch
        {
            "System.Console" => SharpProofCapability.Console,
            "System.Environment" or "System.AppContext" => IsClockMember(memberName)
                ? SharpProofCapability.Clock
                : SharpProofCapability.Environment,
            "System.Guid" when memberName == "NewGuid" => SharpProofCapability.Randomness,
            "System.Diagnostics.Stopwatch" when IsStopwatchClockMember(memberName) => SharpProofCapability.Clock,
            "System.DateTime" or "System.DateTimeOffset" when IsClockMember(memberName) =>
                SharpProofCapability.Clock,
            "System.Random" or "System.Security.Cryptography.RandomNumberGenerator" =>
                SharpProofCapability.Randomness,
            "System.Diagnostics.Process" or "System.Diagnostics.ProcessStartInfo" => SharpProofCapability.Process,
            "Microsoft.Win32.Registry" or "Microsoft.Win32.RegistryKey" => SharpProofCapability.Registry,
            _ when namespaceName.StartsWith("System.Net", StringComparison.Ordinal) => SharpProofCapability.Network,
            _ when IsReflectionType(namespaceName, typeName) =>
                ClassifyReflectionCapability(typeName, memberName, symbol),
            _ when namespaceName.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal) ||
                   typeName.StartsWith("System.Runtime.Loader.AssemblyLoadContext", StringComparison.Ordinal) =>
                SharpProofCapability.NativeInterop,
            _ when IsSynchronizationType(typeName) => SharpProofCapability.Synchronization,
            _ => ClassifyIoCapability(typeName, memberName)
        };

        private static bool IsReflectionType(string namespaceName, string typeName) =>
            namespaceName.StartsWith("System.Reflection", StringComparison.Ordinal) ||
            typeName is "System.Type" or "System.Activator" or "System.Delegate";

        private static bool IsSynchronizationType(string typeName) => typeName is
            "System.Threading.Monitor" or "System.Threading.Mutex" or
            "System.Threading.Semaphore" or "System.Threading.SemaphoreSlim" or
            "System.Threading.Interlocked" or "System.Threading.EventWaitHandle" or
            "System.Threading.AutoResetEvent" or "System.Threading.ManualResetEvent" or
            "System.Threading.ManualResetEventSlim";

        private static SharpProofCapability ClassifyReflectionCapability(
            string typeName,
            string memberName,
            ISymbol symbol) => (typeName, memberName) switch
        {
            ("System.Delegate", "DynamicInvoke") => SharpProofCapability.Reflection,
            ("System.Type", "GetType" or "GetTypeFromHandle") => SharpProofCapability.Reflection,
            _ when typeName == "System.Activator" || symbol.ContainingNamespace?.ToDisplayString()
                .StartsWith("System.Reflection", StringComparison.Ordinal) == true =>
                SharpProofCapability.Reflection,
            _ => SharpProofCapability.None
        };

        private static SharpProofCapability ClassifyIoCapability(string typeName, string memberName) =>
            typeName == "System.IO.Path"
                ? SharpProofCapability.None
                : IsFileLikeType(typeName)
                    ? ClassifyFileLikeMember(memberName)
                    : IsStreamLikeType(typeName) && IsIoMember(memberName)
                        ? SharpProofCapability.IO
                        : SharpProofCapability.None;

        private static bool IsFileLikeType(string typeName) => typeName is
            "System.IO.File" or "System.IO.FileInfo" or "System.IO.Directory" or "System.IO.DirectoryInfo" or
            "System.IO.DriveInfo" or "System.IO.FileSystemWatcher" or "System.IO.FileStream";

        private static bool IsStreamLikeType(string typeName) =>
            typeName.StartsWith("System.IO.Stream", StringComparison.Ordinal) ||
            typeName is "System.IO.BinaryReader" or "System.IO.BinaryWriter" or
                "System.IO.TextReader" or "System.IO.TextWriter" ||
            typeName.StartsWith("System.IO.Pipes.", StringComparison.Ordinal);

        private static SharpProofCapability ClassifyFileLikeMember(string memberName)
        {
            if (memberName is "Open" or "OpenHandle" or "OpenText")
                return SharpProofCapability.FileRead | SharpProofCapability.FileWrite;

            if (IsFileMetadataRead(memberName) ||
                memberName.StartsWith("Read", StringComparison.Ordinal) ||
                memberName.StartsWith("Enumerate", StringComparison.Ordinal) ||
                memberName.StartsWith("Get", StringComparison.Ordinal) ||
                memberName is "OpenRead" or "Exists" or "Refresh")
                return SharpProofCapability.FileRead;

            if (memberName.StartsWith("Write", StringComparison.Ordinal) ||
                memberName.StartsWith("Append", StringComparison.Ordinal) ||
                memberName.StartsWith("Create", StringComparison.Ordinal) ||
                memberName.StartsWith("Set", StringComparison.Ordinal) ||
                memberName is "Delete" or "Move" or "MoveTo" or "Replace" or
                    "Copy" or "CopyTo" or "Encrypt" or "Decrypt")
                return SharpProofCapability.FileWrite;

            return SharpProofCapability.None;
        }

        private static bool IsFileMetadataRead(string memberName) => memberName is
            "Length" or "AvailableFreeSpace" or "TotalFreeSpace" or "TotalSize" or
            "CreationTime" or "CreationTimeUtc" or "LastAccessTime" or "LastAccessTimeUtc" or
            "LastWriteTime" or "LastWriteTimeUtc";

        private static bool IsIoMember(string memberName) =>
            memberName.StartsWith("Read", StringComparison.Ordinal) ||
            memberName.StartsWith("Write", StringComparison.Ordinal) ||
            memberName.StartsWith("Flush", StringComparison.Ordinal) ||
            memberName.StartsWith("BeginRead", StringComparison.Ordinal) ||
            memberName.StartsWith("EndRead", StringComparison.Ordinal) ||
            memberName.StartsWith("BeginWrite", StringComparison.Ordinal) ||
            memberName.StartsWith("EndWrite", StringComparison.Ordinal) ||
            memberName.StartsWith("CopyTo", StringComparison.Ordinal) ||
            string.Equals(memberName, "SetLength", StringComparison.Ordinal);

        private static bool IsClockMember(string memberName) => memberName is
            "Now" or "UtcNow" or "Today" or "TickCount" or "TickCount64" or "GetTimestamp";

        private static bool IsStopwatchClockMember(string memberName) => memberName is
            "Elapsed" or "ElapsedMilliseconds" or "ElapsedTicks" or "Frequency" or
            "GetElapsedTime" or "GetTimestamp" or "IsHighResolution" or
            "QueryPerformanceCounter" or "QueryPerformanceFrequency" or
            "Restart" or "Start" or "StartNew" or "Stop";

        private static bool IsKnownCapabilityNeutralSymbol(
            string namespaceName,
            string typeName,
            string memberName) =>
            namespaceName.StartsWith("System", StringComparison.Ordinal) &&
            (typeName is "System.IO.Path" or "System.Math" or "System.String" ||
             typeName.StartsWith("System.MemoryExtensions", StringComparison.Ordinal) ||
             typeName.StartsWith("System.Convert", StringComparison.Ordinal)) ||
            typeName == "System.Object" && memberName == "ToString";

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

    }

    private sealed class ResolvedCapabilityTarget(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        string methodName,
        string methodDisplayName,
        string declarationKind)
    {
        public SyntaxNode Declaration { get; } = declaration;

        public SemanticModel SemanticModel { get; } = semanticModel;

        public string MethodName { get; } = methodName;

        public string MethodDisplayName { get; } = methodDisplayName;

        public string DeclarationKind { get; } = declarationKind;
    }

    private sealed record CapabilitySummary(
        SharpProofCapability Capabilities,
        ImmutableArray<CapabilitySiteData> Sites,
        ImmutableArray<SymbolicCapabilityUnknownReason> UnknownReasons)
    {
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

    private sealed class CapabilitySiteData(
        SharpProofCapability capabilities,
        IOperation operation,
        string siteKind,
        string symbolDisplayName,
        bool isTransitive,
        bool isUnknown,
        SymbolicCapabilityUnknownReason unknownReason)
    {
        public SharpProofCapability Capabilities { get; } = SymbolicCapabilityFacts.Normalize(capabilities);

        public string SiteKind { get; } = siteKind;

        public string OperationKind { get; } = operation.Kind.ToString();

        public string OperationText { get; } = operation.Syntax.ToString();

        public string SymbolDisplayName { get; } = symbolDisplayName;

        public bool IsTransitive { get; } = isTransitive;

        public bool IsUnknown { get; } = isUnknown;

        public SymbolicCapabilityUnknownReason UnknownReason { get; } = unknownReason;

        public int SpanStart { get; } = operation.Syntax.SpanStart;

        public int SpanLength { get; } = operation.Syntax.Span.Length;

        public string Identity { get; } = operation.Syntax.SpanStart + "|" + operation.Syntax.Span.Length + "|" +
            siteKind + "|" + SymbolicCapabilityFacts.Normalize(capabilities) + "|" + unknownReason + "|" +
            symbolDisplayName;

        public static CapabilitySiteData Proven(
            SharpProofCapability capabilities,
            IOperation operation,
            string siteKind,
            string symbolDisplayName,
            bool isTransitive = false)
            => new(
                capabilities,
                operation,
                siteKind,
                symbolDisplayName,
                isTransitive,
                false,
                SymbolicCapabilityUnknownReason.None);

        public static CapabilitySiteData Unknown(
            IOperation operation,
            string siteKind,
            SymbolicCapabilityUnknownReason unknownReason,
            string symbolDisplayName,
            bool isTransitive = false)
            => new(
                SharpProofCapability.None,
                operation,
                siteKind,
                symbolDisplayName,
                isTransitive,
                true,
                unknownReason);
    }
}
