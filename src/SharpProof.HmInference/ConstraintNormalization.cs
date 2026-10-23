using Microsoft.CodeAnalysis;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static HmType? ConstraintProjection(HmType source, HmType target)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            source = ConversionCore(source);
            target = ConversionCore(target);
            if (source.CSharpSymbol is not INamedTypeSymbol named || target.CSharpSymbol is not INamedTypeSymbol required)
            { return null; }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(named, source, mapping))
            { return null; }
            var ancestors = new List<INamedTypeSymbol>();
            for (var ancestor = named.OriginalDefinition; ancestor is not null; ancestor = ancestor.BaseType)
            { ancestors.Add(ancestor); }
            ancestors.AddRange(named.OriginalDefinition.AllInterfaces);
            var matches = ancestors.Where(ancestor =>
            { if (ancestor is null) { throw new ArgumentNullException(nameof(ancestor)); } return CSharpTypes.ConstructorIdentity(ancestor) == CSharpTypes.ConstructorIdentity(required); }).ToArray();
            if (matches.Length == 1)
            { return CSharpTypes.Rigid(matches[0], mapping); }
            HmType? selected = null;
            foreach (var match in matches)
            {
                var projection = CSharpTypes.Rigid(match, mapping);
                if (!CanUnifyConstraintProjection(projection, target))
                { continue; }
                // A failed alternative must not change the live substitution graph.
                // Leave genuinely ambiguous choices unresolved for a later pass.
                if (selected is not null && !SameConstraintShape(selected, projection))
                { return null; }
                selected = projection;
            }
            return selected;
        }

        private static bool CanUnifyConstraintProjection(HmType projection, HmType target)
        {
            if (projection is null)
            { throw new ArgumentNullException(nameof(projection)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            var copies = new Dictionary<HmType, HmType>();
            HmType Copy(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (copies.TryGetValue(type, out var existing))
                { return existing; }
                var copy = type.WithArguments(type.Arguments.Select(Copy).ToArray());
                copies.Add(type, copy);
                return copy;
            }
            try
            {
                HmType.Unify(Copy(projection), Copy(target));
                return true;
            }
            catch (InferenceException)
            { return false; }
        }

        private void NormalizeConstraints()
        {
            int Measure()
            {
                var free = new HashSet<HmType>();
                foreach (var requirement in inferredConstraints)
                {
                    free.UnionWith(requirement.Variable.FreeVariables());
                    if (requirement.Type is not null)
                    { free.UnionWith(requirement.Type.FreeVariables()); }
                }
                return inferredConstraints.Count + free.Count;
            }
            int previous;
            bool expanded;
            do
            {
                previous = Measure();
                expanded = NormalizeConstraintPass();
            } while (expanded || Measure() < previous);
        }

        private bool NormalizeConstraintPass()
        {
            var expanded = ResolveConcreteLowerBounds();
            expanded |= ResolveDependentConstraints();
            expanded |= NormalizeConstraintNullability();
            foreach (var requirement in inferredConstraints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (requirement.Type is null || ConversionCore(requirement.Variable).IsVariable || ConversionCore(requirement.Type).CSharpSymbol is not INamedTypeSymbol required)
                { continue; }
                // Class inheritance and invariant interfaces determine argument
                // equality. Variant interfaces contribute directional conversion bounds.
                if (required.TypeKind == TypeKind.Interface && required.OriginalDefinition.TypeParameters.Any(parameter =>
                    { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return parameter.Variance != VarianceKind.None; }))
                { expanded |= BindVariantConstraint(requirement.Variable, requirement.Type, required); continue; }
                if (ConstraintProjection(requirement.Variable, requirement.Type) is { } projection)
                { HmType.Unify(projection, ConversionCore(requirement.Type)); }
            }
            var redundant = new HashSet<int>();
            for (var i = 0; i < inferredConstraints.Count; i++)
            {
                var first = inferredConstraints[i];
                if (first.Type is null || !ConversionCore(first.Variable).IsVariable || redundant.Contains(i))
                { continue; }
                for (var j = i + 1; j < inferredConstraints.Count; j++)
                {
                    var second = inferredConstraints[j];
                    if (second.Type is null || first.BaseClass != second.BaseClass || redundant.Contains(j) || !ReferenceEquals(ConversionCore(first.Variable), ConversionCore(second.Variable)))
                    { continue; }
                    if (ConstraintProjection(first.Type, second.Type) is { } parent
                        && (first.BaseClass && second.BaseClass || SameConstraintShape(parent, second.Type)))
                    { if (first.BaseClass && second.BaseClass) { HmType.Unify(parent, ConversionCore(second.Type)); } redundant.Add(j); }
                    else if (ConstraintProjection(second.Type, first.Type) is { } ancestor
                        && (first.BaseClass && second.BaseClass || SameConstraintShape(ancestor, first.Type)))
                    { if (first.BaseClass && second.BaseClass) { HmType.Unify(ancestor, ConversionCore(first.Type)); } redundant.Add(i); break; }
                }
            }
            for (var i = inferredConstraints.Count - 1; i >= 0; i--)
            { if (redundant.Contains(i)) { inferredConstraints.RemoveAt(i); } }
            CollapseConstraintCycles();
            expanded |= NormalizeTransitiveConstraints();
            for (var i = inferredConstraints.Count - 1; i >= 0; i--)
            {
                var requirement = inferredConstraints[i];
                if (requirement.Keyword is null && requirement.Type is not null && ReferenceEquals(ConversionCore(requirement.Variable), ConversionCore(requirement.Type)))
                { inferredConstraints.RemoveAt(i); }
            }
            return expanded;
        }

        private bool NormalizeConstraintNullability()
        {
            var changed = false;
            var notNull = new HashSet<HmType>();
            foreach (var requirement in inferredConstraints)
            {
                var variable = ConversionCore(requirement.Variable);
                if (!variable.IsVariable)
                { continue; }
                if (requirement.Keyword is "class" or "notnull" or "struct" or "unmanaged"
                    || requirement.Type?.Root.CSharpSymbol is INamedTypeSymbol { NullableAnnotation: NullableAnnotation.NotAnnotated })
                { notNull.Add(variable); }
            }
            for (var i = 0; i < inferredConstraints.Count; i++)
            {
                var requirement = inferredConstraints[i];
                if (requirement.Keyword == "notnull" && requirement.Variable.Root.Name == CSharpTypes.NullableParameter
                    && ConversionCore(requirement.Variable).IsVariable
                    && OriginalConstraintShape(requirement.Variable) is { IsValueType: true } original
                    && original.Name != CSharpTypes.NullableValue)
                {
                    // Solving the body can annotate a type argument after its
                    // constraints were imported. Preserve its original value
                    // domain when that annotation would otherwise violate notnull.
                    var core = ConversionCore(requirement.Variable);
                    if (!inferredConstraints.Any(existing => existing.Keyword is "struct" or "unmanaged"
                        && ReferenceEquals(ConversionCore(existing.Variable), core)))
                    { RequireKeyword(core, "struct"); changed = true; }
                }
                if (requirement.Keyword == "class?" && notNull.Contains(ConversionCore(requirement.Variable)))
                { inferredConstraints[i] = (requirement.Variable, "class", requirement.Type, requirement.BaseClass); }
                if (requirement.Type?.Root.Name == CSharpTypes.NullableReference && notNull.Contains(ConversionCore(requirement.Variable)))
                { inferredConstraints[i] = (requirement.Variable, requirement.Keyword, ConversionCore(requirement.Type), requirement.BaseClass); }
            }
            return changed;
        }

        private static bool SameConstraintShape(HmType left, HmType right)
        {
            if (left is null)
            { throw new ArgumentNullException(nameof(left)); }
            if (right is null)
            { throw new ArgumentNullException(nameof(right)); }
            return SameCore(ConversionCore(left), ConversionCore(right));
            static bool SameCore(HmType first, HmType second)
            {
                if (first is null)
                { throw new ArgumentNullException(nameof(first)); }
                if (second is null)
                { throw new ArgumentNullException(nameof(second)); }
                first = first.Root;
                second = second.Root;
                if (ReferenceEquals(first, second))
                { return true; }
                if (first.IsVariable || second.IsVariable || first.Name != second.Name || first.NestedTypeName != second.NestedTypeName
                    || first.Arguments.Length != second.Arguments.Length
                    || first.CSharpSymbol is not null && second.CSharpSymbol is not null
                        && CSharpTypes.ConstructorIdentity(first.CSharpSymbol) != CSharpTypes.ConstructorIdentity(second.CSharpSymbol))
                { return false; }
                for (var i = 0; i < first.Arguments.Length; i++)
                { if (!SameCore(first.Arguments[i], second.Arguments[i])) { return false; } }
                return true;
            }
        }

        private void CollapseConstraintCycles()
        {
            var graph = new Dictionary<HmType, List<HmType>>();
            foreach (var requirement in inferredConstraints)
            {
                var source = ConversionCore(requirement.Variable);
                var target = requirement.Type is null ? null : ConversionCore(requirement.Type);
                if (!source.IsVariable || target is not { IsVariable: true })
                { continue; }
                if (!graph.TryGetValue(source, out var edges))
                { edges = []; graph.Add(source, edges); }
                edges.Add(target);
                if (!graph.ContainsKey(target))
                { graph.Add(target, []); }
            }
            var indices = new Dictionary<HmType, int>();
            var lowLinks = new Dictionary<HmType, int>();
            var stack = new Stack<HmType>();
            var active = new HashSet<HmType>();
            var next = 0;
            void Visit(HmType variable)
            {
                if (variable is null)
                { throw new ArgumentNullException(nameof(variable)); }
                cancellationToken.ThrowIfCancellationRequested();
                indices.Add(variable, next);
                lowLinks.Add(variable, next++);
                stack.Push(variable);
                active.Add(variable);
                foreach (var target in graph[variable])
                {
                    if (!indices.TryGetValue(target, out var targetIndex))
                    { Visit(target); lowLinks[variable] = Math.Min(lowLinks[variable], lowLinks[target]); }
                    else if (active.Contains(target))
                    { lowLinks[variable] = Math.Min(lowLinks[variable], targetIndex); }
                }
                if (lowLinks[variable] != indices[variable])
                { return; }
                HmType member;
                do
                {
                    member = stack.Pop();
                    active.Remove(member);
                    HmType.Unify(member, variable);
                } while (!ReferenceEquals(member, variable));
            }
            foreach (var variable in graph.Keys)
            { if (!indices.ContainsKey(variable)) { Visit(variable); } }
        }
    }
}
