using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private INamedTypeSymbol? asyncBuilder;
        private bool builderUsesResult;
        private HmType? asyncBuilderResult;
        private bool needsBuilderEmission;

        private static AttributeData? BuilderAttribute(IMethodSymbol method, INamedTypeSymbol? returned)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (method.ReturnsVoid)
            { return null; }
            foreach (var attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncMethodBuilderAttribute")
                { return attribute; }
            }
            if (returned is not null)
            {
                foreach (var attribute in returned.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncMethodBuilderAttribute")
                    { return attribute; }
                }
            }
            return null;
        }

        private static (INamedTypeSymbol Type, bool UsesResult) ConstructBuilder(AttributeData attribute, INamedTypeSymbol? returned)
        {
            if (attribute is null)
            { throw new ArgumentNullException(nameof(attribute)); }
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol builder)
            { throw new InferenceException("Unresolved async method builder attribute."); }
            if (!builder.IsUnboundGenericType)
            { return (builder, false); }
            if (returned is null || builder.Arity != returned.Arity || builder.ContainingType is { IsGenericType: true })
            { throw new InferenceException("Invalid unbound async builder shape."); }
            return (builder.ConstructedFrom.Construct(returned.TypeArguments.ToArray()), returned.Arity == 1);
        }

        private void ConfigureBuilder(IMethodSymbol method, INamedTypeSymbol? returned)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            var attribute = BuilderAttribute(method, returned);
            if (attribute is not null)
            {
                (asyncBuilder, builderUsesResult) = ConstructBuilder(attribute, returned);
                asyncBuilderResult = builderUsesResult ? HmType.Variable() : null;
                needsBuilderEmission = true;
                var start = asyncBuilder.GetMembers("Start").OfType<IMethodSymbol>().SingleOrDefault(IsRequiredBuilderMember);
                if (start is not null)
                { BindBuilderStateMachine(start); }
                return;
            }
            if (returned is not null && (returned.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks"
                || returned.Name is not ("Task" or "ValueTask")))
            { throw new InferenceException("Async return type has no method builder."); }
        }

        private void BindBuilderStateMachine(IMethodSymbol method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (asyncBuilder is null || !builderUsesResult || asyncBuilderResult is null)
            { return; }
            var resultParameter = asyncBuilder.OriginalDefinition.TypeParameters[0];
            var machine = method.OriginalDefinition.TypeParameters[method.Arity - 1];
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            bool DependsOnResult(ITypeSymbol type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(type))
                { return false; }
                if (SymbolEqualityComparer.Default.Equals(type, resultParameter))
                { return true; }
                return type switch
                {
                    ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(DependsOnResult),
                    IArrayTypeSymbol array => DependsOnResult(array.ElementType),
                    INamedTypeSymbol named => named.TypeArguments.Any(DependsOnResult)
                        || named.ContainingType is not null && DependsOnResult(named.ContainingType),
                    _ => false
                };
            }
            if (machine.ConstraintTypes.Any(DependsOnResult))
            {
                // A generated state machine cannot become a caller-supplied
                // result subtype. Preserve this required conversion endpoint.
                HmType.Unify(asyncBuilderResult, CSharpTypes.Rigid(asyncBuilder.TypeArguments[0]));
            }
        }
        private void BindBuilderReturn(HmType result, HmType value)
        {
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (asyncBuilder is null)
            { return; }
            if (asyncBuilderResult is not null)
            { HmType.Unify(asyncBuilderResult, value); }
            var definition = asyncBuilder.OriginalDefinition;
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var i = 0; i < definition.TypeParameters.Length; i++)
            {
                var parameter = definition.TypeParameters[i];
                mapping.Add(parameter, builderUsesResult ? value : CSharpTypes.Rigid(asyncBuilder.TypeArguments[i]));
                ImportConstraints(parameter, mapping[parameter], mapping, asyncBuilder.TypeArguments[i]);
            }
            // Builder-required members are declared on the builder itself, not
            // inherited. Their container may be nested without entering the API.
            var task = asyncBuilder.GetMembers("Task").OfType<IPropertySymbol>().FirstOrDefault(property =>
            {
                if (property is null)
                { throw new ArgumentNullException(nameof(property)); }
                return !property.IsStatic && property.Parameters.Length == 0 && property.GetMethod is not null;
            }) ?? throw new InferenceException("Async builder has no declared readable Task property.");
            UnifyRuntimeTypes(result, CSharpTypes.Rigid(task.OriginalDefinition.Type, mapping));
        }

        private void BindBuilderAwaiter(ITypeSymbol source, HmType shape)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (asyncBuilder is null)
            { return; }
            var critical = model.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ICriticalNotifyCompletion");
            var unsafeAwait = critical is not null && ((CSharpCompilation)model.Compilation).ClassifyConversion(source, critical).IsImplicit;
            var name = unsafeAwait ? "AwaitUnsafeOnCompleted" : "AwaitOnCompleted";
            var method = asyncBuilder.GetMembers(name).OfType<IMethodSymbol>().SingleOrDefault(IsRequiredBuilderMember);
            if (method is null)
            { throw new InferenceException("Missing custom-builder await member."); }
            BindBuilderStateMachine(method);
            var definition = method.OriginalDefinition;
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var owner = asyncBuilder; owner is not null; owner = owner.ContainingType)
            {
                for (var i = 0; i < owner.Arity; i++)
                { mapping[owner.OriginalDefinition.TypeParameters[i]] = SymbolEqualityComparer.Default.Equals(owner, asyncBuilder) && builderUsesResult ? asyncBuilderResult! : CSharpTypes.Rigid(owner.TypeArguments[i]); }
            }
            mapping[definition.TypeParameters[0]] = shape;
            mapping[definition.TypeParameters[1]] = HmType.Variable();
            ImportConstraints(definition.TypeParameters[0], shape, mapping, source);
            for (var i = 0; i < definition.TypeParameters[0].ConstraintTypes.Length; i++)
            {
                var expected = CSharpTypes.Rigid(definition.TypeParameters[0].ConstraintTypes[i], mapping);
                BindSubtype(source, method.TypeParameters[0].ConstraintTypes[i], shape, expected, false);
            }
        }
        private static void UnifyRuntimeTypes(HmType left, HmType right)
        {
            if (left is null)
            { throw new ArgumentNullException(nameof(left)); }
            if (right is null)
            { throw new ArgumentNullException(nameof(right)); }
            left = left.Root;
            right = right.Root;
            while (left.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
            { left = left.Arguments[0].Root; }
            while (right.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
            { right = right.Arguments[0].Root; }
            if (!left.IsVariable && !right.IsVariable && left.Name == right.Name && left.Arguments.Length == right.Arguments.Length)
            {
                for (var i = 0; i < left.Arguments.Length; i++)
                { UnifyRuntimeTypes(left.Arguments[i], right.Arguments[i]); }
            }
            else
            { HmType.Unify(left, right); }
        }

        private (bool Valid, Dictionary<string, int> Warnings) BuilderConstraintDiagnostics(MethodDeclarationSyntax method, SemanticModel semantic)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            var builders = new List<INamedTypeSymbol>();
            void Add(IMethodSymbol symbol)
            {
                if (symbol is null)
                { throw new ArgumentNullException(nameof(symbol)); }
                if (!symbol.IsAsync)
                { return; }
                var returned = symbol.ReturnsVoid ? null : symbol.ReturnType as INamedTypeSymbol;
                var attribute = BuilderAttribute(symbol, returned);
                if (attribute is not null)
                { builders.Add(ConstructBuilder(attribute, returned).Type); }
            }
            Add(semantic.GetDeclaredSymbol(method, cancellationToken)!);
            foreach (var lambda in method.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            {
                var local = false;
                foreach (var owner in lambda.Ancestors())
                {
                    if (ReferenceEquals(owner, method))
                    { break; }
                    if (owner is LocalFunctionStatementSyntax)
                    { local = true; break; }
                }
                if (!local && semantic.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation function)
                { Add(function.Symbol); }
            }
            var warnings = new Dictionary<string, int>(StringComparer.Ordinal);
            if (builders.Count == 0)
            { return (true, warnings); }
            var names = new HashSet<string>(method.DescendantTokens().Select((SyntaxToken /* SPINT001: all values valid - ValueText accepts every token. */ token) => token.ValueText), StringComparer.Ordinal);
            var statements = new List<StatementSyntax>();
            for (var i = 0; i < builders.Count; i++)
            {
                var name = "__hmBuilder" + i;
                while (!names.Add(name))
                { name += "_"; }
                var type = SyntaxFactory.ParseTypeName(CSharpTypes.Display(builders[i])).WithAdditionalAnnotations(new SyntaxAnnotation("HmBuilderConstraint", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                statements.Add(SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(type,
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name)))));
            }
            statements.Add(SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("global::System.Exception")).WithArgumentList(SyntaxFactory.ArgumentList())));
            var probe = method.WithBody(SyntaxFactory.Block(statements)).WithExpressionBody(null).WithSemicolonToken(default);
            var tree = method.SyntaxTree;
            var changed = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(method, probe), tree.Options);
            var compilation = semantic.Compilation.ReplaceSyntaxTree(tree, changed);
            var types = changed.GetRoot(cancellationToken).GetAnnotatedNodes("HmBuilderConstraint").OfType<TypeSyntax>().ToArray();
            // C# does not check every constraint of implicitly constructed builder
            // types. Explicit type usages in the same generic scope do check them.
            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                if (diagnostic.Location.SourceTree != changed)
                { continue; }
                foreach (var type in types)
                {
                    if (!type.Span.Contains(diagnostic.Location.SourceSpan))
                    { continue; }
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    { return (false, warnings); }
                    if (diagnostic.Severity != DiagnosticSeverity.Warning)
                    { continue; }
                    var key = type.GetAnnotations("HmBuilderConstraint").Single().Data + ":" + diagnostic.Id;
                    warnings.TryGetValue(key, out var count);
                    warnings[key] = count + 1;
                }
            }
            return (true, warnings);
        }

        private bool IsRequiredBuilderMember(IMethodSymbol method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (method.Name == "Create")
            { return method.IsStatic && method.Arity == 0 && method.Parameters.Length == 0 && method.ReturnType is INamedTypeSymbol returned && SymbolEqualityComparer.Default.Equals(returned.OriginalDefinition, method.ContainingType.OriginalDefinition); }
            if (method.IsStatic || !method.ReturnsVoid)
            { return false; }
            if (method.Name == "SetResult" && method.Arity == 0)
            {
                return method.ContainingType.Arity == 0 ? method.Parameters.Length == 0
                    : method.ContainingType.Arity == 1 && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType.TypeParameters[0]);
            }
            var protocolArity = method.Name == "Start" ? 1 : method.Name is "AwaitOnCompleted" or "AwaitUnsafeOnCompleted" ? 2 : 0;
            if (protocolArity != 0)
            {
                if (method.Arity != protocolArity || method.Parameters.Length != protocolArity)
                { return false; }
                for (var i = 0; i < protocolArity; i++)
                {
                    if (method.Parameters[i].RefKind != RefKind.Ref || !SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, method.TypeParameters[i]))
                    { return false; }
                }
                return true;
            }
            if (method.Arity != 0 || method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None)
            { return false; }
            var metadataName = method.Name == "SetException" ? "System.Exception"
                : method.Name == "SetStateMachine" ? "System.Runtime.CompilerServices.IAsyncStateMachine" : null;
            return metadataName is not null && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, model.Compilation.GetTypeByMetadataName(metadataName));
        }
        private bool HasNestedAsyncBuilder(MethodDeclarationSyntax method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            foreach (var node in method.DescendantNodes())
            {
                IMethodSymbol? symbol = node is LocalFunctionStatementSyntax local ? model.GetDeclaredSymbol(local, cancellationToken)
                    : node is AnonymousFunctionExpressionSyntax lambda && model.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation function ? function.Symbol : null;
                if (symbol is { IsAsync: true } && BuilderAttribute(symbol, symbol.ReturnType as INamedTypeSymbol) is not null)
                { return true; }
            }
            return false;
        }
        private bool IsUsedBuilderMember(IMethodSymbol method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (method.Name is not ("Create" or "SetResult" or "SetException" or "SetStateMachine" or "Start" or "AwaitOnCompleted" or "AwaitUnsafeOnCompleted"))
            { return false; }
            bool Uses(IMethodSymbol symbol)
            {
                if (symbol is null)
                { throw new ArgumentNullException(nameof(symbol)); }
                if (!symbol.IsAsync)
                { return false; }
                var attribute = BuilderAttribute(symbol, symbol.ReturnType as INamedTypeSymbol);
                return attribute?.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Kind == TypedConstantKind.Type && attribute.ConstructorArguments[0].Value is INamedTypeSymbol builder
                    && SymbolEqualityComparer.Default.Equals(builder.OriginalDefinition, method.ContainingType.OriginalDefinition);
            }
            foreach (var tree in model.Compilation.SyntaxTrees)
            {
                var semantic = model.Compilation.GetSemanticModel(tree);
                foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
                {
                    if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax && semantic.GetDeclaredSymbol(node, cancellationToken) is INamedTypeSymbol type)
                    {
                        foreach (var attribute in type.GetAttributes())
                        {
                            if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncMethodBuilderAttribute"
                                && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Kind == TypedConstantKind.Type
                                && attribute.ConstructorArguments[0].Value is INamedTypeSymbol builder
                                && SymbolEqualityComparer.Default.Equals(builder.OriginalDefinition, method.ContainingType.OriginalDefinition))
                            { return true; }
                        }
                    }
                    if (node is MethodDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.AsyncKeyword)
                        && semantic.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol symbol && Uses(symbol))
                    { return true; }
                    if (node is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.AsyncKeyword)
                        && semantic.GetDeclaredSymbol(local, cancellationToken) is IMethodSymbol localSymbol && Uses(localSymbol))
                    { return true; }
                    if (node is AnonymousFunctionExpressionSyntax lambda && semantic.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation function && Uses(function.Symbol))
                    { return true; }
                }
            }
            return false;
        }
        private string? ValidateBuilderEmission(MethodDeclarationSyntax method, MethodDeclarationSyntax changedMethod, SemanticModel changedModel)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedModel is null)
            { throw new ArgumentNullException(nameof(changedModel)); }
            var before = BuilderConstraintDiagnostics(method, model);
            var after = BuilderConstraintDiagnostics(changedMethod, changedModel);
            if (!before.Valid)
            { throw new InferenceException("Original async-builder type arguments violate constraints."); }
            if (!after.Valid)
            { return "Generalization violates async-builder type constraints."; }
            foreach (var warning in after.Warnings)
            {
                if (!before.Warnings.TryGetValue(warning.Key, out var count) || warning.Value > count)
                { return "Generalization introduces async-builder constraint warnings."; }
            }
            using var originalImage = new System.IO.MemoryStream();
            var original = model.Compilation.Emit(originalImage, cancellationToken: cancellationToken);
            if (!original.Success)
            { throw new InferenceException("Original custom-builder compilation cannot be emitted."); }
            using var candidateImage = new System.IO.MemoryStream();
            var candidate = changedModel.Compilation.Emit(candidateImage, cancellationToken: cancellationToken);
            if (!candidate.Success)
            { return "Generalization fails async-builder emission."; }
            return IntroducesCompilerWarnings(changedModel.Compilation, original.Diagnostics, candidate.Diagnostics)
                ? "Generalization introduces async-builder emission warnings." : null;
        }
    }
}
