namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private IrBlockId IntegralConvertInitialization(IInvocationOperation invocation, IrTerm argument, IrBlockId block)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        argument = RepositoryParameterGuard.NotNull(argument, nameof(argument));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var source = _factory.GetOperationInfo(_context.Site(invocation)).SourceSpan;
        var primitive = invocation.TargetMethod.Parameters[0].Type.SpecialType.ToString();
        var site = _factory.CreateOperation(IrOpaqueCallSite.IntegralConvertInitialization(primitive), source);
        var initialize = _context.Temporary(_factory.BooleanType);
        _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, initialize);
        var effects = _builder.CreateBlock("convert:framework-initialization");
        var initialized = _builder.CreateBlock("convert:initialized");
        _builder.Branch(block, site, _factory.Variable(initialize), effects, initialized);
        // Beforefieldinit does not establish a warmed Convert/DBNull singleton.
        // Its initialization can allocate, store static state, synchronize or
        // fail, but cannot reach caller objects through these scalar arguments.
        var member = OpaqueCallMember(invocation.TargetMethod, [argument], _factory.BooleanType, out var display);
        _builder.Call(effects, _context.OpaqueCallSite(invocation, IrOpaqueCallEffects.All & ~IrOpaqueCallEffects.Writes,
            display + ":primitive-runtime-initialization"), null, member, null, [argument]);
        _builder.Write(effects, site, IrWriteRegion.Static);
        _builder.Allocate(effects, site, _factory.ObjectType);
        _builder.Goto(effects, site, initialized);
        var fails = _context.Temporary(_factory.BooleanType);
        _builder.Havoc(initialized, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, fails);
        return ApplyRule(invocation, new TotalScalarRule(_factory.Boolean(true),
            [new(IrExceptionKind.Unknown, _factory.Variable(fails))], FrontendSubsetClassification.Exact), initialized).Continuation;
    }

    private TotalBodyValue? IntegralConvertBody(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton || !CSharpOperationSemantics.IsIntegralConvertToInt64(invocation.TargetMethod))
        { return null; }
        var argument = LowerBodyValue(invocation.Arguments.Single().Value, block, depth + 1);
        if (!argument.Classification.IsExact)
        { return Approximate(invocation, argument.Continuation, argument.Classification.Abstention); }
        block = IntegralConvertInitialization(invocation, argument.Value, argument.Continuation);
        return ApplyRule(invocation, CSharpOperationSemantics.IntegralConvertToInt64(_factory, argument.Value), block);
    }
}
