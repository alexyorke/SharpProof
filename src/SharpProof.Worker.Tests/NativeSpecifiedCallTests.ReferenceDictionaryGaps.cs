using System.Collections.Immutable;
using NUnit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task ReferenceDictionaryBaselineObjectIdentity()
    {
        await CheckInterfaceArrayContract("object value", "Contract.Ensures(object.ReferenceEquals(Contract.Result<object>(), value)); return value;", WorkerClaimOutcome.Proven, result: "object");
    }

    [TestCase("object.ReferenceEquals(Contract.Result<int[]>(), value)")]
    [TestCase("object.ReferenceEquals((object)Contract.Result<int[]>(), (object)value)")]
    [TestCase("object.ReferenceEquals(Contract.Result<int[]>() as object, value as object)")]
    public async Task ReferenceDictionaryBaselineArrayIdentity(string claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await CheckInterfaceArrayContract("int[] value", "Contract.Ensures(" + claim + "); return value;", WorkerClaimOutcome.Proven, result: "int[]");
    }

    [Test]
    public async Task ReferenceDictionaryBaselineBodyIdentity()
    {
        await CheckInterfaceArrayContract("object value", "Contract.Ensures(Contract.Result<int>() == 1); return object.ReferenceEquals(value, value) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ReferenceDictionaryInputObjectIdentityIsPreserved()
    {
        await CheckInterfaceArrayContract("object value", "Contract.Ensures(object.ReferenceEquals(Contract.Result<object>(), value)); " +
            "var data = new System.Collections.Generic.Dictionary<int, object>(); data.Add(17, value); return data[17];", WorkerClaimOutcome.Proven, result: "object");
    }

    [Test]
    public async Task ReferenceDictionarySetterUpdatesIdentityWithoutGrowingCount()
    {
        await CheckInterfaceArrayContract("object first, object second", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var data = new System.Collections.Generic.Dictionary<int, object>(); data[17] = first; var alias = data; alias[17] = second; " +
            "return data.Count == 1 && object.ReferenceEquals(data[17], second) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ReferenceDictionaryPresentNullIsDifferentFromMissing()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var data = new System.Collections.Generic.Dictionary<int, string>(); data[17] = null; " +
            "return data.ContainsKey(17) && !data.ContainsKey(18) && data[17] == null ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ReferenceDictionaryArrayIdentityIsPreserved()
    {
        await CheckInterfaceArrayContract("int[] value", "Contract.Ensures(object.ReferenceEquals(Contract.Result<int[]>(), value)); " +
            "var data = new System.Collections.Generic.Dictionary<int, int[]>(); data.Add(17, value); return data[17];", WorkerClaimOutcome.Proven, result: "int[]");
    }

    [Test]
    public async Task ReferenceDictionaryWrongIdentityHasConcreteCounterexample()
    {
        await CheckInterfaceArrayContract("object first, object second", "Contract.Requires(!object.ReferenceEquals(first, second)); " +
            "Contract.Ensures(object.ReferenceEquals(Contract.Result<object>(), second)); " +
            "var data = new System.Collections.Generic.Dictionary<int, object>(); data.Add(17, first); return data[17];", WorkerClaimOutcome.Refuted, result: "object");
    }
    [Test]
    public async Task ReferenceDictionaryWrongArrayIdentityHasConcreteCounterexample()
    {
        await CheckInterfaceArrayContract("int[] first, int[] second", "Contract.Requires(!object.ReferenceEquals(first, second)); " +
            "Contract.Ensures(object.ReferenceEquals(Contract.Result<int[]>(), second)); " +
            "var data = new System.Collections.Generic.Dictionary<int, int[]>(); data[17] = first; return data[17];", WorkerClaimOutcome.Refuted, result: "int[]");
    }

    [Test]
    public async Task ReferenceDictionarySymbolicLongKeysKeepDistinctIdentities()
    {
        await CheckInterfaceArrayContract("long key, object first, object second", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var data = new System.Collections.Generic.Dictionary<long, object>(); data[key] = first; data[key ^ long.MinValue] = second; " +
            "return data.Count == 2 && object.ReferenceEquals(data[key], first) && object.ReferenceEquals(data[key ^ long.MinValue], second) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ReferenceDictionaryClassValuesPreserveIdentityWithoutEqualityCallbacks()
    {
        await CheckInterfaceArrayContract("Value value", "Contract.Ensures(object.ReferenceEquals(Contract.Result<Value>(), value)); " +
            "var data = new System.Collections.Generic.Dictionary<int, Value>(); data.Add(17, value); return data[17];", WorkerClaimOutcome.Proven, result: "Value",
            members: "public sealed class Value { public override bool Equals(object other) { throw new System.InvalidOperationException(); } public override int GetHashCode() { throw new System.InvalidOperationException(); } }");
    }

    [Test]
    public async Task ReferenceDictionaryIncomingReceiverRetainsUnknown()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Dictionary<int, object> data, object value", "Contract.Requires(data != null); " +
            "Contract.Ensures(object.ReferenceEquals(Contract.Result<object>(), value)); return data[17];", WorkerClaimOutcome.Unknown, result: "object");
    }

    [TestCase("string")]
    [TestCase("System.Action")]
    [TestCase("System.IDisposable")]
    [TestCase("object[]")]
    [TestCase("int[,]")]
    public async Task ReferenceDictionaryOtherReferenceDomainsPreserveIdentity(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        await CheckInterfaceArrayContract(type + " value", "Contract.Ensures(object.ReferenceEquals(Contract.Result<" + type + ">(), value)); " +
            "var data = new System.Collections.Generic.Dictionary<int, " + type + ">(); data.Add(17, value); return data[17];", WorkerClaimOutcome.Proven, result: type);
    }

    [TestCase(true, false, WorkerClaimOutcome.Proven)]
    [TestCase(true, true, WorkerClaimOutcome.Refuted)]
    [TestCase(false, false, WorkerClaimOutcome.Unknown)]
    public async Task ReferenceDictionaryGenericReferenceValuesRequireClassEvidence(bool classConstraint, bool wrongClaim, WorkerClaimOutcome expected)
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { " +
            "public static T Target<T>(T value, T other) " + (classConstraint ? "where T : class " : "") + "{ " +
            (wrongClaim ? "Contract.Requires(!object.ReferenceEquals(value, other)); " : "") +
            "Contract.Ensures(object.ReferenceEquals(Contract.Result<T>(), " + (wrongClaim ? "other" : "value") + ")); " +
            "var data = new System.Collections.Generic.Dictionary<int, T>(); data.Add(17, value); return data[17]; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), response.ClaimResults.Single().Reason.ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReferenceDictionaryDirectIrPreservesInputIdentity(bool isNull)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(object value) { var data = new System.Collections.Generic.Dictionary<int, object>(); data.Add(17, value); return object.ReferenceEquals(data[17], value) ? 1 : 0; } }");
        var compilation = CSharpCompilation.Create("ReferenceDictionaryIrProbe", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(PassiveCallableVcPlan.WarmDefaultDictionary("System_Int32"), Is.True);
        var options = new IrProgramReplayOptions(_ => null)
        {
            ConcreteHavocValueProvider = request => IrOpaqueCallSite.DictionaryInitializationPrimitive(factory, request.Site) != null &&
                factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null
        };
        var inputs = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.SetItem(context.Parameters[0].Entry, isNull ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, new object()));
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 5000, options);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), result.Unsupported?.Detail);
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(1));
        Assert.That(result.ConsumedApproximation, Is.False);
    }
}
