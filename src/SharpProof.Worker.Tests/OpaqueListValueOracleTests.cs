using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class OpaqueListValueOracleTests
{
    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("float")]
    [TestCase("guid")]
    [TestCase("enum")]
    [TestCase("enum-flags")]
    [TestCase("date")]
    [TestCase("nullable-empty")]
    [TestCase("nullable-full")]
    [TestCase("big-integer")]
    [TestCase("managed-struct")]
    [TestCase("reference")]
    [TestCase("reference-null")]
    public void OpaqueValuesSurviveTheOriginalGenericCorpusSwap(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var (type, first, second) = Inputs(domain);
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRepository.FindRoot(), "src/SharpProof.Gates/Corpus/oss-methods.json")));
        var method = corpus.RootElement.GetProperty("methods").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "OSS0002");
        var file = corpus.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("path").GetString() == method.GetProperty("path").GetString());
        var lines = file.GetProperty("content").GetString()!.Split('\n').ToImmutableArray();
        var swap = string.Join("\n", lines.Skip(method.GetProperty("startLine").GetInt32() - 1)
            .Take(method.GetProperty("endLine").GetInt32() - method.GetProperty("startLine").GetInt32() + 1));
        var source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { public static T Target<T>(T first, T second) { Contract.Ensures(true); var data = new List<T>() { first, second }; data.Swap(0, 1); return Read<T>(data); } static U Read<U>(IList<U> data) => data[0]; " + swap + " }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var inputBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
        for (var ordinal = 0; ordinal < total.Parameters.Length; ordinal++)
        {
            var parameter = total.Parameters[ordinal].Entry;
            var parameterType = factory.GetVariableInfo(parameter).Type;
            var value = ordinal == 0 ? first : second;
            // Each opaque value is an abstract atom. Struct payloads are never
            // interpreted as fields or numbers by this differential execution.
            inputBuilder[parameter] = !type.IsValueType && value == null ? factory.CreateNullValue(parameterType)
                : factory.CreateReferenceValue(parameterType, new IrObjectState());
        }
        var inputs = inputBuilder.ToImmutable();
        var choices = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var interpreted = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 10000, choices);
        Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), interpreted.Unsupported?.Detail);
        Assert.That(interpreted.ConsumedApproximation, Is.True, "Selected normal growth paths do not grant native replay authority.");
        var expected = inputs[total.Parameters[1].Entry];
        if (expected.Kind == IrValueKind.Null)
        { Assert.That(interpreted.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Null)); }
        else
        { Assert.That(interpreted.ReturnValue!.Reference, Is.SameAs(expected.Reference)); }

        var compilation = TestCompilation.CreateExact("OpaqueListValueOracle", [("Probe.cs", source)]);
        var syntax = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(syntax,
            syntax.WithRootAndOptions(syntax.GetRoot(), ((CSharpParseOptions)syntax.Options).WithPreprocessorSymbols(ImmutableArray<string>.Empty)));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new AssemblyLoadContext("OpaqueListValueOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.MakeGenericMethod(type);
            var actual = target.Invoke(null, [first, second]);
            if (!type.IsValueType)
            { Assert.That(actual, Is.SameAs(second)); }
            else if (actual is double wide && second is double expectedWide)
            { Assert.That(BitConverter.DoubleToInt64Bits(wide), Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedWide))); }
            else if (actual is float narrow && second is float expectedNarrow)
            { Assert.That(BitConverter.SingleToInt32Bits(narrow), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedNarrow))); }
            else if (actual is decimal precise && second is decimal expectedPrecise)
            { Assert.That(decimal.GetBits(precise), Is.EqualTo(decimal.GetBits(expectedPrecise))); }
            else
            { Assert.That(actual, Is.EqualTo(second)); }
        }
        finally
        { runtime.Unload(); }
    }

    private static (Type Type, object? First, object? Second) Inputs(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return domain switch
        {
            "decimal" => (typeof(decimal), decimal.MinValue, new decimal(12300, 0, 0, false, 4)),
            "double" => (typeof(double), double.NegativeInfinity, BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000123UL))),
            "float" => (typeof(float), float.PositiveInfinity, BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00123U))),
            "guid" => (typeof(Guid), Guid.Empty, new Guid("01234567-89ab-cdef-0123-456789abcdef")),
            "enum" => (typeof(DayOfWeek), DayOfWeek.Monday, DayOfWeek.Sunday),
            "enum-flags" => (typeof(System.Reflection.BindingFlags), System.Reflection.BindingFlags.Public, unchecked((System.Reflection.BindingFlags)(-1))),
            "date" => (typeof(DateTime), DateTime.MinValue, new DateTime(638000000000000000L, DateTimeKind.Utc)),
            "nullable-empty" => (typeof(int?), 17, null),
            "nullable-full" => (typeof(int?), null, int.MaxValue),
            "big-integer" => (typeof(System.Numerics.BigInteger), System.Numerics.BigInteger.MinusOne, System.Numerics.BigInteger.One << 256),
            "managed-struct" => (typeof((int, object)), (17, new object()), (31, new object())),
            "reference" => (typeof(object), new object(), new object()),
            "reference-null" => (typeof(object), new object(), null),
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };
    }
}
