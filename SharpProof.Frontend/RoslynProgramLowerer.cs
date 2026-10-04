using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

public sealed class RoslynProgramLowerer(IrFactory factory)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));

    public FrontendProgramLoweringResult LowerCandidate(ControlFlowGraph graph, TotalLoweringContext context)
    {
        return LowerCandidate(graph, context, default);
    }

    public FrontendProgramLoweringResult LowerCandidate(ControlFlowGraph graph, TotalLoweringContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullGuard.NotNull(context, nameof(context));
        if (!ReferenceEquals(_factory, context.Factory))
        {
            throw new ArgumentException("The context belongs to another factory.", nameof(context));
        }
        return new RoslynTotalProgramLowerer(context, cancellationToken).Lower(ArgumentNullGuard.NotNull(graph, nameof(graph)));
    }

    // An auto-property accessor only reads or writes its backing field. The
    // read is an approximation: the IR does not model the field's value.
    internal FrontendProgramLoweringResult? LowerAutoAccessor(TotalLoweringContext context, AccessorDeclarationSyntax declaration)
    {
        ArgumentNullGuard.NotNull(context, nameof(context));
        if (!ReferenceEquals(_factory, context.Factory))
        { throw new ArgumentException("The context belongs to another factory.", nameof(context)); }
        if (declaration.Body != null || declaration.ExpressionBody != null || !context.HasScalarSignature ||
            context.Target.AssociatedSymbol is not IPropertySymbol property ||
            !property.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(field =>
                SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property)))
        { return null; }
        var getter = context.Target.MethodKind == MethodKind.PropertyGet;
        if (getter != (context.Result != null))
        { return null; }
        var builder = new IrProgramBuilder(_factory);
        var block = builder.CreateBlock("entry");
        builder.SetEntry(block);
        var site = context.SyntaxSite(getter ? OperationKind.FieldReference : OperationKind.SimpleAssignment, declaration);
        var structural = _factory.CreateOperation("candidate:auto-accessor");
        foreach (var binding in context.Parameters)
        {
            builder.Assign(block, structural, binding.Current, _factory.Variable(binding.Entry));
            builder.Assign(block, structural, binding.PreState, _factory.Variable(binding.Entry));
        }
        if (getter)
        {
            var result = context.Result!.Value;
            var value = context.Temporary(_factory.GetVariableInfo(result).Type);
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
            builder.Assign(block, site, result, _factory.Variable(value));
            builder.Return(block, site, _factory.Variable(result));
        }
        else
        {
            builder.Write(block, site, property.IsStatic ? IrWriteRegion.Static : IrWriteRegion.Field);
            builder.Return(block, site);
        }
        return new(builder.Build(), FrontendSubsetClassification.Exact, context.Variables, context.Captures, [], context.Origin);
    }

    internal FrontendProgramLoweringResult LowerShadowSourceBody(ControlFlowGraph graph, TotalLoweringContext context,
        Func<IMethodSymbol, bool> preserveSourceCall, CancellationToken cancellationToken,
        Func<IMethodSymbol, TotalScalarCallModel?>? resolveScalarModel = null)
    {
        ArgumentNullGuard.NotNull(graph, nameof(graph));
        ArgumentNullGuard.NotNull(context, nameof(context));
        ArgumentNullGuard.NotNull(preserveSourceCall, nameof(preserveSourceCall));
        if (!ReferenceEquals(_factory, context.Factory))
        { throw new ArgumentException("The context belongs to another factory.", nameof(context)); }
        var calls = resolveScalarModel != null && graph.OriginalOperation.SemanticModel?.Compilation is { } compilation
            ? new TotalSourceCallSession(compilation, static _ => false, null, cancellationToken, resolveScalarModel) : null;
        return new RoslynTotalProgramLowerer(context, cancellationToken, calls,
            preserveSourceCall: preserveSourceCall).Lower(graph);
    }

    internal FrontendProgramLoweringResult LowerCandidate(ControlFlowGraph graph, TotalLoweringContext context,
        Func<TotalLoweringContext, bool> prepareCallee, CancellationToken cancellationToken)
    {
        return LowerCandidate(graph, context, prepareCallee, null, cancellationToken);
    }

    internal FrontendProgramLoweringResult LowerCandidate(ControlFlowGraph graph, TotalLoweringContext context,
        Func<TotalLoweringContext, bool> prepareCallee, ResolveTotalIlBody? resolveIl, CancellationToken cancellationToken,
        Func<IMethodSymbol, TotalScalarCallModel?>? resolveScalarModel = null,
        Func<TotalLoweringContext, TotalIlBody, bool>? prepareMetadata = null, bool opaqueCalls = false,
        Func<IMethodSymbol, IrOpaqueCallEffects?>? opaqueEffects = null, bool approximateElementReads = false)
    {
        ArgumentNullGuard.NotNull(graph, nameof(graph));
        ArgumentNullGuard.NotNull(context, nameof(context));
        ArgumentNullGuard.NotNull(prepareCallee, nameof(prepareCallee));
        if (!ReferenceEquals(_factory, context.Factory))
        { throw new ArgumentException("The context belongs to another factory.", nameof(context)); }
        if (graph.OriginalOperation.SemanticModel?.Compilation is not { } compilation)
        { return LowerCandidate(graph, context, cancellationToken); }
        return new RoslynTotalProgramLowerer(context, cancellationToken,
            new TotalSourceCallSession(compilation, prepareCallee, resolveIl, cancellationToken, resolveScalarModel, prepareMetadata)
            { OpaqueCalls = opaqueCalls, OpaqueEffects = opaqueEffects, ApproximateElementReads = approximateElementReads }).Lower(graph);
    }
}
