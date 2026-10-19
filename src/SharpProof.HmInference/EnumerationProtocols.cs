using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private Dictionary<ITypeParameterSymbol, HmType> EnumerationMapping(CommonForEachStatementSyntax loop, IMethodSymbol selected)
        {
            if (loop is null)
            { throw new ArgumentNullException(nameof(loop)); }
            if (selected is null)
            { throw new ArgumentNullException(nameof(selected)); }
            var full = selected.ReducedFrom ?? selected;
            if (!full.IsStatic)
            { return ReceiverMapping(selected, loop.Expression); }
            var definition = full.OriginalDefinition;
            if (!full.IsExtensionMethod || definition.Parameters.Length == 0)
            { throw new InferenceException("Unsupported extension enumerator signature."); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            foreach (var parameter in definition.TypeParameters)
            { mapping.Add(parameter, HmType.Variable()); }
            var source = model.GetTypeInfo(loop.Expression, cancellationToken).Type
                ?? throw new InferenceException("Unresolved extension enumeration receiver.");
            var expected = CSharpTypes.Rigid(definition.Parameters[0].Type, mapping);
            BindConvertedArgument(source, full.Parameters[0].Type, Expression(loop.Expression), expected);
            foreach (var parameter in definition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, full.TypeArguments[parameter.Ordinal]); }
            return mapping;
        }

        private static AwaitExpressionInfo? ImplicitAwaitInfo(SemanticModel semantic, SyntaxNode loop, IMethodSymbol? method)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (loop is null)
            { throw new ArgumentNullException(nameof(loop)); }
            if (method is null || semantic.IsSpeculativeSemanticModel)
            { return null; }
            // Bind the protocol return type in the real loop scope, including using
            // directives, extension overloads and the candidate's generic constraints.
            var statement = (ExpressionStatementSyntax)SyntaxFactory.ParseStatement("await default(" + CSharpTypes.Display(method.ReturnType) + ")!;");
            if (!semantic.TryGetSpeculativeSemanticModel(loop.SpanStart, statement, out var speculative))
            { return null; }
            var info = speculative.GetAwaitExpressionInfo((AwaitExpressionSyntax)statement.Expression);
            return info.IsDynamic || info.GetAwaiterMethod is null || info.GetResultMethod is null || info.IsCompletedProperty is null ? null : info;
        }

        private void BindImplicitAwait(CommonForEachStatementSyntax loop, IMethodSymbol? method, Dictionary<ITypeParameterSymbol, HmType> mapping, bool booleanResult)
        {
            if (loop is null)
            { throw new ArgumentNullException(nameof(loop)); }
            if (mapping is null)
            { throw new ArgumentNullException(nameof(mapping)); }
            if (method is null)
            { return; }
            var shape = CSharpTypes.Rigid(method.OriginalDefinition.ReturnType, mapping);
            var info = ImplicitAwaitInfo(model, loop, method);
            if (!info.HasValue)
            { HmType.Unify(shape, CSharpTypes.Rigid(method.ReturnType)); return; }
            try
            {
                var awaited = info.Value;
                var awaiter = AwaitProtocolResult(method.ReturnType, shape, awaited.GetAwaiterMethod!);
                BindBuilderAwaiter(awaited.GetAwaiterMethod!.ReturnType, awaiter);
                HmType.Unify(PatternSymbolType(awaited.IsCompletedProperty, awaiter, awaited.GetAwaiterMethod!.ReturnType), new HmType("bool"));
                var result = AwaitProtocolResult(awaited.GetAwaiterMethod.ReturnType, awaiter, awaited.GetResultMethod!);
                if (booleanResult)
                { HmType.Unify(result, new HmType("bool")); }
            }
            catch (InferenceException)
            {
                // Internal nested/ref-like awaitables can be unrepresentable in
                // the adapter even though the source parameter remains supported.
                HmType.Unify(shape, CSharpTypes.Rigid(method.ReturnType));
            }
        }

        private static void MapEnumerationOwner(INamedTypeSymbol returned, INamedTypeSymbol owner, Dictionary<ITypeParameterSymbol, HmType> mapping)
        {
            if (returned is null)
            { throw new ArgumentNullException(nameof(returned)); }
            if (owner is null)
            { throw new ArgumentNullException(nameof(owner)); }
            if (mapping is null)
            { throw new ArgumentNullException(nameof(mapping)); }
            for (var candidate = returned; candidate is not null; candidate = candidate.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, owner.OriginalDefinition))
                { AddNamedArguments(candidate, mapping); return; }
            }
            foreach (var candidate in returned.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, owner.OriginalDefinition))
                { AddNamedArguments(candidate, mapping); return; }
            }
        }

        private bool SameImplicitAwait(SemanticModel beforeModel, SemanticModel afterModel, SyntaxNode before, SyntaxNode after,
            IMethodSymbol? beforeMethod, IMethodSymbol? afterMethod, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            var left = ImplicitAwaitInfo(beforeModel, before, beforeMethod);
            var right = ImplicitAwaitInfo(afterModel, after, afterMethod);
            if (!left.HasValue || !right.HasValue)
            { return left.HasValue == right.HasValue && (beforeMethod is null ? afterMethod is null : afterMethod is not null && CSharpTypes.Display(beforeMethod.ReturnType) == CSharpTypes.Display(afterMethod.ReturnType)); }
            return SameAwaitMethod(left.Value.GetAwaiterMethod, right.Value.GetAwaiterMethod, changedMethod, changedTree)
                && SameAwaitMethod(left.Value.GetResultMethod, right.Value.GetResultMethod, changedMethod, changedTree)
                && ProtocolMember(left.Value.IsCompletedProperty) == ProtocolMember(right.Value.IsCompletedProperty);
        }
    }
}
