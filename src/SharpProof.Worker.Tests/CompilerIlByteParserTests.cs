using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

// These byte arrays are lexical parser inputs, not PE images or CLR program evidence.
[TestFixture]
public sealed class CompilerIlByteParserTests
{
    private static readonly ImmutableArray<(ImmutableArray<byte> Bytes, string Code, long Operand)> MetadataCases =
    [
        ([0x7b, 0x01, 0x00, 0x00, 0x04], "Ldfld", 0x04000001),
        ([0x8d, 0x01, 0x00, 0x00, 0x02], "Newarr", 0x02000001),
        ([0xd0, 0x01, 0x00, 0x00, 0x02], "Ldtoken", 0x02000001),
        ([0x29, 0x01, 0x00, 0x00, 0x11], "Calli", 0x11000001),
        ([0x72, 0x01, 0x00, 0x00, 0x70], "Ldstr", 0x70000001),
        ([0x22, 0x00, 0x00, 0x80, 0x3f], "Ldc_r4", 0x3f800000),
        ([0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xf0, 0x3f], "Ldc_r8", 0x3ff0000000000000L),
        ([0xfe, 0x16, 0x01, 0x00, 0x00, 0x02], "Constrained", 0x02000001)
    ];

    [Test]
    public void MetadataOperandsAreLexicalFactsWithoutWideningScalarAdmission()
    {
        foreach (var example in MetadataCases)
        {
            var cursor = 0;
            Assert.That(CompilerScalarIlDecoder.TryReadMetadataInstruction(example.Bytes.AsSpan(), ref cursor, out var instruction), Is.True);
            Assert.That(cursor, Is.EqualTo(example.Bytes.Length));
            Assert.That(instruction.Offset, Is.Zero);
            Assert.That(instruction.RawCode, Is.EqualTo(example.Code));
            Assert.That(instruction.Operand, Is.EqualTo(example.Operand));
            Assert.That(instruction.TargetByteOffset, Is.EqualTo(-1));
            cursor = 0;
            Assert.That(CompilerScalarIlDecoder.TryReadInstruction(example.Bytes.AsSpan(), ref cursor, out _), Is.False);
        }
    }

    [Test]
    public void TruncatedMetadataOperandCannotProduceAnInstruction()
    {
        ImmutableArray<byte> code = [0x7b, 0x01];
        var cursor = 0;
        Assert.Throws<BadImageFormatException>((Action)(() =>
            CompilerScalarIlDecoder.TryReadMetadataInstruction(code.AsSpan(), ref cursor, out _)));
        Assert.That(cursor, Is.EqualTo(1));
    }

    private static readonly ImmutableArray<(ImmutableArray<byte> Bytes, string Code, long Operand, int Cursor, int Target)> ScalarCases =
    [
        ([0x00], "Nop", 0, 1, -1),
        ([0x1f, 0x80], "Ldc_i4_s", -128, 2, -1),
        ([0x0e, 0xff], "Ldarg_s", 255, 2, -1),
        ([0xfe, 0x0c, 0x34, 0x12], "Ldloc", 4660, 4, -1),
        ([0x20, 0x78, 0x56, 0x34, 0x12], "Ldc_i4", 305419896, 5, -1),
        ([0x21, 0xef, 0xcd, 0xab, 0x89, 0x67, 0x45, 0x23, 0x01], "Ldc_i8", 0x0123456789abcdefL, 9, -1),
        ([0x28, 0x01, 0x00, 0x00, 0x06], "Call", 0x06000001, 5, -1),
        ([0x2b, 0x00, 0x2a], "Br_s", 0, 2, 2),
        ([0x38, 0x00, 0x00, 0x00, 0x00, 0x2a], "Br", 0, 5, 5)
    ];
    private static readonly ImmutableArray<byte> OffsetInput = [0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x2a];
    private static readonly ImmutableArray<byte> UnknownSingle = [0x24];
    private static readonly ImmutableArray<byte> KnownSingleNeighbor = [0x25];
    private static readonly ImmutableArray<byte> UnknownDouble = [0xfe, 0x08];
    private static readonly ImmutableArray<byte> KnownDoubleNeighbor = [0xfe, 0x09, 0x00, 0x00];
    private static readonly ImmutableArray<byte> StringOperand = [0x72, 0x00, 0x00, 0x00, 0x00];
    private static readonly ImmutableArray<byte> TruncatedPrefix = [0xfe];
    private static readonly ImmutableArray<byte> TruncatedInteger = [0x20, 0x01];
    private static readonly ImmutableArray<byte> EndTarget = [0x2b, 0x00];
    private static readonly ImmutableArray<byte> NegativeTarget = [0x2b, 0xfd, 0x2a];
    private static readonly ImmutableArray<byte> OverflowTarget = [0x38, 0xff, 0xff, 0xff, 0x7f];
    private static readonly ImmutableArray<byte> ForwardBoundary = [0x2b, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x2a];
    private static readonly ImmutableArray<byte> ForwardInterior = [0x2b, 0x01, 0x20, 0x00, 0x00, 0x00, 0x00, 0x2a];
    private static readonly ImmutableArray<byte> BackwardBoundary = [0x00, 0x2b, 0xfd];
    private static readonly ImmutableArray<(int Input, int Expected)> StringControls = [(112, 0), (120, 5), (63, -1)];

    [Test]
    public void SupportedOperandsRetainRawNamesValuesAndAbsoluteCursor()
    {
        foreach (var example in ScalarCases)
        {
            var cursor = 0;
            Assert.That(CompilerScalarIlDecoder.TryReadInstruction(example.Bytes.AsSpan(), ref cursor, out var instruction), Is.True);
            Assert.That(instruction.Offset, Is.Zero);
            Assert.That(instruction.RawCode, Is.EqualTo(example.Code));
            Assert.That(instruction.Operand, Is.EqualTo(example.Operand));
            Assert.That(instruction.TargetByteOffset, Is.EqualTo(example.Target));
            Assert.That(cursor, Is.EqualTo(example.Cursor));
        }
    }

    [Test]
    public void ReaderKeepsTheFullStreamOffsetWhenStartingAfterAnInstruction()
    {
        var cursor = 1;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(OffsetInput.AsSpan(), ref cursor, out var instruction), Is.True);
        Assert.That(instruction.Offset, Is.EqualTo(1));
        Assert.That(instruction.RawCode, Is.EqualTo("Ldc_i4"));
        Assert.That(instruction.Operand, Is.EqualTo(1));
        Assert.That(instruction.TargetByteOffset, Is.EqualTo(-1));
        Assert.That(cursor, Is.EqualTo(6));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownOpcodeIsRejectedWhileItsKnownNeighborIsRead(bool twoByte)
    {
        var unknown = twoByte ? UnknownDouble : UnknownSingle;
        var known = twoByte ? KnownDoubleNeighbor : KnownSingleNeighbor;
        var cursor = 0;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(unknown.AsSpan(), ref cursor, out _), Is.False);
        Assert.That(cursor, Is.EqualTo(unknown.Length));
        cursor = 0;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(known.AsSpan(), ref cursor, out var instruction), Is.True);
        Assert.That(instruction.RawCode, Is.EqualTo(twoByte ? "Ldarg" : "Dup"));
        Assert.That(cursor, Is.EqualTo(known.Length));
    }

    [Test]
    public void UnsupportedOperandCategoryStopsAfterTheOpcode()
    {
        var cursor = 0;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(StringOperand.AsSpan(), ref cursor, out _), Is.False);
        Assert.That(cursor, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TruncatedInstructionRetainsBlobReaderExceptionAndConsumedCursor(bool prefix)
    {
        var code = prefix ? TruncatedPrefix : TruncatedInteger;
        var cursor = 0;
        Assert.Throws<BadImageFormatException>((Action)(() =>
            CompilerScalarIlDecoder.TryReadInstruction(code.AsSpan(), ref cursor, out _)));
        Assert.That(cursor, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BranchTargetOutsideTheCodeIsRejectedBeforeNormalization(bool negative)
    {
        var code = negative ? NegativeTarget : EndTarget;
        var cursor = 0;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(code.AsSpan(), ref cursor, out _), Is.False);
        Assert.That(cursor, Is.EqualTo(2));
    }

    [Test]
    public void BranchTargetArithmeticKeepsCheckedOverflowAndConsumedCursor()
    {
        var cursor = 0;
        Assert.Throws<OverflowException>((Action)(() =>
            CompilerScalarIlDecoder.TryReadInstruction(OverflowTarget.AsSpan(), ref cursor, out _)));
        Assert.That(cursor, Is.EqualTo(5));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TargetNormalizerDistinguishesAnInstructionStartFromItsOperandInterior(bool interior)
    {
        var decoded = ReadUnboundSyntax(interior ? ForwardInterior : ForwardBoundary);
        var normalized = CompilerScalarIlDecoder.NormalizeTargets(decoded.ToBuilder());
        Assert.That(normalized.IsDefault, Is.EqualTo(interior));
        if (!interior)
        {
            Assert.That(normalized, Has.Length.EqualTo(3));
            Assert.That(normalized[0].Target, Is.EqualTo(1));
            Assert.That(normalized[0].Offset, Is.Zero);
            Assert.That(normalized[0].Code, Is.EqualTo("Br_s"));
            Assert.That(normalized[0].Operand, Is.Zero);
            Assert.That(normalized[1].Offset, Is.EqualTo(2));
            Assert.That(normalized[2].Offset, Is.EqualTo(7));
            Assert.That(normalized[1].Target, Is.EqualTo(-1));
            Assert.That(normalized.All(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Method == null)), Is.True);
        }
    }

    [Test]
    public void BackwardTargetResolvesToTheOriginalInstructionIndex()
    {
        var normalized = CompilerScalarIlDecoder.NormalizeTargets(ReadUnboundSyntax(BackwardBoundary).ToBuilder());
        Assert.That(normalized.IsDefault, Is.False);
        Assert.That(normalized, Has.Length.EqualTo(2));
        Assert.That(normalized[1].Offset, Is.EqualTo(1));
        Assert.That(normalized[1].Code, Is.EqualTo("Br_s"));
        Assert.That(normalized[1].Operand, Is.EqualTo(-3));
        Assert.That(normalized[1].Target, Is.Zero);
        Assert.That(normalized[1].Method, Is.Null);
    }

    [Test]
    public void GenuineCompiledStringLiteralExercisesTheProviderSharedFailureReturn()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => \"prefix\".IndexOf((char)value); }",
            "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var method = (IMethodSymbol)subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").Single();
        var assembly = method.ContainingAssembly;
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.MetadataToken, Is.EqualTo(subject.Method.MetadataToken));
        Assert.That(CompilerScalarIlDecoder.Candidate(method), Is.True);
        foreach (var control in StringControls)
        {
            Assert.That(subject.Method.Invoke(null, [control.Input]), Is.EqualTo(control.Expected));
            Assert.That(subject.RootMethod.Invoke(null, [control.Input]), Is.EqualTo(control.Expected));
        }

        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var captured = references.SelectMany(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.Modules)).Single(module =>
            module is null ? throw new System.ArgumentNullException(nameof(module)) : (Path.GetFullPath(module.Path) == Path.GetFullPath(subject.ImagePath)));
        Assert.That(captured.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(subject.Image.AsSpan()))));
        Assert.That(captured.SizeBytes, Is.EqualTo((long)subject.Image.Length));
        Assert.That(captured.Mvid, Is.EqualTo(subject.Method.Module.ModuleVersionId.ToString("D")));

        using var pe = new PEReader(subject.Image);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken));
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
        var code = body.GetILBytes()!.ToImmutableArray();
        Assert.That(code, Is.Not.Empty);
        Assert.That(code![0], Is.EqualTo((byte)ILOpCode.Ldstr), "Observe actual compiler output before claiming the shared parser rejection.");
        var cursor = 0;
        Assert.That(CompilerScalarIlDecoder.TryReadInstruction(code.AsSpan(), ref cursor, out _), Is.False);
        Assert.That(cursor, Is.EqualTo(1));
        Assert.That(CompilerScalarIlDecoder.Decode(body, metadata, assembly, method.ContainingModule.Name,
            TestContext.CurrentContext.CancellationToken).IsDefault, Is.True);
        Assert.Throws<OperationCanceledException>((Action)(() => CompilerScalarIlDecoder.Decode(
            body, metadata, assembly, method.ContainingModule.Name, new CancellationToken(true))));
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        Assert.That(provider.Resolve(method, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    // Parser records remain unbound. They are never wrapped in TotalIlBody or
    // supplied as compiler/worker authority; no method owner is fabricated.
    private static ImmutableArray<TotalIlInstruction> ReadUnboundSyntax(ImmutableArray<byte> code)
    {
        if (code.IsDefault)
        {
            throw new ArgumentException("Array must be initialized.", nameof(code));
        }
        var decoded = ImmutableArray.CreateBuilder<TotalIlInstruction>();
        var cursor = 0;
        while (cursor != code.Length)
        {
            Assert.That(CompilerScalarIlDecoder.TryReadInstruction(code.AsSpan(), ref cursor, out var instruction), Is.True);
            decoded.Add(new(instruction.Offset, instruction.RawCode, instruction.Operand, instruction.TargetByteOffset));
        }
        return decoded.ToImmutable();
    }
}
