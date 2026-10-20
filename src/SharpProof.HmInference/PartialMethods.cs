using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private static IMethodSymbol? CanonicalMethod(IMethodSymbol? method)
    {
        method = method?.ReducedFrom ?? method;
        method = method?.OriginalDefinition;
        return method?.PartialDefinitionPart ?? method;
    }

    private static bool SameMethodDefinition(IMethodSymbol? first, IMethodSymbol? second)
    {
        return SymbolEqualityComparer.Default.Equals(CanonicalMethod(first)?.OriginalDefinition, CanonicalMethod(second)?.OriginalDefinition);
    }

    private static IMethodSymbol ImplementationMethod(IMethodSymbol method)
    {
        if (method is null)
        { throw new ArgumentNullException(nameof(method)); }
        method = method.OriginalDefinition;
        return method.PartialImplementationPart ?? method;
    }

    public static IReadOnlyDictionary<SyntaxTree, CompilationUnitSyntax> RewriteMethodParts(MethodDeclarationSyntax method,
        MethodDeclarationSyntax replacement, SemanticModel model, CancellationToken cancellationToken)
    {
        if (method is null)
        { throw new ArgumentNullException(nameof(method)); }
        if (replacement is null)
        { throw new ArgumentNullException(nameof(replacement)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        var changes = new List<(MethodDeclarationSyntax Before, MethodDeclarationSyntax After)> { (method, replacement) };
        var symbol = model.GetDeclaredSymbol(method, cancellationToken);
        var partner = symbol?.PartialDefinitionPart ?? symbol?.PartialImplementationPart;
        if (partner?.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration)
        {
            var originalNames = method.TypeParameterList?.Parameters ?? default;
            var partnerNames = declaration.TypeParameterList?.Parameters ?? default;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < originalNames.Count; i++)
            { names[originalNames[i].Identifier.ValueText] = partnerNames[i].Identifier.ValueText; }
            TypeSyntax Rename(TypeSyntax type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                return (TypeSyntax)new PartialTypeRenamer(names).Visit(type)!;
            }
            var constraints = declaration.ConstraintClauses;
            foreach (var clause in replacement.ConstraintClauses)
            {
                var previous = method.ConstraintClauses.FirstOrDefault(existing =>
                {
                    if (existing is null)
                    { throw new ArgumentNullException(nameof(existing)); }
                    return existing.Name.Identifier.ValueText == clause.Name.Identifier.ValueText;
                });
                if (previous is not null && previous.ToFullString() == clause.ToFullString())
                { continue; }
                var renamed = (TypeParameterConstraintClauseSyntax)new PartialTypeRenamer(names).Visit(clause)!;
                var existingPartner = constraints.FirstOrDefault(existing =>
                {
                    if (existing is null)
                    { throw new ArgumentNullException(nameof(existing)); }
                    return existing.Name.Identifier.ValueText == renamed.Name.Identifier.ValueText;
                });
                constraints = existingPartner is null ? constraints.Add(renamed) : constraints.Replace(existingPartner, renamed.WithTriviaFrom(existingPartner));
            }

            var newParameters = replacement.TypeParameterList?.Parameters.Skip(originalNames.Count) ?? [];
            var typeParameters = declaration.TypeParameterList is null ? replacement.TypeParameterList
                : declaration.TypeParameterList.WithParameters(partnerNames.AddRange(newParameters));
            var mirrored = declaration.WithReturnType(Rename(replacement.ReturnType).WithTriviaFrom(declaration.ReturnType))
                .WithTypeParameterList(typeParameters)
                .WithConstraintClauses(constraints)
                .WithParameterList(declaration.ParameterList.WithParameters(SyntaxFactory.SeparatedList(declaration.ParameterList.Parameters.Select((parameter, index) =>
                {
                    if (parameter is null)
                    { throw new ArgumentNullException(nameof(parameter)); }
                    if (index < 0)
                    { throw new ArgumentOutOfRangeException(nameof(index)); }
                    return parameter.WithType(Rename(replacement.ParameterList.Parameters[index].Type!).WithTriviaFrom(parameter.Type!));
                }), declaration.ParameterList.Parameters.GetSeparators())))
                .WithAdditionalAnnotations(new SyntaxAnnotation("HM.PartialRewrite"));
            changes.Add((declaration, mirrored));
        }
        var roots = new Dictionary<SyntaxTree, CompilationUnitSyntax>();
        foreach (var group in changes.GroupBy(change => change.Before.SyntaxTree))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semantic = model.Compilation.GetSemanticModel(group.Key);
            var root = (CompilationUnitSyntax)group.Key.GetRoot(cancellationToken);
            var bySpan = group.ToDictionary(change => change.Before.SpanStart, change => change.After);
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(declaration =>
            { if (declaration is null) { throw new ArgumentNullException(nameof(declaration)); } return bySpan.ContainsKey(declaration.SpanStart); }).ToArray();
            root = root.ReplaceNodes(methods, (before, after) =>
            {
                if (before is null)
                { throw new ArgumentNullException(nameof(before)); }
                if (after is null)
                { throw new ArgumentNullException(nameof(after)); }
                return bySpan[before.SpanStart];
            });
            foreach (var change in group)
            { root = AddRequiredExternAliases(root, change.After, semantic, cancellationToken); }
            roots[group.Key] = root;
        }
        return roots;
    }

    private sealed class PartialTypeRenamer(Dictionary<string, string> names) : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> names = names ?? throw new ArgumentNullException(nameof(names));

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node is null)
            { throw new ArgumentNullException(nameof(node)); }
            if (names.TryGetValue(node.Identifier.ValueText, out var name)
                && !(node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
                && !(node.Parent is AliasQualifiedNameSyntax aliased && aliased.Name == node))
            { return SyntaxFactory.IdentifierName(SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name).WithTriviaFrom(node); }
            return base.VisitIdentifierName(node);
        }
    }

    private sealed partial class Session
    {
        private readonly Dictionary<SyntaxTree, SyntaxTree> replacementTrees = [];
    }
}
