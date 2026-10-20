using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private sealed class LegacyLayoutMarker(string name, SemanticModel semantic, CancellationToken cancellationToken) : CSharpSyntaxRewriter
        {
            private readonly string name = name ?? throw new ArgumentNullException(nameof(name));
            private readonly SemanticModel semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
            private int next;
            internal int Count => next;

            private BlockSyntax Mark(int index, BlockSyntax? body, ExpressionSyntax? expression, ExpressionSyntax? original, SyntaxNode source)
            {
                if (index < 0)
                { throw new ArgumentOutOfRangeException(nameof(index)); }
                if (source is null)
                { throw new ArgumentNullException(nameof(source)); }
                cancellationToken.ThrowIfCancellationRequested();
                if (body is null)
                {
                    if (expression is null || original is null)
                    { throw new InferenceException("Missing expression-bodied layout function."); }
                    StatementSyntax statement = expression is ThrowExpressionSyntax thrown ? SyntaxFactory.ThrowStatement(thrown.Expression)
                        : semantic.GetTypeInfo(original, cancellationToken).Type?.SpecialType == SpecialType.System_Void ? SyntaxFactory.ExpressionStatement(expression)
                        : SyntaxFactory.ReturnStatement(expression);
                    body = SyntaxFactory.Block(statement);
                }
                // The switch consumes the constant without an unused-local warning or a stored field.
                var identifier = name + "_" + index.ToString(CultureInfo.InvariantCulture);
                var hidden = source.SyntaxTree.GetLineVisibility(source.SpanStart, cancellationToken) == LineVisibility.Hidden;
                var consume = "switch (" + identifier + ") { default: break; }";
                if (hidden)
                { consume = "\n#line default\n" + consume + "\n#line hidden\n"; }
                return body.WithStatements(body.Statements.InsertRange(0, [
                    SyntaxFactory.ParseStatement("const int " + identifier + " = 0;"),
                    SyntaxFactory.ParseStatement(consume)
                ]));
            }

            public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
                return index < 0 ? visited : visited.WithBody(Mark(index, visited.Body, visited.ExpressionBody?.Expression, node.ExpressionBody?.Expression, node.Body ?? (SyntaxNode?)node.ExpressionBody?.Expression ?? node))
                    .WithExpressionBody(null).WithSemicolonToken(default);
            }

            public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
                return index < 0 ? visited : visited.WithBody(Mark(index, visited.Body as BlockSyntax, visited.Body as ExpressionSyntax, node.Body as ExpressionSyntax, node.Body));
            }

            public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
                return index < 0 ? visited : visited.WithBody(Mark(index, visited.Body as BlockSyntax, visited.Body as ExpressionSyntax, node.Body as ExpressionSyntax, node.Body));
            }

            public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                var index = node.Modifiers.Any(SyntaxKind.AsyncKeyword) ? next++ : -1;
                var visited = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
                return index < 0 ? visited : visited.WithBlock(Mark(index, visited.Block, null, null, node.Block));
            }
        }

        private (CSharpCompilation Metadata, Dictionary<int, INamedTypeSymbol> States)? ReadLegacyBuilderStates(Compilation compilation,
            MethodDeclarationSyntax root, string markerName)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (markerName is null)
            { throw new ArgumentNullException(nameof(markerName)); }
            var semantic = compilation.GetSemanticModel(root.SyntaxTree);
            var marker = new LegacyLayoutMarker(markerName, semantic, cancellationToken);
            var marked = (MethodDeclarationSyntax)marker.Visit(root)!;
            foreach (var tree in compilation.SyntaxTrees.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tree != root.SyntaxTree && tree.Encoding is not null)
                { continue; }
                var syntax = tree.GetRoot(cancellationToken);
                if (tree == root.SyntaxTree)
                { syntax = syntax.ReplaceNode(root, marked); }
                var replacement = CSharpSyntaxTree.Create((CSharpSyntaxNode)syntax, (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8);
                compilation = compilation.ReplaceSyntaxTree(tree, replacement);
                if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
                { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, tree, replacement))); }
            }
            using var image = new MemoryStream();
            using var symbols = new MemoryStream();
            if (!compilation.Emit(image, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb), cancellationToken: cancellationToken).Success)
            { return null; }
            var reference = MetadataReference.CreateFromImage(image.ToArray());
            var metadata = CSharpCompilation.Create("HmLegacyLayout", references: compilation.References.Concat([reference]),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
            var assembly = metadata.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol
                ?? throw new InferenceException("Cannot inspect legacy state-machine assembly.");
            image.Position = 0;
            symbols.Position = 0;
            using var pe = new PEReader(image, PEStreamOptions.LeaveOpen);
            using var pdb = MetadataReaderProvider.FromPortablePdbStream(symbols);
            var definitions = pe.GetMetadataReader();
            var debug = pdb.GetMetadataReader();
            string TypeName(TypeDefinitionHandle handle)
            {
                // SPINT001: all values valid - nil handles are rejected explicitly.
                if (handle.IsNil)
                { throw new InferenceException("Missing emitted method owner."); }
                var type = definitions.GetTypeDefinition(handle);
                var parent = type.GetDeclaringType();
                var name = definitions.GetString(type.Name);
                if (!parent.IsNil)
                { return TypeName(parent) + "+" + name; }
                var scope = definitions.GetString(type.Namespace);
                return scope.Length == 0 ? name : scope + "." + name;
            }
            var states = new Dictionary<int, INamedTypeSymbol>();
            foreach (var handle in debug.LocalScopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scope = debug.GetLocalScope(handle);
                foreach (var constantHandle in scope.GetLocalConstants())
                {
                    var name = debug.GetString(debug.GetLocalConstant(constantHandle).Name);
                    var prefix = markerName + "_";
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)
                        || !int.TryParse(name.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    { continue; }
                    var kickoff = debug.GetMethodDebugInformation(scope.Method).GetStateMachineKickoffMethod();
                    if (kickoff.IsNil)
                    { throw new InferenceException("Layout marker has no async kickoff method."); }
                    var method = definitions.GetMethodDefinition(kickoff);
                    var owner = assembly.GetTypeByMetadataName(TypeName(method.GetDeclaringType()))
                        ?? throw new InferenceException("Cannot resolve legacy async function owner.");
                    var candidates = owner.GetMembers(definitions.GetString(method.Name)).OfType<IMethodSymbol>().ToArray();
                    if (candidates.Length != 1)
                    { throw new InferenceException("Legacy async function metadata is ambiguous."); }
                    var attribute = candidates[0].GetAttributes().FirstOrDefault(item =>
                    { if (item is null) { throw new ArgumentNullException(nameof(item)); } return item.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"; });
                    if (attribute?.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol state)
                    { throw new InferenceException("Cannot inspect legacy async function state."); }
                    states.Add(index, state.OriginalDefinition);
                }
            }
            if (states.Count != marker.Count)
            { throw new InferenceException("Portable debug metadata omitted an async layout marker."); }
            return (metadata, states);
        }
    }
}
