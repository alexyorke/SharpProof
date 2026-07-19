namespace SharpProof.Analyzer.Engine.Rules;

internal static class CompilationSyntaxAccess
{
    internal static SemanticModel GetSemanticModel(SemanticModel anchorModel, SyntaxNode node)
    {
        return node.SyntaxTree == anchorModel.SyntaxTree
            ? anchorModel
            : anchorModel.Compilation.GetSemanticModel(node.SyntaxTree);
    }

    internal static IOperation? GetOperation(
        SemanticModel anchorModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        return GetSemanticModel(anchorModel, node).GetOperation(node, cancellationToken);
    }

    internal static Optional<object?> GetConstantValue(
        SemanticModel anchorModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        return GetSemanticModel(anchorModel, node).GetConstantValue(node, cancellationToken);
    }

}
