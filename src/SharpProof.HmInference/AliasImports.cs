using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    public static bool PreservesCallerInfoArguments(SyntaxNode before, SemanticModel beforeModel,
        SyntaxNode after, SemanticModel afterModel, CancellationToken cancellationToken)
    {
        if (before is null)
        { throw new ArgumentNullException(nameof(before)); }
        if (beforeModel is null)
        { throw new ArgumentNullException(nameof(beforeModel)); }
        if (after is null)
        { throw new ArgumentNullException(nameof(after)); }
        if (afterModel is null)
        { throw new ArgumentNullException(nameof(afterModel)); }
        return CallerInfoArguments(before, beforeModel, cancellationToken).SequenceEqual(CallerInfoArguments(after, afterModel, cancellationToken));
    }

    private static IEnumerable<(int Ordinal, bool HasValue, object? Value)> CallerInfoArguments(SyntaxNode root,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (root is null)
        { throw new ArgumentNullException(nameof(root)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        var pending = new Stack<IOperation>();
        foreach (var node in root.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(node, cancellationToken) is { Parent: null } operation)
            { pending.Push(operation); }
        }
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = pending.Pop();
            if (operation is IArgumentOperation { IsImplicit: true, ArgumentKind: ArgumentKind.DefaultValue, Parameter: { } parameter } argument
                && parameter.GetAttributes().Any(attribute =>
                {
                    if (attribute is null)
                    { throw new ArgumentNullException(nameof(attribute)); }
                    return attribute.AttributeClass is { } type && type.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                        && type.Name is "CallerLineNumberAttribute" or "CallerFilePathAttribute" or "CallerMemberNameAttribute" or "CallerArgumentExpressionAttribute";
                }))
            {
                var constant = argument.Value.ConstantValue;
                yield return (parameter.Ordinal, constant.HasValue, constant.HasValue ? constant.Value : null);
            }
            foreach (var child in operation.ChildOperations.Reverse())
            { pending.Push(child); }
        }
    }


    internal static bool CanAddExternAlias(SemanticModel model, string alias, CancellationToken cancellationToken)
    {
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        if (alias is null)
        { throw new ArgumentNullException(nameof(alias)); }
        if (alias == "global" || !model.Compilation.References.Any(reference =>
        {
            if (reference is null)
            { throw new ArgumentNullException(nameof(reference)); }
            return reference.Properties.Aliases.Contains(alias);
        }))
        { return false; }
        var root = model.SyntaxTree.GetRoot(cancellationToken);
        // A parameter may share the extern alias name: Alias::Type still names
        // the assembly, while ordinary identifier uses keep binding to the parameter.
        // Leave all other same-file names conservative until their scopes are proven.
        if (root.DescendantTokens(descendIntoTrivia: true).Any(token =>
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != alias)
            { return false; }
            if (token.Parent is ParameterSyntax parameter && parameter.Identifier == token)
            { return false; }
            return token.Parent is not IdentifierNameSyntax identifier
                || model.GetSymbolInfo(identifier, cancellationToken).Symbol is not IParameterSymbol;
        }))
        { return false; }
        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            foreach (var directive in tree.GetRoot(cancellationToken).DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (directive.GlobalKeyword.RawKind != 0 && directive.Alias?.Name.Identifier.ValueText == alias)
                { return false; }
            }
        }
        return true;
    }

    public static CompilationUnitSyntax AddRequiredExternAliases(CompilationUnitSyntax root, MethodDeclarationSyntax method,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (root is null)
        { throw new ArgumentNullException(nameof(root)); }
        if (method is null)
        { throw new ArgumentNullException(nameof(method)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        var aliases = method.DescendantNodes().OfType<AliasQualifiedNameSyntax>().Select(name =>
        {
            if (name is null)
            { throw new ArgumentNullException(nameof(name)); }
            return name.Alias.Identifier.ValueText;
        }).Distinct(StringComparer.Ordinal).OrderBy(name =>
        {
            if (name is null)
            { throw new ArgumentNullException(nameof(name)); }
            return name;
        }, StringComparer.Ordinal).ToArray();
        foreach (var alias in aliases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanAddExternAlias(model, alias, cancellationToken)
                || root.Externs.Any(directive =>
                {
                    if (directive is null)
                    { throw new ArgumentNullException(nameof(directive)); }
                    return directive.Identifier.ValueText == alias;
                }))
            { continue; }
            var escaped = SyntaxFacts.GetKeywordKind(alias) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(alias) != SyntaxKind.None ? "@" + alias : alias;
            var added = SyntaxFactory.ParseCompilationUnit("extern alias " + escaped + ";\n").Externs.Single();
            if (root.Externs.Count == 0)
            {
                var first = root.GetFirstToken();
                var banner = first.LeadingTrivia.TakeWhile(trivia => !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)).ToArray();
                if (banner.Length != 0)
                {
                    root = root.ReplaceToken(first, first.WithLeadingTrivia(first.LeadingTrivia.Skip(banner.Length)));
                    added = added.WithLeadingTrivia(banner);
                }
            }
            root = root.WithExterns(root.Externs.Add(added));
        }
        return root;
    }
}
