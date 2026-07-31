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
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (witness == null)
        {
            throw new ArgumentNullException(nameof(witness));
        }

        if (apiSpecs == null)
        {
            throw new ArgumentNullException(nameof(apiSpecs));
        }

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
                cancellationToken,
                out var treeOrdinal,
                out var treeSha256))
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
            SyntaxStart = syntax.SpanStart,
            SyntaxLength = syntax.Span.Length,
            MemberIdentity = memberIdentity,
            MemberDocumentationId = memberDocumentationId,
            TypeIdentity = typeIdentity,
            TypeDocumentationId = typeDocumentationId,
            SpecWitnessIdentifier = null,
            ScalarOperands = [],
            ExactExceptionTypeHierarchy = [],
            Location = location
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
        CancellationToken cancellationToken,
        out int treeOrdinal,
        out string treeSha256)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trees = compilation.SyntaxTrees;
        treeOrdinal = trees.IndexOf(operation.Syntax.SyntaxTree);
        if (treeOrdinal < 0)
        {
            treeSha256 = string.Empty;
            return false;
        }

        var text = operation.Syntax.SyntaxTree.GetText(cancellationToken);
        if (operation.Syntax.SpanStart < 0 ||
            operation.Syntax.Span.End > text.Length)
        {
            treeSha256 = string.Empty;
            return false;
        }

        treeSha256 =
            CompilerCompilationCapture.ComputeTextSha256(text);
        return true;
    }
}
