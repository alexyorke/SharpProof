using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ResourceAdmissionCompilerArtifactCoverageTests
{
    [Test]
    public void ForkJoinSerializationRejectsAMissingChildBeforePublishingPayload()
    {
        var root = new CompilerTotalEntryPreparation("root", new IrFactory(IrExecutionSemantics.Total), [], []);
        var projection = new CompilerForkJoinProjection("root", "framework", [], [null!, null!]);
        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerForkJoinProjectionCodec.Encode(projection, root, TestContext.CurrentContext.CancellationToken)));
        Assert.That(exception!.Message, Is.EqualTo("Fork-join child is malformed."));
    }

    [Test]
    public void ResourceEvidenceCannotContainBothAProofAndAnUnknownReason()
    {
        var preparation = new CompilerCallablePreparation(new WorkerCallableManifestEntry(), WorkerClaimReason.None)
        { ResourceProgram = ResourceProgram.Zero, ResourceUnknownReason = "OpaqueCall" };
        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerLoweredArtifact.Encode(preparation, TestContext.CurrentContext.CancellationToken)));
        Assert.That(exception!.Message, Is.EqualTo("Resource proof and gap are mutually exclusive."));
    }

    [Test]
    public void AStructurallyBoundedProgramThatExceedsWireBudgetBecomesAnExplicitGap()
    {
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength,
            new string('n', ResourceExpression.MaximumTextLength), int.MaxValue));
        var program = ResourceProgram.Array(input);
        for (var depth = 0; depth < 6; depth++)
        { program = ResourceProgram.Sequence(program, program); }
        Assert.That(program.NodeCount, Is.LessThan(ResourceExpression.MaximumNodes));
        Assert.That(program.Depth, Is.LessThan(ResourceExpression.MaximumDepth));
        var preparation = new CompilerCallablePreparation(new WorkerCallableManifestEntry(), WorkerClaimReason.None)
        { ResourceProgram = program };
        var artifact = CompilerLoweredArtifact.Encode(preparation, TestContext.CurrentContext.CancellationToken);
        Assert.That(artifact.ResourceProgram, Is.Null);
        Assert.That(artifact.ResourceUnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("contains space")]
    [TestCase("contains\nnewline")]
    public void AClaimWithNoProofRequiresACanonicalGap(string? reason)
    {
        var artifact = new CompilerCallableArtifact { ResourceUnknownReason = reason };
        AssertResourceRejected(artifact, "Resource proof gap is missing or malformed.");
    }

    [Test]
    public void AResourceProofRequiresItsTotalCallableOwner()
    {
        var artifact = new CompilerCallableArtifact
        { ResourceProgram = ResourceProgramCodec.Encode(ResourceProgram.Zero, TestContext.CurrentContext.CancellationToken) };
        AssertResourceRejected(artifact, "Resource proof ownership is invalid.");
    }

    [TestCase("invalid-source")]
    [TestCase("duplicate-source")]
    [TestCase("null-forwarder")]
    [TestCase("invalid-forwarder")]
    [TestCase("invalid-target")]
    [TestCase("missing-source")]
    public void EmittedBodyCoverageMustMatchOwnedSourceAndCallbackMethodTokens(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        const string root = "root";
        const string framework = "framework";
        var module = new string('a', 64);
        ImmutableArray<byte> code = [0x2a];
        var body = new CompilerTaskEmittedFaultBody(0x06000001, code, WorkerProtocolJson.ComputeSha256(code), []);
        var inventory = new CompilerTaskEmittedFaultBodyInventory(root, module, "compiler", framework, [body]);
        var source = new CompilerTaskSourceBodyInventory(root, module, [0x06000001]);
        var callbacks = new CompilerTaskCallbackActivation(root, module, [new(0x06000002, 0x06000001)]);
        var shapes = new CompilerTaskCarrierShapes(root, module, "compiler", 1, [], null)
        { SourceBodies = new(source, null), CallbackActivations = new(callbacks, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.Validate(inventory, shapes, framework, cancellation), Is.Null);
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.ValidateAndDecode(inventory, shapes, framework, cancellation, out var decoded), Is.Null);
        Assert.That(decoded.Length, Is.EqualTo(1));
        Assert.That(decoded[0].Body, Is.SameAs(body));
        Assert.That(decoded[0].Instructions.Single().OpCode, Is.EqualTo(0x2a));
        switch (corruption)
        {
            case "invalid-source":
                source = source with { MethodTokens = [0x02000001] };
                break;
            case "duplicate-source":
                source = source with { MethodTokens = [0x06000001, 0x06000001] };
                break;
            case "null-forwarder":
                callbacks = callbacks with { Forwarders = [null!] };
                break;
            case "invalid-forwarder":
                callbacks = callbacks with { Forwarders = [new(0x02000001, 0x06000001)] };
                break;
            case "invalid-target":
                callbacks = callbacks with { Forwarders = [new(0x06000002, 0x02000001)] };
                break;
            case "missing-source":
                callbacks = callbacks with { Forwarders = [new(0x06000002, 0x06000003)] };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        shapes = shapes with { SourceBodies = new(source, null), CallbackActivations = new(callbacks, null) };
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.Validate(inventory, shapes, framework, cancellation),
            Is.EqualTo("TaskEmittedFaultBodyCoverageInvalid"));
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.ValidateAndDecode(inventory, shapes, framework, cancellation, out var rejected),
            Is.EqualTo("TaskEmittedFaultBodyCoverageInvalid"));
        Assert.That(rejected.IsDefault, Is.False);
        Assert.That(rejected, Is.Empty);
    }

    [Test]
    public void ALaterInvalidBodyCannotLeakEarlierDecodedInstructions()
    {
        var module = new string('a', 64);
        ImmutableArray<byte> code = [0x2a];
        var first = new CompilerTaskEmittedFaultBody(0x06000001, code, WorkerProtocolJson.ComputeSha256(code), []);
        var second = first with { MethodToken = 0x06000002, CodeSha256 = new string('b', 64) };
        var inventory = new CompilerTaskEmittedFaultBodyInventory("root", module, "compiler", "framework", [first, second]);
        var shapes = new CompilerTaskCarrierShapes("root", module, "compiler", 2, [], null)
        {
            SourceBodies = new(new("root", module, [first.MethodToken, second.MethodToken]), null),
            CallbackActivations = new(new("root", module,
                [new(0x06000003, first.MethodToken), new(0x06000004, second.MethodToken)]), null)
        };
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.ValidateAndDecode(inventory, shapes, "framework",
            TestContext.CurrentContext.CancellationToken, out var decoded), Is.EqualTo("TaskEmittedFaultBodyEvidenceInvalid"));
        Assert.That(decoded.IsDefault, Is.False);
        Assert.That(decoded, Is.Empty);
    }

    [Test]
    public void DecodedBodyContextsRejectAnUninitializedInstructionInventory()
    {
        ImmutableArray<byte> code = [0x2a];
        var body = new CompilerTaskEmittedFaultBody(0x06000001, code, WorkerProtocolJson.ComputeSha256(code), []);
        var exception = Assert.Throws<ArgumentException>((Action)(() => new CompilerTaskDecodedFaultBody(body, default)));
        Assert.That(exception!.ParamName, Is.EqualTo("Instructions"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingEmittedCodeCannotBecomeAnEmptySuccessfulScan(bool uninitialized)
    {
        ImmutableArray<byte> code = uninitialized ? default : [];
        var scan = CompilerTaskPrimitiveIlScanner.Scan(code, TestContext.CurrentContext.CancellationToken);
        Assert.That(scan.Instructions, Is.Null);
        Assert.That(scan.UnknownReason, Is.EqualTo("TaskEmittedFaultBodyInstructionUnsupported"));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        canceled.Cancel();
        Action canceledScan = () => { _ = CompilerTaskPrimitiveIlScanner.Scan(code, canceled.Token); };
        var exception = Assert.Throws<OperationCanceledException>(canceledScan);
        Assert.That(exception!.CancellationToken, Is.EqualTo(canceled.Token));
    }

    [TestCase(0x09)]
    [TestCase(0x0b)]
    [TestCase(0x0c)]
    [TestCase(0x0e)]
    public void WideIlVariableOperandsAreDecodedLittleEndianWithoutTruncation(int opcode)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var scan = CompilerTaskPrimitiveIlScanner.Scan([0xfe, checked((byte)opcode), 0x34, 0x12, 0x2a], cancellation);
        Assert.That(scan.UnknownReason, Is.Null);
        var instructions = scan.Instructions!.Value;
        Assert.That(instructions.Length, Is.EqualTo(2));
        Assert.That(instructions[0].OpCode, Is.EqualTo(0xfe00 | opcode));
        Assert.That(instructions[0].Operand, Is.EqualTo(0x1234));
        Assert.That(instructions[0].NextOffset, Is.EqualTo(4));
        Assert.That(instructions[1].Offset, Is.EqualTo(4));
        var truncated = CompilerTaskPrimitiveIlScanner.Scan([0xfe, checked((byte)opcode), 0x34], cancellation);
        Assert.That(truncated.Instructions, Is.Null);
        Assert.That(truncated.UnknownReason, Is.EqualTo("TaskEmittedFaultBodyInstructionUnsupported"));
    }

    private static void AssertResourceRejected(CompilerCallableArtifact artifact, string message)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(message);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var claim = new WorkerClaimManifestEntry
        {
            EffectContractKind = WorkerEffectContractKind.ResourceUpperBound,
            ResourceUpperBound = new WorkerResourceConstraint
            { UpperBound = ResourceExpressionCodec.Encode(ResourceExpression.Zero, cancellation) }
        };
        var decode = typeof(CompilerLoweredArtifact).GetMethod("DecodeResource", BindingFlags.NonPublic | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>((Action)(() => decode.Invoke(null,
            [artifact, new WorkerCallableManifestEntry { CallableId = "owned" }, ImmutableArray.Create(claim), null, cancellation])));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>());
        Assert.That(exception.InnerException!.Message, Is.EqualTo(message));
    }
}
