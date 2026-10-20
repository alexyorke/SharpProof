using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static ITypeSymbol ResourceCoreType(ITypeSymbol type)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
        }

        private static IMethodSymbol? ResourcePattern(SemanticModel semantic, SyntaxNode site, ITypeSymbol type, bool asynchronous)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (site is null)
            { throw new ArgumentNullException(nameof(site)); }
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            if (!asynchronous && !type.IsRefLikeType)
            { return null; }
            var name = asynchronous ? "DisposeAsync" : "Dispose";
            var spelling = CSharpTypes.Render(CSharpTypes.Rigid(type), type, [], true, semantic, site.SpanStart);
            var invocation = SyntaxFactory.ParseExpression("default(" + spelling + ")!." + name + "()");
            var selected = semantic.GetSpeculativeSymbolInfo(site.SpanStart, invocation, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
            return selected is { IsStatic: false, IsExtensionMethod: false } ? selected : null;
        }

        private static (IMethodSymbol Method, bool Pattern) ResourceMethod(SemanticModel semantic, SyntaxNode site, ITypeSymbol type, bool asynchronous)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (site is null)
            { throw new ArgumentNullException(nameof(site)); }
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            type = ResourceCoreType(type);
            var contract = semantic.Compilation.GetTypeByMetadataName(asynchronous ? "System.IAsyncDisposable" : "System.IDisposable");
            var member = contract?.GetMembers(asynchronous ? "DisposeAsync" : "Dispose").OfType<IMethodSymbol>().FirstOrDefault(method =>
            {
                if (method is null)
                { throw new ArgumentNullException(nameof(method)); }
                return !method.IsStatic && method.Parameters.Length == 0 && method.TypeParameters.Length == 0;
            });
            if (!asynchronous && type is IDynamicTypeSymbol && member is not null)
            {
                // Synchronous dynamic resources convert to IDisposable at runtime.
                return (member, false);
            }
            var selected = ResourcePattern(semantic, site, type, asynchronous);
            if (selected is not null)
            {
                // A valid disposal pattern does not require the framework interface.
                if (member is null)
                { return (selected, true); }
                var implementation = (type as INamedTypeSymbol)?.FindImplementationForInterfaceMember(member) as IMethodSymbol;
                if (!type.IsRefLikeType && SymbolEqualityComparer.Default.Equals(implementation?.OriginalDefinition, selected.OriginalDefinition))
                { return (member, false); }
                if (selected.ContainingType.TypeKind == TypeKind.Interface && SymbolEqualityComparer.Default.Equals(selected.OriginalDefinition, member.OriginalDefinition))
                { return (member, false); }
                return (selected, true);
            }
            if (contract is null || member is null || !((CSharpCompilation)semantic.Compilation).ClassifyConversion(type, contract).IsImplicit)
            { throw new InferenceException("Unresolved disposal contract."); }
            return (member, false);
        }

        private void InferResource(ExpressionSyntax expression, SyntaxNode site, bool asynchronous, HmType? expected = null, ITypeSymbol? declaredType = null)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (site is null)
            { throw new ArgumentNullException(nameof(site)); }
            var source = model.GetTypeInfo(expression, cancellationToken).Type ?? model.GetTypeInfo(expression, cancellationToken).ConvertedType
                ?? throw new InferenceException("Untyped resource expression.");
            var type = declaredType ?? source;
            var shape = Expression(expression);
            if (expected is not null)
            { BindConvertedArgument(source, type, shape, expected); shape = expected; }
            ImportConstructedConstraints(type, shape);
            var core = ResourceCoreType(type);
            var coreShape = ConversionCore(shape);
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            { coreShape = coreShape.Arguments[0].Root; }
            var selected = ResourceMethod(model, site, type, asynchronous);
            if (!selected.Pattern)
            {
                if (coreShape.IsVariable)
                { RequireType(coreShape, selected.Method.ContainingType); }
            }
            else
            {
                if (coreShape.IsVariable)
                { HmType.Unify(coreShape, CSharpTypes.Rigid(core)); }
            }
            if (asynchronous)
            {
                var returned = selected.Pattern
                    ? AwaitProtocolResult(core, coreShape, selected.Method) : CSharpTypes.Rigid(selected.Method.ReturnType);
                var awaited = ImplicitAwaitInfo(model, site, selected.Method);
                if (!awaited.HasValue)
                { HmType.Unify(returned, CSharpTypes.Rigid(selected.Method.ReturnType)); return; }
                var awaiter = AwaitProtocolResult(selected.Method.ReturnType, returned, awaited.Value.GetAwaiterMethod!);
                BindBuilderAwaiter(awaited.Value.GetAwaiterMethod!.ReturnType, awaiter);
                HmType.Unify(PatternSymbolType(awaited.Value.IsCompletedProperty, awaiter, awaited.Value.GetAwaiterMethod.ReturnType), new("bool"));
                AwaitProtocolResult(awaited.Value.GetAwaiterMethod.ReturnType, awaiter, awaited.Value.GetResultMethod!);
            }
        }

        private static IEnumerable<(SyntaxNode Site, ITypeSymbol Type, bool Asynchronous)> ResourceSites(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            foreach (var node in root.DescendantNodes())
            {
                var declaration = node is UsingStatementSyntax statement ? statement.Declaration
                    : node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } local ? local.Declaration : null;
                var asynchronous = node is UsingStatementSyntax usingStatement ? usingStatement.AwaitKeyword != default
                    : node is LocalDeclarationStatementSyntax localDeclaration && localDeclaration.AwaitKeyword != default;
                if (declaration is not null)
                {
                    foreach (var variable in declaration.Variables)
                    {
                        if (semantic.GetDeclaredSymbol(variable) is ILocalSymbol symbol)
                        { yield return (node, symbol.Type, asynchronous); }
                    }
                }
                else if (node is UsingStatementSyntax { Expression: not null } expressionStatement)
                {
                    var info = semantic.GetTypeInfo(expressionStatement.Expression);
                    if ((info.Type ?? info.ConvertedType) is { } type)
                    { yield return (node, type, asynchronous); }
                }
            }
        }

        private bool PreservesResourceBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            var left = ResourceSites(before, beforeModel).ToArray();
            var right = ResourceSites(after, afterModel).ToArray();
            if (left.Length != right.Length)
            { return false; }
            for (var i = 0; i < left.Length; i++)
            {
                var original = ResourceMethod(beforeModel, left[i].Site, left[i].Type, left[i].Asynchronous);
                var candidate = ResourceMethod(afterModel, right[i].Site, right[i].Type, right[i].Asynchronous);
                if (left[i].Asynchronous != right[i].Asynchronous || original.Pattern != candidate.Pattern
                    || !SameAwaitMethod(original.Method, candidate.Method, changedMethod, changedTree))
                { return false; }
                if (left[i].Asynchronous && !SameImplicitAwait(beforeModel, afterModel, left[i].Site, right[i].Site, original.Method, candidate.Method, changedMethod, changedTree))
                { return false; }
            }
            return true;
        }
    }
}
