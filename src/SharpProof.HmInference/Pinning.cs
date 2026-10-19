using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private bool requiresPinningEmission;

        private static IMethodSymbol? PinningMethod(SemanticModel semantic, ExpressionSyntax expression)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var type = semantic.GetTypeInfo(expression).Type;
            // Arrays and strings use intrinsic pinning. Pointer initializers
            // include addresses and fixed buffers, without an implicit call.
            if (type is null or IArrayTypeSymbol or IPointerTypeSymbol || type.SpecialType == SpecialType.System_String)
            { return null; }
            var invocation = SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()), SyntaxFactory.IdentifierName("GetPinnableReference")));
            var selected = semantic.GetSpeculativeSymbolInfo(expression.SpanStart, invocation, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
            if (selected is not { Parameters.Length: 0, RefKind: not RefKind.None } || selected.IsStatic && selected.ReducedFrom is null)
            { return null; }
            if (selected.ReducedFrom is not { } extension)
            { return selected; }
            if (!extension.IsGenericMethod)
            { return extension; }
            // ReducedFrom omits the type arguments inferred from the receiver.
            // Reconstruct the actual static call for constraint and binding checks.
            var arguments = extension.TypeParameters.Select(parameter =>
            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return selected.GetTypeInferredDuringReduction(parameter) ?? throw new InferenceException("Unresolved pinning extension type argument."); }).ToArray();
            return extension.ConstructedFrom.Construct(arguments);
        }

        private void DeclarePinned(VariableDeclarationSyntax declaration)
        {
            if (declaration is null)
            { throw new ArgumentNullException(nameof(declaration)); }
            foreach (var variable in declaration.Variables)
            {
                var symbol = model.GetDeclaredSymbol(variable, cancellationToken) as ILocalSymbol
                    ?? throw new InferenceException("Unresolved pinned local.");
                if (symbol.Type is not IPointerTypeSymbol pointer || variable.Initializer?.Value is not { } initializer)
                { throw new InferenceException("Unresolved pinning initializer."); }
                var original = model.GetTypeInfo(initializer, cancellationToken).Type
                    ?? throw new InferenceException("Untyped pinning initializer.");
                var source = Expression(initializer);
                var core = ConversionCore(source);
                var selected = PinningMethod(model, initializer);
                HmType element;
                ITypeSymbol originalElement;
                if (selected is not null)
                {
                    element = AwaitProtocolResult(original, source, selected);
                    originalElement = selected.ReturnType;
                    ImportConstructedConstraints(original, source);
                    ImportConstructedConstraints(originalElement, element);
                }
                else if (original is IArrayTypeSymbol array)
                { element = core.Arguments[0]; originalElement = array.ElementType; }
                else if (original.SpecialType == SpecialType.System_String)
                { Anchor(initializer); element = new("char"); originalElement = model.Compilation.GetSpecialType(SpecialType.System_Char); }
                else if (original is IPointerTypeSymbol sourcePointer && core.Name == CSharpTypes.Pointer)
                { element = core.Arguments[0]; originalElement = sourcePointer.PointedAtType; }
                else
                { throw new InferenceException("Unresolved pinning pattern."); }
                if (originalElement.IsUnmanagedType)
                { RequireUnmanaged(element, originalElement); }
                if (pointer.PointedAtType.SpecialType != SpecialType.System_Void)
                { HmType.Unify(element, CSharpTypes.Rigid(pointer.PointedAtType)); }
                environment.Add(symbol, CSharpTypes.Rigid(symbol.Type));
            }
        }

        private bool PreservesPinningBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel,
            MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
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
            var originals = before.DescendantNodes().OfType<FixedStatementSyntax>().SelectMany(statement =>
            { if (statement is null) { throw new ArgumentNullException(nameof(statement)); } return statement.Declaration.Variables; }).ToArray();
            var candidates = after.DescendantNodes().OfType<FixedStatementSyntax>().SelectMany(statement =>
            { if (statement is null) { throw new ArgumentNullException(nameof(statement)); } return statement.Declaration.Variables; }).ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originals[i].Initializer is not { Value: var original } || candidates[i].Initializer is not { Value: var candidate })
                { return false; }
                var originalMethod = PinningMethod(beforeModel, original);
                var candidateMethod = PinningMethod(afterModel, candidate);
                requiresPinningEmission |= originalMethod is not null
                    && SymbolEqualityComparer.Default.Equals(originalMethod.OriginalDefinition, currentMethod?.OriginalDefinition);
                if (!SameAwaitMethod(originalMethod, candidateMethod, changedMethod, changedTree))
                { return false; }
            }
            return true;
        }
    }
}
