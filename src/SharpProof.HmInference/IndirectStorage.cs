using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        // A generic [MaybeNull] read can store a default value even when the
        // original value-type instantiation has no nullable flow state. Discover
        // the storage annotation before inferring the body, just as for a default
        // literal. The ordinary call binding still relates the source to the slot.
        private HmType? IndirectNullableStoredValue(ExpressionSyntax source, ITypeSymbol target)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            while (source is ParenthesizedExpressionSyntax parentheses)
            { source = parentheses.Expression; }
            if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                || !model.ClassifyConversion(source, target).IsIdentity)
            { return null; }
            ITypeSymbol? declaredType;
            IEnumerable<AttributeData> attributes;
            switch (model.GetOperation(source, cancellationToken))
            {
                case IInvocationOperation invocation:
                    declaredType = invocation.TargetMethod.OriginalDefinition.ReturnType;
                    attributes = invocation.TargetMethod.GetReturnTypeAttributes();
                    break;
                case IPropertyReferenceOperation property:
                    declaredType = property.Property.OriginalDefinition.Type;
                    attributes = property.Property.GetAttributes();
                    if (property.Property.GetMethod is { } getter)
                    { attributes = attributes.Concat(getter.GetReturnTypeAttributes()); }
                    break;
                case IFieldReferenceOperation field:
                    declaredType = field.Field.OriginalDefinition.Type;
                    attributes = field.Field.GetAttributes();
                    break;
                default:
                    return null;
            }
            if (declaredType is not ITypeParameterSymbol
                { HasValueTypeConstraint: false, HasUnmanagedTypeConstraint: false })
            { return null; }
            var nullableDeclaration = declaredType.NullableAnnotation == NullableAnnotation.Annotated;
            var maybeNull = false;
            var strongerContract = false;
            foreach (var attribute in attributes)
            {
                if (attribute is null)
                { throw new InvalidOperationException("Unexpected null return attribute."); }
                var name = attribute.AttributeClass?.ToDisplayString();
                maybeNull |= name == "System.Diagnostics.CodeAnalysis.MaybeNullAttribute";
                strongerContract |= name is "System.Diagnostics.CodeAnalysis.NotNullAttribute"
                    or "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";
            }
            if ((!nullableDeclaration && !maybeNull) || strongerContract)
            { return null; }
            return ApplyMaybeNullContract(target, CSharpTypes.Erase(target));
        }
    }
}
