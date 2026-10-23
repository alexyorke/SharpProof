using System.Numerics;

namespace SharpProof.Worker;

// Conditional callback-source upper costs only. The owning compiler envelope
// and an at-most-once runtime model are separate premises for a root claim.
internal sealed record NativeForkJoinSourceCostCheck(NativeResourceCost? Upper, string? UnknownReason)
{
    internal bool HasSourceCosts => Upper is not null;
}

// Child storage only. Compiler/runtime carriers are not included, so this
// conditional envelope is never itself a root allocation/lifetime verdict.
internal sealed record NativeForkJoinChildStorageCheck(ResourceExpression? CumulativeUpper,
    ResourceExpression? PeakAuxiliaryUpper, ResourceExpression? RetainedUpper, string? UnknownReason);

// Logical source activations per callback, not physical CLR frames or a trace
// allocation bound. Runtime/helper frames require a separate checked bridge.
internal sealed record NativeForkJoinSourceActivationCheck(ResourceExpression? Upper, string? UnknownReason)
{
    internal string? RequiredSourceModel => Upper is null ? null : NativeResourceStackVerifier.ModelId;
}

// Logical source depth plus the checked emitted forwarding activation. Runtime
// helpers and trace enumeration remain outside this conditional component.
internal sealed record NativeForkJoinCallbackSourceActivationCheck(ResourceExpression? Upper, string? UnknownReason)
{
    internal string? RequiredSourceModel => Upper is null ? null : NativeForkJoinSourceCostVerifier.CallbackSourceActivationModel;
}

internal static class NativeForkJoinSourceCostVerifier
{
    internal const string ChildStorageModel = "fork-join-isolated-child-storage-v1";
    internal const string CallbackSourceActivationModel = "fork-join-source-and-callback-activations-v1";

    internal static NativeForkJoinCallbackSourceActivationCheck CheckCallbackSourceActivations(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        var source = CheckSourceActivations(projection, root, cancellation);
        if (source.Upper is null)
        { return new(null, source.UnknownReason); }
        if (projection.Carriers?.Shapes is not { } shapes)
        { return new(null, "TaskCompilerCarrierEvidenceUnavailable"); }
        var callbacks = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(shapes, projection, root, layout, cancellation);
        if (callbacks.WrapperActivationsUpper is not { } wrappers)
        { return new(null, callbacks.UnknownReason); }
        try
        { return new(ResourceExpression.Add(source.Upper, ResourceExpression.Literal(wrappers)), null); }
        catch (ResourceExpressionLimitException)
        { return new(null, "ForkJoinCallbackActivationBudgetExceeded"); }
    }

    internal static NativeForkJoinSourceActivationCheck CheckSourceActivations(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        cancellation.ThrowIfCancellationRequested();
        var source = Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return new(null, source.UnknownReason); }
        var maximum = ResourceExpression.Zero;
        var remaining = ResourceExpression.MaximumNodes;
        try
        {
            foreach (var child in projection.Children)
            {
                cancellation.ThrowIfCancellationRequested();
                // Source ownership and its complete resource program were checked
                // above. Derive the rank/frame bound, then substitute the checked
                // formal-to-root mapping rather than accepting a supplied cap.
                var stack = NativeResourceStackVerifier.Derive(child.Cost, cancellation);
                if (stack.Bounds is not { } bounds)
                { return new(null, stack.UnknownReason ?? "ForkJoinSourceActivationUnproved"); }
                var arguments = new ResourceExpression[child.Arguments.Length];
                foreach (var argument in child.Arguments)
                {
                    cancellation.ThrowIfCancellationRequested();
                    arguments[argument.ParameterOrdinal] = argument.RootParameterOrdinal is { } ordinal
                        ? ResourceExpression.InputSize(new ResourceInput(ordinal, ResourceInputKind.NonNegativeScalar,
                            projection.RootParameterNames[ordinal], int.MaxValue))
                        : ResourceExpression.Literal(BigInteger.Max(0, argument.Constant!.Value));
                }
                maximum = ResourceExpression.Max(maximum, Substitute(bounds.Depth, arguments, ref remaining, cancellation));
            }
            return new(maximum, null);
        }
        catch (ResourceExpressionLimitException)
        { return new(null, "ForkJoinSourceActivationBudgetExceeded"); }
    }

    internal static NativeForkJoinChildStorageCheck CheckChildStorage(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var source = Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return new(null, null, null, source.UnknownReason); }
        try
        {
            var cumulative = ResourceExpression.Zero;
            var remaining = ResourceExpression.MaximumNodes;
            foreach (var child in projection.Children)
            {
                cancellation.ThrowIfCancellationRequested();
                var allocation = NativeResourceAllocationVerifier.Verify(child.Cost, child.Owner.CallableId, child.Owner, layout, cancellation);
                if (allocation.ConditionalUpperBound is not { } upper)
                { return new(null, null, null, allocation.UnknownReason ?? "ForkJoinChildStorageUnproved"); }
                // Check already established a complete, distinct formal map and
                // scalar entry ownership for every child, including later ones.
                var arguments = new ResourceExpression[child.Arguments.Length];
                foreach (var argument in child.Arguments)
                {
                    cancellation.ThrowIfCancellationRequested();
                    arguments[argument.ParameterOrdinal] = argument.RootParameterOrdinal is { } ordinal
                        ? ResourceExpression.InputSize(new ResourceInput(ordinal, ResourceInputKind.NonNegativeScalar,
                            projection.RootParameterNames[ordinal], int.MaxValue))
                        : ResourceExpression.Literal(BigInteger.Max(0, argument.Constant!.Value));
                }
                cumulative = ResourceExpression.Add(cumulative, Substitute(upper, arguments, ref remaining, cancellation));
            }
            // Primitive local graphs cannot escape the scalar-only, void child
            // boundary. Cumulative storage bounds their union at any overlap.
            return new(cumulative, cumulative, ResourceExpression.Zero, null);
        }
        catch (ResourceExpressionLimitException)
        { return new(null, null, null, "ForkJoinChildStorageBudgetExceeded"); }
        catch (InvalidDataException)
        { return new(null, null, null, "ForkJoinChildStorageOwnershipInvalid"); }
    }

    internal static NativeForkJoinSourceCostCheck Check(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        cancellation.ThrowIfCancellationRequested();
        try
        {
            if (string.IsNullOrWhiteSpace(root.CallableId) || projection.RootCallableId != root.CallableId || root.HasReceiver ||
                root.Factory is null || root.Factory.Semantics != IrExecutionSemantics.Total || root.Parameters.IsDefault || root.Clauses.IsDefault ||
                projection.RootParameterNames.IsDefault || projection.RootParameterNames.Length != root.Parameters.Length ||
                root.Parameters.Length > ResourceExpression.MaximumNodes || projection.Children.IsDefault || projection.Children.Length is < 2 or > 16)
            { return Unknown("ForkJoinRootOwnershipInvalid"); }
            CheckInputs(root.Factory, root.Parameters);
            var total = new NativeResourceCost(ResourceExpression.Zero, ResourceExpression.Zero, ResourceExpression.Zero);
            var remaining = ResourceExpression.MaximumNodes;
            var instructions = ResourceExpression.MaximumNodes;
            for (var index = 0; index < projection.Children.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var child = projection.Children[index];
                if (child is null || child.Owner is null || child.Owner.Program is null || child.Owner.CallableId != root.CallableId + ":fork-child:" + index.ToString(CultureInfo.InvariantCulture) ||
                    child.Owner.HasReceiver || child.Owner.Result != null || child.Owner.IsBodyAbstraction || !child.Owner.EffectsCompleteAtEntry ||
                    child.Owner.Clauses.IsDefault || !child.Owner.Clauses.IsEmpty || child.Owner.CallPreconditions.IsDefault || !child.Owner.CallPreconditions.IsEmpty ||
                    child.Owner.ValidEffectClaimIds.IsDefault || !child.Owner.ValidEffectClaimIds.IsEmpty)
                { return Unknown("ForkJoinChildOwnershipInvalid"); }
                CheckInputs(child.Owner.Program.Factory, child.Owner.Parameters);
                foreach (var block in child.Owner.Program.Blocks)
                {
                    cancellation.ThrowIfCancellationRequested();
                    instructions -= block.Instructions.Length;
                    if (instructions < 0)
                    { return Unknown("ForkJoinSourceCostBudgetExceeded"); }
                }
                if (!NativeResourceMemoryEnvelope.Admits(child.Owner, cancellation))
                { return Unknown("ForkJoinChildIsolationUnproved"); }
                if (child.Arguments.IsDefault || child.Arguments.Length != child.Owner.Parameters.Length)
                { return Unknown("ForkJoinArgumentMapInvalid"); }
                var arguments = new ResourceExpression[child.Arguments.Length];
                foreach (var argument in child.Arguments)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (argument is null || argument.ParameterOrdinal < 0 || argument.ParameterOrdinal >= arguments.Length ||
                        arguments[argument.ParameterOrdinal] != null || argument.RootParameterOrdinal.HasValue == argument.Constant.HasValue)
                    { return Unknown("ForkJoinArgumentMapInvalid"); }
                    if (argument.RootParameterOrdinal is { } ordinal)
                    {
                        if (ordinal < 0 || ordinal >= root.Parameters.Length)
                        { return Unknown("ForkJoinArgumentMapInvalid"); }
                        arguments[argument.ParameterOrdinal] = ResourceExpression.InputSize(new ResourceInput(ordinal,
                            ResourceInputKind.NonNegativeScalar, projection.RootParameterNames[ordinal], int.MaxValue));
                    }
                    else
                    { arguments[argument.ParameterOrdinal] = ResourceExpression.Literal(BigInteger.Max(0, argument.Constant!.Value)); }
                }
                var checkedChild = NativeResourceProgramVerifier.Verify(child.Cost, child.Owner.CallableId, child.Owner, cancellation);
                if (!checkedChild.Verified)
                { return Unknown("ForkJoinChildCostUnproved:" + checkedChild.UnknownReason); }
                var cost = checkedChild.Cost!;
                total = new(
                    ResourceExpression.Add(total.LoopEntries, Substitute(cost.LoopEntries, arguments, ref remaining, cancellation)),
                    ResourceExpression.Add(total.Arrays, Substitute(cost.Arrays, arguments, ref remaining, cancellation)),
                    ResourceExpression.Add(total.Elements, Substitute(cost.Elements, arguments, ref remaining, cancellation)));
            }
            return new(total, null);
        }
        catch (ResourceExpressionLimitException)
        { return Unknown("ForkJoinSourceCostBudgetExceeded"); }
        catch (ArgumentException)
        { return Unknown("ForkJoinInputOwnershipInvalid"); }
        catch (InvalidDataException)
        { return Unknown("ForkJoinInputOwnershipInvalid"); }
    }

    private static NativeForkJoinSourceCostCheck Unknown(string reason)
    { reason = ArgumentNullGuard.NotNull(reason, nameof(reason)); return new(null, reason); }

    private static void CheckInputs(IrFactory factory, ImmutableArray<CompilerTotalParameter> parameters)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(parameters.IsDefault, nameof(parameters), "Parameters must be initialized.");
        var entries = new HashSet<IrVarId>();
        foreach (var parameter in parameters)
        {
            var type = factory.GetVariableInfo(parameter.Entry).Type;
            if (!entries.Add(parameter.Entry) || !entries.Add(parameter.Current) || !entries.Add(parameter.Old) ||
                factory.GetVariableInfo(parameter.Current).Type != type ||
                factory.GetVariableInfo(parameter.Old).Type != type || factory.GetTypeInfo(type) is not { Kind: IrTypeKind.Integer, Signed: true, Width: 32 })
            { throw new InvalidDataException("Fork-join inputs must be distinct Int32 bindings."); }
        }
    }

    private static ResourceExpression Substitute(ResourceExpression expression, ResourceExpression[] arguments,
        ref int remaining, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        arguments = ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0)
        { throw new ResourceExpressionLimitException(); }
        if (expression.Input is { } input)
        {
            if (input.Kind != ResourceInputKind.NonNegativeScalar || input.Maximum != int.MaxValue || input.Ordinal >= arguments.Length)
            { throw new InvalidDataException("Fork-join child cost uses an unowned input."); }
            return arguments[input.Ordinal];
        }
        if (expression.Constant is { } constant)
        { return ResourceExpression.Literal(constant); }
        var left = Substitute(expression.Operands[0], arguments, ref remaining, cancellation);
        return expression.Kind switch
        {
            ResourceExpressionKind.Add => ResourceExpression.Add(left, Substitute(expression.Operands[1], arguments, ref remaining, cancellation)),
            ResourceExpressionKind.Multiply => ResourceExpression.Multiply(left, Substitute(expression.Operands[1], arguments, ref remaining, cancellation)),
            ResourceExpressionKind.Maximum => ResourceExpression.Max(left, Substitute(expression.Operands[1], arguments, ref remaining, cancellation)),
            ResourceExpressionKind.PositiveDifference => ResourceExpression.Difference(left, expression.Number),
            ResourceExpressionKind.CeilingDivide => ResourceExpression.DivideCeiling(left, expression.Number),
            ResourceExpressionKind.Digits => ResourceExpression.Geometric(left, expression.Number, true, cancellation),
            ResourceExpressionKind.CeilingLog => ResourceExpression.Geometric(left, expression.Number, false, cancellation),
            ResourceExpressionKind.Align => ResourceExpression.Aligned(left, expression.Number),
            _ => throw new InvalidDataException("Unsupported fork-join cost expression.")
        };
    }
}
