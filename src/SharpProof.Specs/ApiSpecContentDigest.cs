namespace SharpProof.Specs;

internal static class ApiSpecContentDigest
{
    internal static string Compute(ImmutableArray<ApiSpecTemplate> templates)
    {

        RepositoryParameterGuard.RequireInitialized(templates.IsDefault, nameof(templates), "Array must be initialized.");
        using var hash = new CanonicalHashWriter();
        hash.Add("api-spec-content-v3").Add(templates.Length);
        foreach (var template in templates)
        {
            var target = template.Target;
            hash.Add("target")
                .Add(target.WitnessIdentifier)
                .Add(target.DocumentationCommentId)
                .Add(target.ContainingTypeMetadataName)
                .Add(target.MemberKind)
                .Add(target.MemberName)
                .Add(target.IsStatic)
                .Add(target.GenericArity)
                .Add(target.ReceiverType)
                .Add(target.ResultType)
                .Add(target.ParameterTypes.Length);
            foreach (var type in target.ParameterTypes)
            {
                hash.Add(type);
            }

            foreach (var assembly in target.ApprovedAssemblies
                         .OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Name), StringComparer.Ordinal)
                         .ThenBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.PublicKeyToken), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ReferenceFamily)))
            {
                hash.Add("assembly")
                    .Add(assembly.Name)
                    .Add(assembly.PublicKeyToken.ToUpperInvariant())
                    .Add(assembly.ReferenceFamily);
            }

            var facets = template.Facets;
            hash.Add(facets.Effects.Effects)
                .Add(facets.Effects.Evidence.Kind)
                .Add(facets.Effects.Evidence.Source);
            hash.Add(facets.Allocation.Behavior)
                .Add(facets.Allocation.Evidence.Kind)
                .Add(facets.Allocation.Evidence.Source);
            var exceptionMetadataNames = facets.Throws.ExceptionMetadataNames
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name), StringComparer.Ordinal)
                .ToImmutableArray();
            hash.Add(facets.Throws.Behavior)
                .Add(exceptionMetadataNames.Length)
                .Add(facets.Throws.Evidence.Kind)
                .Add(facets.Throws.Evidence.Source);
            foreach (var exception in exceptionMetadataNames)
            {
                hash.Add(exception);
            }
            if (facets.Throws.NormalCompletion == null)
            {
                hash.Add("normal-completion").Add((string?)null);
            }
            else
            {
                hash.Add("normal-completion");
                Add(
                    hash,
                    facets.Throws.NormalCompletion,
                    template.ValidatedTerms);
            }

            hash.Add(facets.Nullness.Result)
                .Add(facets.Nullness.Evidence.Kind)
                .Add(facets.Nullness.Evidence.Source);
            hash.Add(facets.Cardinality.Result)
                .Add(facets.Cardinality.ExactCount)
                .Add(facets.Cardinality.Evidence.Kind)
                .Add(facets.Cardinality.Evidence.Source);
            if (facets.Termination == null)
            {
                hash.Add("termination").Add((string?)null);
            }
            else
            {
                hash.Add(facets.Termination.Behavior)
                    .Add(facets.Termination.Evidence.Kind)
                    .Add(facets.Termination.Evidence.Source);
            }
            hash.Add("variables").Add(template.Variables.Length);
            foreach (var variable in template.Variables)
            {
                hash.Add(variable.Role).Add(variable.Ordinal).Add(variable.Type);
            }

            hash.Add("postconditions").Add(template.Postconditions.Length);
            foreach (var postcondition in template.Postconditions)
            {
                hash.Add(postcondition.Evidence.Kind)
                    .Add(postcondition.Evidence.Source);
                Add(hash, postcondition.Condition, template.ValidatedTerms);
            }
        }
        return hash.Finish();
    }

    private static void Add(
        CanonicalHashWriter hash, SpecTermDeclaration term,
        ImmutableDictionary<SpecTermDeclaration, ValidatedSpecTerm> validatedTerms)
    {
        hash = RepositoryParameterGuard.NotNull(hash, nameof(hash));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        validatedTerms = RepositoryParameterGuard.NotNull(validatedTerms, nameof(validatedTerms));
        var node = validatedTerms[term];
        hash.Add(node.Declaration.GetType().Name.Replace("Declaration", "Term"))
            .Add(node.Declaration.Type);
        if (node.Payload != null)
        {
            hash.Add(node.Payload);
        }

        foreach (var child in node.Children)
        {
            Add(hash, child, validatedTerms);
        }
    }
}
