using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public sealed record RecursiveGroupInfo(SyntaxTree Tree, int AnchorStart, IReadOnlyList<string> Signatures)
{
    public SyntaxTree Tree { get; } = Tree ?? throw new ArgumentNullException(nameof(Tree));
    public IReadOnlyList<string> Signatures { get; } = Signatures ?? throw new ArgumentNullException(nameof(Signatures));
    public const string DiagnosticAnchorProperty = "HM.RecursiveGroup.AnchorStart";
    public const string DiagnosticCountProperty = "HM.RecursiveGroup.Count";
    public const string DiagnosticSignaturePrefix = "HM.RecursiveGroup.Signature.";
}

public sealed class RecursiveGroupAnalysis
{
    public RecursiveGroupAnalysis(IReadOnlyDictionary<MethodDeclarationSyntax, MethodResult> results,
        IReadOnlyDictionary<SyntaxTree, CompilationUnitSyntax> rewrittenRoots)
    {
        Results = results ?? throw new ArgumentNullException(nameof(results));
        RewrittenRoots = rewrittenRoots ?? throw new ArgumentNullException(nameof(rewrittenRoots));
    }

    public IReadOnlyDictionary<MethodDeclarationSyntax, MethodResult> Results { get; }
    public IReadOnlyDictionary<SyntaxTree, CompilationUnitSyntax> RewrittenRoots { get; }
}

public static partial class Inference
{
    private sealed class RecursiveGroupContext
    {
        public Dictionary<IMethodSymbol, HmType> Functions { get; } = new(SymbolEqualityComparer.Default);
        public Dictionary<IMethodSymbol, MethodDeclarationSyntax> Candidates { get; } = new(SymbolEqualityComparer.Default);
    }

    public static RecursiveGroupAnalysis? AnalyzeRecursiveGroup(
        MethodDeclarationSyntax method, SemanticModel model, CancellationToken cancellationToken)
    {
        if (method is null || model is null || model.Compilation is not CSharpCompilation compilation)
        { return null; }

        var first = model.GetDeclaredSymbol(method, cancellationToken);
        if (first is null || !Eligible(method, first))
        { return null; }
        // Most methods are not candidates. Avoid scanning a containing type or a
        // compilation unless this body calls a distinct method in that type.
        if (!DirectTargets(method, model, cancellationToken).Any(target /* SPINT001: all values valid - Roslyn supplies a bound nonnull target. */ =>
            !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, first.OriginalDefinition)
            && SymbolEqualityComparer.Default.Equals(target.ContainingType, first.ContainingType)))
        { return null; }
        var nodes = new List<(MethodDeclarationSyntax Method, IMethodSymbol Symbol)>();
        foreach (var candidate in first.ContainingType.GetMembers().OfType<IMethodSymbol>())
        {
            if (candidate.DeclaringSyntaxReferences.Length != 1
                || candidate.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration
                || declaration.SyntaxTree != method.SyntaxTree
                || !Eligible(declaration, candidate))
            { continue; }
            nodes.Add((declaration, candidate));
        }
        var bySymbol = new Dictionary<IMethodSymbol, (MethodDeclarationSyntax Method, IMethodSymbol Symbol)>(
            SymbolEqualityComparer.Default);
        foreach (var node in nodes)
        { bySymbol.Add(node.Symbol.OriginalDefinition, node); }
        var edges = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var node in nodes)
        {
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var target in DirectTargets(node.Method, model, cancellationToken))
            {
                if (SymbolEqualityComparer.Default.Equals(target.ContainingType, node.Symbol.ContainingType)
                    && bySymbol.ContainsKey(target.OriginalDefinition))
                { targets.Add(target.OriginalDefinition); }
            }
            edges.Add(node.Symbol.OriginalDefinition, targets);
        }

        HashSet<IMethodSymbol> Reachable(bool reverse)
        {
            var reached = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<IMethodSymbol>();
            pending.Push(first.OriginalDefinition);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                if (!reached.Add(current))
                { continue; }
                var neighbors = reverse
                    ? edges.Where(entry => entry.Value.Contains(current)).Select(entry => entry.Key)
                    : edges[current];
                foreach (var next in neighbors)
                { pending.Push(next); }
            }
            return reached;
        }

        var forward = Reachable(false);
        var backward = Reachable(true);
        var members = nodes.Where(node =>
                forward.Contains(node.Symbol.OriginalDefinition)
                && backward.Contains(node.Symbol.OriginalDefinition))
            .OrderBy(node => node.Method.SpanStart).ToArray();
        // A group requires an actual bound cycle; singleton self-recursion stays
        // on the existing independently validated method path.
        if (members.Length < 2)
        { return null; }
        if (members.Any(item => item.Symbol.ContainingType.GetMembers(item.Symbol.Name)
                .OfType<IMethodSymbol>().Count() != 1)
            || !members.All(member => SafeCoreBody(member.Method,
                member.Symbol, members, model, cancellationToken))
            || !OnlyDirectReferences(compilation, members, cancellationToken)
            || compilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
                IsCompilationError(d)))
        { return null; }

        var group = new RecursiveGroupContext();
        foreach (var member in members)
        {
            var inputs = member.Symbol.Parameters.Select(parameter /* SPINT001: all values valid - Symbol.Parameters contains nonnull parameter symbols. */ => CSharpTypes.Erase(parameter.Type)).ToArray();
            group.Functions.Add(member.Symbol.OriginalDefinition,
                HmType.Function(inputs, CSharpTypes.Erase(member.Symbol.ReturnType)));
        }

        var results = new Dictionary<MethodDeclarationSyntax, MethodResult>();
        try
        {
            // Every call inside the SCC sees the same monomorphic function nodes.
            // No method is generalized or rendered until every body has unified.
            foreach (var member in members)
            {
                var body = new Session(model, cancellationToken, recursiveGroup: group, groupBodyOnly: true)
                    .Analyze(member.Method, []);
                if (body.Status != "Prepared")
                { return null; }
            }
            foreach (var member in members)
            {
                var proposal = new Session(model, cancellationToken, recursiveGroup: group)
                    .Analyze(member.Method, []);
                if (proposal.Status != "Suggestion" || proposal.Signature is null
                    || !group.Candidates.ContainsKey(member.Symbol.OriginalDefinition))
                { return null; }
                results.Add(member.Method, proposal);
            }
        }
        catch (InferenceException)
        { return null; }

        var originalTree = method.SyntaxTree;
        var originalRoot = (CompilationUnitSyntax)originalTree.GetRoot(cancellationToken);
        var markers = members.Select((_, i /* SPINT001: all values valid - Enumerable indexes are valid positions. */) => new SyntaxAnnotation("HM.RecursiveGroup." + i)).ToArray();
        var replacements = members.Select((member, i /* SPINT001: all values valid - Index addresses the equally sized marker array. */) =>
            group.Candidates[member.Symbol.OriginalDefinition].WithAdditionalAnnotations(markers[i])).ToArray();
        var rewrittenRoot = originalRoot.ReplaceNodes(members.Select(member => member.Method),
            (old /* SPINT001: all values valid - Roslyn supplies an existing node being replaced. */, _) => replacements[Array.FindIndex(members, item => item.Method == old)]);
        var changedTree = originalTree.WithRootAndOptions(rewrittenRoot, originalTree.Options);
        var changed = compilation.ReplaceSyntaxTree(originalTree, changedTree);
        if (changed.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
                IsCompilationError(d))
            || new Session(model, cancellationToken).IntroducesCompilerWarnings(changed))
        { return null; }

        var changedRoot = changedTree.GetRoot(cancellationToken);
        var changedModel = changed.GetSemanticModel(changedTree);
        var changedSymbols = markers.Select(marker /* SPINT001: all values valid - Markers were just constructed from group members. */ =>
        {
            var node = changedRoot.GetAnnotatedNodes(marker).OfType<MethodDeclarationSyntax>().Single();
            return changedModel.GetDeclaredSymbol(node, cancellationToken)!;
        }).ToArray();
        if (!PreservesGroupBindings(compilation, changed, members, changedSymbols, originalTree, changedTree, cancellationToken))
        { return null; }

        var groupInfo = new RecursiveGroupInfo(originalTree, members[0].Method.SpanStart,
            Array.AsReadOnly(members.Select(member => results[member.Method].Signature!).ToArray()));
        foreach (var member in members)
        {
            var result = results[member.Method];
            results[member.Method] = new MethodResult(result.Method, result.Status,
                "Recursive group validated; apply all member signatures together.", result.Signature)
            { RecursiveGroup = groupInfo };
        }

        return new RecursiveGroupAnalysis(results,
            new Dictionary<SyntaxTree, CompilationUnitSyntax> { [originalTree] = rewrittenRoot });
    }

    private static bool Eligible(MethodDeclarationSyntax method, IMethodSymbol symbol)
    {
        return symbol.MethodKind == MethodKind.Ordinary && symbol.IsStatic && !symbol.IsAsync
            && !symbol.IsExtern && !symbol.ReturnsVoid && symbol.RefKind == RefKind.None
            && symbol.Arity == 0 && symbol.PartialDefinitionPart is null
            && symbol.PartialImplementationPart is null
            && (method.Body is not null || method.ExpressionBody is not null)
            && method.TypeParameterList is null && method.ConstraintClauses.Count == 0
            && method.AttributeLists.Count == 0
            && method.ParameterList.Parameters.All(parameter =>
                parameter.Default is null && parameter.AttributeLists.Count == 0
                && parameter.Modifiers.Count == 0)
            && symbol.Parameters.All(parameter /* SPINT001: all values valid - Roslyn parameter symbols are nonnull. */ =>
                parameter.RefKind == RefKind.None && !parameter.IsOptional && !parameter.IsParams);
    }

    // Admit a small statement calculus whose every path returns. A block may
    // contain only returns or if/else returns over the safe expression calculus.
    private static bool SafeCoreBody(MethodDeclarationSyntax method, IMethodSymbol owner,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (method is null)
        { throw new ArgumentNullException(nameof(method)); }
        if (owner is null)
        { throw new ArgumentNullException(nameof(owner)); }
        if (members is null)
        { throw new ArgumentNullException(nameof(members)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        return method.ExpressionBody is { } arrow
            ? SafeCoreExpression(arrow.Expression, owner, members, model, cancellationToken)
            : method.Body is { } block
                && SafeCoreStatements(block.Statements, owner, members, model, cancellationToken);
    }

    private static bool SafeCoreStatements(IEnumerable<StatementSyntax> statements, IMethodSymbol owner,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (statements is null)
        { throw new ArgumentNullException(nameof(statements)); }
        if (owner is null)
        { throw new ArgumentNullException(nameof(owner)); }
        if (members is null)
        { throw new ArgumentNullException(nameof(members)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        var returned = false;
        foreach (var statement in statements)
        {
            if (returned)
            { return false; }
            switch (statement)
            {
                case ReturnStatementSyntax { Expression: { } value }:
                    if (!SafeCoreExpression(value, owner, members, model, cancellationToken))
                    { return false; }
                    returned = true;
                    break;
                case IfStatementSyntax conditional:
                    if (!SafeCoreExpression(conditional.Condition, owner, members, model, cancellationToken)
                        || !SafeCoreBranch(conditional.Statement, owner, members, model, cancellationToken)
                        || conditional.Else is { } alternate
                            && !SafeCoreBranch(alternate.Statement, owner, members, model, cancellationToken))
                    { return false; }
                    returned = conditional.Else is not null;
                    break;
                default:
                    return false;
            }
        }
        return returned;
    }

    private static bool SafeCoreBranch(StatementSyntax statement, IMethodSymbol owner,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (statement is null)
        { throw new ArgumentNullException(nameof(statement)); }
        if (owner is null)
        { throw new ArgumentNullException(nameof(owner)); }
        if (members is null)
        { throw new ArgumentNullException(nameof(members)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        return statement is BlockSyntax block
            ? SafeCoreStatements(block.Statements, owner, members, model, cancellationToken)
            : SafeCoreStatements(SyntaxFactory.SingletonList(statement), owner, members, model, cancellationToken);
    }

    // Until the group's protocol and operator checks share the ordinary
    // candidate validator, admit only the side-effect-free expression calculus:
    // parameter reads, literals, conditionals and direct calls within the SCC.
    private static bool SafeCoreExpression(ExpressionSyntax expression, IMethodSymbol owner,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is null)
        { throw new ArgumentNullException(nameof(expression)); }
        if (owner is null)
        { throw new ArgumentNullException(nameof(owner)); }
        if (members is null)
        { throw new ArgumentNullException(nameof(members)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                return model.GetSymbolInfo(identifier, cancellationToken).Symbol is IParameterSymbol parameter
                    && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, owner);
            case LiteralExpressionSyntax literal:
                return literal.IsKind(SyntaxKind.NumericLiteralExpression)
                    || literal.IsKind(SyntaxKind.TrueLiteralExpression)
                    || literal.IsKind(SyntaxKind.FalseLiteralExpression)
                    || literal.IsKind(SyntaxKind.StringLiteralExpression)
                    || literal.IsKind(SyntaxKind.CharacterLiteralExpression);
            case ParenthesizedExpressionSyntax parenthesized:
                return SafeCoreExpression(parenthesized.Expression, owner, members, model, cancellationToken);
            case ConditionalExpressionSyntax conditional:
                return SafeCoreExpression(conditional.Condition, owner, members, model, cancellationToken)
                    && SafeCoreExpression(conditional.WhenTrue, owner, members, model, cancellationToken)
                    && SafeCoreExpression(conditional.WhenFalse, owner, members, model, cancellationToken);
            case InvocationExpressionSyntax invocation:
                var called = DirectStaticTarget(invocation, model, cancellationToken);
                return called is not null
                    && SymbolEqualityComparer.Default.Equals(called.ContainingType, owner.ContainingType)
                    && members.Any(member => SymbolEqualityComparer.Default.Equals(
                        member.Symbol.OriginalDefinition, called.OriginalDefinition))
                    && invocation.ArgumentList.Arguments.All(argument =>
                        argument.NameColon is null && argument.RefKindKeyword == default
                        && SafeCoreExpression(argument.Expression, owner, members, model, cancellationToken));
            default:
                return false;
        }
    }
    private static IMethodSymbol[] DirectTargets(
        MethodDeclarationSyntax method, SemanticModel model, CancellationToken cancellationToken)
    {
        if (method is null)
        { throw new ArgumentNullException(nameof(method)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        return method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation /* SPINT001: all values valid - Descendant syntax nodes are nonnull. */ =>
                DirectStaticTarget(invocation, model, cancellationToken))
            .OfType<IMethodSymbol>().ToArray();
    }

    // A direct static call may use the method name alone or a qualifier bound to
    // the target's containing type. A value receiver and method group are not calls.
    private static IMethodSymbol? DirectStaticTarget(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation is null)
        { throw new ArgumentNullException(nameof(invocation)); }
        if (model is null)
        { throw new ArgumentNullException(nameof(model)); }
        if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation call
            || !call.TargetMethod.IsStatic)
        { return null; }
        if (invocation.Expression is IdentifierNameSyntax)
        { return call.TargetMethod; }
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        { return null; }
        var qualifier = model.GetSymbolInfo(member.Expression, cancellationToken).Symbol;
        if (qualifier is IAliasSymbol alias)
        { qualifier = alias.Target; }
        return qualifier is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named, call.TargetMethod.ContainingType)
            ? call.TargetMethod : null;
    }
    private static bool OnlyDirectReferences(
        CSharpCompilation compilation,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        CancellationToken cancellationToken)
    {
        if (compilation is null)
        { throw new ArgumentNullException(nameof(compilation)); }
        if (members is null)
        { throw new ArgumentNullException(nameof(members)); }
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(name, cancellationToken).Symbol is not IMethodSymbol reference
                    || !members.Any(item =>
                        SymbolEqualityComparer.Default.Equals(reference.OriginalDefinition, item.Symbol.OriginalDefinition)))
                { continue; }
                InvocationExpressionSyntax? invocation = null;
                if (name.Parent is InvocationExpressionSyntax simple && simple.Expression == name)
                { invocation = simple; }
                else if (name.Parent is MemberAccessExpressionSyntax member
                    && member.Name == name
                    && member.Parent is InvocationExpressionSyntax qualified
                    && qualified.Expression == member)
                { invocation = qualified; }
                if (invocation is null
                    || DirectStaticTarget(invocation, model, cancellationToken) is not { } target
                    || !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, reference.OriginalDefinition))
                { return false; }
            }
        }
        return true;
    }

    private static bool PreservesGroupBindings(
        CSharpCompilation before, CSharpCompilation after,
        (MethodDeclarationSyntax Method, IMethodSymbol Symbol)[] members,
        IMethodSymbol[] changedSymbols, SyntaxTree oldTree, SyntaxTree newTree,
        CancellationToken cancellationToken)
    {
        foreach (var tree in before.SyntaxTrees)
        {
            var replacement = tree == oldTree ? newTree : tree;
            var oldRoot = tree.GetRoot(cancellationToken);
            var newRoot = replacement.GetRoot(cancellationToken);
            var oldCalls = oldRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var newCalls = newRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            if (oldCalls.Length != newCalls.Length)
            { return false; }
            var oldModel = before.GetSemanticModel(tree);
            var newModel = after.GetSemanticModel(replacement);
            for (var i = 0; i < oldCalls.Length; i++)
            {
                var oldOperation = oldModel.GetOperation(oldCalls[i], cancellationToken);
                var newOperation = newModel.GetOperation(newCalls[i], cancellationToken);
                if (oldOperation is INameOfOperation oldNameof && newOperation is INameOfOperation newNameof)
                {
                    if (!oldNameof.ConstantValue.HasValue || !newNameof.ConstantValue.HasValue
                        || !Equals(oldNameof.ConstantValue.Value, newNameof.ConstantValue.Value)
                        || !SymbolEqualityComparer.Default.Equals(oldNameof.Type, newNameof.Type))
                    { return false; }
                    continue;
                }
                var oldTarget = (oldOperation as IInvocationOperation)?.TargetMethod;
                var newTarget = (newOperation as IInvocationOperation)?.TargetMethod;
                if (oldTarget is null || newTarget is null)
                { return false; }
                var memberIndex = Array.FindIndex(members, item =>
                    SymbolEqualityComparer.Default.Equals(item.Symbol.OriginalDefinition, oldTarget.OriginalDefinition));
                if (memberIndex >= 0)
                {
                    if (!SymbolEqualityComparer.Default.Equals(changedSymbols[memberIndex], newTarget.OriginalDefinition))
                    { return false; }
                    var insideGroup = tree == oldTree && members.Any(item => item.Method.Span.Contains(oldCalls[i].Span));
                    if (!insideGroup && !SameInstantiatedSignature(oldTarget, newTarget))
                    { return false; }
                }
                else if (oldTarget.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                        != newTarget.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    || !SameInstantiatedSignature(oldTarget, newTarget))
                { return false; }
            }
        }
        return true;
    }

    private static bool SameInstantiatedSignature(IMethodSymbol before, IMethodSymbol after)
    {
        if (before.RefKind != after.RefKind || before.Parameters.Length != after.Parameters.Length
            || !CSharpTypes.SameSignatureType(before.ReturnType, after.ReturnType))
        { return false; }
        for (var i = 0; i < before.Parameters.Length; i++)
        {
            if (before.Parameters[i].RefKind != after.Parameters[i].RefKind
                || !CSharpTypes.SameSignatureType(before.Parameters[i].Type, after.Parameters[i].Type))
            { return false; }
        }
        return true;
    }
}
