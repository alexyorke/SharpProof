using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FrontendSemanticEdgeCaseTests
{
    private static readonly long[] SequenceValue = [1L, 2L];
    private static readonly ulong[] UnsupportedSequenceValue = [1UL];
    private static readonly Array MultidimensionalSequenceValue =
        Array.CreateInstance(typeof(long), 2, 3);

    [Test]
    public void FixedSemanticEdgesMatchRuntimeOrAbstainExactly()
    {
        var cases = new[] {
            Exact("sbyte", "", "-3"),
            Exact("byte", "", "3"),
            Exact("short", "", "-3"),
            Exact("ushort", "", "3"),
            Exact("int", "", "-3"),
            Exact("uint", "", "3"),
            Exact("char", "", "'A'"),
            Exact("long", "short value", "(long)value", short.MinValue),
            Exact("long", "int value", "(long)value", int.MaxValue),
            Exact("long", "uint value", "(long)value", uint.MaxValue),
            Exact("long", "", "checked((int)3L)"),
            Exact("object?", "object? value", "value", new object()),
            Exact("long[]", "long[] value", "value", SequenceValue),
            Closed(
                "ulong",
                "ulong[] value",
                "value[0]",
                FrontendAbstention.UnsupportedType,
                UnsupportedSequenceValue),
            Exact("string?", "object? value", "(string)value", (object?)null),
            Exact("string?", "object? value", "(string)value", "proof"),
            Exact("string?", "object? value", "(string)value", new object()),
            Exact(
                "long",
                "long[,] value",
                "value.LongLength",
                MultidimensionalSequenceValue),
            Closed(
                "long",
                "long value",
                "checked((int)value)",
                FrontendAbstention.ConversionMayChangeValue,
                long.MaxValue),
            Closed(
                "long",
                "long value",
                "unchecked((int)value)",
                FrontendAbstention.ConversionMayChangeValue,
                long.MaxValue),
            Closed(
                "string?",
                "object? value",
                "value as string",
                FrontendAbstention.ConversionMayChangeValue,
                "proof"),
            Closed(
                "long",
                "long value",
                "(long)new SharpProofGeneratedConvertible(value)",
                FrontendAbstention.UserDefinedOperator,
                1L),
            Closed(
                "SharpProofGeneratedEdgeEnum",
                "",
                "SharpProofGeneratedEdgeEnum.One",
                FrontendAbstention.UnsupportedType),
            Closed(
                "long?",
                "",
                "(long?)1L",
                FrontendAbstention.UnsupportedType),
            Closed(
                "long?",
                "long value",
                "(long?)value",
                FrontendAbstention.ConversionMayChangeValue,
                1L),
            Closed(
                "long?",
                "long? value",
                "value + 1L",
                FrontendAbstention.LiftedOperator,
                (long?)1L),
            Closed(
                "bool",
                "long? value",
                "value == 0L",
                FrontendAbstention.LiftedOperator,
                (long?)0L),
            Closed(
                "long?",
                "long? value",
                "-value",
                FrontendAbstention.LiftedOperator,
                (long?)1L)
        };

        var results = new FrontendDifferentialOracle()
            .CompareSemanticEdges(cases);

        Assert.That(results, Has.Length.EqualTo(cases.Length));
        Assert.That(
            results.Select(static result => result.Status),
            Is.All.EqualTo(FuzzOracleStatus.Agreement),
            string.Join(
                Environment.NewLine,
                results.Select((result, index) =>
                    index + ": " + result.Detail)));
        Assert.That(
            results[16].ExceptionKind,
            Is.EqualTo(IrExceptionKind.InvalidCast));
        for (var index = 0; index < cases.Length; index++)
        {
            Assert.That(
                results[index].ActualDecision,
                Is.EqualTo(cases[index].ExpectedDecision));
            Assert.That(
                results[index].ActualAbstention,
                Is.EqualTo(cases[index].ExpectedAbstention));
        }
    }

    [Test]
    public void CompileInvalidSemanticEdgeDoesNotPoisonValidPeer()
    {
        var results = new FrontendDifferentialOracle().CompareSemanticEdges(
        [
            Exact("long", "", "0L"),
            Exact("long", "", "long.MaxValue + 1L")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    [Test]
    public void CompileSuccessfulSemanticEdgeInjectionDoesNotPoisonValidPeer()
    {
        var results = new FrontendDifferentialOracle().CompareSemanticEdges(
        [
            Exact("long", "", "0L"),
            Exact(
                "long",
                "",
                "0L; public static long EdgeTarget999() => 0L")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    [Test]
    public void NonnumericSemanticEdgeInjectionDoesNotEscapeBatchIsolation()
    {
        var results = new FrontendDifferentialOracle().CompareSemanticEdges(
        [
            Exact("long", "", "0L"),
            Exact(
                "long",
                "",
                "0L; public static long EdgeTargetOops() => 0L")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    [Test]
    public void StaticInitializerInjectionDoesNotPoisonValidPeer()
    {
        var results = new FrontendDifferentialOracle().CompareSemanticEdges(
        [
            Exact("long", "", "0L"),
            Exact(
                "long",
                "",
                "0L; static readonly long Poison = Throw(); " +
                "static long Throw() => throw new System.Exception()")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    [Test]
    public void TopLevelInitializerInjectionDoesNotPoisonValidPeer()
    {
        var results = new FrontendDifferentialOracle().CompareSemanticEdges(
        [
            Exact("long", "", "0L"),
            Exact(
                "long",
                "",
                "0L; } public static class Injected { " +
                "[System.Runtime.CompilerServices.ModuleInitializer] " +
                "public static void Initialize() => " +
                "throw new System.Exception(); } public static class Tail { " +
                "public static long Value => 0L")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    private static FrontendSemanticEdgeCase Exact(
        string returnType,
        string parameters,
        string expression,
        params object?[] arguments)
    {
        return new(
            returnType,
            parameters,
            expression,
            arguments,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None);
    }

    private static FrontendSemanticEdgeCase Closed(
        string returnType,
        string parameters,
        string expression,
        FrontendAbstention abstention,
        params object?[] arguments)
    {
        return new(
            returnType,
            parameters,
            expression,
            arguments,
            FrontendSubsetDecision.ClosedAbstention,
            abstention);
    }
}
