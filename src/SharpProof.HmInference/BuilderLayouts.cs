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

        private static (CSharpCompilation Metadata, INamedTypeSymbol State) ReadBuilderState(Compilation compilation, IMethodSymbol source, byte[] image)
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
            { throw new InferenceException("Cannot inspect emitted async state machine."); }
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
            var originalLayout = ReadBuilderState(model.Compilation, originalSource, originalImage.ToArray());
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
                var probeTree = sourceTree.WithRootAndOptions(sourceTree.GetRoot(cancellationToken).ReplaceNode(proposal, probe), sourceTree.Options);
                compilation = compilation.ReplaceSyntaxTree(sourceTree, probeTree);
                if (compilation.Options.SyntaxTreeOptionsProvider is { } provider)
                { compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(provider, sourceTree, probeTree))); }
                image.SetLength(0);
                image.Position = 0;
                if (!compilation.Emit(image, cancellationToken: cancellationToken).Success)
                { return false; }
                proposal = probeTree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
                { if (method is null) { throw new ArgumentNullException(nameof(method)); } return method.SpanStart == proposal.SpanStart; });
            }
            var source = compilation.GetSemanticModel(proposal.SyntaxTree).GetDeclaredSymbol(proposal, cancellationToken)
                ?? throw new InferenceException("Cannot resolve state-machine source method.");
            var layout = ReadBuilderState(compilation, source, image.ToArray());
            var state = layout.State;
            if (!RequiresUnmanagedMachine(layout.Metadata, state))
            { return false; }
            return AddBuilderStorageConstraints(state, variables);
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
            void Inspect(ITypeSymbol type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(type))
                { return; }
                if (type is ITypeParameterSymbol parameter)
                { if (byName.TryGetValue(parameter.Name, out var variable)) { required.Add(variable); } return; }
                if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Enum)
                { return; }
                if (type is not INamedTypeSymbol { IsValueType: true } value)
                { throw new InferenceException("The emitted state machine contains managed storage despite an unmanaged builder requirement."); }
                foreach (var field in value.GetMembers().OfType<IFieldSymbol>())
                { if (!field.IsStatic) { Inspect(field.Type); } }
            }
            foreach (var field in fields)
            { Inspect(field.Type); }
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
