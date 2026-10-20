using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private readonly List<(HmType Shape, ITypeSymbol Original)> conversionTargets = [];
        private readonly List<(HmType Source, HmType Target, ITypeSymbol OriginalSource, ITypeSymbol OriginalTarget, bool ReferenceOnly)> conversionBounds = [];

        private bool TryBindArgumentConversion(IOperation operation, HmType expected, HmType inferred)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (operation is not IConversionOperation { Conversion.IsIdentity: false } conversion || operation.Syntax is not ExpressionSyntax expression
                || model.GetTypeInfo(expression, cancellationToken).Type is not ITypeSymbol source || conversion.Type is not ITypeSymbol target
                || SymbolEqualityComparer.Default.Equals(source, target))
            { return false; }
            BindConvertedArgument(source, target, inferred, expected);
            conversionTargets.Add((expected, target));
            return true;
        }

        private static HmType ConversionCore(HmType shape)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            shape = shape.Root;
            while (shape.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
            { shape = shape.Arguments[0].Root; }
            return shape;
        }

        private void BindConvertedArgument(ITypeSymbol source, ITypeSymbol target, HmType inferred, HmType expected, bool allowUserDefined = true)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (SymbolEqualityComparer.Default.Equals(source, target))
            { UnifyDeconstructionReceiver(expected, inferred); return; }
            var destination = ConversionCore(expected);
            if (destination.IsVariable && (target is IArrayTypeSymbol || target is INamedTypeSymbol { IsGenericType: true }))
            {
                HmType.Unify(destination, ConversionCore(CSharpTypes.Erase(target)));
                destination = destination.Root;
            }
            var value = ConversionCore(inferred);
            if (source is INamedTypeSymbol { IsTupleType: true } tuple && target is INamedTypeSymbol { IsTupleType: true } convertedTuple
                && tuple.TupleElements.Length == convertedTuple.TupleElements.Length && value.Name == "Tuple" && destination.Name == "Tuple")
            {
                for (var i = 0; i < tuple.TupleElements.Length; i++)
                { BindConvertedArgument(tuple.TupleElements[i].Type, convertedTuple.TupleElements[i].Type, value.Arguments[i], destination.Arguments[i]); }
                return;
            }
            if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(source, nullable.TypeArguments[0]) && destination.Name == CSharpTypes.NullableValue)
            { HmType.Unify(inferred, destination.Arguments[0]); return; }
            var classified = ((CSharpCompilation)model.Compilation).ClassifyConversion(source, target);
            if (allowUserDefined && classified.IsUserDefined && TryUserConversion(source, target, inferred, expected, classified.MethodSymbol))
            { return; }
            if (!classified.IsUserDefined && classified.IsImplicit && (classified.IsReference || classified.IsBoxing))
            { BindSubtype(source, target, inferred, expected, false); return; }
            // Numeric, lifted numeric and user-defined conversions are not equality.
            // Preserve their endpoints, including the original boxing/operator effects.
            PinConversion(source, target, inferred, expected);
        }

        private static void PinConversion(ITypeSymbol source, ITypeSymbol target, HmType inferred, HmType expected)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            HmType.Unify(inferred, CSharpTypes.Rigid(source));
            UnifyDeconstructionReceiver(expected, CSharpTypes.Rigid(target));
        }

        private void BindSubtype(ITypeSymbol source, ITypeSymbol target, HmType inferred, HmType expected, bool referenceOnly)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            var value = ConversionCore(inferred);
            var destination = ConversionCore(expected);
            if (SymbolEqualityComparer.Default.Equals(source, target))
            { UnifyDeconstructionReceiver(expected, inferred); return; }
            if (!value.IsVariable && destination.IsVariable)
            {
                // A constructed source cannot appear on the left of a C# where
                // clause. Keep the target constructor rather than invent a lower bound.
                HmType.Unify(destination, CSharpTypes.Rigid(target));
                destination = ConversionCore(destination);
            }
            if (value.IsVariable)
            {
                if (target is IArrayTypeSymbol || target.TypeKind == TypeKind.Delegate
                    || target is INamedTypeSymbol { IsSealed: true, TypeKind: TypeKind.Class })
                { PinConversion(source, target, inferred, expected); return; }
                conversionBounds.Add((value, destination, source, target, referenceOnly));
                return;
            }
            if (target.SpecialType == SpecialType.System_Object || target.SpecialType == SpecialType.System_ValueType && source.IsValueType)
            { UnifyDeconstructionReceiver(expected, CSharpTypes.Rigid(target)); return; }
            if (source is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional && value.Name == CSharpTypes.NullableValue)
            { BindSubtype(optional.TypeArguments[0], target, value.Arguments[0], expected, false); return; }
            if (source is IArrayTypeSymbol array)
            {
                if (target is IArrayTypeSymbol converted && array.Rank == converted.Rank && value.Arguments.Length == 1 && destination.Arguments.Length == 1)
                { BindSubtype(array.ElementType, converted.ElementType, value.Arguments[0], destination.Arguments[0], true); return; }
                if (target is INamedTypeSymbol targetInterface && targetInterface.TypeKind == TypeKind.Interface)
                {
                    if (targetInterface.Arity == 0)
                    { UnifyDeconstructionReceiver(expected, CSharpTypes.Rigid(target)); return; }
                    if (array.IsSZArray && targetInterface.Arity == 1 && targetInterface.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
                        && targetInterface.Name is "IEnumerable" or "ICollection" or "IList" or "IReadOnlyCollection" or "IReadOnlyList")
                    {
                        BindSubtype(array.ElementType, targetInterface.TypeArguments[0], value.Arguments[0], destination.Arguments[0],
                            !SymbolEqualityComparer.Default.Equals(array.ElementType, targetInterface.TypeArguments[0]));
                        return;
                    }
                }
                if (target is INamedTypeSymbol { SpecialType: SpecialType.System_Array })
                { UnifyDeconstructionReceiver(expected, CSharpTypes.Rigid(target)); return; }
            }
            if (source is INamedTypeSymbol named && target is INamedTypeSymbol targetNamed && TryProjectArgument(named, targetNamed, value) is { } projection
                && BindProjectedArgument(projection.Type, targetNamed, projection.Shape, destination))
            { return; }
            PinConversion(source, target, inferred, expected);
        }

        private bool BindProjectedArgument(INamedTypeSymbol source, INamedTypeSymbol target, HmType projected, HmType destination)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (projected is null)
            { throw new ArgumentNullException(nameof(projected)); }
            if (destination is null)
            { throw new ArgumentNullException(nameof(destination)); }
            projected = ConversionCore(projected);
            destination = ConversionCore(destination);
            if (projected.Name != destination.Name || projected.Arguments.Length != destination.Arguments.Length)
            { return false; }
            if (projected.NestedTypeName is not null)
            { BindSubtype(source.ContainingType, target.ContainingType, projected.Arguments[0], destination.Arguments[0], true); }
            var projectedArguments = CSharpTypes.OwnArguments(projected);
            var destinationArguments = CSharpTypes.OwnArguments(destination);
            for (var i = 0; i < projectedArguments.Length; i++)
            {
                var before = source.TypeArguments[i];
                var after = target.TypeArguments[i];
                if (SymbolEqualityComparer.Default.Equals(before, after))
                { UnifyDeconstructionReceiver(destinationArguments[i], projectedArguments[i]); }
                else if (target.OriginalDefinition.TypeParameters[i].Variance == VarianceKind.Out)
                { BindSubtype(before, after, projectedArguments[i], destinationArguments[i], true); }
                else if (target.OriginalDefinition.TypeParameters[i].Variance == VarianceKind.In)
                { BindSubtype(after, before, destinationArguments[i], projectedArguments[i], true); }
                else
                { PinConversion(before, after, projectedArguments[i], destinationArguments[i]); }
            }
            return true;
        }

        private (INamedTypeSymbol Type, HmType Shape)? TryProjectArgument(INamedTypeSymbol source, INamedTypeSymbol target, HmType value)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            foreach (var projection in ProjectArguments(source, target, value))
            { return projection; }
            return null;
        }

        private IEnumerable<(INamedTypeSymbol Type, HmType Shape)> ProjectArguments(INamedTypeSymbol source, INamedTypeSymbol target, HmType value)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            var actualMapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(source, value, mapping) || !CSharpTypes.MapNamedArguments(source, CSharpTypes.Rigid(source), actualMapping))
            { yield break; }
            var originals = new List<INamedTypeSymbol>();
            var actuals = new List<INamedTypeSymbol>();
            for (var type = source.OriginalDefinition; type is not null; type = type.BaseType)
            { originals.Add(type); }
            for (var type = source; type is not null; type = type.BaseType)
            { actuals.Add(type); }
            originals.AddRange(source.OriginalDefinition.AllInterfaces);
            actuals.AddRange(source.AllInterfaces);
            foreach (var actual in actuals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolEqualityComparer.Default.Equals(actual.OriginalDefinition, target.OriginalDefinition))
                { continue; }
                var conversion = model.Compilation.ClassifyCommonConversion(actual, target);
                if (!conversion.IsIdentity && !(conversion.IsImplicit && conversion.IsReference))
                { continue; }
                foreach (var original in originals)
                {
                    if (SymbolEqualityComparer.Default.Equals(original.OriginalDefinition, actual.OriginalDefinition)
                        && SameConversionShape(CSharpTypes.Rigid(original, actualMapping), CSharpTypes.Rigid(actual)))
                    { yield return (actual, CSharpTypes.Rigid(original, mapping)); }
                }
            }
            yield break;
        }

        private static bool SameConversionShape(HmType left, HmType right)
        {
            if (left is null)
            { throw new ArgumentNullException(nameof(left)); }
            if (right is null)
            { throw new ArgumentNullException(nameof(right)); }
            left = ConversionCore(left);
            right = ConversionCore(right);
            if (left.Name != right.Name || left.NestedTypeName != right.NestedTypeName || left.Arguments.Length != right.Arguments.Length)
            { return false; }
            for (var i = 0; i < left.Arguments.Length; i++)
            { if (!SameConversionShape(left.Arguments[i], right.Arguments[i])) { return false; } }
            return true;
        }

        private int completedConversionBounds;
        private readonly HashSet<int> completedConcreteBounds = [];

        private void CompleteArgumentConversions(HashSet<HmType> inferable)
        {
            if (inferable is null)
            { throw new ArgumentNullException(nameof(inferable)); }
            foreach (var target in conversionTargets)
            { PreserveResultOnly(target.Shape, CSharpTypes.Rigid(target.Original), inferable); }
            CompleteArgumentBounds();
        }

        private bool CompleteArgumentBounds()
        {
            var start = completedConversionBounds;
            for (var i = 0; i < start; i++)
            {
                var previous = conversionBounds[i];
                if (!ConversionCore(previous.Source).IsVariable && completedConcreteBounds.Add(i))
                { BindSubtype(previous.OriginalSource, previous.OriginalTarget, previous.Source, previous.Target, previous.ReferenceOnly); }
            }
            for (; completedConversionBounds < conversionBounds.Count; completedConversionBounds++)
            {
                var bound = conversionBounds[completedConversionBounds];
                var source = ConversionCore(bound.Source);
                var target = ConversionCore(bound.Target);
                if (ReferenceEquals(source, target))
                { continue; }
                if (!source.IsVariable)
                { completedConcreteBounds.Add(completedConversionBounds); BindSubtype(bound.OriginalSource, bound.OriginalTarget, source, target, bound.ReferenceOnly); continue; }
                if (bound.ReferenceOnly)
                {
                    RequireKeyword(source, "class");
                    if (target.IsVariable)
                    { RequireKeyword(target, "class"); }
                }
                if (target.Name == "object")
                {
                    if (bound.OriginalTarget.NullableAnnotation == NullableAnnotation.NotAnnotated)
                    { RequireKeyword(source, "notnull"); }
                    continue;
                }
                if (target.Name == "global::System.ValueType")
                {
                    if (bound.OriginalSource.IsValueType)
                    { RequireKeyword(source, "struct"); }
                    else
                    { HmType.Unify(source, CSharpTypes.Rigid(bound.OriginalSource)); }
                    continue;
                }
                inferredConstraints.Add((source, null, target, !target.IsVariable && bound.OriginalTarget.TypeKind == TypeKind.Class && target.Name != "global::System.Enum"));
            }
            return completedConversionBounds != start;
        }
    }
}
