namespace SharpProof.Specs;

internal static class ApiSpecContentDigest
{
    internal static string Compute(ImmutableArray<ApiSpecTemplate> templates)
    {
        using var hash = new CanonicalHashWriter();
        hash.Add("api-spec-content-v2", templates.Length);
        foreach (var template in templates)
        {
            var target = template.Target;
            hash.Add("target", target.WitnessIdentifier, target.DocumentationCommentId,
                target.ContainingTypeMetadataName, target.MemberKind, target.MemberName,
                target.IsStatic, target.GenericArity, target.ReceiverType, target.ResultType,
                target.ParameterTypes.Length);
            foreach (var type in target.ParameterTypes)
            {
                hash.Add(type);
            }

            foreach (var assembly in target.ApprovedAssemblies
                         .OrderBy(static item => item.Name, StringComparer.Ordinal)
                         .ThenBy(static item => item.PublicKeyToken, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.ReferenceFamily))
            {
                hash.Add("assembly", assembly.Name, assembly.PublicKeyToken.ToUpperInvariant(),
                    assembly.ReferenceFamily);
            }

            var facets = template.Facets;
            Add(hash, facets.Effects.Evidence, facets.Effects.Effects);
            Add(hash, facets.Allocation.Evidence, facets.Allocation.Behavior);
            var exceptionMetadataNames = facets.Throws.ExceptionMetadataNames
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToImmutableArray();
            Add(
                hash,
                facets.Throws.Evidence,
                facets.Throws.Behavior,
                exceptionMetadataNames.Length);
            foreach (var exception in exceptionMetadataNames)
            {
                hash.Add(exception);
            }

            Add(hash, facets.Nullness.Evidence, facets.Nullness.Result);
            Add(hash, facets.Cardinality.Evidence, facets.Cardinality.Result, facets.Cardinality.ExactCount);
            if (facets.Termination == null)
            {
                hash.Add("termination", null);
            }
            else
            {
                Add(
                    hash,
                    facets.Termination.Evidence,
                    facets.Termination.Behavior);
            }
            hash.Add("variables", template.Variables.Length);
            foreach (var variable in template.Variables)
            {
                hash.Add(variable.Role, variable.Ordinal, variable.Type);
            }

            hash.Add("postconditions", template.Postconditions.Length);
            foreach (var postcondition in template.Postconditions)
            {
                Add(hash, postcondition.Evidence);
                Add(hash, postcondition.Condition, template.Variables);
            }
        }
        return hash.Finish();
    }

    private static void Add(
        CanonicalHashWriter hash, SpecEvidence evidence,
        params object?[] values)
    {
        hash.Add(values).Add(evidence.Kind, evidence.Source);
    }

    private static void Add(
        CanonicalHashWriter hash, SpecTermDeclaration term,
        ImmutableArray<SpecVariableInfo> variables)
    {
        hash.Add(
            term.GetType().Name.Replace("Declaration", "Term"),
            term.Type);
        (object? Payload, SpecTermDeclaration[] Children) parts = term switch
        {
            SpecVariableDeclaration variable => (
                variables.Single(item =>
                    item.Role == variable.Role && item.Ordinal == variable.Ordinal).Id.Value, []),
            SpecBooleanDeclaration boolean => (boolean.Value, []),
            SpecIntegerDeclaration integer => (integer.Value, []),
            SpecStringDeclaration text => (text.Value, []),
            SpecNullDeclaration => (null, []),
            SpecUnaryDeclaration unary => (unary.Operator, [unary.Operand]),
            SpecBinaryDeclaration binary => (binary.Operator, [binary.Left, binary.Right]),
            SpecConditionalDeclaration conditional => (
                null, [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse]),
            SpecLengthDeclaration length => (null, [length.Value]),
            _ => throw new ArgumentOutOfRangeException(nameof(term))
        };
        if (parts.Payload != null)
        {
            hash.Add(parts.Payload);
        }

        foreach (var child in parts.Children)
        {
            Add(hash, child, variables);
        }
    }
}
