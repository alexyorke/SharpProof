using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FrontendSemanticEdgeCaseTests
{
    private static readonly ImmutableArray<long> SequenceValue = [1L, 2L];
    private static readonly ImmutableArray<ulong> UnsupportedSequenceValue = [1UL];
    private static readonly Array MultidimensionalSequenceValue =
        Array.CreateInstance(typeof(long), 2, 3);

    [Test]
    public void FixedSemanticEdgesMatchRuntimeOrAbstainExactly()
    {
        var cases = ImmutableArray.Create(
            Exact("sbyte", "", "-3", []),
            Exact("byte", "", "3", []),
            Exact("short", "", "-3", []),
            Exact("ushort", "", "3", []),
            Exact("int", "", "-3", []),
            Exact("uint", "", "3", []),
            Exact("char", "", "'A'", []),
            Exact("long", "short value", "(long)value", [short.MinValue]),
            Exact("long", "int value", "(long)value", [int.MaxValue]),
            Exact("long", "uint value", "(long)value", [uint.MaxValue]),
            Exact("long", "", "checked((int)3L)", []),
            Exact("object?", "object? value", "value", [new object()]),
            Exact("long[]", "long[] value", "value", [SequenceValue.ToArray()]),
            Exact("ulong", "ulong[] value", "value[0]", [UnsupportedSequenceValue.ToArray()]),
            // String is its own IR value domain, so object-to-string casts close.
            Closed("string?", "object? value", "(string)value", FrontendAbstention.UnsupportedOperationKind, [(object?)null]),
            Closed("string?", "object? value", "(string)value", FrontendAbstention.UnsupportedOperationKind, ["proof"]),
            Closed("string?", "object? value", "(string)value", FrontendAbstention.UnsupportedOperationKind, [new object()]),
            Closed("long", "long[,] value", "value.LongLength", FrontendAbstention.UnsupportedOperationKind, [MultidimensionalSequenceValue]),
            Exact("long", "long value", "checked((int)value)", [long.MaxValue]),
            Exact("long", "long value", "unchecked((int)value)", [long.MaxValue]),
            Closed("string?", "object? value", "value as string", FrontendAbstention.UnsupportedOperationKind, ["proof"]),
            Closed("long", "long value", "(long)new SharpProofGeneratedConvertible(value)", FrontendAbstention.UnsupportedOperationKind, [1L]),
            Closed("long?", "", "(long?)1L", FrontendAbstention.UnsupportedType, []),
            Closed("long?", "long value", "(long?)value", FrontendAbstention.UnsupportedType, [1L]),
            Closed("long?", "long? value", "value + 1L", FrontendAbstention.UnsupportedType, [(long?)1L]),
            Closed("bool", "long? value", "value == 0L", FrontendAbstention.UnsupportedType, [(long?)0L]),
            Closed("long?", "long? value", "-value", FrontendAbstention.UnsupportedType, [(long?)1L]));

        var results = FrontendDifferentialOracle.CompareSemanticEdges(cases, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(results, Has.Length.EqualTo(cases.Length));
        Assert.That(
            results.Select(static result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Status)),
            Is.All.EqualTo(FuzzOracleStatus.Agreement),
            string.Join(
                Environment.NewLine,
                results.Select((result, index) =>
                    result is null ? throw new System.ArgumentNullException(nameof(result)) : (index + ": " + result.Detail))));
        Assert.That(
            results[18].ExceptionKind,
            Is.EqualTo(IrExceptionKind.Overflow));
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
    public void SameContentSequenceIdentityMismatchIsDetected()
    {
        long[] first = [1, 2];
        long[] second = [1, 2];
        var result = FrontendDifferentialOracle.CompareSemanticEdges(
        [
            new FrontendSemanticEdgeCase(
                "long[]",
                "long[] first, long[] second",
                "first",
                new SwappedSequenceArguments(first, second),
                FrontendSubsetDecision.Exact,
                FrontendAbstention.None)
        ], cancellationToken: TestContext.CurrentContext.CancellationToken).Single();

        Assert.That(
            result.Status,
            Is.EqualTo(FuzzOracleStatus.Mismatch),
            result.Detail);
    }

    [Test]
    public void CompileInvalidSemanticEdgeDoesNotPoisonValidPeer()
    {
        AssertValidPeerIsolation("long.MaxValue + 1L");
    }

    [Test]
    public void CompileSuccessfulSemanticEdgeInjectionDoesNotPoisonValidPeer()
    {
        AssertValidPeerIsolation(
            "0L; public static long EdgeTarget999() => 0L");
    }

    [Test]
    public void NonnumericSemanticEdgeInjectionDoesNotEscapeBatchIsolation()
    {
        AssertValidPeerIsolation(
            "0L; public static long EdgeTargetOops() => 0L");
    }

    [Test]
    public void StaticInitializerInjectionDoesNotPoisonValidPeer()
    {
        AssertValidPeerIsolation(
            "0L; static readonly long Poison = Throw(); " +
            "static long Throw() => throw new System.Exception()");
    }

    [Test]
    public void TopLevelInitializerInjectionDoesNotPoisonValidPeer()
    {
        AssertValidPeerIsolation(
            "0L; } public static class Injected { " +
            "[System.Runtime.CompilerServices.ModuleInitializer] " +
            "public static void Initialize() => " +
            "throw new System.Exception(); } public static class Tail { " +
            "public static long Value => 0L");
    }

    private static void AssertValidPeerIsolation(string injectedExpression)
    {
        System.ArgumentNullException.ThrowIfNull(injectedExpression);
        var results = FrontendDifferentialOracle.CompareSemanticEdges(
        [
            Exact("long", "", "0L", []),
            Exact("long", "", injectedExpression, [])
        ], cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        ImmutableArray<object?> arguments)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(returnType);
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
        ImmutableArray<object?> arguments)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(returnType);
        return new(
            returnType,
            parameters,
            expression,
            arguments,
            FrontendSubsetDecision.ClosedAbstention,
            abstention);
    }

    private sealed class SwappedSequenceArguments(
        long[] first,
        long[] second) : IReadOnlyList<object?>
    {
        private readonly long[] _guarded_second = second ?? throw new System.ArgumentNullException(nameof(second));
        private readonly long[] _guarded_first = first ?? throw new System.ArgumentNullException(nameof(first));
        public int Count => 2;

        public object this[int index] => index switch
        {
            0 => _guarded_first,
            1 => _guarded_second,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public IEnumerator<object?> GetEnumerator()
        {
            yield return _guarded_second;
            yield return _guarded_first;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
