namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityService
{
    public SymbolicComplexityResult Query(
        SymbolicQueryContext request,
        CancellationToken cancellationToken)
    {
        return SymbolicMethodLikeQueryDispatcher.Execute(
            request,
            SymbolicSourceCompilationKind.Complexity,
            "Complexity source kind is not supported.",
            "Complexity queries support point, position, line, or node targets only.",
            "Node complexity queries require a node target.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(
                node,
                includeAnonymousFunctions: true,
                includeDestructors: true),
            ExecuteAnalysis,
            cancellationToken);
    }

    private static SymbolicComplexityResult ExecuteAnalysis(
        ResolvedMethodLikeTarget target,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (target.BodyNode == null)
            throw new ArgumentException("The requested method-like declaration does not have a body.");
        if (target.MethodSymbol == null)
            throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");

        var summary = new SymbolicComplexityAnalysisSession(compilation, cancellationToken).Analyze(target);
        return SymbolicComplexityResultProjector.Project(target, summary, cancellationToken);
    }
}
