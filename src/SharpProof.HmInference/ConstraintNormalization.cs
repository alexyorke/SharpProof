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
            source = source.Root;
            target = target.Root;
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
            // Multiple implementations of one interface may denote different
            // domains. Their original selected instantiation must disambiguate them.
            return matches.Length == 1 ? CSharpTypes.Rigid(matches[0], mapping) : null;
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
            do
            {
                previous = Measure();
                NormalizeConstraintPass();
            } while (Measure() < previous);
        }

        private void NormalizeConstraintPass()
        {
            foreach (var requirement in inferredConstraints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (requirement.Variable.Root.IsVariable || requirement.Type?.Root.CSharpSymbol is not INamedTypeSymbol required)
                { continue; }
                // Class inheritance and invariant interfaces determine argument
                // equality. Variant interfaces require directional bounds instead.
                if (required.TypeKind == TypeKind.Interface && required.OriginalDefinition.TypeParameters.Any(parameter =>
                    { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return parameter.Variance != VarianceKind.None; }))
                { continue; }
                if (ConstraintProjection(requirement.Variable, requirement.Type) is { } projection)
                { HmType.Unify(projection, requirement.Type); }
            }
            var redundant = new HashSet<int>();
            for (var i = 0; i < inferredConstraints.Count; i++)
            {
                var first = inferredConstraints[i];
                if (!first.BaseClass || first.Type is null || !first.Variable.Root.IsVariable || redundant.Contains(i))
                { continue; }
                for (var j = i + 1; j < inferredConstraints.Count; j++)
                {
                    var second = inferredConstraints[j];
                    if (!second.BaseClass || second.Type is null || redundant.Contains(j) || !ReferenceEquals(first.Variable.Root, second.Variable.Root))
                    { continue; }
                    if (ConstraintProjection(first.Type, second.Type) is { } parent)
                    { HmType.Unify(parent, second.Type); redundant.Add(j); }
                    else if (ConstraintProjection(second.Type, first.Type) is { } ancestor)
                    { HmType.Unify(ancestor, first.Type); redundant.Add(i); break; }
                }
            }
            for (var i = inferredConstraints.Count - 1; i >= 0; i--)
            { if (redundant.Contains(i)) { inferredConstraints.RemoveAt(i); } }
            CollapseConstraintCycles();
            for (var i = inferredConstraints.Count - 1; i >= 0; i--)
            {
                var requirement = inferredConstraints[i];
                if (requirement.Keyword is null && requirement.Type is not null && ReferenceEquals(requirement.Variable.Root, requirement.Type.Root))
                { inferredConstraints.RemoveAt(i); }
            }
        }

        private void CollapseConstraintCycles()
        {
            var graph = new Dictionary<HmType, List<HmType>>();
            foreach (var requirement in inferredConstraints)
            {
                var source = requirement.Variable.Root;
                var target = requirement.Type?.Root;
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
