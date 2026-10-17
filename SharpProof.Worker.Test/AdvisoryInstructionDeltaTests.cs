using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AdvisoryInstructionDeltaTests
{
    // Independent syntactic output facets: these are deltas, not completed
    // callable summaries. Neutral instructions assert every coordinate is zero.
    [TestCase("Allocation", (int)SourceMayEffect.Allocation, 0, 0, false, false, false)]
    [TestCase("Lock", (int)SourceMayEffect.Synchronization, 0, 0, false, false, false)]
    [TestCase("WriteStatic", (int)SourceMayEffect.NonlocalWrite, 0, 0, false, false, false)]
    [TestCase("WriteLocal", 0, 0, 0, false, false, false)]
    [TestCase("Assign", 0, 0, 0, false, false, false)]
    [TestCase("Load", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("Store", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("Call", 0, 0, 0, true, false, false)]
    [TestCase("Assume", 0, 0, 0, false, false, false)]
    [TestCase("Assert", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("InputVariables", 0, 0, 0, false, false, false)]
    [TestCase("InputMemory", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("ApproximationVariables", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("ApproximationMemory", 0, (int)SourceMayEffect.All, 0, true, true, true)]
    [TestCase("Throw", (int)SourceMayEffect.Allocation, 0, 1 << (int)IrExceptionKind.Overflow, false, false, false)]
    [TestCase("ExceptionalExit", 0, 0, 0, false, false, false)]
    [TestCase("Branch", 0, 0, 0, false, false, false)]
    [TestCase("Goto", 0, 0, 0, false, false, false)]
    [TestCase("Return", 0, 0, 0, false, false, false)]
    public void OwnedInstructionRetainsItsExactAdvisoryDelta(
        string shape, int may, int unknown, int exceptions,
        bool unknownExceptions, bool mayDiverge, bool completionUnknown)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        var (factory, instruction) = BuildInstruction(shape);
        var actual = EffectSummaryFixpoint.InstructionDelta(factory, instruction);

        AssertDelta(actual, may, unknown, exceptions,
            unknownExceptions, mayDiverge, completionUnknown);
    }

    // Existing real compiler source producers provide these annotations.
    // The test never writes an opaque-operation description or body flags.
    [TestCase("int[] Root() => System.Array.Empty<int>();", (int)IrOpaqueCallEffects.Allocates,
        (int)SourceMayEffect.Allocation)]
    [TestCase("int[] Root(int n) => new int[n];", (int)IrOpaqueCallEffects.Throws, 0)]
    public void CompilerProducedSpecialHavocRetainsOnlyItsDeclaredDelta(
        string method, int effect, int expectedUnknown)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static " + method + " }");
        var prepared = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        var body = prepared.ReachableSource!.Bodies.Single();
        Assert.That(body.Graph, Is.Not.Null);
        var decoded = PortableIrGraphCodec.Decode(body.Graph!);
        Assert.That(decoded.Program, Is.Not.Null);
        var markers = decoded.Instructions.OfType<IrHavocInstruction>().Where(instruction =>
            instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin == IrHavocOrigin.Approximation &&
            instruction.HavocKind == IrHavocKind.Variables &&
            IrOpaqueCallSite.Effects(decoded.Factory, instruction.Operation) == (IrOpaqueCallEffects)effect)).ToArray();
        Assert.That(markers, Is.Not.Empty, "The unchanged compiler producer must supply the declared marker.");
        foreach (var marker in markers)
        {
            var actual = EffectSummaryFixpoint.InstructionDelta(decoded.Factory, marker);
            AssertDelta(actual, 0, expectedUnknown, 0, false, false, false);
        }
    }

    private static void AssertDelta(SourceEffectSummary actual,
        int may, int unknown, int exceptions,
        bool unknownExceptions, bool mayDiverge, bool completionUnknown)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.MayEffects, Is.EqualTo((SourceMayEffect)may));
            Assert.That(actual.UnknownEffects, Is.EqualTo((SourceMayEffect)unknown));
            Assert.That(actual.ExceptionKinds, Is.EqualTo(exceptions));
            Assert.That(actual.UnknownExceptions, Is.EqualTo(unknownExceptions));
            Assert.That(actual.MayDiverge, Is.EqualTo(mayDiverge));
            Assert.That(actual.CompletionUnknown, Is.EqualTo(completionUnknown));
        }
    }

    private static (IrFactory Factory, IrInstruction Instruction) BuildInstruction(string shape)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var integer = factory.CreateVariable("integer", factory.IntegerType);
        var sequence = factory.CreateVariable("sequence", factory.GetOrCreateSequenceType(factory.IntegerType));
        builder.Havoc(entry, factory.CreateOperation("integer-input"), IrHavocKind.Variables, IrHavocOrigin.Input, integer);
        builder.Havoc(entry, factory.CreateOperation("sequence-input"), IrHavocKind.Variables, IrHavocOrigin.Input, sequence);
        var site = factory.CreateOperation("instruction-delta");
        var location = builder.SequenceLocation(factory.Variable(sequence), factory.Integer(0));
        var terminal = false;
        IrInstruction instruction;
        switch (shape)
        {
            case "Allocation":
                instruction = builder.Allocate(entry, site, factory.ObjectType);
                break;
            case "Lock":
                var monitor = factory.CreateVariable("monitor", factory.ObjectType);
                builder.Allocate(entry, factory.CreateOperation("monitor-allocation"), factory.ObjectType, monitor);
                instruction = builder.Lock(entry, site, factory.Variable(monitor));
                break;
            case "WriteStatic":
                instruction = builder.Write(entry, site, IrWriteRegion.Static);
                break;
            case "WriteLocal":
                instruction = builder.Write(entry, site, IrWriteRegion.Local);
                break;
            case "Assign":
                instruction = builder.Assign(entry, site, integer, factory.Integer(7));
                break;
            case "Load":
                instruction = builder.Load(entry, site, integer, location);
                break;
            case "Store":
                instruction = builder.Store(entry, site, location, factory.Integer(7));
                break;
            case "Call":
                var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
                    "DeltaCall", factory.IntegerType, true, []);
                instruction = builder.Call(entry, site, integer, member, null);
                break;
            case "Assume":
                instruction = builder.Assume(entry, site, factory.Boolean(true));
                break;
            case "Assert":
                instruction = builder.Assert(entry, site, factory.Boolean(true));
                break;
            case "InputVariables":
                instruction = builder.Havoc(entry, site, IrHavocKind.Variables, IrHavocOrigin.Input, integer);
                break;
            case "InputMemory":
                instruction = builder.Havoc(entry, site, IrHavocKind.Memory, IrHavocOrigin.Input);
                break;
            case "ApproximationVariables":
                instruction = builder.Havoc(entry, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, integer);
                break;
            case "ApproximationMemory":
                instruction = builder.Havoc(entry, site, IrHavocKind.Memory, IrHavocOrigin.Approximation);
                break;
            case "Throw":
                var exceptional = builder.CreateBlock("exceptional");
                builder.ExceptionalExit(exceptional, factory.CreateOperation("exceptional-exit"));
                instruction = builder.Throw(entry, site, IrExceptionKind.Overflow, exceptional);
                terminal = true;
                break;
            case "ExceptionalExit":
                instruction = builder.ExceptionalExit(entry, site);
                terminal = true;
                break;
            case "Branch":
                var left = builder.CreateBlock("left");
                var right = builder.CreateBlock("right");
                builder.Return(left, factory.CreateOperation("left-return"));
                builder.Return(right, factory.CreateOperation("right-return"));
                instruction = builder.Branch(entry, site, factory.Boolean(true), left, right);
                terminal = true;
                break;
            case "Goto":
                var destination = builder.CreateBlock("destination");
                builder.Return(destination, factory.CreateOperation("destination-return"));
                instruction = builder.Goto(entry, site, destination);
                terminal = true;
                break;
            case "Return":
                instruction = builder.Return(entry, site, factory.Integer(7));
                terminal = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
        if (!terminal)
        { builder.Return(entry, factory.CreateOperation("return")); }
        var program = builder.Build();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(program.Factory, Is.SameAs(factory));
            Assert.That(program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)), Does.Contain(instruction));
        }
        return (factory, instruction);
    }
}
