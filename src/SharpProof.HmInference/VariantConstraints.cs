using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private readonly Dictionary<HmType, ITypeSymbol?> originalConstraintArguments = [];
        private readonly HashSet<(HmType Source, HmType Target)> completedVariantConstraints = [];

        private void RecordConstraintArgument(HmType variable, ITypeSymbol actual)
        {
            if (variable is null)
            { throw new ArgumentNullException(nameof(variable)); }
            if (actual is null)
            { throw new ArgumentNullException(nameof(actual)); }
            if (originalConstraintArguments.TryGetValue(variable, out var previous)
                && !SymbolEqualityComparer.IncludeNullability.Equals(previous, actual))
            { originalConstraintArguments[variable] = null; }
            else
            { originalConstraintArguments[variable] = actual; }
        }

        private HmType? OriginalConstraintShape(HmType shape)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            cancellationToken.ThrowIfCancellationRequested();
            // Look up the original call's node before following its substitution link.
            if (originalConstraintArguments.TryGetValue(shape, out var original))
            { return original is null ? null : CSharpTypes.Rigid(original); }
            shape = shape.Root;
            if (shape.IsVariable)
            { return null; }
            var arguments = new List<HmType>();
            foreach (var argument in shape.Arguments)
            {
                if (OriginalConstraintShape(argument) is not { } resolved)
                { return null; }
                arguments.Add(resolved);
            }
            return shape.WithArguments(arguments.ToArray());
        }

        private ITypeSymbol? OriginalConstraintSymbol(HmType target, ITypeSymbol declared)
        {
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (OriginalConstraintShape(target) is not { } original)
            { return null; }
            var position = currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax method
                ? method.ParameterList.Span.End : 0;
            var spelling = CSharpTypes.Render(original, declared, [], model: model, position: position);
            var resolved = model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName(spelling), SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
            return resolved is { TypeKind: not TypeKind.Error } ? resolved : null;
        }

        private bool BindVariantConstraint(HmType source, HmType target, INamedTypeSymbol declared)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            var value = ConversionCore(source);
            var destination = ConversionCore(target);
            var key = (value, destination);
            if (completedVariantConstraints.Contains(key) || value.CSharpSymbol is not INamedTypeSymbol originalSource
                || ConstraintProjection(value, destination) is null)
            { return false; }
            if (OriginalConstraintSymbol(target, declared) is not INamedTypeSymbol originalTarget)
            { return false; }
            var conversion = model.Compilation.ClassifyCommonConversion(originalSource, originalTarget);
            if (!conversion.IsImplicit || conversion.IsUserDefined)
            { return false; }
            BindSubtype(originalSource, originalTarget, source, target, false);
            completedVariantConstraints.Add(key);
            return true;
        }
    }
}
