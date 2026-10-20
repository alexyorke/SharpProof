using Microsoft.CodeAnalysis;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private bool ResolveDependentConstraints()
        {
            var changed = false;
            for (var i = inferredConstraints.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requirement = inferredConstraints[i];
                if (requirement.Type is null)
                { continue; }
                var source = ConversionCore(requirement.Variable);
                var target = ConversionCore(requirement.Type);
                var declared = target.CSharpSymbol;
                if (declared is null)
                { continue; }
                var type = requirement.Type.Root;
                if (type.Name == CSharpTypes.NullableParameter)
                {
                    type = declared.IsReferenceType ? new(CSharpTypes.NullableReference, target) : target;
                    inferredConstraints[i] = requirement = (requirement.Variable, requirement.Keyword, type, requirement.BaseClass);
                    changed = true;
                }
                if (declared.SpecialType == SpecialType.System_Object)
                {
                    if (source.IsVariable && type.Name != CSharpTypes.NullableReference)
                    { inferredConstraints[i] = (requirement.Variable, "notnull", null, false); }
                    else
                    { inferredConstraints.RemoveAt(i); }
                    changed = true;
                    continue;
                }
                if (declared.SpecialType == SpecialType.System_Enum && source.IsVariable
                    && inferredConstraints.Any(other =>
                    { if (other.Variable is null) { throw new ArgumentException("Expected a constraint owner.", nameof(other)); } return ReferenceEquals(ConversionCore(other.Variable), source) && other.Keyword is "class" or "class?"; }))
                {
                    // Enum's only reference-type instantiation is System.Enum.
                    HmType.Unify(source, target);
                    inferredConstraints.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (declared is INamedTypeSymbol { IsSealed: true } closed
                    && (closed.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum
                        || closed.TypeKind == TypeKind.Delegate && closed.TypeParameters.All(parameter =>
                            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return parameter.Variance == VarianceKind.None; })))
                {
                    HmType.Unify(source, target);
                    inferredConstraints.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (source.CSharpSymbol is { } original && (original is IArrayTypeSymbol
                    || original.TypeKind == TypeKind.Delegate && declared.TypeKind == TypeKind.Delegate))
                {
                    ITypeSymbol? endpoint = declared;
                    if (declared is INamedTypeSymbol { TypeKind: TypeKind.Interface } contract
                        && contract.OriginalDefinition.TypeParameters.Any(parameter =>
                            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return parameter.Variance != VarianceKind.None; }))
                    {
                        endpoint = OriginalConstraintSymbol(requirement.Type, declared);
                        if (endpoint is null)
                        { continue; }
                    }
                    BindSubtype(original, endpoint, source, target, true);
                    inferredConstraints.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (declared.TypeKind == TypeKind.Class && !requirement.BaseClass)
                {
                    inferredConstraints[i] = (requirement.Variable, requirement.Keyword, requirement.Type, true);
                    changed = true;
                }
            }
            return CompleteArgumentBounds() || changed;
        }

        private IEnumerable<HmType> DependentNominalBounds(HmType owner, bool requireNonNull)
        {
            if (owner is null)
            { throw new ArgumentNullException(nameof(owner)); }
            // SPINT001: all requireNonNull values are valid traversal modes.
            var seen = new HashSet<HmType> { owner };
            var pending = new Stack<HmType>();
            void AddEdges(HmType variable)
            {
                if (variable is null)
                { throw new ArgumentNullException(nameof(variable)); }
                foreach (var requirement in inferredConstraints)
                {
                    if (requirement.Type is null || !ReferenceEquals(ConversionCore(requirement.Variable), variable)
                        || requireNonNull && requirement.Type.Root.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference)
                    { continue; }
                    var target = ConversionCore(requirement.Type);
                    if (target.IsVariable && seen.Add(target))
                    { pending.Push(target); }
                }
            }
            AddEdges(owner);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                foreach (var requirement in inferredConstraints)
                {
                    if (requirement.Type is null || !ReferenceEquals(ConversionCore(requirement.Variable), current)
                        || requireNonNull && requirement.Type.Root.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference)
                    { continue; }
                    if (!ConversionCore(requirement.Type).IsVariable)
                    { yield return requirement.Type; }
                }
                AddEdges(current);
            }
        }

        private bool NormalizeTransitiveConstraints()
        {
            var variables = new HashSet<HmType>();
            foreach (var requirement in inferredConstraints)
            {
                variables.UnionWith(requirement.Variable.FreeVariables());
                if (requirement.Type is not null)
                { variables.UnionWith(requirement.Type.FreeVariables()); }
            }
            foreach (var requirement in inferredConstraints)
            {
                if (!requirement.BaseClass || requirement.Type is null || !ConversionCore(requirement.Variable).IsVariable)
                { continue; }
                foreach (var upper in DependentNominalBounds(ConversionCore(requirement.Variable), false))
                {
                    if (ConversionCore(upper).CSharpSymbol?.TypeKind == TypeKind.Class
                        && ConstraintProjection(upper, requirement.Type) is { } projection)
                    { HmType.Unify(projection, ConversionCore(requirement.Type)); }
                }
            }
            // Unification can expose a new dependency cycle. Recompute components
            // before removing bounds, so two equivalent owners cannot erase both.
            if (variables.Any(variable =>
                { if (variable is null) { throw new ArgumentNullException(nameof(variable)); } return !ReferenceEquals(variable, variable.Root); }))
            { return true; }
            var redundant = new List<int>();
            for (var i = 0; i < inferredConstraints.Count; i++)
            {
                var requirement = inferredConstraints[i];
                if (requirement.Type is null || ConversionCore(requirement.Type).CSharpSymbol is not INamedTypeSymbol required)
                { continue; }
                var owner = ConversionCore(requirement.Variable);
                if (!owner.IsVariable)
                { continue; }
                var knownNotNull = inferredConstraints.Any(other =>
                { if (other.Variable is null) { throw new ArgumentException("Expected a constraint owner.", nameof(other)); } return ReferenceEquals(ConversionCore(other.Variable), owner) && other.Keyword is "class" or "notnull" or "struct" or "unmanaged"; });
                var requiresNotNull = !knownNotNull && requirement.Type.Root.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter);
                foreach (var upper in DependentNominalBounds(owner, requiresNotNull))
                {
                    if (ConversionCore(upper).CSharpSymbol is INamedTypeSymbol stronger && stronger.TypeKind == required.TypeKind
                        && ConstraintProjection(upper, requirement.Type) is { } projection && SameConstraintShape(projection, requirement.Type))
                    { redundant.Add(i); break; }
                }
            }
            for (var i = redundant.Count - 1; i >= 0; i--)
            { inferredConstraints.RemoveAt(redundant[i]); }
            return redundant.Count != 0;
        }

        private static bool IsReferenceBase(HmType type)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            return ConversionCore(type).CSharpSymbol is INamedTypeSymbol named && named.TypeKind == TypeKind.Class
                && named.SpecialType is not (SpecialType.System_Enum or SpecialType.System_ValueType);
        }
    }
}
