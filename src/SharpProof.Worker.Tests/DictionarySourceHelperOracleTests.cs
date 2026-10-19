using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class DictionarySourceHelperOracleTests
{
    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public void NestedGenericHelpersShareBoundaryKeysAndCallerState(string keyType)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        var source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { " +
            "public static int Target() { Contract.Ensures(true); var data = new Dictionary<" + keyType + ", int>(); " +
            "var alias = data; Store<" + keyType + ", int>(alias, " + keyType + ".MinValue, 17); " +
            "Store<" + keyType + ", int>(data, " + keyType + ".MaxValue, 23); " +
            "Store<" + keyType + ", int>(alias, " + keyType + ".MinValue, 31); " +
            "return data.Count == 2 && data[" + keyType + ".MinValue] == 31 && " +
            "Read<" + keyType + ", int>(alias, " + keyType + ".MaxValue) == 23 ? 1 : 0; } " +
            "static void Store<K, V>(Dictionary<K, V> data, K key, V value) { Forward<K, V>(data, key, value); } " +
            "static void Forward<K, V>(Dictionary<K, V> data, K key, V value) { if (data.ContainsKey(key)) data[key] = value; else data.Add(key, value); } " +
            "static V Read<K, V>(Dictionary<K, V> data, K key) => data[key]; }";
        Compare(source, [], [], opaqueReturn: false);
    }

    [Test]
    public void SourceHelpersKeepDistinctDictionaryAllocationsSeparate()
    {
        const string source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { " +
            "public static int Target() { Contract.Ensures(true); var first = new Dictionary<long, int>(); var second = new Dictionary<long, int>(); " +
            "Store<long, int>(first, 1L, 17); Store<long, int>(second, 1L, 23); Store<long, int>(first, 4294967296L, 31); " +
            "return first.Count == 2 && second.Count == 1 && Read<long, int>(first, 1L) == 17 && " +
            "Read<long, int>(second, 1L) == 23 && Read<long, int>(first, 4294967296L) == 31 ? 1 : 0; } " +
            "static void Store<K, V>(Dictionary<K, V> data, K key, V value) { data[key] = value; } " +
            "static V Read<K, V>(Dictionary<K, V> data, K key) => data[key]; }";
        Compare(source, [], [], opaqueReturn: false);
    }

    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("float")]
    [TestCase("guid")]
    [TestCase("enum")]
    [TestCase("date")]
    [TestCase("nullable-empty")]
    [TestCase("nullable-full")]
    [TestCase("big-integer")]
    [TestCase("managed-struct")]
    [TestCase("reference")]
    [TestCase("reference-null")]
    public void GenericDictionaryHelpersPreserveOpaqueValueAtoms(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var (type, first, second) = Inputs(domain);
        const string source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { " +
            "public static T Target<T>(T first, T second) { Contract.Ensures(true); var data = new Dictionary<long, T>(); " +
            "Store<long, T>(data, long.MinValue, first); var alias = data; Replace<long, T>(alias, long.MinValue, second); " +
            "Store<long, T>(data, long.MaxValue, first); return Read<long, T>(data, long.MinValue); } " +
            "static void Store<K, V>(Dictionary<K, V> data, K key, V value) { data.Add(key, value); } " +
            "static void Replace<K, V>(Dictionary<K, V> data, K key, V value) { Forward<K, V>(data, key, value); } " +
            "static void Forward<K, V>(Dictionary<K, V> data, K key, V value) { data[key] = value; } " +
            "static V Read<K, V>(Dictionary<K, V> data, K key) => data[key]; }";
        Compare(source, [type], [first, second], opaqueReturn: true);
    }

    private static void Compare(string source, Type[] genericTypes, object?[] runtimeInputs, bool opaqueReturn)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(genericTypes);
        ArgumentNullException.ThrowIfNull(runtimeInputs);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>();
        for (var ordinal = 0; ordinal < total.Parameters.Length; ordinal++)
        {
            var parameter = total.Parameters[ordinal].Entry;
            var parameterType = factory.GetVariableInfo(parameter).Type;
            // Opaque structs are atoms, including Nullable<T> with no value.
            // Only an actual reference-type null becomes the IR null value.
            inputs[parameter] = !genericTypes[0].IsValueType && runtimeInputs[ordinal] == null
                ? factory.CreateNullValue(parameterType)
                : factory.CreateReferenceValue(parameterType, new IrObjectState());
        }
        var choices = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var interpreted = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 50000, choices);
        Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), interpreted.Unsupported?.Detail);
        Assert.That(interpreted.ConsumedApproximation, Is.True,
            "Selecting successful framework initialization is differential evidence, not native replay authority.");
        if (opaqueReturn)
        {
            var expected = inputs[total.Parameters[1].Entry];
            if (expected.Kind == IrValueKind.Null)
            { Assert.That(interpreted.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Null)); }
            else
            { Assert.That(interpreted.ReturnValue!.Reference, Is.SameAs(expected.Reference)); }
        }
        else
        { Assert.That(interpreted.ReturnValue!.Integer, Is.EqualTo(1)); }

        var compilation = TestCompilation.Create("DictionarySourceHelperOracle", ("Probe.cs", source));
        var syntax = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(syntax,
            syntax.WithRootAndOptions(syntax.GetRoot(), ((CSharpParseOptions)syntax.Options).WithPreprocessorSymbols(Array.Empty<string>())));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("DictionarySourceHelperOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!;
            if (genericTypes.Length != 0)
            { target = target.MakeGenericMethod(genericTypes); }
            var actual = target.Invoke(null, runtimeInputs);
            if (!opaqueReturn)
            { Assert.That(actual, Is.EqualTo(1)); }
            else if (!genericTypes[0].IsValueType)
            { Assert.That(actual, Is.SameAs(runtimeInputs[1])); }
            else if (actual is double wide && runtimeInputs[1] is double expectedWide)
            { Assert.That(BitConverter.DoubleToInt64Bits(wide), Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedWide))); }
            else if (actual is float narrow && runtimeInputs[1] is float expectedNarrow)
            { Assert.That(BitConverter.SingleToInt32Bits(narrow), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedNarrow))); }
            else if (actual is decimal precise && runtimeInputs[1] is decimal expectedPrecise)
            { Assert.That(decimal.GetBits(precise), Is.EqualTo(decimal.GetBits(expectedPrecise))); }
            else
            { Assert.That(actual, Is.EqualTo(runtimeInputs[1])); }
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
