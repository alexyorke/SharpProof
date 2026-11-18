namespace SharpProof.Frontend;

// An interpolated string converted to string. When every hole is a string
// with no alignment or format, the compiler emits String.Concat over the
// parts, exactly as for a `+` chain, so the value is the concatenation. Other
// admitted holes (char, bool, built-in integers, references) are formatted by
// DefaultInterpolatedStringHandler, or String.Format on older frameworks, and
// the result is a fresh string whose content is not modeled:
//   - char and bool format without culture or user code ("True"/"False");
//   - an integer formats through the ambient culture: a call that reads
//     ambient state and, by the culture assumption, runs no user code, cannot
//     throw and yields at most MaximumFormattedIntegerLength characters;
//   - a reference calls the value's ToString (or IFormattable.ToString), which
//     is user code: an unknown call.
// The handler's ArrayPool<char>.Shared buffer is not modeled state: every
// renter must already treat a rented array's contents and identity as
// unspecified.
internal sealed partial class RoslynTotalExpressionLowerer
{
    private const int MaximumFormattedIntegerLength = 64;

    private TotalBodyValue LowerInterpolatedString(IInterpolatedStringOperation operation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (operation.ConstantValue is { HasValue: true, Value: string text })
        {
            return operation.Type?.SpecialType == SpecialType.System_String && Utf16WellFormedness.IsWellFormed(text)
                ? Capture(operation, new(_factory.String(text), block, FrontendSubsetClassification.Exact))
                : Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind);
        }
        var parts = CSharpOperationSemantics.InterpolationParts(operation);
        // The compiler may splice an interpolation into an enclosing `+` chain.
        if (parts.IsDefault || CSharpOperationSemantics.IsConcatenationOperand(operation.Syntax))
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        if (parts.All(static part => part is InterpolationPart.Text or InterpolationPart.String))
        {
            var leaves = ImmutableArray.CreateBuilder<IOperation>(operation.Parts.Length);
            foreach (var part in operation.Parts)
            {
                leaves.Add(part is IInterpolationOperation hole ? hole.Expression : ((IInterpolatedStringTextOperation)part).Text);
            }
            return ConcatenateLeaves(operation, leaves.MoveToImmutable(), block, depth, deferred: false);
        }
        return LowerFormattedInterpolation(operation, parts, block, depth);
    }

    private TotalBodyValue LowerFormattedInterpolation(IInterpolatedStringOperation operation, ImmutableArray<InterpolationPart> parts,
        IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        RepositoryParameterGuard.RequireInitialized(parts.IsDefault, nameof(parts), "Array must be initialized.");
        var calls = parts.Any(static part => part is InterpolationPart.Integer or InterpolationPart.Reference);
        if (calls && !AllowOpaqueCalls && !ShadowCallSkeleton)
        { return Approximate(operation, block, FrontendAbstention.UnsupportedInvocationShape); }
        var compilation = _context.Compilation ?? operation.SemanticModel?.Compilation;
        var objectFormat = compilation == null ? null : ParameterlessToString(compilation.GetSpecialType(SpecialType.System_Object));
        var lengths = ImmutableArray.CreateBuilder<IrTerm>();
        // Characters the text and the bounded holes contribute at most.
        // A hole with control flow makes the control-flow graph evaluate the
        // holes into captures before this operation, while the handler formats
        // each hole before it evaluates the next. A formatting effect would
        // then be placed after a later hole that may already have thrown.
        var lastHoisted = -1;
        for (var index = 0; index < parts.Length; index++)
        {
            if (operation.Parts[index] is IInterpolationOperation { Expression: IFlowCaptureReferenceOperation })
            { lastHoisted = index; }
        }
        for (var index = 0; index < lastHoisted; index++)
        {
            if (parts[index] is InterpolationPart.Integer or InterpolationPart.Reference)
            { return Approximate(operation, block, FrontendAbstention.UnsupportedControlFlow); }
        }
        long reserved = 0;
        var nonempty = false;
        var unbounded = false;
        for (var index = 0; index < parts.Length; index++)
        {
            Spend?.Invoke();
            var kind = parts[index];
            if (kind == InterpolationPart.Text)
            {
                var text = (string)((IInterpolatedStringTextOperation)operation.Parts[index]).Text.ConstantValue.Value!;
                reserved += text.Length;
                nonempty |= text.Length != 0;
                continue;
            }
            var hole = (IInterpolationOperation)operation.Parts[index];
            IMethodSymbol? format = null;
            if (kind == InterpolationPart.Integer)
            { format = ParameterlessToString(hole.Expression.Type!); }
            else if (kind == InterpolationPart.Reference)
            { format = objectFormat; }
            if (format == null && kind is InterpolationPart.Integer or InterpolationPart.Reference)
            { return Approximate(operation, block, FrontendAbstention.UnsupportedInvocationShape); }
            var value = LowerBodyValue(hole.Expression, block, depth + 1);
            if (!value.Classification.IsExact)
            { return Approximate(operation, value.Continuation, value.Classification.Abstention); }
            block = value.Continuation;
            switch (kind)
            {
                case InterpolationPart.String:
                    lengths.Add(value.Value);
                    break;
                case InterpolationPart.Character:
                    reserved += 1;
                    nonempty = true;
                    break;
                case InterpolationPart.Boolean:
                    reserved += 5;
                    nonempty = true;
                    break;
                case InterpolationPart.Integer:
                    reserved += MaximumFormattedIntegerLength;
                    nonempty = true;
                    block = FormatHole(hole, format!, value.Value, block,
                        IrOpaqueCallEffects.Reads | IrOpaqueCallEffects.ReadsAmbient | IrOpaqueCallEffects.Allocates);
                    break;
                default:
                    unbounded = true;
                    block = FormatHole(hole, format!, value.Value, block, IrOpaqueCallEffects.All);
                    break;
            }
        }
        // A result longer than the runtime's maximum string length throws
        // OutOfMemoryException, as String.Concat does.
        if (unbounded || reserved > MaximumStringLength)
        { block = MayFault(operation, _factory.Boolean(true), "interpolation:beyond-maximum-length", block); }
        else if (lengths.Count != 0)
        {
            block = ExceedsMaximumStringLength(operation, lengths.ToImmutable(), block, (int)reserved,
                "interpolation:beyond-maximum-length");
        }
        var site = _context.Site(operation);
        // A result that always has a character is a new string. Otherwise a
        // reference hole's unknown call already may allocate.
        if (nonempty)
        { _builder!.Allocate(block, site, _factory.StringType); }
        var formatted = _context.Temporary(_factory.StringType);
        _builder!.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, formatted);
        var result = _factory.Variable(formatted);
        return Capture(operation, new(_factory.Conditional(
            _factory.Binary(IrBinaryOperator.Equal, result, _factory.Null(_factory.StringType)), _factory.String(""), result),
            block, FrontendSubsetClassification.Exact));
    }

    // The formatting call for one hole: an effect site only, since the
    // formatted text is not modeled.
    private IrBlockId FormatHole(IInterpolationOperation hole, IMethodSymbol format, IrTerm value, IrBlockId block,
        IrOpaqueCallEffects effects)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (ShadowCallSkeleton)
        { return block; }
        return EmitOpaqueCall(hole, format, [value], block, null, effects).Continuation;
    }

    private static IMethodSymbol? ParameterlessToString(ITypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.GetMembers("ToString").OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method.Parameters.IsEmpty && !method.IsStatic && !method.IsGenericMethod);
    }
}
