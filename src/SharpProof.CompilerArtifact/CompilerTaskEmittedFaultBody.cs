using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal enum CompilerTaskEmittedArrayElementKind { FrameworkInt32 }

internal sealed record CompilerTaskEmittedArrayTypeOperand(int IlOffset, int TypeToken,
    CompilerTaskEmittedArrayElementKind ElementKind, string AssemblyIdentity)
{
    public string AssemblyIdentity { get; init; } = RepositoryParameterGuard.NotNull(AssemblyIdentity, nameof(AssemblyIdentity));
}

internal sealed record CompilerTaskEmittedFaultBody(int MethodToken, ImmutableArray<byte> Code,
    string CodeSha256, ImmutableArray<CompilerTaskEmittedArrayTypeOperand> ArrayTypeOperands)
{
    public ImmutableArray<byte> Code { get; init; } = Code.IsDefault
        ? throw new ArgumentException("Emitted code must be initialized.", nameof(Code)) : Code;
    public string CodeSha256 { get; init; } = RepositoryParameterGuard.NotNull(CodeSha256, nameof(CodeSha256));
    public ImmutableArray<CompilerTaskEmittedArrayTypeOperand> ArrayTypeOperands { get; init; } = ArrayTypeOperands.IsDefault
        ? throw new ArgumentException("Array type operands must be initialized.", nameof(ArrayTypeOperands)) : ArrayTypeOperands;
}

// Complete emitted bytes from the same owned PE as the source/forwarder facts.
// Hashes check identity/corruption inside the trusted compiler envelope; they
// do not authenticate arbitrary supplied bytes independently of that envelope.
internal sealed record CompilerTaskEmittedFaultBodyInventory(string RootCallableId, string ModuleSha256,
    string CompilerIdentity, string FrameworkAssemblyIdentity, ImmutableArray<CompilerTaskEmittedFaultBody> Bodies)
{
    public string RootCallableId { get; init; } = RepositoryParameterGuard.NotNull(RootCallableId, nameof(RootCallableId));
    public string ModuleSha256 { get; init; } = RepositoryParameterGuard.NotNull(ModuleSha256, nameof(ModuleSha256));
    public string CompilerIdentity { get; init; } = RepositoryParameterGuard.NotNull(CompilerIdentity, nameof(CompilerIdentity));
    public string FrameworkAssemblyIdentity { get; init; } = RepositoryParameterGuard.NotNull(FrameworkAssemblyIdentity, nameof(FrameworkAssemblyIdentity));
    public ImmutableArray<CompilerTaskEmittedFaultBody> Bodies { get; init; } = Bodies.IsDefault
        ? throw new ArgumentException("Emitted bodies must be initialized.", nameof(Bodies)) : Bodies;
}

internal sealed record CompilerTaskEmittedFaultBodyCapture(CompilerTaskEmittedFaultBodyInventory? Inventory, string? UnknownReason);

internal sealed record CompilerTaskDecodedFaultBody(CompilerTaskEmittedFaultBody Body,
    ImmutableArray<CompilerTaskPrimitiveIlInstruction> Instructions)
{
    public CompilerTaskEmittedFaultBody Body { get; init; } = RepositoryParameterGuard.NotNull(Body, nameof(Body));
    public ImmutableArray<CompilerTaskPrimitiveIlInstruction> Instructions { get; init; } = Instructions.IsDefault
        ? throw new ArgumentException("Decoded instructions must be initialized.", nameof(Instructions)) : Instructions;
}

internal static class CompilerTaskEmittedFaultBodyOwnership
{
    internal static string? Validate(CompilerTaskEmittedFaultBodyInventory inventory, CompilerTaskCarrierShapes shapes,
        string framework, CancellationToken cancellation)
    {
        return ValidateAndDecode(inventory, shapes, framework, cancellation, out _);
    }

    internal static string? ValidateAndDecode(CompilerTaskEmittedFaultBodyInventory inventory, CompilerTaskCarrierShapes shapes,
        string framework, CancellationToken cancellation, out ImmutableArray<CompilerTaskDecodedFaultBody> decodedBodies)
    {
        decodedBodies = [];
        inventory = ArgumentNullGuard.NotNull(inventory, nameof(inventory));
        shapes = ArgumentNullGuard.NotNull(shapes, nameof(shapes));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        cancellation.ThrowIfCancellationRequested();
        if (inventory.RootCallableId != shapes.RootCallableId || inventory.ModuleSha256 != shapes.ModuleSha256 ||
            inventory.CompilerIdentity != shapes.CompilerIdentity || inventory.FrameworkAssemblyIdentity != framework ||
            !WorkerProtocolJson.IsSha256(inventory.ModuleSha256) || string.IsNullOrWhiteSpace(inventory.CompilerIdentity) ||
            inventory.CompilerIdentity.Length > 512 || string.IsNullOrWhiteSpace(framework) ||
            inventory.Bodies.IsDefault || inventory.Bodies.Length is < 1 or > 16)
        { return "TaskEmittedFaultBodyEvidenceInvalid"; }
        if (shapes.SourceBodies is not { UnknownReason: null, Inventory: { } source } ||
            shapes.CallbackActivations is not { UnknownReason: null, Activations: { } callbacks } ||
            source.RootCallableId != shapes.RootCallableId || source.ModuleSha256 != shapes.ModuleSha256 ||
            callbacks.RootCallableId != shapes.RootCallableId || callbacks.ModuleSha256 != shapes.ModuleSha256 ||
            source.MethodTokens.IsDefault || source.MethodTokens.Length is < 1 or > 16 || callbacks.Forwarders.IsDefault)
        { return "TaskEmittedFaultBodyCoverageInvalid"; }
        var sourceMethods = new HashSet<int>();
        foreach (var token in source.MethodTokens)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!MethodToken(token) || !sourceMethods.Add(token))
            { return "TaskEmittedFaultBodyCoverageInvalid"; }
        }
        var targets = new HashSet<int>();
        foreach (var forwarder in callbacks.Forwarders)
        {
            cancellation.ThrowIfCancellationRequested();
            if (forwarder is null || !MethodToken(forwarder.MethodToken) || !MethodToken(forwarder.ChildMethodToken))
            { return "TaskEmittedFaultBodyCoverageInvalid"; }
            targets.Add(forwarder.ChildMethodToken);
        }
        if (!sourceMethods.SetEquals(targets))
        { return "TaskEmittedFaultBodyCoverageInvalid"; }
        var decoded = ImmutableArray.CreateBuilder<CompilerTaskDecodedFaultBody>(inventory.Bodies.Length);
        var methods = new HashSet<int>();
        var previous = 0;
        foreach (var body in inventory.Bodies)
        {
            cancellation.ThrowIfCancellationRequested();
            if (body is null || !MethodToken(body.MethodToken) || body.MethodToken <= previous || !methods.Add(body.MethodToken) ||
                body.Code.IsDefault || body.Code.Length is < 1 or > CompilerTaskPrimitiveIlScanner.MaximumCodeBytes ||
                !WorkerProtocolJson.IsSha256(body.CodeSha256) || body.CodeSha256 != WorkerProtocolJson.ComputeSha256(body.Code) ||
                body.ArrayTypeOperands.IsDefault || body.ArrayTypeOperands.Length > body.Code.Length)
            { return "TaskEmittedFaultBodyEvidenceInvalid"; }
            previous = body.MethodToken;
            var scan = CompilerTaskPrimitiveIlScanner.Scan(body.Code, cancellation);
            if (scan.Instructions is not { } instructions)
            { return scan.UnknownReason; }
            var arrayIndex = 0;
            foreach (var instruction in instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (instruction.OpCode != 0x8d)
                { continue; }
                if (arrayIndex >= body.ArrayTypeOperands.Length || body.ArrayTypeOperands[arrayIndex++] is not { } operand ||
                    operand.IlOffset != instruction.Offset || operand.TypeToken != instruction.Operand ||
                    operand.TypeToken >> 24 != 1 || (operand.TypeToken & 0x00ffffff) == 0 ||
                    operand.ElementKind != CompilerTaskEmittedArrayElementKind.FrameworkInt32 || operand.AssemblyIdentity != framework)
                { return "TaskEmittedFaultBodyEvidenceInvalid"; }
            }
            if (arrayIndex != body.ArrayTypeOperands.Length)
            { return "TaskEmittedFaultBodyEvidenceInvalid"; }
            decoded.Add(new(body, instructions));
        }
        if (!methods.SetEquals(sourceMethods))
        { return "TaskEmittedFaultBodyCoverageInvalid"; }
        decodedBodies = decoded.MoveToImmutable();
        return null;
    }

    private static bool MethodToken(int token)
    { return token >> 24 == 6 && (token & 0x00ffffff) != 0; }
}
