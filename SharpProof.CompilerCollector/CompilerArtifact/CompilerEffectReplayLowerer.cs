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

        replay = null;
        witnessDetail = string.Empty;
        if (!HasAllocationShape(witness) ||
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

    private static bool HasAllocationShape(EffectDirectWitness witness)
    {
        return witness is
        {
            Effects: EffectContractKind.Allocates,
            Capabilities: EffectContractCapabilityKind.None,
            ExceptionType: null
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
                         apiSpecs):
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
                witnessDetail = !string.IsNullOrWhiteSpace(
                    memberDocumentationId)
                    ? memberDocumentationId!
                    : memberIdentity;
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
                witnessDetail = !string.IsNullOrWhiteSpace(
                    typeDocumentationId)
                    ? typeDocumentationId!
                    : typeIdentity;
                break;
            default:
                return false;
        }

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
            ExactExceptionTypeHierarchy = [],
            Location = location,
            SourceTreeOrdinal = sourceTreeOrdinal,
            SourceTreePath = sourceTreePath,
            SourceTreeSha256 = sourceTreeSha256,
            SourceLineMapSha256 = sourceLineMapSha256
        };
        return true;
    }

    private static bool IsDefiniteObjectAllocation(
        IObjectCreationOperation creation,
        ResolvedApiSpecTable apiSpecs)
    {
        return creation.Type is INamedTypeSymbol
        {
            IsReferenceType: true
        } type &&
        !EffectMethodNodeBuilder
            .HasPotentialConstructionInitialization(
                type,
                apiSpecs) &&
        creation.Initializer == null &&
        creation.Arguments.All(static argument =>
            DefiniteOperationFacts.IsHarmlessValue(argument.Value));
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
        var trees = compilation.SyntaxTrees;
        treeOrdinal = trees.IndexOf(operation.Syntax.SyntaxTree);
        if (treeOrdinal < 0)
        {
            treeSha256 = string.Empty;
            treeSnapshotSha256 = string.Empty;
            treeLineMapSha256 = string.Empty;
            sourceTreeOrdinal = -1;
            sourceTreePath = string.Empty;
            sourceTreeSha256 = string.Empty;
            sourceLineMapSha256 = string.Empty;
            return false;
        }

        var tree = operation.Syntax.SyntaxTree;
        var text = tree.GetText(cancellationToken);
        if (operation.Syntax.SpanStart < 0 ||
            operation.Syntax.Span.End > text.Length)
        {
            treeSha256 = string.Empty;
            treeSnapshotSha256 = string.Empty;
            treeLineMapSha256 = string.Empty;
            sourceTreeOrdinal = -1;
            sourceTreePath = string.Empty;
            sourceTreeSha256 = string.Empty;
            sourceLineMapSha256 = string.Empty;
            return false;
        }

        var syntaxTree = CompilerCompilationCapture.CaptureTree(
            tree,
            cancellationToken);
        treeSha256 = syntaxTree.Sha256;
        treeLineMapSha256 = syntaxTree.LineMapSha256;
        treeSnapshotSha256 = CompilationFingerprint
            .ComputeSyntaxTreeSnapshotSha256(syntaxTree);

        sourceTreeOrdinal = -1;
        sourceTreePath = string.Empty;
        sourceTreeSha256 = string.Empty;
        sourceLineMapSha256 = string.Empty;
        for (var index = 0; index < trees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = CompilerCompilationCapture.CaptureTree(
                trees[index],
                cancellationToken);
            if (!CompilerSourceLocationAuthority.HasValidLocationGeometry(
                    location,
                    candidate))
            {
                continue;
            }

            if (sourceTreeOrdinal >= 0)
            {
                sourceTreeOrdinal = -1;
                sourceTreePath = string.Empty;
                sourceTreeSha256 = string.Empty;
                sourceLineMapSha256 = string.Empty;
                return false;
            }

            sourceTreeOrdinal = index;
            sourceTreePath = candidate.Path;
            sourceTreeSha256 = candidate.Sha256;
            sourceLineMapSha256 = candidate.LineMapSha256;
        }

        return sourceTreeOrdinal >= 0;
    }
}
