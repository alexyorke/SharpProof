namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsMonitorAttempt(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var owner = method.ContainingType;
        return method.IsStatic && method.ReturnsVoid && method.Name is "Enter" or "Exit" &&
            owner.MetadataName == "Monitor" && owner.ContainingType == null &&
            owner.ContainingNamespace is { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } &&
            owner.DeclaringSyntaxReferences.Length == 0 &&
            method.Parameters.Length >= 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
            SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, method.Parameters[0].Type.ContainingAssembly) &&
            (method.Parameters.Length == 1 && invocation.Arguments.Length == 1 ||
                invocation.IsImplicit && invocation.Syntax.AncestorsAndSelf().Any(syntax => syntax is Microsoft.CodeAnalysis.CSharp.Syntax.LockStatementSyntax) && method.Name == "Enter" &&
                method.Parameters.Length == 2 && invocation.Arguments.Length == 2 &&
                method.Parameters[1].RefKind == RefKind.Ref && method.Parameters[1].Type.SpecialType == SpecialType.System_Boolean &&
                invocation.Arguments[1].Value is ILocalReferenceOperation);
    }
}
