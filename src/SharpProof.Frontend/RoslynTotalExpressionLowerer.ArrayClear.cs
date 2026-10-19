namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalExpressionLowerer
{
    private TotalBodyValue? ArrayClearBody(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton || !CSharpOperationSemantics.IsArrayClear(invocation.TargetMethod))
        { return null; }
        // Array's general shape does not imply zero-based, single-dimensional
        // storage. Peel only the actual compiler reference conversion from T[].
        if (CSharpOperationSemantics.TypedArrayClearReceiver(invocation) is not { Type: IArrayTypeSymbol arrayType } arrayOperation)
        { return null; }
        var valueBuilder = ImmutableArray.CreateBuilder<IrTerm>(invocation.Arguments.Length);
        valueBuilder.Count = invocation.Arguments.Length;
        foreach (var argument in invocation.Arguments.OrderBy(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Syntax.SpanStart))
        {
            var value = Pin(invocation, LowerBodyValue(argument.Parameter!.Ordinal == 0 ? arrayOperation : argument.Value, block, depth + 1));
            if (!value.Classification.IsExact)
            { return Approximate(invocation, value.Continuation, value.Classification.Abstention); }
            valueBuilder[argument.Parameter.Ordinal] = value.Value;
            block = value.Continuation;
        }
        var values = valueBuilder.MoveToImmutable();
        var array = values[0];
        var index = values.Length == 1 ? _factory.Integer(0) : values[1];
        var length = values.Length == 1 ? _factory.Length(array) : values[2];
        var invalid = _factory.Binary(IrBinaryOperator.Equal, array, _factory.Null(array.Type));
        if (values.Length != 1)
        {
            invalid = Or(invalid, Or(_factory.Binary(IrBinaryOperator.LessThan, index, _factory.Integer(0)),
                Or(_factory.Binary(IrBinaryOperator.LessThan, length, _factory.Integer(0)),
                    Or(_factory.Binary(IrBinaryOperator.GreaterThan, index, _factory.Length(array)),
                        _factory.Binary(IrBinaryOperator.GreaterThan, length, _factory.Binary(IrBinaryOperator.Subtract, _factory.Length(array), index))))));
        }
        var failed = _builder!.CreateBlock("array-clear:invalid-range");
        var valid = _builder.CreateBlock("array-clear:valid-range");
        var site = _context.Site(invocation);
        _builder.Branch(block, site, invalid, failed, valid);
        // Failure diagnostics may allocate, access resources and invoke culture
        // callbacks. Keep their exception identity and callback behavior opaque;
        // no invalid range reaches the normal-return element-store model.
        failed = EmitOpaqueCall(invocation, invocation.TargetMethod, values, failed, null, IrOpaqueCallEffects.All).Continuation;
        var unreachable = ApplyRule(invocation, new TotalScalarRule(_factory.Boolean(false),
            [new(IrExceptionKind.Unknown, _factory.Boolean(true))], FrontendSubsetClassification.Exact), failed).Continuation;
        _builder.Goto(unreachable, site, valid);
        // Null is valid for every reference array, including covariant views.
        // Opaque structs keep an effect-only overapproximation: their default
        // atom is not a CLR null reference and is not interpreted here.
        var nonempty = _builder.CreateBlock("array-clear:nonempty");
        var done = _builder.CreateBlock("array-clear:done");
        _builder.Branch(valid, site, _factory.Binary(IrBinaryOperator.Equal, length, _factory.Integer(0)), done, nonempty);
        var writeSite = CSharpOperationSemantics.IsFreshReceiver(arrayOperation.Syntax, _context.Compilation)
            ? _context.FreshWriteSite(invocation) : site;
        var elementType = _context.KnownValueType(arrayType.ElementType) ?? arrayType.ElementType;
        if (CSharpOperationSemantics.IsScalar(elementType) || CSharpOperationSemantics.IsReferenceDomain(elementType))
        {
            var zero = CSharpOperationSemantics.DefaultValue(_factory, _context.Type(elementType));
            _builder.ElementStore(nonempty, writeSite, array, index, zero, length);
        }
        else
        { _builder.Write(nonempty, writeSite, IrWriteRegion.Element); }
        _builder.Goto(nonempty, site, done);
        return new(_factory.Boolean(false), done, FrontendSubsetClassification.Exact);
    }
}
