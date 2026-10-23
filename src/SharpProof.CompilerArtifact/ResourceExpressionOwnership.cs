using System.Collections.Immutable;
using System.Numerics;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// Input binding is necessary evidence validation, not proof of a cost claim.
// The caller must obtain the preparation from the owning manifest envelope.
internal static class ResourceExpressionOwnership
{
    internal static void Validate(ResourceExpression expression, string callableId,
        CompilerTotalCallablePreparation preparation, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(callableId) || !StringComparer.Ordinal.Equals(callableId, preparation.CallableId) ||
            preparation.IsBodyAbstraction || preparation.Program is null || preparation.Parameters.IsDefault)
        { throw new InvalidDataException("Resource expression callable ownership is invalid."); }
        ValidateBindings(expression, preparation.Program.Factory, preparation.Parameters, preparation.HasReceiver, cancellation);
    }

    internal static void Validate(ResourceExpression expression, string callableId,
        CompilerTotalEntryPreparation preparation, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(callableId) || !StringComparer.Ordinal.Equals(callableId, preparation.CallableId) ||
            preparation.Factory is null || preparation.Factory.Semantics != IrExecutionSemantics.Total || preparation.Parameters.IsDefault)
        { throw new InvalidDataException("Resource expression entry ownership is invalid."); }
        ValidateBindings(expression, preparation.Factory, preparation.Parameters, preparation.HasReceiver, cancellation);
    }

    private static void ValidateBindings(ResourceExpression expression, IrFactory factory,
        ImmutableArray<CompilerTotalParameter> parameters, bool hasReceiver, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(parameters.IsDefault, nameof(parameters), "Parameters must be initialized.");
        var count = parameters.Length - (hasReceiver ? 1 : 0);
        if (count < 0)
        { throw new InvalidDataException("Resource expression receiver ownership is invalid."); }
        var entries = new HashSet<IrVarId>();
        try
        {
            foreach (var parameter in parameters)
            {
                cancellation.ThrowIfCancellationRequested();
                var type = factory.GetVariableInfo(parameter.Entry).Type;
                if (!entries.Add(parameter.Entry) || factory.GetVariableInfo(parameter.Current).Type != type ||
                    factory.GetVariableInfo(parameter.Old).Type != type)
                { throw new InvalidDataException("Resource expression parameter map is invalid."); }
            }
            Visit(expression, factory, parameters, count, cancellation);
        }
        catch (ArgumentException exception)
        { throw new InvalidDataException("Resource expression uses an unowned parameter.", exception); }
    }

    private static void Visit(ResourceExpression expression, IrFactory factory,
        ImmutableArray<CompilerTotalParameter> parameters, int count, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(parameters.IsDefault, nameof(parameters), "Parameters must be initialized.");
        cancellation.ThrowIfCancellationRequested();
        if (expression.Input is { } input)
        {
            if (input.Ordinal >= count)
            { throw new InvalidDataException("Resource expression input ordinal is not owned by the callable."); }
            var type = factory.GetTypeInfo(factory.GetVariableInfo(parameters[input.Ordinal].Entry).Type);
            BigInteger maximum;
            switch (input.Kind)
            {
                case ResourceInputKind.NonNegativeScalar when type.Kind == IrTypeKind.Integer && type.Signed && type.Width is 32 or 64:
                    maximum = (BigInteger.One << (type.Width - 1)) - 1;
                    break;
                case ResourceInputKind.ArrayLength when type.Kind == IrTypeKind.Sequence:
                case ResourceInputKind.StringLength when type.Kind == IrTypeKind.String:
                    maximum = int.MaxValue;
                    break;
                default:
                    throw new InvalidDataException("Resource expression input kind does not match the owned parameter type.");
            }
            if (input.Maximum != maximum)
            { throw new InvalidDataException("Resource expression input domain does not match the owned parameter type."); }
        }
        foreach (var operand in expression.Operands)
        { Visit(operand, factory, parameters, count, cancellation); }
    }
}
