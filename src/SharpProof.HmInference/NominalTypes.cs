using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static partial class CSharpTypes
{
    // Display strings omit assembly identity and can hide distinct enclosing T's.
    internal static string ConstructorIdentity(ITypeSymbol type)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        if (type is INamedTypeSymbol named)
        {
            return named.ContainingAssembly.Identity + ":" + (named.ContainingType is null
                ? named.ContainingNamespace.ToDisplayString() : ConstructorIdentity(named.ContainingType)) + "." + named.MetadataName;
        }
        if (type is ITypeParameterSymbol parameter)
        {
            var owner = parameter.ContainingSymbol;
            var declaration = owner.DeclaringSyntaxReferences.FirstOrDefault();
            var scope = owner.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (declaration is not null)
            {
                var node = declaration.GetSyntax();
                // Method ordinal survives replacing a signature in the same tree.
                scope = ConstructorIdentity(parameter.ContainingType) + ":" + owner.Name + ":" + declaration.SyntaxTree.FilePath + ":" + declaration.SyntaxTree.GetRoot().DescendantNodes()
                    .Where(n => { if (n is null) { throw new ArgumentNullException(nameof(n)); } return n is MethodDeclarationSyntax or LocalFunctionStatementSyntax; })
                    .TakeWhile(n => { if (n is null) { throw new ArgumentNullException(nameof(n)); } return n.SpanStart != node.SpanStart; }).Count();
                if (owner is INamedTypeSymbol ownerType)
                { scope = ConstructorIdentity(ownerType); }
            }
            return owner.ContainingAssembly.Identity + ":parameter:" + owner.Kind + ":" + scope + ":" + parameter.Ordinal;
        }
        return type.TypeKind + ":" + type.MetadataName;
    }

    private static string BoundSpelling(INamedTypeSymbol type, SemanticModel? model, int position)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        var full = Display(type);
        if (model is null)
        { return full; }
        bool Binds(string text)
        {
            if (text is null)
            { throw new ArgumentNullException(nameof(text)); }
            var bound = model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName(text), SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
            return SymbolEqualityComparer.Default.Equals(type, bound);
        }
        if (Binds(full))
        { return full; }
        var candidates = new List<string> { full };
        foreach (var reference in model.Compilation.References)
        {
            if (!SymbolEqualityComparer.Default.Equals(model.Compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly))
            { continue; }
            foreach (var alias in reference.Properties.Aliases)
            {
                if (alias == "global")
                { continue; }
                candidates.Add(Identifier(alias) + "::" + (full.StartsWith("global::", StringComparison.Ordinal) ? full.Substring(8) : full));
            }
        }
        candidates.Add(type.ToMinimalDisplayString(model, position, SymbolDisplayFormat.FullyQualifiedFormat));
        foreach (var candidate in candidates)
        { if (Binds(candidate)) { return candidate; } }
        // Imported constraints can mention foreign type parameters. Their own
        // arguments will be substituted by Render; verify the constructor here.
        foreach (var candidate in candidates)
        {
            var bound = model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName(candidate), SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
            if (bound is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition))
            { return candidate; }
        }
        throw new InferenceException("The inferred nominal type cannot be named unambiguously at this declaration.");
    }

    internal static bool SameSignatureType(ITypeSymbol left, ITypeSymbol right)
    {
        if (left is null)
        { throw new ArgumentNullException(nameof(left)); }
        if (right is null)
        { throw new ArgumentNullException(nameof(right)); }
        if (Display(left) != Display(right))
        { return false; }
        if (left is IArrayTypeSymbol a && right is IArrayTypeSymbol b)
        { return a.Rank == b.Rank && a.IsSZArray == b.IsSZArray && SameSignatureType(a.ElementType, b.ElementType); }
        if (ConstructorIdentity(left) != ConstructorIdentity(right))
        { return false; }
        if (left is INamedTypeSymbol x && right is INamedTypeSymbol y)
        {
            if ((x.ContainingType is null) != (y.ContainingType is null) || x.TypeArguments.Length != y.TypeArguments.Length)
            { return false; }
            if (x.ContainingType is not null && !SameSignatureType(x.ContainingType, y.ContainingType!))
            { return false; }
            for (var i = 0; i < x.TypeArguments.Length; i++)
            { if (!SameSignatureType(x.TypeArguments[i], y.TypeArguments[i])) { return false; } }
        }
        return true;
    }
}
