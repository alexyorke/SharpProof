using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Worker.Test;

public sealed class DictionaryStateOracleTests
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
    public void BoundaryKeysAliasesMembershipAndUpdatesAgreeWithCompiledClr(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Compare("var data = new System.Collections.Generic.Dictionary<" + type + ", int>(); " +
            "var alias = data; alias.Add(value: 23, key: " + type + ".MinValue); " +
            "data[" + type + ".MaxValue] = 29; alias[" + type + ".MinValue] = 31; " +
            "return data.Count == 2 && data[" + type + ".MinValue] == 31 && " +
            "data[" + type + ".MaxValue] == 29 && data.ContainsKey(" + type + ".MinValue) ? 1 : 0;");
    }

    [TestCase("return data.Count;")]
    [TestCase("return data.ContainsKey(0) ? 1 : 0;")]
    [TestCase("data.Add(17, 23); return data[17];")]
    [TestCase("data.Add(17, 23); return data.ContainsKey(18) ? 1 : 0;")]
    [TestCase("data[17] = 23; data[17] = 29; return data.Count * 100 + data[17];")]
    [TestCase("var alias = data; alias.Add(17, 23); return data[17];")]
    [TestCase("data.Add(17, 23); var before = data[17]; data[17] = 29; return before + data[17];")]
    [TestCase("var other = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); other.Add(17, 99); return data[17];")]
    public void FreshIntegerDictionaryProgramsAgreeWithCompiledClr(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare("var data = new System.Collections.Generic.Dictionary<int, int>(); " + body);
    }

    [Test]
    public void DistinctLongKeysWithEqualHashCodesKeepSeparateValues()
    {
        Compare("var data = new System.Collections.Generic.Dictionary<long, bool>(); data.Add(1L, true); " +
            "data.Add(4294967296L, false); return data.Count == 2 && data[1L] && !data[4294967296L] ? 1 : 0;");
    }

    [TestCase("var data = new System.Collections.Generic.Dictionary<int, object>(); var first = new object(); var second = new object(); data.Add(17, first); var before = data[17]; var alias = data; alias[17] = second; return data.Count == 1 && object.ReferenceEquals(before, first) && object.ReferenceEquals(data[17], second) && !object.ReferenceEquals(first, second) ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, string>(); var value = \"sample\"; data[17] = value; data[18] = null; return data.Count == 2 && object.ReferenceEquals(data[17], value) && data.ContainsKey(18) && data[18] == null && !data.ContainsKey(19) ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int[]>(); var value = new int[1]; data[17] = value; value[0] = 23; return object.ReferenceEquals(data[17], value) && data[17][0] == 23 ? 1 : 0;")]
    public void ReferenceDictionaryStateAgreesWithCompiledClr(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare(body);
    }

    [Test]
    public void ReferenceDictionaryDoesNotInvokeValueEqualityCallbacks()
    {
        var first = new ObservedValueEquality();
        var second = new ObservedValueEquality();
        Compare("var data = new System.Collections.Generic.Dictionary<int, object>(); data.Add(17, value0); " +
            "data[17] = value1; return data.Count == 1 && object.ReferenceEquals(data[17], value1) ? 1 : 0;", first, second);
        Assert.That(first.Callbacks, Is.Zero);
        Assert.That(second.Callbacks, Is.Zero);
    }

    private sealed class ObservedValueEquality
    {
        public int Callbacks { get; private set; }

        public override bool Equals(object? obj)
        {
            Callbacks++;
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode()
        {
            Callbacks++;
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }
    }

    [TestCase("decimal")]
    [TestCase("System.DateTime")]
    [TestCase("System.Guid")]
    [TestCase("System.Numerics.BigInteger")]
    public void EmptyDictionaryWithOpaqueValueTypeAgreesWithCompiledClr(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Compare("var data = new System.Collections.Generic.Dictionary<int, " + type + ">(); var alias = data; " +
            "return data.Count + alias.Count + (alias.ContainsKey(17) ? 100 : 0);");
    }

    [TestCase("string")]
    [TestCase("object")]
    [TestCase("System.Guid")]
    public void NonIntegerKeyEmptyDictionaryCountAgreesWithCompiledClr(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Compare("var data = new System.Collections.Generic.Dictionary<" + key + ", int>(); var alias = data; return data.Count + alias.Count;", true);
    }

    private static void Compare(string body, params object?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(inputs);
        Compare(body, false, inputs);
    }

    private static void Compare(string body, bool approximateInitialization, params object?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(inputs);
        var parameters = string.Join(", ", Enumerable.Range(0, inputs.Length).Select(index => "object value" + index));
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(" + parameters + ") { " + body + " } }");
        var compilation = CSharpCompilation.Create("DictionaryStateOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        var runtime = new AssemblyLoadContext("DictionaryStateOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, inputs);
            // Primitive operations use observed replay authority. Generic-key
            // initialization selects a bounded normal model choice and must
            // remain approximation-dependent, never trusted native replay.
            var options = new IrProgramReplayOptions(request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return approximateInitialization && factory.GetVariableInfo(request.Variable).Type == factory.BooleanType
                    ? factory.CreateBooleanValue(false) : null;
            })
            {
                ConcreteHavocValueProvider = request => IrOpaqueCallSite.DictionaryInitializationPrimitive(factory, request.Site) != null &&
                    factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null
            };
            var argumentBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
            for (var index = 0; index < inputs.Length; index++)
            {
                argumentBuilder.AddUnique(context.Parameters[index].Entry, inputs[index] is { } value
                    ? factory.CreateReferenceValue(factory.ObjectType, value) : factory.CreateNullValue(factory.ObjectType));
            }
            var arguments = argumentBuilder.ToImmutable();
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, arguments, 5000, options);
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
            Assert.That(result.ConsumedApproximation, Is.EqualTo(approximateInitialization));
            Assert.That(result.ReturnValue!.Integer, Is.EqualTo(actual));
        }
        finally
        { runtime.Unload(); }
    }
}
