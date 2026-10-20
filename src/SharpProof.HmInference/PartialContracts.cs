using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private static TypeSyntax PreservePartialType(TypeSyntax proposed, ITypeSymbol source, ITypeSymbol target, CancellationToken cancellationToken)
    {
        if (proposed is null)
        { throw new ArgumentNullException(nameof(proposed)); }
        if (source is null)
        { throw new ArgumentNullException(nameof(source)); }
        if (target is null)
        { throw new ArgumentNullException(nameof(target)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (proposed is RefTypeSyntax reference)
        { return reference.WithType(PreservePartialType(reference.Type, source, target, cancellationToken)); }
        if (proposed is NullableTypeSyntax nullableValue && source is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } sourceValue
            && target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } targetValue)
        { return nullableValue.WithElementType(PreservePartialType(nullableValue.ElementType, sourceValue.TypeArguments[0], targetValue.TypeArguments[0], cancellationToken)); }
        var core = proposed is NullableTypeSyntax nullable ? nullable.ElementType : proposed;
        if (source.TypeKind == TypeKind.Dynamic && target.SpecialType == SpecialType.System_Object)
        { core = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)).WithTriviaFrom(core); }
        else if (source.SpecialType == SpecialType.System_Object && target.TypeKind == TypeKind.Dynamic)
        { core = SyntaxFactory.IdentifierName("dynamic").WithTriviaFrom(core); }
        else if (core is ArrayTypeSyntax array && source is IArrayTypeSymbol sourceArray && target is IArrayTypeSymbol targetArray)
        { core = array.WithElementType(PreservePartialType(array.ElementType, sourceArray.ElementType, targetArray.ElementType, cancellationToken)); }
        else if (core is PointerTypeSyntax pointer && source is IPointerTypeSymbol sourcePointer && target is IPointerTypeSymbol targetPointer)
        { core = pointer.WithElementType(PreservePartialType(pointer.ElementType, sourcePointer.PointedAtType, targetPointer.PointedAtType, cancellationToken)); }
        else if (core is TupleTypeSyntax tuple && source is INamedTypeSymbol { IsTupleType: true } sourceTuple && target is INamedTypeSymbol { IsTupleType: true } targetTuple)
        {
            var elements = tuple.Elements;
            for (var i = 0; i < elements.Count; i++)
            { elements = elements.Replace(elements[i], elements[i].WithType(PreservePartialType(elements[i].Type, sourceTuple.TupleElements[i].Type, targetTuple.TupleElements[i].Type, cancellationToken))); }
            core = tuple.WithElements(elements);
        }
        else if (core is FunctionPointerTypeSyntax pointerType && source is IFunctionPointerTypeSymbol sourceFunction && target is IFunctionPointerTypeSymbol targetFunction)
        {
            var parameters = pointerType.ParameterList.Parameters;
            for (var i = 0; i < parameters.Count; i++)
            {
                var sourceType = i == sourceFunction.Signature.Parameters.Length ? sourceFunction.Signature.ReturnType : sourceFunction.Signature.Parameters[i].Type;
                var targetType = i == targetFunction.Signature.Parameters.Length ? targetFunction.Signature.ReturnType : targetFunction.Signature.Parameters[i].Type;
                parameters = parameters.Replace(parameters[i], parameters[i].WithType(PreservePartialType(parameters[i].Type, sourceType, targetType, cancellationToken)));
            }
            core = pointerType.WithParameterList(pointerType.ParameterList.WithParameters(parameters));
        }
        else if (source is INamedTypeSymbol sourceNamed && target is INamedTypeSymbol targetNamed)
        {
            SimpleNameSyntax Arguments(SimpleNameSyntax name)
            {
                if (name is null)
                { throw new ArgumentNullException(nameof(name)); }
                if (name is not GenericNameSyntax generic || generic.TypeArgumentList.Arguments.Count != sourceNamed.TypeArguments.Length)
                { return name; }
                var arguments = generic.TypeArgumentList.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                { arguments = arguments.Replace(arguments[i], PreservePartialType(arguments[i], sourceNamed.TypeArguments[i], targetNamed.TypeArguments[i], cancellationToken)); }
                return generic.WithTypeArgumentList(generic.TypeArgumentList.WithArguments(arguments));
            }
            if (core is QualifiedNameSyntax qualified)
            {
                var owner = sourceNamed.ContainingType is { } sourceOwner && targetNamed.ContainingType is { } targetOwner
                    ? (NameSyntax)PreservePartialType(qualified.Left, sourceOwner, targetOwner, cancellationToken) : qualified.Left;
                core = qualified.WithLeft(owner).WithRight(Arguments(qualified.Right));
            }
            else if (core is AliasQualifiedNameSyntax aliased)
            { core = aliased.WithName(Arguments(aliased.Name)); }
            else if (core is SimpleNameSyntax simple)
            { core = Arguments(simple); }
        }
        var wasNullable = proposed is NullableTypeSyntax;
        var referenceAnnotation = source.IsReferenceType || target.IsReferenceType || source is ITypeParameterSymbol;
        var annotate = referenceAnnotation && source.NullableAnnotation != target.NullableAnnotation
            ? target.NullableAnnotation == NullableAnnotation.Annotated : wasNullable;
        return annotate ? SyntaxFactory.NullableType(core.WithoutTrivia()).WithTriviaFrom(proposed) : core.WithTriviaFrom(proposed);
    }

    private sealed partial class Session
    {
        private void ImportPartialContract(HmType shape, ITypeSymbol source, ITypeSymbol target, IMethodSymbol implementation)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (implementation is null)
            { throw new ArgumentNullException(nameof(implementation)); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            var definition = implementation.PartialDefinitionPart;
            if (definition is not null)
            {
                for (var i = 0; i < definition.TypeParameters.Length; i++)
                { mapping[definition.TypeParameters[i]] = CSharpTypes.Rigid(implementation.TypeParameters[i]); }
            }
            var from = CSharpTypes.Rigid(source);
            var to = CSharpTypes.Rigid(target, mapping);
            if (ContainsDynamic(from) || ContainsDynamic(to))
            { _ = DynamicIdentityShape(from, to, shape); }
            ImportPartialNullableBounds(shape, target);
        }

        private void ImportPartialNullableBounds(HmType shape, ITypeSymbol declared)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            cancellationToken.ThrowIfCancellationRequested();
            var core = ConversionCore(shape);
            if (declared.NullableAnnotation == NullableAnnotation.Annotated && declared.IsReferenceType && core.IsVariable)
            { RequireKeyword(core, "class"); }
            if (declared is IArrayTypeSymbol array && core.Arguments.Length == 1)
            { ImportPartialNullableBounds(core.Arguments[0], array.ElementType); }
            else if (declared is IFunctionPointerTypeSymbol pointer && core.Arguments.Length == pointer.Signature.Parameters.Length + 1)
            {
                for (var i = 0; i < pointer.Signature.Parameters.Length; i++)
                { ImportPartialNullableBounds(core.Arguments[i], pointer.Signature.Parameters[i].Type); }
                ImportPartialNullableBounds(core.Arguments[core.Arguments.Length - 1], pointer.Signature.ReturnType);
            }
            else if (declared is INamedTypeSymbol { IsTupleType: true } tuple && core.Arguments.Length == tuple.TupleElements.Length)
            {
                for (var i = 0; i < core.Arguments.Length; i++)
                { ImportPartialNullableBounds(core.Arguments[i], tuple.TupleElements[i].Type); }
            }
            else if (declared is INamedTypeSymbol named)
            {
                var offset = core.NestedTypeName is not null ? 1 : 0;
                if (offset == 1 && named.ContainingType is { } owner)
                { ImportPartialNullableBounds(core.Arguments[0], owner); }
                if (core.Arguments.Length - offset == named.TypeArguments.Length)
                {
                    for (var i = 0; i < named.TypeArguments.Length; i++)
                    { ImportPartialNullableBounds(core.Arguments[i + offset], named.TypeArguments[i]); }
                }
            }
        }
    }
}
