using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static bool SameLayoutType(ITypeSymbol left, ITypeSymbol right)
        {
            if (left is null)
            { throw new ArgumentNullException(nameof(left)); }
            if (right is null)
            { throw new ArgumentNullException(nameof(right)); }
            if (left is ITypeParameterSymbol first && right is ITypeParameterSymbol second)
            {
                return first.TypeParameterKind == second.TypeParameterKind && first.Ordinal == second.Ordinal && first.Name == second.Name
                    && (first.TypeParameterKind == TypeParameterKind.Method || CSharpTypes.ConstructorIdentity(first.ContainingType) == CSharpTypes.ConstructorIdentity(second.ContainingType));
            }
            if (left is IPointerTypeSymbol pointer && right is IPointerTypeSymbol otherPointer)
            { return SameLayoutType(pointer.PointedAtType, otherPointer.PointedAtType); }
            if (left is IFunctionPointerTypeSymbol function && right is IFunctionPointerTypeSymbol otherFunction)
            {
                if (CSharpTypes.FunctionPointerName(function.Signature) != CSharpTypes.FunctionPointerName(otherFunction.Signature)
                    || function.Signature.Parameters.Length != otherFunction.Signature.Parameters.Length
                    || !SameLayoutType(function.Signature.ReturnType, otherFunction.Signature.ReturnType))
                { return false; }
                for (var i = 0; i < function.Signature.Parameters.Length; i++)
                { if (!SameLayoutType(function.Signature.Parameters[i].Type, otherFunction.Signature.Parameters[i].Type)) { return false; } }
                return true;
            }
            if (left is IArrayTypeSymbol array && right is IArrayTypeSymbol otherArray)
            { return array.Rank == otherArray.Rank && array.IsSZArray == otherArray.IsSZArray && SameLayoutType(array.ElementType, otherArray.ElementType); }
            if (left is INamedTypeSymbol named && right is INamedTypeSymbol otherNamed)
            {
                if (CSharpTypes.ConstructorIdentity(named) != CSharpTypes.ConstructorIdentity(otherNamed) || named.TypeArguments.Length != otherNamed.TypeArguments.Length)
                { return false; }
                for (var i = 0; i < named.TypeArguments.Length; i++)
                { if (!SameLayoutType(named.TypeArguments[i], otherNamed.TypeArguments[i])) { return false; } }
                return named.ContainingType is null ? otherNamed.ContainingType is null
                    : otherNamed.ContainingType is not null && SameLayoutType(named.ContainingType, otherNamed.ContainingType);
            }
            return CSharpTypes.Display(left) == CSharpTypes.Display(right);
        }

        private bool RequiresUnmanagedMachineMember(IMethodSymbol member)
        {
            if (member is null)
            { throw new ArgumentNullException(nameof(member)); }
            return IsRequiredBuilderMember(member) && member.TypeParameters.Length != 0
                && member.TypeParameters[member.TypeParameters.Length - 1].HasUnmanagedTypeConstraint;
        }

        private static (CSharpCompilation Metadata, INamedTypeSymbol State) ReadBuilderState(Compilation compilation, IMethodSymbol source, byte[] image, CancellationToken cancellationToken)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (image is null)
            { throw new ArgumentNullException(nameof(image)); }
            var reference = MetadataReference.CreateFromImage(image);
            var metadata = CSharpCompilation.Create("HmStateLayout", references: compilation.References.Concat([reference]),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
            var assembly = metadata.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol
                ?? throw new InferenceException("Cannot inspect emitted state-machine assembly.");
            string MetadataName(INamedTypeSymbol type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                if (type.ContainingType is not null)
                { return MetadataName(type.ContainingType) + "+" + type.MetadataName; }
                var scopes = new Stack<string>();
                for (var scope = type.ContainingNamespace; !scope.IsGlobalNamespace; scope = scope.ContainingNamespace)
                { scopes.Push(scope.Name); }
                var prefix = string.Join(".", scopes);
                return (prefix.Length == 0 ? "" : prefix + ".") + type.MetadataName;
            }
            var owner = assembly.GetTypeByMetadataName(MetadataName(source.ContainingType))
                ?? throw new InferenceException("Cannot inspect state-machine method owner.");
            var matches = owner.GetMembers(source.MetadataName).OfType<IMethodSymbol>().Where(member =>
            {
                if (member is null)
                { throw new ArgumentNullException(nameof(member)); }
                if (member.Arity != source.Arity || member.Parameters.Length != source.Parameters.Length)
                { return false; }
                for (var i = 0; i < member.Parameters.Length; i++)
                { if (member.Parameters[i].RefKind != source.Parameters[i].RefKind || !SameLayoutType(member.Parameters[i].Type, source.Parameters[i].Type)) { return false; } }
                return true;
            }).ToArray();
            if (matches.Length != 1)
            { throw new InferenceException("Cannot uniquely identify emitted state-machine method."); }
            var attribute = matches[0].GetAttributes().FirstOrDefault(item =>
            { if (item is null) { throw new ArgumentNullException(nameof(item)); } return item.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"; });
            if (attribute is null || attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol state)
            {
                if (source.PartialDefinitionPart is not null)
                { return ReadPartialBuilderState(compilation, source, cancellationToken); }
                throw new InferenceException("Cannot inspect emitted async state machine.");
            }
            return (metadata, state.OriginalDefinition);
        }

        private static (CSharpCompilation Metadata, INamedTypeSymbol State) ReadPartialBuilderState(Compilation compilation,
            IMethodSymbol source, CancellationToken cancellationToken)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            var marker = "__hm_partial_layout_marker";
            while (compilation.SyntaxTrees.Any(tree =>
            { if (tree is null) { throw new ArgumentNullException(nameof(tree)); } return tree.GetText(cancellationToken).ToString().IndexOf(marker, StringComparison.Ordinal) >= 0; }))
            { marker += "_"; }
            var method = (MethodDeclarationSyntax)source.DeclaringSyntaxReferences.Single().GetSyntax(cancellationToken);
            var body = method.Body;
            if (body is null && method.ExpressionBody is { } expression)
            {
                var semantic = compilation.GetSemanticModel(method.SyntaxTree);
                var resultType = semantic.GetTypeInfo(expression.Expression, cancellationToken).Type;
                body = SyntaxFactory.Block(expression.Expression is ThrowExpressionSyntax thrown ? SyntaxFactory.ThrowStatement(thrown.Expression)
                    : resultType?.SpecialType == SpecialType.System_Void
                    ? SyntaxFactory.ExpressionStatement(expression.Expression) : SyntaxFactory.ReturnStatement(expression.Expression));
            }
            if (body is null)
            { throw new InferenceException("Partial async method has no inspectable body."); }
            var consume = "switch (" + marker + ") { default: break; }";
            var position = method.Body?.SpanStart ?? method.ExpressionBody!.Expression.SpanStart;
            if (method.SyntaxTree.GetLineVisibility(position, cancellationToken) == LineVisibility.Hidden)
            { consume = "\n#line default\n" + consume + "\n#line hidden\n"; }
            var probe = method.WithExpressionBody(null).WithSemicolonToken(default)
                .WithBody(body.WithStatements(body.Statements.Insert(0, SyntaxFactory.ParseStatement("const int " + marker + " = 0;"))
                    .Insert(1, SyntaxFactory.ParseStatement(consume))));
            var originalTree = method.SyntaxTree;
            var probeTree = originalTree.WithRootAndOptions(originalTree.GetRoot(cancellationToken).ReplaceNode(method, probe), originalTree.Options);
            compilation = compilation.ReplaceSyntaxTree(originalTree, probeTree);
            if (compilation.Options.SyntaxTreeOptionsProvider is { } sourceOptions)
            { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(sourceOptions, originalTree, probeTree))); }
            foreach (var tree in compilation.SyntaxTrees.ToArray())
            {
                var text = tree.GetText(cancellationToken);
                if (text.Encoding is not null)
                { continue; }
                var encoded = CSharpSyntaxTree.Create((CSharpSyntaxNode)tree.GetRoot(cancellationToken), (CSharpParseOptions)tree.Options, tree.FilePath, System.Text.Encoding.UTF8);
                compilation = compilation.ReplaceSyntaxTree(tree, encoded);
                if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
                { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, tree, encoded))); }
            }
            using var image = new System.IO.MemoryStream();
            using var symbols = new System.IO.MemoryStream();
            if (!compilation.Emit(image, pdbStream: symbols,
                options: new Microsoft.CodeAnalysis.Emit.EmitOptions(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb),
                cancellationToken: cancellationToken).Success)
            { throw new InferenceException("Cannot emit partial async layout probe."); }
            image.Position = 0;
            symbols.Position = 0;
            using var pe = new System.Reflection.PortableExecutable.PEReader(image);
            var definitions = pe.GetMetadataReader();
            using var pdb = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(symbols);
            var debug = pdb.GetMetadataReader();
            string Name(System.Reflection.Metadata.TypeDefinitionHandle handle)
            {
                if (handle.IsNil)
                { throw new ArgumentException("A type definition is required.", nameof(handle)); }
                var type = definitions.GetTypeDefinition(handle);
                var parent = type.GetDeclaringType();
                if (!parent.IsNil)
                { return Name(parent) + "+" + definitions.GetString(type.Name); }
                var scope = definitions.GetString(type.Namespace);
                return (scope.Length == 0 ? "" : scope + ".") + definitions.GetString(type.Name);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scopeHandle in debug.LocalScopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scope = debug.GetLocalScope(scopeHandle);
                foreach (var constantHandle in scope.GetLocalConstants())
                {
                    if (debug.GetString(debug.GetLocalConstant(constantHandle).Name) != marker)
                    { continue; }
                    if (debug.GetMethodDebugInformation(scope.Method).GetStateMachineKickoffMethod().IsNil)
                    { continue; }
                    names.Add(Name(definitions.GetMethodDefinition(scope.Method).GetDeclaringType()));
                }
            }
            if (names.Count != 1)
            { throw new InferenceException("Cannot uniquely identify partial async layout state."); }
            var reference = MetadataReference.CreateFromImage(image.ToArray());
            var metadata = CSharpCompilation.Create("HmPartialLayout", references: compilation.References.Concat([reference]),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
            var assembly = metadata.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            var state = assembly?.GetTypeByMetadataName(names.Single())
                ?? throw new InferenceException("Cannot resolve partial async layout state.");
            return (metadata, state.OriginalDefinition);
        }

        private bool RequiresUnmanagedMachine(CSharpCompilation metadata, INamedTypeSymbol state)
        {
            if (metadata is null)
            { throw new ArgumentNullException(nameof(metadata)); }
            if (state is null)
            { throw new ArgumentNullException(nameof(state)); }
            var fields = state.GetMembers().OfType<IFieldSymbol>().Where(field =>
            { if (field is null) { throw new ArgumentNullException(nameof(field)); } return !field.IsStatic; }).ToArray();
            var builder = fields.FirstOrDefault(field =>
            { if (field is null) { throw new ArgumentNullException(nameof(field)); } return field.Name == "<>t__builder"; })?.Type as INamedTypeSymbol
                ?? throw new InferenceException("Cannot inspect emitted async builder field.");
            var active = builder.GetMembers("Start").OfType<IMethodSymbol>().Any(RequiresUnmanagedMachineMember);
            var critical = metadata.GetTypeByMetadataName("System.Runtime.CompilerServices.ICriticalNotifyCompletion");
            foreach (var field in fields)
            {
                if (!field.Name.StartsWith("<>u__", StringComparison.Ordinal))
                { continue; }
                var unsafeAwait = critical is not null && metadata.ClassifyCommonConversion(field.Type, critical).IsImplicit;
                var memberName = unsafeAwait ? "AwaitUnsafeOnCompleted" : "AwaitOnCompleted";
                active |= builder.GetMembers(memberName).OfType<IMethodSymbol>().Any(RequiresUnmanagedMachineMember);
            }
            return active;
        }

        private static MethodDeclarationSyntax StrengthenBuilderLayoutProbe(MethodDeclarationSyntax proposal, Dictionary<HmType, string> variables)
        {
            if (proposal is null)
            { throw new ArgumentNullException(nameof(proposal)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            var probe = proposal;
            foreach (var variable in variables)
            {
                if (variable.Key.IsValueType == false)
                { continue; }
                var existing = probe.ConstraintClauses.FirstOrDefault(clause =>
                { if (clause is null) { throw new ArgumentNullException(nameof(clause)); } return clause.Name.Identifier.ValueText == variable.Value; });
                var retained = existing?.Constraints.Where(constraint =>
                { if (constraint is null) { throw new ArgumentNullException(nameof(constraint)); } return constraint.ToString() is not ("struct" or "unmanaged" or "notnull" or "new()"); }).Select(constraint =>
                { if (constraint is null) { throw new ArgumentNullException(nameof(constraint)); } return constraint.ToString(); }).ToArray() ?? [];
                var declaration = SyntaxFactory.ParseMemberDeclaration("void __Layout<" + variable.Value + ">() where " + variable.Value + " : unmanaged" + (retained.Length == 0 ? "" : ", " + string.Join(", ", retained)) + "{}") as MethodDeclarationSyntax
                    ?? throw new InferenceException("Cannot construct state-machine layout probe.");
                probe = probe.WithConstraintClauses(existing is null ? probe.ConstraintClauses.Add(declaration.ConstraintClauses[0])
                    : probe.ConstraintClauses.Replace(existing, declaration.ConstraintClauses[0]));
            }
            return probe;
        }

        private bool InferRootBuilderLayoutConstraints(Compilation compilation, MethodDeclarationSyntax proposal, Dictionary<HmType, string> variables)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (proposal is null)
            { throw new ArgumentNullException(nameof(proposal)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            if (asyncBuilder is null || !proposal.Modifiers.Any(SyntaxKind.AsyncKeyword))
            { return false; }
            if (!asyncBuilder.GetMembers().OfType<IMethodSymbol>().Any(RequiresUnmanagedMachineMember))
            { return false; }
            using var originalImage = new System.IO.MemoryStream();
            if (!model.Compilation.Emit(originalImage, cancellationToken: cancellationToken).Success)
            { return false; }
            var originalSource = currentMethod ?? throw new InferenceException("Missing original state-machine method.");
            var originalLayout = ReadBuilderState(model.Compilation, originalSource, originalImage.ToArray(), cancellationToken);
            if (!RequiresUnmanagedMachine(originalLayout.Metadata, originalLayout.State))
            { return false; }
            using var image = new System.IO.MemoryStream();
            if (!compilation.Emit(image, cancellationToken: cancellationToken).Success)
            {
                // A compiler may enforce the synthesized unmanaged constraint
                // before exposing its fields. Strengthen only this temporary probe;
                // the proposal receives requirements derived from actual storage.
                var probe = StrengthenBuilderLayoutProbe(proposal, variables);
                var sourceTree = proposal.SyntaxTree;
                var probeMarker = new SyntaxAnnotation("HM.LayoutProbe");
                probe = probe.WithAdditionalAnnotations(probeMarker);
                var probeRoots = RewriteMethodParts(proposal, probe, compilation.GetSemanticModel(sourceTree), cancellationToken);
                SyntaxTree? probeTree = null;
                foreach (var entry in probeRoots)
                {
                    var rewrittenTree = entry.Key.WithRootAndOptions(entry.Value, entry.Key.Options);
                    compilation = compilation.ReplaceSyntaxTree(entry.Key, rewrittenTree);
                    if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
                    { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, entry.Key, rewrittenTree))); }
                    if (entry.Key == sourceTree)
                    { probeTree = rewrittenTree; }
                }

                image.SetLength(0);
                image.Position = 0;
                if (!compilation.Emit(image, cancellationToken: cancellationToken).Success)
                { return false; }
                proposal = probeTree!.GetRoot(cancellationToken).GetAnnotatedNodes(probeMarker).OfType<MethodDeclarationSyntax>().Single();
            }
            var source = compilation.GetSemanticModel(proposal.SyntaxTree).GetDeclaredSymbol(proposal, cancellationToken)
                ?? throw new InferenceException("Cannot resolve state-machine source method.");
            var layout = ReadBuilderState(compilation, source, image.ToArray(), cancellationToken);
            var state = layout.State;
            if (!RequiresUnmanagedMachine(layout.Metadata, state))
            { return false; }
            return AddBuilderStorageConstraints(state, variables);
        }

        private void InspectUnmanagedStorage(ITypeSymbol type, HashSet<ITypeSymbol> visited, Action<ITypeParameterSymbol> require)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            if (visited is null)
            { throw new ArgumentNullException(nameof(visited)); }
            if (require is null)
            { throw new ArgumentNullException(nameof(require)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(type))
            { return; }
            if (type is ITypeParameterSymbol parameter)
            { require(parameter); return; }
            if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Enum)
            { return; }
            if (type is not INamedTypeSymbol { IsValueType: true } value)
            { throw new InferenceException("Unmanaged state-machine storage contains a managed type."); }
            foreach (var field in value.GetMembers().OfType<IFieldSymbol>())
            { if (!field.IsStatic) { InspectUnmanagedStorage(field.Type, visited, require); } }
        }
        private bool AddBuilderStorageConstraints(INamedTypeSymbol state, Dictionary<HmType, string> variables)
        {
            if (state is null)
            { throw new ArgumentNullException(nameof(state)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            var fields = state.GetMembers().OfType<IFieldSymbol>().Where(field =>
            { if (field is null) { throw new ArgumentNullException(nameof(field)); } return !field.IsStatic; }).ToArray();
            if (state.TypeKind != TypeKind.Struct)
            { throw new InferenceException("The builder requires an unmanaged state machine but lowering produces a class."); }
            var byName = variables.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
            var required = new HashSet<HmType>();
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var field in fields)
            { InspectUnmanagedStorage(field.Type, visited, parameter => { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } if (byName.TryGetValue(parameter.Name, out var variable)) { required.Add(variable); } }); }
            var added = false;
            foreach (var variable in required)
            {
                if (inferredConstraints.Any(requirement =>
                    { if (requirement.Variable is null) { throw new ArgumentException("Expected a constraint owner.", nameof(requirement)); } return ReferenceEquals(ConversionCore(requirement.Variable), variable.Root) && requirement.Keyword == "unmanaged"; }))
                { continue; }
                RequireKeyword(variable, "unmanaged");
                added = true;
            }
            return added;
        }
    }
}
