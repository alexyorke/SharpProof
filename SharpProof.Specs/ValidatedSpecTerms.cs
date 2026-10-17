namespace SharpProof.Specs;

internal static partial class ApiSpecTermValidator
{
    // Only the nested validation context can construct these nodes. A friend
    // assembly can neither derive a new kind nor wrap an unknown declaration.
    internal sealed partial class ValidatedSpecTerm
    {
        private ValidatedSpecTerm(
            SpecTermDeclaration declaration,
            object? payload,
            ImmutableArray<SpecTermDeclaration> children)
        {

            if (children.IsDefault)
            {
                throw new System.ArgumentException("Array must be initialized.", nameof(children));
            }
            if (declaration is null)
            {
                throw new System.ArgumentNullException(nameof(declaration));
            }
            Declaration = declaration;
            Payload = payload;
            Children = children;
        }

        internal SpecTermDeclaration Declaration { get; }
        internal object? Payload { get; }
        internal ImmutableArray<SpecTermDeclaration> Children { get; }

        internal static TermFacts ValidateAndCompile(
            SpecTermDeclaration declaration,
            IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
            ApiSpecFacets facets,
            IDictionary<SpecTermDeclaration, ValidatedSpecTerm> validatedTerms,
            bool allowResult)
        {
            if (facets is null)
            {
                throw new System.ArgumentNullException(nameof(facets));
            }
            if (validatedTerms is null)
            {
                throw new System.ArgumentNullException(nameof(validatedTerms));
            }
            if (variables is null)
            {
                throw new System.ArgumentNullException(nameof(variables));
            }
            var facts = new ValidationContext(variables, facets, validatedTerms, allowResult)
                .Validate(declaration, depth: 1);
            ValidateExpansion(declaration);
            return facts;
        }
    }
}
