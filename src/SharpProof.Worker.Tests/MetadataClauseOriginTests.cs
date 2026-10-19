using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataClauseOriginTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RoundtripPreservesPhysicalCallAndImmutableClauseOrigin(bool metadata)
    {
        var (body, references, origin) = Create(metadata);
        var dto = CompilerTotalCallableArtifactCodec.Encode(body)!;
        var json = JsonSerializer.Serialize(dto);
        if (!metadata)
        { Assert.That(json, Does.Not.Contain("MetadataClause")); }
        var detached = JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(json)!;
        var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBodyCore(body.CallableId, detached,
            TestContext.CurrentContext.CancellationToken, references);
        var row = decoded.CallPreconditions.Single();
        Assert.That(decoded.Program.Factory.GetOperationInfo(decoded.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrAssignInstruction>().Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Id == row.Instruction)).Operation).SourceSpan!.Document,
            Is.EqualTo("Caller.cs"));
        Assert.That(decoded.Program.Factory.GetOperationInfo(row.ClauseSite).SourceSpan == null, Is.EqualTo(metadata));
        Assert.That(row.MetadataClause == null, Is.EqualTo(!metadata));
        if (metadata)
        {
            Assert.That(row.MetadataClause!.ImageSha256, Is.EqualTo(origin.ImageSha256));
            Assert.That(row.MetadataClause.ModuleMvid, Is.EqualTo(origin.ModuleMvid));
            Assert.That(row.MetadataClause.AttributeToken, Is.EqualTo(origin.AttributeToken));
            Assert.That(row.MetadataClause.ValueBlob, Is.EqualTo(origin.ValueBlob));
        }
        if (metadata)
        {
            { detached = detached with { CallPreconditions = detached.CallPreconditions.SetItem(0, detached.CallPreconditions[0] with { MetadataClause = origin with { Minimum = -100 } }) }; }
            { detached = detached with { CallPreconditions = [] }; }
            references = references.SetItem(0, references[0] with { Modules = [] });
            Assert.That(row.MetadataClause!.Minimum, Is.EqualTo(1));
            Assert.That(row.MetadataClause.ValueBlob, Is.EqualTo(origin.ValueBlob));
        }
        else
        {
            Assert.That(CompilerTotalCallableArtifactCodec.TryCreateSourceMarkerName("Library.Target", 0,
                new("Caller.cs", 10, 8), new("Library.cs", 20, 5), out var name), Is.True);
            Assert.That(name, Is.EqualTo("$sharpproof.requires:v1:TGlicmFyeS5UYXJnZXQ=:0:Q2FsbGVyLmNz:10:8:TGlicmFyeS5jcw==:20:5"));
        }
    }

    [TestCase("image")]
    [TestCase("mvid")]
    [TestCase("module")]
    [TestCase("assembly")]
    [TestCase("method")]
    [TestCase("parameter")]
    [TestCase("attribute")]
    [TestCase("constructor")]
    [TestCase("ordinal")]
    [TestCase("sequence")]
    [TestCase("kind")]
    [TestCase("minimum")]
    [TestCase("maximum")]
    [TestCase("signature")]
    [TestCase("blob")]
    [TestCase("oversized")]
    [TestCase("missing-origin")]
    [TestCase("source-span")]
    [TestCase("missing-owner")]
    [TestCase("wrong-owner")]
    [TestCase("remove-row")]
    [TestCase("duplicate-row")]
    [TestCase("wrong-table")]
    public void CorruptOrDetachedMetadataTransportRejects(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        var (body, references, origin) = Create(true);
        var dto = CompilerTotalCallableArtifactCodec.Encode(body)!;
        _ = CompilerTotalCallableArtifactCodec.DecodeShadowBodyCore(body.CallableId, dto, TestContext.CurrentContext.CancellationToken, references);
        var row = dto.CallPreconditions.Single();
        {
            row = row with
            {
                MetadataClause = mutation switch
                {
                    "image" => origin with { ImageSha256 = new string('0', 64) },
                    "mvid" => origin with { ModuleMvid = Guid.Empty.ToString("D") },
                    "module" => origin with { ModuleName = "other.dll" },
                    "assembly" => origin with { AssemblyIdentity = "other" },
                    "method" => origin with { MethodToken = origin.MethodToken + 1 },
                    "parameter" => origin with { ParameterToken = origin.ParameterToken + 1 },
                    "attribute" => origin with { AttributeToken = origin.AttributeToken + 1 },
                    "constructor" => origin with { ConstructorToken = origin.ConstructorToken + 1 },
                    "ordinal" => origin with { ParameterOrdinal = 128 },
                    "sequence" => origin with { ParameterSequence = 0 },
                    "kind" => origin with { Kind = "Positive" },
                    "minimum" => origin with { Minimum = 2 },
                    "maximum" => origin with { Maximum = 9 },
                    "signature" => origin with { ConstructorSignature = [0x20, 0, 1] },
                    "blob" => origin with { ValueBlob = [1] },
                    "oversized" => origin with { ValueBlob = ImmutableArray.CreateRange(Enumerable.Repeat((byte)0, 65_536)) },
                    "missing-origin" => null,
                    "wrong-table" => origin with { MethodToken = 0x08000001 },
                    _ => origin
                }
            };
            dto = dto with { CallPreconditions = dto.CallPreconditions.ReplaceReference(dto.CallPreconditions.Single(), row) };
        }
        switch (mutation)
        {
            case "source-span":
                { dto = dto with { Graph = dto.Graph with { Operations = dto.Graph.Operations.SetItem(row.ClauseSite, dto.Graph.Operations[row.ClauseSite] with { SourceSpan = new() { Document = "Fake.cs", Start = 0, Length = 1 } }) } }; }
                break;
            case "missing-owner":
                references = [];
                break;
            case "wrong-owner":
                references = [.. references.Select(reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
                    reference with { Modules = [.. reference.Modules.Select(module => module is null ? throw new ArgumentNullException(nameof(module)) :
                        module with { Sha256 = new string('0', 64) })] })];
                break;
            case "remove-row":
                { dto = dto with { CallPreconditions = [] }; }
                break;
            case "duplicate-row":
                { dto = dto with { CallPreconditions = [row, row] }; }
                break;
        }
        Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBodyCore(body.CallableId, dto, TestContext.CurrentContext.CancellationToken, references)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataRequiresExplicitOwnershipContext(bool presentDefault)
    {
        var (body, _, _) = Create(true);
        var dto = CompilerTotalCallableArtifactCodec.Encode(body)!;
        ImmutableArray<CompilerReferenceSnapshot>? references = presentDefault
            ? default(ImmutableArray<CompilerReferenceSnapshot>)
            : null;
        Assert.That(references.HasValue, Is.EqualTo(presentDefault));
        Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBodyCore(
                body.CallableId, dto, TestContext.CurrentContext.CancellationToken, references)));
    }

    [Test]
    public void MetadataEvidenceDigestRejectsMissingOrigin()
    {
        var (_, _, origin) = Create(true);
        Assert.That(CompilerTotalCallableArtifactCodec.TryGetMetadataEvidenceDigest(
            origin, out var validDigest), Is.True);
        Assert.That(validDigest, Has.Length.EqualTo(64));
        Assert.That(CompilerTotalCallableArtifactCodec.TryGetMetadataEvidenceDigest(
            null!, out var invalidDigest), Is.False);
        Assert.That(invalidDigest, Is.Empty);
    }

    private static (CompilerTotalCallablePreparation Body, ImmutableArray<CompilerReferenceSnapshot> References,
        CompilerMetadataClauseOrigin Origin) Create(bool metadata)
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([InRange(1, 10)] int value) => value; }",
            "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var il = new CompilerTotalIlBodyProvider(subject.Compilation, null).Resolve(method, TestContext.CurrentContext.CancellationToken)!;
        Assert.That(il, Is.Not.Null);
        var evidence = il.ParameterAttributes.Single();
        var origin = new CompilerMetadataClauseOrigin(method.ContainingAssembly.Identity.ToString(), il.ImageSha256, il.Module,
            il.ModuleMvid, evidence.MethodToken, evidence.ParameterOrdinal, evidence.ParameterSequence, evidence.ParameterToken,
            evidence.AttributeToken, evidence.ConstructorToken, evidence.AttributeIdentity, evidence.ConstructorIdentity,
            evidence.Kind, evidence.Minimum, evidence.Maximum, evidence.ConstructorSignature, evidence.ValueBlob);
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        var entry = factory.CreateVariable("entry:0", type);
        var current = factory.CreateVariable("current:0", type);
        var old = factory.CreateVariable("old:0", type);
        var result = factory.CreateVariable("result", type);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("source metadata call", new("Caller.cs", 10, 8));
        var clauseSite = factory.CreateOperation("metadata declaration", metadata ? null : new("Library.cs", 20, 5));
        var prologue = factory.CreateOperation("canonical initialization");
        builder.Assign(block, prologue, current, factory.Variable(entry));
        builder.Assign(block, prologue, old, factory.Variable(entry));
        var context = new TotalLoweringContext(factory, method);
        var bound = new ContractBinder(subject.Compilation, factory).BindTotalMetadataRequires(context, cancellationToken: TestContext.CurrentContext.CancellationToken).Clauses.Single();
        var value = IrSubstitution.Substitute(factory, bound.Value,
            ImmutableDictionary<IrVarId, IrTerm>.Empty.SetItem(context.Parameters[0].Entry, factory.Variable(entry)));
        string name;
        Assert.That(metadata ? CompilerTotalCallableArtifactCodec.TryCreateMetadataMarkerName("Library.Target", 0,
            factory.GetOperationInfo(site).SourceSpan, origin, out name) :
            CompilerTotalCallableArtifactCodec.TryCreateSourceMarkerName("Library.Target", 0, factory.GetOperationInfo(site).SourceSpan,
                factory.GetOperationInfo(clauseSite).SourceSpan, out name), Is.True);
        var marker = builder.Assign(block, site, factory.CreateVariable(name, factory.BooleanType),
            factory.Binary(IrBinaryOperator.AndAlso, factory.Boolean(true), value));
        builder.Assign(block, site, result, factory.Variable(current));
        builder.Return(block, site, factory.Variable(result));
        return (new("M:Caller.Root", builder.Build(), [new(entry, current, old)], result, [])
        {
            CallPreconditions = [new(marker.Id, "Library.Target", 0, clauseSite, value, factory.Boolean(true), metadata ? origin : null)]
        }, references, origin);
    }
}
