namespace SharpProof.Effects;

internal sealed class InvocationEmissionPolicy(Compilation compilation)
{
    private readonly CSharpInvocationEmissionPolicy _policy = new(ArgumentNullGuard.NotNull(compilation, nameof(compilation)));

    internal bool IsElided(IOperation operation) { if (operation is null) { throw new System.ArgumentNullException(nameof(operation)); } return _policy.IsElided(operation); }

    internal static bool IsUnimplementedPartial(IMethodSymbol method)
    { return CSharpInvocationEmissionPolicy.IsUnimplementedPartial(ArgumentNullGuard.NotNull(method, nameof(method))); }
}
