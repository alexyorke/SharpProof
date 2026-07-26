using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.V2Fuzz;

namespace SharpProof.V2Fuzz.Test;

[TestFixture]
public sealed class FrontendSemanticEdgeCaseTests {
    [Test]
    public void FixedSemanticEdgesMatchRuntimeOrAbstainExactly() {
        var cases = new[] {
            Exact("long", "short value", "(long)value", short.MinValue),
            Exact("long", "int value", "(long)value", int.MaxValue),
            Exact("long", "uint value", "(long)value", uint.MaxValue),
            Exact("long", "", "checked((int)3L)"),
            Exact("string?", "object? value", "(string)value", (object?)null),
            Exact("string?", "object? value", "(string)value", "proof"),
            Exact("string?", "object? value", "(string)value", new object()),
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
            results[6].ExceptionKind,
            Is.EqualTo(IrExceptionKind.InvalidCast));
        for (var index = 0; index < cases.Length; index++) {
            Assert.That(
                results[index].ActualDecision,
                Is.EqualTo(cases[index].ExpectedDecision));
            Assert.That(
                results[index].ActualAbstention,
                Is.EqualTo(cases[index].ExpectedAbstention));
        }
    }

    private static FrontendSemanticEdgeCase Exact(
        string returnType,
        string parameters,
        string expression,
        params object?[] arguments) =>
        new(
            returnType,
            parameters,
            expression,
            arguments,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None);

    private static FrontendSemanticEdgeCase Closed(
        string returnType,
        string parameters,
        string expression,
        FrontendAbstention abstention,
        params object?[] arguments) =>
        new(
            returnType,
            parameters,
            expression,
            arguments,
            FrontendSubsetDecision.ClosedAbstention,
            abstention);
}
