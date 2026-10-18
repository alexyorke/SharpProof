using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    private static IEnumerable<TestCaseData> GenericArrayReturnCases()
    {
        foreach (var element in new[] { "bool", "char", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            foreach (var local in new[] { false, true })
            {
                foreach (var correct in new[] { false, true })
                { yield return new TestCaseData(element, local, correct); }
            }
        }
    }

    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericArrayHelperReturnUsesConcreteElementRepresentation(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Read<T>(System.Collections.Generic.IList<T> list, int index) => list[index];";
        var body = "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() " + (correct ? "==" : "!=") + " values[index]); " +
            "return Read<" + element + ">(values, index); " + (local ? helper : "");
        await CheckInterfaceArrayContract(element + "[] values, int index", body,
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [TestCase("int[] values", "return Read<int>(values);", "static T Read<T>(T[] list) => list[0];")]
    [TestCase("int[] values", "return Read<int>(values[0]);", "static T Read<T>(T value) => value;")]
    [TestCase("System.Collections.Generic.IList<int> unknown", "return Read<int>(unknown);", "static T Read<T>(System.Collections.Generic.IList<T> list) => list[0];")]
    [TestCase("int[] values, System.Collections.Generic.IList<int> unknown", "return Read<int>(values, unknown);", "static T Read<T>(System.Collections.Generic.IList<T> list, System.Collections.Generic.IList<T> other) { list = other; return list[0]; }")]
    [TestCase("int[] values", "return Read<int, int>(values);", "static U Read<T, U>(System.Collections.Generic.IList<T> list) => default(U);")]
    public async Task UnprovenGenericArrayReturnRepresentationsStayConservative(string parameters, string call, string helper)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(helper);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<int>() == 17); " + call + " " + helper,
            WorkerClaimOutcome.Unknown);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericScalarReturnThroughArrayTryCastPreservesContentsAndEffects(bool correct)
    {
        const string helper = "static T Read<T>(System.Collections.Generic.IList<T> list, int index) => list[index];";
        const string requires = "Contract.Requires(values != null && index >= 0 && index < values.Length); ";
        var body = "return Read<int>(values as System.Collections.Generic.IList<int>, index); " + helper;
        await CheckInterfaceArrayContract("int[] values, int index", requires +
            "Contract.Ensures(Contract.Result<int>() " + (correct ? "==" : "!=") + " values[index]); " + body,
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values, int index",
            requires + body, true, true, true);
    }
    private static IEnumerable<TestCaseData> GenericArrayReturnOracleCases()
    {
        foreach (var element in new[] { "bool", "char", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            foreach (var local in new[] { false, true })
            { yield return new TestCaseData(element, local); }
        }
    }

    [TestCaseSource(nameof(GenericArrayReturnOracleCases))]
    public async Task GenericScalarReturnBitsAndFaultsAgreeWithClr(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Read<T>(System.Collections.Generic.IList<T> list, int index) => list[index];";
        using var project = WorkerTests.TestProject.Create("public static class C { public static " + element + " Target(" + element +
            "[] values, int index) { return Read<" + element + ">(values, index); " + (local ? helper : "") + " } " + (local ? "" : helper) + " }");
        project.UseNetCoreReferencePack();
        var compilation = project.CreateCompilation();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = (await tree.GetRootAsync(TestContext.CurrentContext.CancellationToken)).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static frame => { ArgumentNullException.ThrowIfNull(frame); return true; }, null,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true, pinElementReads: true);
        Assert.That(lowered.IsExact, Is.True);
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        emitted.Position = 0;
        var load = new System.Runtime.Loader.AssemblyLoadContext("generic-scalar-returns", isCollectible: true);
        try
        {
            var runtime = load.LoadFromStream(emitted).GetType("C")!.GetMethod("Target")!;
            var runtimeElement = runtime.GetParameters()[0].ParameterType.GetElementType()!;
            object[] boundaries = element switch
            {
                "bool" => [false, true],
                "char" => [char.MinValue, '\ud800', char.MaxValue],
                "sbyte" => [sbyte.MinValue, (sbyte)0, sbyte.MaxValue],
                "byte" => [byte.MinValue, (byte)1, byte.MaxValue],
                "short" => [short.MinValue, (short)0, short.MaxValue],
                "ushort" => [ushort.MinValue, (ushort)1, ushort.MaxValue],
                "int" => [int.MinValue, 0, int.MaxValue],
                "uint" => [uint.MinValue, 1u, uint.MaxValue],
                "long" => [long.MinValue, 0L, long.MaxValue],
                "ulong" => [ulong.MinValue, 1UL, ulong.MaxValue],
                _ => throw new ArgumentOutOfRangeException(nameof(element))
            };
            var all = Array.CreateInstance(runtimeElement, boundaries.Length);
            var inputs = new List<Array?> { null, Array.CreateInstance(runtimeElement, 0), all };
            for (var ordinal = 0; ordinal < boundaries.Length; ordinal++)
            {
                all.SetValue(boundaries[ordinal], ordinal);
                var single = Array.CreateInstance(runtimeElement, 1);
                single.SetValue(boundaries[ordinal], 0);
                inputs.Add(single);
            }
            var arrayType = factory.GetVariableInfo(context.Parameters[0].Entry).Type;
            var elementType = factory.GetTypeInfo(arrayType).ElementType!.Value;
            foreach (var values in inputs)
            {
                foreach (var index in new[] { -1, 0, (values?.Length ?? 0) - 1, values?.Length ?? 0, int.MaxValue }.Distinct())
                {
                    Exception? fault = null;
                    object? expected = null;
                    try
                    { expected = runtime.Invoke(null, [values?.Clone(), index]); }
                    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is NullReferenceException or ArgumentOutOfRangeException)
                    { fault = exception.InnerException; }
                    var input = values == null ? factory.CreateNullValue(arrayType) : factory.CreateSequenceValue(arrayType,
                        values.Cast<object>().Select(value => GenericReturnOracleValue(factory, elementType, value ?? throw new ArgumentNullException(nameof(value)))));
                    var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                        new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
                        {
                            [context.Parameters[0].Entry] = input,
                            [context.Parameters[1].Entry] = factory.CreateIntegerValue(index)
                        }, 100000, new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)));
                    Assert.That(replay.Status, Is.EqualTo(fault == null ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Exception));
                    if (fault == null)
                    {
                        Assert.That(replay.ConsumedApproximation, Is.False);
                        var expectedValue = GenericReturnOracleValue(factory, elementType, expected!);
                        var actual = replay.ReturnValue!;
                        Assert.That(actual.Type, Is.EqualTo(elementType));
                        Assert.That(actual.Kind, Is.EqualTo(expectedValue.Kind));
                        Assert.That(actual.Kind == IrValueKind.Boolean ? (ulong)(actual.Boolean ? 1 : 0) : actual.IntegerBits,
                            Is.EqualTo(expectedValue.Kind == IrValueKind.Boolean ? (ulong)(expectedValue.Boolean ? 1 : 0) : expectedValue.IntegerBits));
                    }
                    else
                    { Assert.That(replay.Exception!.Kind, Is.EqualTo(fault is NullReferenceException ? IrExceptionKind.NullReference : IrExceptionKind.ArgumentOutOfRange)); }
                }
            }
        }
        finally
        { load.Unload(); }
    }

    private static IrValue GenericReturnOracleValue(IrFactory factory, ScopedIrId<IrTypeTag> type, object value)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(value);
        if (type.IsDefault)
        { throw new ArgumentOutOfRangeException(nameof(type)); }
        if (value is bool boolean)
        { return factory.CreateBooleanValue(boolean); }
        return value switch
        {
            sbyte number => factory.CreateIntegerValue(type, (long)number),
            short number => factory.CreateIntegerValue(type, (long)number),
            int number => factory.CreateIntegerValue(type, (long)number),
            long number => factory.CreateIntegerValue(type, number),
            byte number => factory.CreateIntegerValue(type, (ulong)number),
            ushort number => factory.CreateIntegerValue(type, (ulong)number),
            uint number => factory.CreateIntegerValue(type, (ulong)number),
            ulong number => factory.CreateIntegerValue(type, number),
            char character => factory.CreateIntegerValue(type, (ulong)character),
            _ => throw new ArgumentException("The runtime oracle requires a supported scalar.", nameof(value))
        };
    }
    [TestCase("bool")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("ulong")]
    public async Task NestedGenericArrayHelpersPreserveTheCallersSpecializedReturnType(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract(element + "[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() == values[index]); return Outer<" + element + ">(values, index);",
            WorkerClaimOutcome.Proven, element,
            "static T Outer<T>(System.Collections.Generic.IList<T> list, int index) { return Inner<T>(list, index); " +
            "static U Inner<U>(System.Collections.Generic.IList<U> values, int read) => values[read]; }");
    }

    [Test]
    public async Task NestedGenericReturnDoesNotBorrowAnotherTypeParametersArrayEvidence()
    {
        await CheckInterfaceArrayContract("int[] values", "Contract.Ensures(Contract.Result<int>() == 17); return Outer<int>(values);",
            WorkerClaimOutcome.Unknown, members:
            "static T Outer<T>(System.Collections.Generic.IList<T> list) { return Inner<T, T>(list); " +
            "static U Inner<V, U>(System.Collections.Generic.IList<V> values) => default(U); }");
    }
    [TestCase("bool")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("ulong")]
    public async Task GenericContainingTypeReturnUsesItsArrayElementEvidence(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract(element + "[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() == values[index]); return Reader<" + element + ">.Read(values, index);",
            WorkerClaimOutcome.Proven, element,
            "static class Reader<T> { public static T Read(System.Collections.Generic.IList<T> list, int index) => list[index]; }");
    }
}
