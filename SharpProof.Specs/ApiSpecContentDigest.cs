namespace SharpProof.Specs;

internal static class ApiSpecContentDigest
{
    internal static string Compute(ImmutableArray<ApiSpecTemplate> templates)
    {
        using var hash = new CanonicalHashWriter();
        hash.Add("api-spec-content-v3").Add(templates.Length);
        foreach (var template in templates)
        {
            var partsVisitor = new PartsVisitor(template.VariablesBySlot);
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
                         .OrderBy(static item => item.Name, StringComparer.Ordinal)
                         .ThenBy(static item => item.PublicKeyToken, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.ReferenceFamily))
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
                .OrderBy(static name => name, StringComparer.Ordinal)
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
                    template.ValidatedTerms,
                    partsVisitor);
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
                Add(hash, postcondition.Condition, template.ValidatedTerms, partsVisitor);
            }
        }
        return hash.Finish();
    }

    private static void Add(
        CanonicalHashWriter hash, SpecTermDeclaration term,
        IReadOnlyDictionary<SpecTermDeclaration, ValidatedSpecTerm> validatedTerms,
        PartsVisitor visitor)
    {
        hash.Add(term.GetType().Name.Replace("Declaration", "Term"))
            .Add(term.Type);
        var parts = validatedTerms[term].Accept(visitor);
        if (parts.Payload != null)
        {
            hash.Add(parts.Payload);
        }

        foreach (var child in parts.Children)
        {
            Add(hash, child, validatedTerms, visitor);
        }
    }

    private readonly record struct TermParts(object? Payload, SpecTermDeclaration[] Children);

    private sealed class PartsVisitor(
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables)
        : IValidatedSpecTermVisitor<TermParts>
    {
        public TermParts Variable(SpecVariableDeclaration declaration)
        {
            return new(variables[(declaration.Role, declaration.Ordinal)].Id.Value, []);
        }

        public TermParts Boolean(SpecBooleanDeclaration declaration)
        {
            return new(declaration.Value, []);
        }

        public TermParts Integer(SpecIntegerDeclaration declaration)
        {
            return new(declaration.Value, []);
        }

        public TermParts String(SpecStringDeclaration declaration)
        {
            return new(declaration.Value, []);
        }

        public TermParts Null(SpecNullDeclaration declaration)
        {
            return new(null, []);
        }

        public TermParts Unary(SpecUnaryDeclaration declaration)
        {
            return new(declaration.Operator, [declaration.Operand]);
        }

        public TermParts Binary(SpecBinaryDeclaration declaration)
        {
            return new(declaration.Operator, [declaration.Left, declaration.Right]);
        }

        public TermParts Conditional(SpecConditionalDeclaration declaration)
        {
            return new(null, [declaration.Condition, declaration.WhenTrue, declaration.WhenFalse]);
        }

        public TermParts Length(SpecLengthDeclaration declaration)
        {
            return new(null, [declaration.Value]);
        }
    }
}
