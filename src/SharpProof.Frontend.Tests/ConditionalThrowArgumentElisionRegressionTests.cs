using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalThrowArgumentElisionRegressionTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ConditionalSymbolAndThrowOrientationPreserveRuntimeEvaluation(bool defined, bool throwWhenNull)
    {
        var argument = throwWhenNull
            ? "value == null ? throw new System.NullReferenceException() : value.Length > 0"
            : "value == null ? value.Length > 0 : throw new System.NullReferenceException()";
        using var subject = TypedProgramSubject.Create("int Target(string value) { Log(" + argument +
            "); return 7; } [System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool value) { }",
            preprocessorSymbols: defined ? ["ABSENT"] : []);
        var lowered = defined ? subject.LowerSourceCalls() : subject.Lower();
        foreach (var input in new string?[] { null, "word", "" })
        {
            var throws = defined && (!throwWhenNull || input == null);
            var kind = !throwWhenNull && input == null ? IrExceptionKind.NullReference : IrExceptionKind.Explicit;
            AssertExecution(subject, lowered, input, 7, throws ? typeof(NullReferenceException) : null, kind);
        }
    }

    [Test]
    public void ElidedShortCircuitArgumentPreservesFollowingRuntimeReturn()
    {
        using var subject = TypedProgramSubject.Create("int Target(string value) { Log(value != null && value.Length > 0);" +
            " return 7; } [System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool value) { }");
        var lowered = subject.Lower();
        foreach (var input in new string?[] { null, "word", "" })
        { AssertExecution(subject, lowered, input, 7, null); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedThrowArgumentsPreserveFinallyAndEnabledExceptions(bool defined)
    {
        using var subject = TypedProgramSubject.Create("int Target(string value) { int result = 7; try {" +
            " Log(value == null ? throw new System.NullReferenceException() : value.Length > 0 ? true : throw new System.InvalidOperationException());" +
            " } finally { result += 1; } return result; }" +
            " [System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool value) { }",
            preprocessorSymbols: defined ? ["ABSENT"] : []);
        var lowered = defined ? subject.LowerSourceCalls() : subject.Lower();
        foreach (var input in new string?[] { null, "word", "" })
        {
            var exception = !defined || input == "word" ? null :
                input == null ? typeof(NullReferenceException) : typeof(InvalidOperationException);
            AssertExecution(subject, lowered, input, 8, exception);
        }
    }

    private static void AssertExecution(TypedProgramSubject subject, FrontendProgramLoweringResult lowered,
        string? input, int returned, Type? exception, IrExceptionKind exceptionKind = IrExceptionKind.Explicit)
    {
        System.ArgumentNullException.ThrowIfNull(lowered);
        System.ArgumentNullException.ThrowIfNull(subject);
        var native = subject.Invoke([input!]);
        if (exception != null)
        { Assert.That(native, Is.TypeOf(exception)); }
        else
        { Assert.That(native, Is.EqualTo(returned)); }
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter, value));
        Assert.That(replay.Status, Is.EqualTo(exception != null
            ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        if (exception != null)
        { Assert.That(replay.Exception!.Kind, Is.EqualTo(exceptionKind)); }
        else
        { Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(returned))); }
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
