using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private void RequireUnmanaged(HmType shape, ITypeSymbol original)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            shape = shape.Root;
            if (shape.IsVariable)
            { RequireKeyword(shape, "unmanaged"); return; }
            if (original is not INamedTypeSymbol named || named.SpecialType != SpecialType.None || named.TypeKind == TypeKind.Enum)
            { return; }
            if (named.IsTupleType && shape.Name == "Tuple")
            {
                for (var i = 0; i < named.TupleElements.Length; i++)
                { RequireUnmanaged(shape.Arguments[i], named.TupleElements[i].Type); }
                return;
            }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(named, shape, mapping))
            { return; }
            foreach (var field in named.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic || field.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                { continue; }
                RequireUnmanaged(CSharpTypes.Rigid(field.OriginalDefinition.Type, mapping), field.Type);
            }
        }
        private HmType PointerElement(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var pointer = Expression(expression).Root;
            if (pointer.Name != CSharpTypes.Pointer || pointer.Arguments.Length != 1)
            { throw new InferenceException("Unresolved pointer element."); }
            return pointer.Arguments[0];
        }
        private HmType AddressOf(PrefixUnaryExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var value = Expression(expression.Operand);
            var original = model.GetTypeInfo(expression.Operand, cancellationToken).Type
                ?? throw new InferenceException("Unresolved address operand.");
            if (original.IsUnmanagedType)
            { RequireUnmanaged(value, original); }
            else
            { HmType.Unify(value, CSharpTypes.Rigid(original)); }
            return new(CSharpTypes.Pointer, value);
        }
        private HmType PointerBinary(BinaryExpressionSyntax binary)
        {
            if (binary is null)
            { throw new ArgumentNullException(nameof(binary)); }
            var leftPointer = model.GetTypeInfo(binary.Left, cancellationToken).Type is IPointerTypeSymbol;
            var rightPointer = model.GetTypeInfo(binary.Right, cancellationToken).Type is IPointerTypeSymbol;
            var left = leftPointer ? Expression(binary.Left) : null;
            var right = rightPointer ? Expression(binary.Right) : null;
            if (!leftPointer && !binary.Left.IsKind(SyntaxKind.NullLiteralExpression))
            { Anchor(binary.Left); }
            if (!rightPointer && !binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
            { Anchor(binary.Right); }
            if (binary.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression)
            {
                if (left is not null && right is not null)
                { HmType.Unify(left, right); return CSharpTypes.Rigid(model.GetTypeInfo(binary, cancellationToken).Type!); }
                return left ?? right ?? throw new InferenceException("Unresolved pointer arithmetic.");
            }
            return CSharpTypes.Rigid(model.GetTypeInfo(binary, cancellationToken).Type!);
        }
        private HmType StackAllocation(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var type = model.GetTypeInfo(expression, cancellationToken).Type
                ?? model.GetTypeInfo(expression, cancellationToken).ConvertedType
                ?? throw new InferenceException("Unresolved stack allocation.");
            var element = type is IPointerTypeSymbol pointer ? pointer.PointedAtType
                : type is INamedTypeSymbol { TypeArguments.Length: 1 } span ? span.TypeArguments[0]
                : throw new InferenceException("Unsupported stack allocation target.");
            var explicitAllocation = expression as StackAllocArrayCreationExpressionSyntax;
            var initializer = explicitAllocation?.Initializer ?? (expression as ImplicitStackAllocArrayCreationExpressionSyntax)?.Initializer;
            var inferred = explicitAllocation is null ? HmType.Variable() : CSharpTypes.Rigid(element);
            if (explicitAllocation?.Type is ArrayTypeSyntax arrayType)
            {
                foreach (var rank in arrayType.RankSpecifiers)
                {
                    foreach (var size in rank.Sizes)
                    {
                        if (size is not OmittedArraySizeExpressionSyntax)
                        { Anchor(size); }
                    }
                }
            }
            if (initializer is not null)
            {
                foreach (var item in initializer.Expressions)
                {
                    if (item.IsKind(SyntaxKind.DefaultLiteralExpression))
                    { continue; }
                    var source = model.GetTypeInfo(item, cancellationToken).Type;
                    if (source is not null && !SymbolEqualityComparer.Default.Equals(source, element))
                    { Anchor(item); HmType.Unify(inferred, CSharpTypes.Rigid(element)); }
                    else
                    { HmType.Unify(inferred, Expression(item)); }
                }
            }
            RequireUnmanaged(inferred, element);
            return CSharpTypes.Rigid(type).WithArguments(inferred);
        }
    }
}
