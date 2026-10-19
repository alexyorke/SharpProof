using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

internal static class ExceptionConstructorNormalReplay
{
    // These CLR/IR elision probes deliberately choose a normal constructor
    // path. Resource callbacks remain possible in the universal model, and
    // the consumed approximation cannot authorize a native refutation.
    internal static IrProgramReplayOptions Options(IrFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return request.Origin == IrHavocOrigin.Approximation && factory.GetVariableInfo(request.Variable).Type == factory.BooleanType
                ? factory.CreateBooleanValue(false) : null;
        });
    }

    internal static void AssertApproximation(IrProgramExecutionResult replay, bool expected)
    {
        ArgumentNullException.ThrowIfNull(replay);
        Assert.That(replay.ConsumedApproximation, Is.EqualTo(expected));
        Assert.That(replay.ApproximationVariables, expected ? Is.Not.Empty : Is.Empty);
    }
}
