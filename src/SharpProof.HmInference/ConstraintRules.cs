using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private readonly List<(HmType Variable, string? Keyword, HmType? Type, bool BaseClass)> inferredConstraints = [];

        private void RequireKeyword(HmType variable, string keyword)
        {
            if (variable is null)
            { throw new ArgumentNullException(nameof(variable)); }
            if (keyword is null)
            { throw new ArgumentNullException(nameof(keyword)); }
            inferredConstraints.Add((variable, keyword, null, false));
        }

        private void RequireType(HmType variable, ITypeSymbol type, Dictionary<ITypeParameterSymbol, HmType>? mapping = null)
        {
            if (variable is null)
            { throw new ArgumentNullException(nameof(variable)); }
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            inferredConstraints.Add((variable, null, CSharpTypes.Rigid(type, mapping), type.TypeKind == TypeKind.Class));
        }

        private void ImportConstraints(ITypeParameterSymbol parameter, HmType variable, Dictionary<ITypeParameterSymbol, HmType> mapping, ITypeSymbol? actual = null)
        {
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            if (variable is null)
            { throw new ArgumentNullException(nameof(variable)); }
            if (mapping is null)
            { throw new ArgumentNullException(nameof(mapping)); }
            if (parameter.HasUnmanagedTypeConstraint)
            { RequireKeyword(variable, "unmanaged"); }
            else if (parameter.HasValueTypeConstraint)
            { RequireKeyword(variable, "struct"); }
            if (parameter.HasReferenceTypeConstraint)
            {
                RequireKeyword(variable, parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            if (parameter.HasNotNullConstraint)
            {
                RequireKeyword(variable, "notnull");
                if (variable.Root.Name == CSharpTypes.NullableParameter && actual is { IsValueType: true }
                    && actual is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    // An annotated type argument satisfies notnull without a new
                    // warning only in the non-nullable value domain. Preserve that
                    // domain when the original constructed call proves it applies.
                    var underlying = variable.Root;
                    while (underlying.Name == CSharpTypes.NullableParameter)
                    { underlying = underlying.Arguments[0].Root; }
                    RequireKeyword(underlying, "struct");
                }
            }
            foreach (var type in parameter.ConstraintTypes)
            { RequireType(variable, type, mapping); }
            if (parameter.HasConstructorConstraint)
            { RequireKeyword(variable, "new()"); }
        }

        private void ImportConstructedConstraints(ITypeSymbol declared, HmType inferred)
        {
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            inferred = inferred.Root;
            if (inferred.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
            {
                ImportConstructedConstraints(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated), inferred.Arguments[0]);
                return;
            }
            if (declared is IArrayTypeSymbol array && inferred.Name.StartsWith("[", StringComparison.Ordinal))
            {
                ImportConstructedConstraints(array.ElementType, inferred.Arguments[0]);
                return;
            }
            if (declared is not INamedTypeSymbol named || inferred.Arguments.Length != named.TypeArguments.Length || named.TypeArguments.Length == 0)
            { return; }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var i = 0; i < named.TypeArguments.Length; i++)
            { mapping.Add(named.OriginalDefinition.TypeParameters[i], inferred.Arguments[i]); }
            for (var i = 0; i < named.TypeArguments.Length; i++)
            {
                ImportConstraints(named.OriginalDefinition.TypeParameters[i], inferred.Arguments[i], mapping, named.TypeArguments[i]);
                ImportConstructedConstraints(named.TypeArguments[i], inferred.Arguments[i]);
            }
        }

        private List<TypeParameterConstraintClauseSyntax> BuildConstraints(Dictionary<HmType, string> variables, bool nullableAnnotations)
        {
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            // Constraints establish annotation domains only after solving. Apply all
            // domains before rendering constraints that mention other variables.
            foreach (var requirement in inferredConstraints)
            {
                if (requirement.Type?.Root.Name == "object")
                { continue; }
                var constrained = requirement.Variable.Root;
                while (constrained.Name == CSharpTypes.NullableParameter)
                { constrained = constrained.Arguments[0].Root; }
                if (!constrained.IsVariable)
                { continue; }
                if (requirement.Keyword is "struct" or "unmanaged")
                { constrained.IsValueType = true; }
                else if (constrained.IsValueType != true && (requirement.Keyword is "class" or "class?" || requirement.BaseClass))
                { constrained.IsValueType = false; }
            }
            var clauses = new List<TypeParameterConstraintClauseSyntax>();
            foreach (var variable in variables)
            {
                var keywords = new HashSet<string>(StringComparer.Ordinal);
                var types = new List<string>();
                var hasBaseClass = false;
                foreach (var requirement in inferredConstraints)
                {
                    if (requirement.Type?.Root.Name == "object")
                    { continue; }
                    var constrained = requirement.Variable.Root;
                    while (constrained.Name == CSharpTypes.NullableParameter)
                    { constrained = constrained.Arguments[0].Root; }
                    if (!ReferenceEquals(constrained, variable.Key.Root))
                    { continue; }
                    if (requirement.Keyword is not null)
                    { keywords.Add(requirement.Keyword); }
                    if (requirement.Type is not null)
                    {
                        foreach (var free in requirement.Type.FreeVariables())
                        {
                            if (!variables.ContainsKey(free))
                            { throw new InferenceException("A constraint-only type variable cannot be inferred from method arguments."); }
                        }
                        var type = requirement.Type.Format(variables, nullableAnnotations);
                        if (!types.Contains(type))
                        { if (requirement.BaseClass) { types.Insert(0, type); } else { types.Add(type); } }
                        hasBaseClass |= requirement.BaseClass;
                    }
                }
                var value = keywords.Contains("unmanaged") || keywords.Contains("struct");
                var reference = keywords.Contains("class") || keywords.Contains("class?");
                if (value && reference)
                { throw new InferenceException("Conflicting reference/value constraints."); }
                var parts = new List<string>();
                if (value)
                { parts.Add(keywords.Contains("unmanaged") ? "unmanaged" : "struct"); }
                else if (reference && !hasBaseClass)
                { parts.Add(keywords.Contains("class") || keywords.Contains("notnull") ? "class" : "class?"); }
                else if (keywords.Contains("notnull") && !hasBaseClass)
                { parts.Add("notnull"); }
                parts.AddRange(types);
                if (keywords.Contains("new()") && !value)
                { parts.Add("new()"); }
                if (parts.Count == 0)
                { continue; }
                var shape = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration($"void C<{variable.Value}>() where {variable.Value} : {string.Join(", ", parts)} {{}}")!;
                clauses.Add(shape.ConstraintClauses[0]);
            }
            return clauses;
        }
    }
}
