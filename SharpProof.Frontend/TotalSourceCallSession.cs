using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

// One compiler-owned expansion session covers the caller and every fresh frame.
// It supplies source bodies, never relational summaries or contract premises.
internal sealed class TotalSourceCallSession(Compilation compilation,
    Func<TotalLoweringContext, bool> prepareCallee, ResolveTotalIlBody? resolveIl, CancellationToken cancellationToken)
{
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _activeIl = new(StringComparer.Ordinal);
    private int _remaining = RoslynTotalProgramLowerer.MaximumRegionSteps;

    internal bool Spend(int amount = 1)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount < 0 || amount > _remaining)
        { _remaining = 0; return false; }
        _remaining -= amount;
        return true;
    }

    internal bool Enter(IMethodSymbol method)
    {
        return Spend() && _active.Count < 256 && _active.Add(method);
    }

    internal void Leave(IMethodSymbol method)
    {
        _active.Remove(method);
    }

    internal TotalIlBody? PrepareIl(IMethodSymbol method)
    {
        if (resolveIl == null || !Spend(method.Parameters.Length + 1) || _activeIl.Contains(IlKey(method)))
        { return null; }
        return resolveIl(method, cancellationToken);
    }

    internal bool EnterIl(IMethodSymbol method)
    {
        return Spend() && _active.Count + _activeIl.Count < 256 && _activeIl.Add(IlKey(method));
    }

    internal void LeaveIl(IMethodSymbol method)
    { _activeIl.Remove(IlKey(method)); }

    private static string IlKey(IMethodSymbol method)
    { return method.ContainingAssembly.Identity + "/" + method.ContainingModule.Name + "/" + method.MetadataToken; }

    internal bool TryPrepare(TotalLoweringContext caller, IInvocationOperation invocation,
        out TotalLoweringContext? frame, out ControlFlowGraph? graph)
    {
        frame = null;
        graph = null;
        var method = invocation.TargetMethod;
        if (!Spend(method.Parameters.Length + 1) || _active.Contains(method) || method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic || method.IsAsync || method.IsExtern || method.IsVirtual ||
            method.Arity != 0 || method.ReducedFrom != null || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.PartialDefinitionPart != null || method.PartialImplementationPart != null ||
            method.ContainingType.IsGenericType || invocation.Instance != null ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams ||
                !CSharpOperationSemantics.IsValueDomain(parameter.Type)) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType) ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) ||
            method.DeclaringSyntaxReferences.Length != 1 || invocation.Arguments.Length != method.Parameters.Length)
        { return false; }
        var ordinals = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            if (!Spend() || argument.Parameter is not { } parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method) ||
                !ordinals.Add(parameter.Ordinal) || argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue) ||
                argument.ArgumentKind == ArgumentKind.DefaultValue &&
                    (!parameter.HasExplicitDefaultValue || !argument.Value.ConstantValue.HasValue))
            { return false; }
        }
        var reference = method.DeclaringSyntaxReferences[0];
        if (!compilation.ContainsSyntaxTree(reference.SyntaxTree) ||
            reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
        { return false; }
        foreach (var node in declaration.DescendantNodesAndSelf())
        {
            if (!Spend() || node is YieldStatementSyntax)
            { return false; }
        }
        if (!HasNoTypeInitialization(method.ContainingType) || !Spend(method.Parameters.Length * 3 + 1))
        { return false; }
        frame = caller.CreateFrame(method);
        if (!prepareCallee(frame))
        { frame = null; return false; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        { graph = ControlFlowGraph.Create(declaration, CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree), cancellationToken); }
        catch (ArgumentException)
        { frame = null; return false; }
        return graph != null;
    }

    private bool HasNoTypeInitialization(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            foreach (var member in current.GetMembers())
            {
                if (!Spend())
                { return false; }
                if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: false })
                { return false; }
                if (!member.IsStatic || member is IFieldSymbol { IsConst: true })
                { continue; }
                foreach (var reference in member.DeclaringSyntaxReferences)
                {
                    if (!Spend())
                    { return false; }
                    var syntax = reference.GetSyntax(cancellationToken);
                    var value = syntax switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value,
                        _ => null
                    };
                    if (value != null && (!CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree).GetConstantValue(value, cancellationToken).HasValue ||
                        !CSharpOperationSemantics.IsScalar(member is IFieldSymbol field ? field.Type : (member as IPropertySymbol)?.Type)))
                    { return false; }
                }
            }
        }
        return true;
    }
}
