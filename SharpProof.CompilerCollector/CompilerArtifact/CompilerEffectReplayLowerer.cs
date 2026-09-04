// This lowerer runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal static class CompilerEffectReplayLowerer
{
    internal static bool TryCreate(
        CSharpCompilation compilation,
        ResolvedApiSpecTable apiSpecs,
        EffectDirectWitness witness,
        WorkerSourceLocation location,
        CancellationToken cancellationToken,
        out CompilerEffectReplayArtifact? replay,
        out string witnessDetail)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        witness = ArgumentNullGuard.NotNull(witness, nameof(witness));
        apiSpecs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
        cancellationToken.ThrowIfCancellationRequested();

        replay = null;
        witnessDetail = string.Empty;
        if (!HasReplayableShape(witness) ||
            !TryCreateEvent(
                compilation,
                apiSpecs,
                witness,
                location,
                cancellationToken,
                out var @event,
                out witnessDetail))
        {
            return false;
        }

        @event.OperationIdentitySha256 =
            CompilerEffectClaimArtifactCodec.ComputeReplayOperationSha256(
                @event);
        replay = new CompilerEffectReplayArtifact
        {
            PathKind = CompilerEffectReplayPathKind.Unconditional,
            Events = [@event]
        };
        return true;
    }

    private static bool HasReplayableShape(EffectDirectWitness witness)
    {
        return witness.EventKind switch
        {
            EffectDirectEventKind.ManagedObjectAllocation or
            EffectDirectEventKind.ManagedArrayAllocation => witness is
            {
                Effects: EffectContractKind.Allocates,
                Capabilities: EffectContractCapabilityKind.None,
                ExceptionType: null
            },
            EffectDirectEventKind.ExplicitThrow => witness is
            {
                Effects: EffectContractKind.Throws,
                Capabilities: EffectContractCapabilityKind.None,
                ExceptionType: not null
            },
            EffectDirectEventKind.MonitorCall or
            EffectDirectEventKind.EmptyLock => witness is
            {
                Effects: EffectContractKind.Synchronizes,
                Capabilities:
                    EffectContractCapabilityKind.Synchronization,
                ExceptionType: null
            },
            _ => false
        };
    }

    private static bool TryCreateEvent(
        CSharpCompilation compilation,
        ResolvedApiSpecTable apiSpecs,
        EffectDirectWitness witness,
        WorkerSourceLocation location,
        CancellationToken cancellationToken,
        out CompilerEffectReplayEventArtifact @event,
        out string witnessDetail)
    {
        @event = new CompilerEffectReplayEventArtifact();
        witnessDetail = string.Empty;
        if (!TryResolveSource(
                compilation,
                witness.Origin,
                location,
                cancellationToken,
                out var treeOrdinal,
                out var treeSha256,
                out var treeSnapshotSha256,
                out var treeLineMapSha256,
                out var sourceTreeOrdinal,
                out var sourceTreePath,
                out var sourceTreeSha256,
                out var sourceLineMapSha256))
        {
            return false;
        }

        var operation = witness.Origin;
        var syntax = operation.Syntax;
        string memberIdentity;
        string? memberDocumentationId;
        string typeIdentity;
        string? typeDocumentationId;
        string[] exactExceptionTypeHierarchy = [];
        CompilerEffectReplayEventKind eventKind;
        switch (witness.EventKind, operation)
        {
            case (
                EffectDirectEventKind.ManagedObjectAllocation,
                IObjectCreationOperation
                {
                    Constructor: { } constructor,
                    Type: INamedTypeSymbol type
                } creation)
                when witness.Kind == "managed-allocation" &&
                     IsDefiniteObjectAllocation(
                         creation,
                         apiSpecs,
                         cancellationToken):
                eventKind =
                    CompilerEffectReplayEventKind.ManagedObjectAllocation;
                memberIdentity =
                    CompilerIdentityBridge.CreateSymbolDisplay(constructor);
                memberDocumentationId =
                    DocumentationCommentId.CreateDeclarationId(constructor);
                typeIdentity =
                    CompilerIdentityBridge.CreateTypeDisplay(type);
                typeDocumentationId =
                    DocumentationCommentId.CreateReferenceId(type);
                witnessDetail = PreferDocumentationId(
                    memberDocumentationId,
                    memberIdentity);
                break;
            case (
                EffectDirectEventKind.ManagedArrayAllocation,
                IArrayCreationOperation { Type: IArrayTypeSymbol type } array)
                when witness.Kind == "managed-array-allocation" &&
                     DefiniteOperationFacts.IsDirectArrayCreationComplete(
                         array):
                eventKind =
                    CompilerEffectReplayEventKind.ManagedArrayAllocation;
                memberIdentity = string.Empty;
                memberDocumentationId = null;
                typeIdentity =
                    CompilerIdentityBridge.CreateTypeDisplay(type);
                typeDocumentationId =
                    DocumentationCommentId.CreateReferenceId(type);
                witnessDetail = PreferDocumentationId(
                    typeDocumentationId,
                    typeIdentity);
                break;
            case (
                EffectDirectEventKind.ExplicitThrow,
                IThrowOperation { Exception: { } exception }) when
                witness.Kind == "explicit-throw" &&
                witness.ExceptionType is { } exactExceptionType &&
                DefiniteOperationFacts.UnwrapHarmlessValue(exception) is
                    IObjectCreationOperation
                {
                    Constructor: { } constructor,
                    Type: INamedTypeSymbol exceptionType
                } creation &&
                SymbolEqualityComparer.Default.Equals(
                    exactExceptionType,
                    exceptionType) &&
                IsExactFrameworkException(
                    compilation,
                    exceptionType) &&
                HasNonThrowingConstructorSpec(creation, apiSpecs):
                eventKind = CompilerEffectReplayEventKind.ExplicitThrow;
                memberIdentity =
                    CompilerIdentityBridge.CreateSymbolDisplay(constructor);
                memberDocumentationId =
                    DocumentationCommentId.CreateDeclarationId(constructor);
                typeIdentity =
                    CompilerIdentityBridge.CreateTypeDisplay(exceptionType);
                typeDocumentationId =
                    DocumentationCommentId.CreateReferenceId(exceptionType);
                exactExceptionTypeHierarchy =
                    CompilerExceptionTypeIdentity.EncodeHierarchy(
                        exceptionType);
                witnessDetail = PreferDocumentationId(
                    typeDocumentationId,
                    typeIdentity);
                break;
            case (
                EffectDirectEventKind.MonitorCall,
                IInvocationOperation invocation) when
                witness.Kind == "synchronization-call" &&
                IsDefiniteMonitorCall(compilation, invocation):
                eventKind = CompilerEffectReplayEventKind.MonitorCall;
                memberIdentity = CompilerIdentityBridge.CreateSymbolDisplay(
                    invocation.TargetMethod);
                memberDocumentationId =
                    DocumentationCommentId.CreateDeclarationId(
                        invocation.TargetMethod);
                typeIdentity = CompilerIdentityBridge.CreateTypeDisplay(
                    invocation.TargetMethod.ContainingType);
                typeDocumentationId =
                    DocumentationCommentId.CreateReferenceId(
                        invocation.TargetMethod.ContainingType);
                witnessDetail = PreferDocumentationId(
                    memberDocumentationId,
                    memberIdentity);
                break;
            case (
                EffectDirectEventKind.EmptyLock,
                ILockOperation @lock) when
                witness.Kind == "synchronization-lock" &&
                IsDefiniteEmptyLock(
                    @lock,
                    apiSpecs,
                    cancellationToken) &&
                compilation.GetTypeByMetadataName(
                    FrameworkTypeMetadataNames.Monitor) is { } monitorType:
                eventKind = CompilerEffectReplayEventKind.EmptyLock;
                memberIdentity = string.Empty;
                memberDocumentationId = null;
                typeIdentity =
                    CompilerIdentityBridge.CreateTypeDisplay(monitorType);
                typeDocumentationId =
                    DocumentationCommentId.CreateReferenceId(monitorType);
                witnessDetail = PreferDocumentationId(
                    typeDocumentationId,
                    typeIdentity);
                break;
            default:
                return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(typeIdentity) ||
            string.IsNullOrWhiteSpace(witnessDetail))
        {
            return false;
        }

        @event = new CompilerEffectReplayEventArtifact
        {
            Ordinal = 0,
            Kind = eventKind,
            SyntaxTreeOrdinal = treeOrdinal,
            SyntaxTreeSha256 = treeSha256,
            SyntaxTreeSnapshotSha256 = treeSnapshotSha256,
            SyntaxTreeLineMapSha256 = treeLineMapSha256,
            SyntaxStart = syntax.SpanStart,
            SyntaxLength = syntax.Span.Length,
            MemberIdentity = memberIdentity,
            MemberDocumentationId = memberDocumentationId,
            TypeIdentity = typeIdentity,
            TypeDocumentationId = typeDocumentationId,
            SpecWitnessIdentifier = null,
            ScalarOperands = [],
            ExactExceptionTypeHierarchy = exactExceptionTypeHierarchy,
            Location = location,
            SourceTreeOrdinal = sourceTreeOrdinal,
            SourceTreePath = sourceTreePath,
            SourceTreeSha256 = sourceTreeSha256,
            SourceLineMapSha256 = sourceLineMapSha256
        };
        return true;
    }

    private static string PreferDocumentationId(
        string? documentationId,
        string identity)
    {
        return string.IsNullOrWhiteSpace(documentationId)
            ? identity
            : documentationId!;
    }

    private static bool IsDefiniteObjectAllocation(
        IObjectCreationOperation creation,
        ResolvedApiSpecTable apiSpecs,
        CancellationToken cancellationToken)
    {
        return creation.Type is INamedTypeSymbol
        {
            IsReferenceType: true
        } type &&
        !EffectMethodNodeBuilder
            .HasPotentialConstructionInitialization(
                type,
                apiSpecs,
                cancellationToken) &&
        creation.Initializer == null &&
        creation.Arguments.All(static argument =>
            DefiniteOperationFacts.IsHarmlessValue(argument.Value));
    }

    private static bool HasNonThrowingConstructorSpec(
        IObjectCreationOperation creation,
        ResolvedApiSpecTable apiSpecs)
    {
        return creation.Constructor is { } constructor &&
            apiSpecs.IsNonThrowingAndTerminating(constructor);
    }

    private static bool IsExactFrameworkException(
        CSharpCompilation compilation,
        INamedTypeSymbol type)
    {
        var exceptionType = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.Exception);
        return exceptionType != null &&
            SymbolEqualityComparer.Default.Equals(
                type.ContainingAssembly,
                exceptionType.ContainingAssembly) &&
            EffectTypeFacts.IsDerivedFrom(type, exceptionType);
    }

    private static bool IsDefiniteMonitorCall(
        CSharpCompilation compilation,
        IInvocationOperation invocation)
    {
        var monitorType = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.Monitor);
        return !invocation.IsImplicit &&
            invocation.Instance == null &&
            !invocation.Arguments.IsDefaultOrEmpty &&
            invocation.Arguments.All(static argument =>
                DefiniteOperationFacts.IsHarmlessValue(argument.Value)) &&
            DefiniteOperationFacts.IsDefinitelyNonNull(
                invocation.Arguments[0].Value) &&
            invocation.TargetMethod.Name is
                "Enter" or "Exit" or "Pulse" or "PulseAll" or
                "TryEnter" or "Wait" &&
            monitorType != null &&
            SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.ContainingType.OriginalDefinition,
                monitorType.OriginalDefinition);
    }

    private static bool IsDefiniteEmptyLock(
        ILockOperation @lock,
        ResolvedApiSpecTable apiSpecs,
        CancellationToken cancellationToken)
    {
        if (@lock.Body is not IBlockOperation { Operations.Length: 0 })
        {
            return false;
        }

        var receiver = DefiniteOperationFacts.UnwrapHarmlessValue(
            @lock.LockedValue);
        return receiver switch
        {
            IObjectCreationOperation creation =>
                IsDefiniteObjectAllocation(
                    creation,
                    apiSpecs,
                    cancellationToken) &&
                HasNonThrowingConstructorSpec(creation, apiSpecs),
            IArrayCreationOperation array =>
                DefiniteOperationFacts.IsDirectArrayCreationComplete(array),
            IInstanceReferenceOperation or
            IConditionalAccessInstanceOperation or
            ITypeOfOperation => true,
            _ => receiver.ConstantValue is
            { HasValue: true, Value: not null }
        };
    }

    private static bool TryResolveSource(
        CSharpCompilation compilation,
        IOperation operation,
        WorkerSourceLocation location,
        CancellationToken cancellationToken,
        out int treeOrdinal,
        out string treeSha256,
        out string treeSnapshotSha256,
        out string treeLineMapSha256,
        out int sourceTreeOrdinal,
        out string sourceTreePath,
        out string sourceTreeSha256,
        out string sourceLineMapSha256)
    {
        cancellationToken.ThrowIfCancellationRequested();
        treeOrdinal = -1;
        treeSha256 = string.Empty;
        treeSnapshotSha256 = string.Empty;
        treeLineMapSha256 = string.Empty;
        sourceTreeOrdinal = -1;
        sourceTreePath = string.Empty;
        sourceTreeSha256 = string.Empty;
        sourceLineMapSha256 = string.Empty;

        var trees = compilation.SyntaxTrees;
        treeOrdinal = trees.IndexOf(operation.Syntax.SyntaxTree);
        if (treeOrdinal < 0)
        {
            return false;
        }

        var tree = operation.Syntax.SyntaxTree;
        var text = tree.GetText(cancellationToken);
        if (operation.Syntax.SpanStart < 0 ||
            operation.Syntax.Span.End > text.Length)
        {
            return false;
        }

        var capturedTrees = CompilerCompilationCapture.CaptureTrees(
            compilation, cancellationToken);
        var syntaxTree = capturedTrees[treeOrdinal];
        treeSha256 = syntaxTree.Sha256;
        treeLineMapSha256 = syntaxTree.LineMapSha256;
        treeSnapshotSha256 = CompilationFingerprint
            .ComputeSyntaxTreeSnapshotSha256(syntaxTree);

        sourceTreeOrdinal = CompilerSourceLocationAuthority.FindUniqueTree(
            location,
            capturedTrees,
            cancellationToken);
        if (sourceTreeOrdinal < 0)
        {
            return false;
        }

        var sourceTree = capturedTrees[sourceTreeOrdinal];
        sourceTreePath = sourceTree.Path;
        sourceTreeSha256 = sourceTree.Sha256;
        sourceLineMapSha256 = sourceTree.LineMapSha256;
        return true;
    }
}
