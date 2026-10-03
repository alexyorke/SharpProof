using System.Globalization;
using System.Numerics;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TypedMetadataConversionTests
{
    private static readonly string[] NumericTypes = ["sbyte", "byte", "short", "ushort", "char", "int", "uint", "long", "ulong"];

    private static IEnumerable<TestCaseData> Conversions()
    {
        foreach (var source in NumericTypes)
        {
            foreach (var target in NumericTypes)
            {
                yield return new(source, target, false);
                yield return new(source, target, true);
            }
        }
    }

    [TestCaseSource(nameof(Conversions))]
    public void EveryNumericConversionMatchesCompiledBoundaryBehavior(string source, string target, bool checkedConversion)
    {
        var mode = checkedConversion ? "checked" : "unchecked";
        using var subject = new MetadataTestSubject(
            $"public static class Library {{ public static {target} Target({source} value) {{ return {mode}(({target})value); }} }}",
            $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject { public static {{target}} Target({{source}} value) {
                Contract.Ensures(true); return Library.Target(value);
            } }
            """);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var total = preparations.Single().Total;
        Assert.That(total, Is.Not.Null);
        var factory = total!.Program.Factory;
        var entry = total.Parameters.Single().Entry;
        var entryType = factory.GetVariableInfo(entry).Type;
        var (minimum, maximum) = Range(source);
        var (targetMinimum, targetMaximum) = Range(target);
        BigInteger[] boundaries = [minimum, maximum, -1, 0, 1, targetMinimum - 1, targetMinimum, targetMinimum + 1,
            targetMaximum - 1, targetMaximum, targetMaximum + 1, int.MinValue, int.MaxValue, uint.MaxValue, long.MinValue, long.MaxValue];
        foreach (var boundary in boundaries.Distinct().Where(value => value >= minimum && value <= maximum))
        {
            var runtimeType = subject.Method.GetParameters().Single().ParameterType;
            var input = runtimeType == typeof(char) ? (object)(char)(ushort)boundary :
                Convert.ChangeType((decimal)boundary, runtimeType, CultureInfo.InvariantCulture);
            var compiled = subject.Invoke(input);
            var entryValue = source == "ulong" ? factory.CreateIntegerValue(entryType, (ulong)boundary) :
                factory.CreateIntegerValue(entryType, (long)boundary);
            var execution = new IrProgramInterpreter(factory).Execute(total.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = entryValue });
            var description = $"{source} -> {target}, {mode}, input={boundary}";
            Assert.That(execution.ConsumedApproximation, Is.False, description);
            if (compiled is OverflowException)
            {
                Assert.That(execution.Exception?.Kind, Is.EqualTo(IrExceptionKind.Overflow), description);
            }
            else
            {
                Assert.That(compiled, Is.Not.InstanceOf<Exception>(), description);
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), description);
                var expected = compiled is ulong unsigned ? new BigInteger(unsigned) : new BigInteger(Convert.ToInt64(compiled, CultureInfo.InvariantCulture));
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(expected), description);
            }
        }
    }

    private static (BigInteger Minimum, BigInteger Maximum) Range(string type)
    {
        return type switch
        {
            "sbyte" => (sbyte.MinValue, sbyte.MaxValue),
            "byte" => (byte.MinValue, byte.MaxValue),
            "short" => (short.MinValue, short.MaxValue),
            "ushort" or "char" => (ushort.MinValue, ushort.MaxValue),
            "int" => (int.MinValue, int.MaxValue),
            "uint" => (uint.MinValue, uint.MaxValue),
            "long" => (long.MinValue, long.MaxValue),
            "ulong" => (ulong.MinValue, ulong.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
