using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private sealed class LayoutMarker(string name, SemanticModel semantic, CancellationToken cancellationToken) : CSharpSyntaxRewriter
        {
            private readonly string name = name ?? throw new ArgumentNullException(nameof(name));
            private readonly SemanticModel semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
            private int next;
            internal int Count => next;

            private AttributeListSyntax Mark(int index)
            {
                if (index < 0)
                { throw new ArgumentOutOfRangeException(nameof(index)); }
                cancellationToken.ThrowIfCancellationRequested();
                return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::" + name),
                    SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(index))))))));
            }

            public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
                return index < 0 ? visited : visited.AddAttributeLists(Mark(index));
            }

            public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
                return index < 0 ? visited : visited.AddAttributeLists(Mark(index));
            }

            public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
                if (index < 0)
                { return visited; }
                return SyntaxFactory.ParenthesizedLambdaExpression().WithModifiers(visited.Modifiers)
                    .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(visited.Parameter)))
                    .WithBody(visited.Body).AddAttributeLists(Mark(index)).WithTriviaFrom(node);
            }

            public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
                if (index < 0)
                { return visited; }
                var converted = semantic.GetTypeInfo(node, cancellationToken).ConvertedType as INamedTypeSymbol;
                var parameters = visited.ParameterList;
                if (parameters is null)
                {
                    var invoke = converted?.DelegateInvokeMethod ?? throw new InferenceException("Cannot inspect anonymous-method parameters.");
                    var names = new HashSet<string>(node.DescendantTokens().Select((SyntaxToken /* SPINT001: all values valid - ValueText accepts every token. */ token) => token.ValueText), StringComparer.Ordinal);
                    var generated = new List<ParameterSyntax>();
                    for (var i = 0; i < invoke.Parameters.Length; i++)
                    {
                        var parameterName = "__hmUnused" + i;
                        while (!names.Add(parameterName))
                        { parameterName += "_"; }
                        generated.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName)));
                    }
                    parameters = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(generated));
                }
                var lambda = SyntaxFactory.ParenthesizedLambdaExpression().WithModifiers(visited.Modifiers).WithParameterList(parameters)
                    .WithBody(visited.Block).AddAttributeLists(Mark(index));
                if (converted is null)
                { throw new InferenceException("Cannot inspect anonymous-method target."); }
                var spelling = CSharpTypes.Render(CSharpTypes.Rigid(converted), converted, [], model: semantic, position: node.SpanStart);
                return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(spelling), SyntaxFactory.ParenthesizedExpression(lambda)).WithTriviaFrom(node);
            }
        }

        private (CSharpCompilation Metadata, Dictionary<int, INamedTypeSymbol> States)? ReadNestedBuilderStates(Compilation compilation,
            MethodDeclarationSyntax root, string markerName)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (markerName is null)
            { throw new ArgumentNullException(nameof(markerName)); }
            if (root.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: < LanguageVersion.CSharp10 })
            { return ReadLegacyBuilderStates(compilation, root, markerName); }
            var semantic = compilation.GetSemanticModel(root.SyntaxTree);
            var marker = new LayoutMarker(markerName, semantic, cancellationToken);
            var marked = (MethodDeclarationSyntax)marker.Visit(root)!;
            var tree = root.SyntaxTree;
            var replacement = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(root, marked), tree.Options);
            compilation = compilation.ReplaceSyntaxTree(tree, replacement);
            if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
            { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, tree, replacement))); }
            var attributeSource = "[global::System.AttributeUsage(global::System.AttributeTargets.Method)]internal sealed class " + markerName
                + ":global::System.Attribute{public " + markerName + "(int index){}}";
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(attributeSource, (CSharpParseOptions)tree.Options, cancellationToken: cancellationToken));
            using var image = new System.IO.MemoryStream();
            if (!compilation.Emit(image, cancellationToken: cancellationToken).Success)
            { return null; }
            var reference = MetadataReference.CreateFromImage(image.ToArray());
            var metadata = CSharpCompilation.Create("HmNestedLayout", references: compilation.References.Concat([reference]),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
            var assembly = metadata.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol
                ?? throw new InferenceException("Cannot inspect nested state-machine assembly.");
            var states = new Dictionary<int, INamedTypeSymbol>();
            void Type(INamedTypeSymbol type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
                {
                    var attributes = member.GetAttributes();
                    var marker = attributes.FirstOrDefault(attribute =>
                    { if (attribute is null) { throw new ArgumentNullException(nameof(attribute)); } return attribute.AttributeClass?.Name == markerName; });
                    if (marker is null || marker.ConstructorArguments.Length != 1 || marker.ConstructorArguments[0].Value is not int index)
                    { continue; }
                    var state = attributes.FirstOrDefault(attribute =>
                    { if (attribute is null) { throw new ArgumentNullException(nameof(attribute)); } return attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"; });
                    if (state?.ConstructorArguments.Length != 1 || state.ConstructorArguments[0].Value is not INamedTypeSymbol machine)
                    { throw new InferenceException("Cannot correlate marked async state machine."); }
                    states.Add(index, machine.OriginalDefinition);
                }
                foreach (var nested in type.GetTypeMembers())
                { Type(nested); }
            }
            void Namespace(INamespaceSymbol scope)
            {
                if (scope is null)
                { throw new ArgumentNullException(nameof(scope)); }
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var type in scope.GetTypeMembers())
                { Type(type); }
                foreach (var child in scope.GetNamespaceMembers())
                { Namespace(child); }
            }
            Namespace(assembly.GlobalNamespace);
            if (states.Count != marker.Count)
            { throw new InferenceException("Emitted metadata omitted an async layout marker."); }
            return (metadata, states);
        }

        private bool InferBuilderLayoutConstraints(Compilation compilation, MethodDeclarationSyntax proposal, Dictionary<HmType, string> variables)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (proposal is null)
            { throw new ArgumentNullException(nameof(proposal)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            var added = InferRootBuilderLayoutConstraints(compilation, proposal, variables);
            var originalRoot = currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) as MethodDeclarationSyntax
                ?? throw new InferenceException("Missing original nested-layout method.");
            var potential = false;
            foreach (var node in originalRoot.DescendantNodes())
            {
                IMethodSymbol? function = node is LocalFunctionStatementSyntax local ? model.GetDeclaredSymbol(local, cancellationToken)
                    : node is AnonymousFunctionExpressionSyntax && model.GetOperation(node, cancellationToken) is IAnonymousFunctionOperation operation ? operation.Symbol : null;
                if (function is not { IsAsync: true })
                { continue; }
                var attribute = BuilderAttribute(function, function.ReturnType as INamedTypeSymbol);
                if (attribute is not null && ConstructBuilder(attribute, function.ReturnType as INamedTypeSymbol).Type.GetMembers().OfType<IMethodSymbol>().Any(RequiresUnmanagedMachineMember))
                { potential = true; break; }
            }
            if (!potential)
            { return added; }
            var markerName = "__HmLayout_" + Guid.NewGuid().ToString("N");
            var before = ReadNestedBuilderStates(model.Compilation, originalRoot, markerName)
                ?? throw new InferenceException("Original nested-layout probe cannot be emitted.");
            var active = before.States.Where(pair => RequiresUnmanagedMachine(before.Metadata, pair.Value)).Select(pair => pair.Key).ToArray();
            if (active.Length == 0)
            { return added; }
            var after = ReadNestedBuilderStates(compilation, proposal, markerName);
            if (after is null)
            {
                var tree = proposal.SyntaxTree;
                var stronger = StrengthenBuilderLayoutProbe(proposal, variables);
                var replacement = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(proposal, stronger), tree.Options);
                var probeCompilation = compilation.ReplaceSyntaxTree(tree, replacement);
                if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
                { probeCompilation = probeCompilation.WithOptions(probeCompilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, tree, replacement))); }
                var probeRoot = replacement.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
                { if (method is null) { throw new ArgumentNullException(nameof(method)); } return method.SpanStart == proposal.SpanStart; });
                after = ReadNestedBuilderStates(probeCompilation, probeRoot, markerName)
                    ?? throw new InferenceException("Nested-layout candidate probe cannot be emitted.");
            }
            added |= InferLocalLayoutCalls(compilation, proposal, after.Value.States, active, variables);
            return added;
        }
    }
}
