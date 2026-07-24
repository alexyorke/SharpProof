using static SharpProof.Symbolic.SymbolicRuntimeHazardSourceCandidateFactory;
namespace SharpProof.Symbolic;
internal static class SymbolicRuntimeHazardCandidateFactory {
    internal static IEnumerable<RuntimeHazardCandidate> EnumerateCandidates(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeNestedCallables) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodesAndSelf(
                     descendIntoTrivia: false,
                     descendIntoChildren: candidate =>
                         includeNestedCallables ||
                         ReferenceEquals(candidate, root) ||
                         !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in EnumerateCandidatesForNode(node, semanticModel, cancellationToken)) {
                var operation = candidate.Operation;
                var key = candidate.Kind + ":" + operation.PreconditionKind + ":" + operation.ExceptionType + ":" +
                          operation.Category + ":" + candidate.Site.SpanStart + ":" + candidate.Site.Span.End + ":" +
                          operation.Origin.Provenance + ":" + SymbolicState.CreateProofConditionKey(operation.Trigger);
                if (seen.Add(key)) yield return candidate;
            }
        }
    }
    private static IEnumerable<RuntimeHazardCandidate> EnumerateCandidatesForNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var operation = semanticModel.GetOperation(node, cancellationToken);
        if (operation != null)
            foreach (var hazard in SymbolicOperationLowerer.LowerOperationHazards(operation, context))
                yield return new RuntimeHazardCandidate(node, hazard);
        // Invocation targets can have no member-level operation because Roslyn owns the operation at the parent call.
        if (node is MemberAccessExpressionSyntax memberAccess &&
            (operation == null || !ReferenceEquals(operation.Syntax, memberAccess)) &&
            SymbolicOperationLowerer.TryLowerMemberAccessNullDereferenceHazard(memberAccess, context, out var memberNullHazard))
            yield return new RuntimeHazardCandidate(memberAccess, memberNullHazard);
        if (node is ThrowStatementSyntax or ThrowExpressionSyntax)
            foreach (var throwCandidate in CreateThrowCandidates(node, semanticModel, cancellationToken))
                yield return throwCandidate;
    }
}
