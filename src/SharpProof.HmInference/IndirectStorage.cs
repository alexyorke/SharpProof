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
            var attributes = new List<AttributeData>();
            IEnumerable<IArgumentOperation>? arguments = null;
            Dictionary<int, HmType>? argumentValues = null;
            IInvocationOperation? relationalCall = null;
            var preferNotNull = false;
            switch (model.GetOperation(source, cancellationToken))
            {
                case IInvocationOperation invocation:
                    declaredType = invocation.TargetMethod.OriginalDefinition.ReturnType;
                    attributes.AddRange(invocation.TargetMethod.GetReturnTypeAttributes());
                    arguments = invocation.Arguments;
                    relationalCall = invocation;
                    var guaranteed = new Dictionary<int, HmType>();
                    foreach (var argument in invocation.Arguments)
                    {
                        if (argument.Parameter is not { } parameter)
                        { continue; }
                        var created = argument.Value;
                        if (created is IConversionOperation conversion && !conversion.Conversion.IsUserDefined
                            && (conversion.Conversion.IsReference || conversion.Conversion.IsIdentity))
                        { created = conversion.Operand; }
                        if ((created is IObjectCreationOperation or IArrayCreationOperation)
                            && created.Type is { IsReferenceType: true } type)
                        { guaranteed[parameter.Ordinal] = CSharpTypes.Rigid(type); }
                        else if (created.ConstantValue.HasValue && created.ConstantValue.Value is not null
                            && created.Type is { } constantType)
                        { guaranteed[parameter.Ordinal] = CSharpTypes.Rigid(constantType); }
                    }
                    if (guaranteed.Count != 0)
                    { argumentValues = guaranteed; }
                    break;
                case IPropertyReferenceOperation property:
                    declaredType = property.Property.OriginalDefinition.Type;
                    preferNotNull = true;
                    var propertyFlowContract = false;
                    foreach (var attribute in property.Property.GetAttributes())
                    {
                        if (attribute is null)
                        { throw new InvalidOperationException("Unexpected null property attribute."); }
                        var name = attribute.AttributeClass?.ToDisplayString();
                        if (name == "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute")
                        { continue; }
                        propertyFlowContract |= name is "System.Diagnostics.CodeAnalysis.MaybeNullAttribute"
                            or "System.Diagnostics.CodeAnalysis.NotNullAttribute";
                        attributes.Add(attribute);
                    }
                    // A source property's flow contract takes precedence over
                    // attributes on its getter return, matching normal reads.
                    if (!propertyFlowContract && property.Property.GetMethod is { } getter)
                    { attributes.AddRange(getter.GetReturnTypeAttributes()); }
                    break;
                case IFieldReferenceOperation field:
                    declaredType = field.Field.OriginalDefinition.Type;
                    attributes.AddRange(field.Field.GetAttributes());
                    break;
                default:
                    return null;
            }
            if (declaredType is not ITypeParameterSymbol
                { HasValueTypeConstraint: false, HasUnmanagedTypeConstraint: false })
            { return null; }
            var value = CSharpTypes.Erase(target);
            if (relationalCall is not null)
            {
                // Reuse already bound argument shapes for relational return
                // contracts. A return T guarded by the same T input can remain
                // a bare variable; an annotated input still carries its mark.
                var mapped = new Dictionary<int, HmType>();
                if (argumentValues is not null)
                {
                    foreach (var entry in argumentValues)
                    { mapped.Add(entry.Key, entry.Value); }
                }
                foreach (var attribute in attributes)
                {
                    if (attribute is null)
                    { throw new InvalidOperationException("Unexpected null return attribute."); }
                    if (attribute.AttributeClass?.ToDisplayString() != "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute"
                        || attribute.ConstructorArguments.Length != 1
                        || attribute.ConstructorArguments[0].Value is not string guardName)
                    { continue; }
                    foreach (var argument in relationalCall.Arguments)
                    {
                        var syntax = argument.Syntax as ArgumentSyntax
                            ?? argument.Syntax.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
                        if (argument.Parameter is not { } parameter || parameter.Name != guardName
                            || syntax is null)
                        { continue; }
                        var expression = StorageExpression(syntax.Expression);
                        if (expression is PostfixUnaryExpressionSyntax suppression
                            && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                        { expression = StorageExpression(suppression.Operand); }
                        if (expression is not IdentifierNameSyntax identifier
                            || model.GetSymbolInfo(identifier, cancellationToken).Symbol is not { } symbol
                            || !environment.TryGetValue(symbol, out var actual)
                            || defaultStores?.ContainsKey(symbol) == true)
                        { continue; }
                        if (nullableReadFlow.TryGetValue(identifier.Span, out var flow)
                            && flow == NullableFlowState.MaybeNull
                            && model.GetTypeInfo(identifier, cancellationToken).Type is { } readType)
                        { actual = ApplyMaybeNullContract(readType, actual); }
                        mapped[parameter.Ordinal] = actual;
                        if (SymbolEqualityComparer.Default.Equals(parameter.OriginalDefinition.Type, declaredType))
                        { value = actual; }
                    }
                }
                if (mapped.Count != 0)
                { argumentValues = mapped; }
            }
            if (declaredType.NullableAnnotation == NullableAnnotation.Annotated)
            { value = ApplyMaybeNullContract(target, value); }
            value = ApplyNullabilityContract(target, value, attributes, forStorage: false,
                arguments: arguments, argumentValues: argumentValues, preferNotNull: preferNotNull,
                allowBareRelational: true);
            return HasDefaultValueAnnotation(value) ? value : null;
        }

    }
}
