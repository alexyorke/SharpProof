using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataConditionalStackMergeCoverageTests
{
    [TestCase(true, true)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task RealIntegerConditionalMergeMatchesNativeReplayAndPublicClaim(
        bool equalToZero, bool matchingClaim)
    {
        var expression = equalToZero ? "(value == 0 ? value : 1) + 2" : "(value != 0 ? value : 1) + 2";
        var expectedClaim = equalToZero
            ? "(value == 0 ? Contract.Result<int>() == 2 : Contract.Result<int>() == 3)"
            : "(value == 0 ? Contract.Result<int>() == 3 : Contract.Result<int>() == value + 2)";
        var claim = matchingClaim ? expectedClaim : "Contract.Result<int>() == 100";
        using var subject = new MetadataTestSubject(
            $"public static class Library {{ public static int Target(int value) => {expression}; }}",
            $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value >= -2 && value <= 2);
                    Contract.Ensures({{claim}});
                    return Library.Target(value);
                }
            }
            """);
        TestCompilation.AssertNoErrors(subject.Compilation);
        Assert.That(subject.Image, Is.Not.Empty);
        Assert.That(subject.Method.GetMethodBody()!.GetILAsByteArray(), Is.Not.Empty);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, subject.Compilation.Assembly), Is.False);
        var decoded = new CompilerTotalIlBodyProvider(subject.Compilation, null).Resolve(method, CancellationToken.None);
        Assert.That(decoded, Is.Not.Null);
        var instructions = decoded!.Instructions;
        Assert.That(instructions.Any(instruction => instruction.Code is "Brtrue" or "Brfalse"), Is.True);
        var merge = Enumerable.Range(0, instructions.Length)
            .Single(index => instructions[index].Code == "Ldc_i4" && instructions[index].Operand == 2);
        Assert.That(instructions[merge + 1].Code, Is.EqualTo("Add"));
        var incoming = Enumerable.Range(0, instructions.Length)
            .Where(index => TotalIlStack.Successors(instructions, index).Contains(merge)).ToArray();
        Assert.That(incoming, Has.Length.EqualTo(2));
        var armValues = incoming.Select(index => instructions[index].Code == "Br" ? instructions[index - 1] : instructions[index]).ToArray();
        Assert.That(armValues.Count(instruction => instruction.Code == "Ldarg" && instruction.Operand == 0), Is.EqualTo(1));
        Assert.That(armValues.Count(instruction => instruction.Code == "Ldc_i4" && instruction.Operand == 1), Is.EqualTo(1));

        var artifact = subject.CreateArtifact();
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        var publicClaim = artifact.Manifest.Claims.Single();
        var prepared = artifact.Callables.Single();
        Assert.That(prepared.CallableId, Is.EqualTo(publicClaim.CallableId));
        Assert.That(prepared.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(prepared.Total, Is.Not.Null);
        Assert.That(prepared.Total!.IsBodyAbstraction, Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single().Total;
        Assert.That(total, Is.Not.Null);
        var factory = total!.Program.Factory;
        var entry = total.Parameters.Single().Entry;
        var entryType = factory.GetVariableInfo(entry).Type;
        int[] inputs = [-1, 0, 1, 2];
        int[] expected = equalToZero ? [3, 2, 3, 3] : [1, 3, 3, 4];
        for (var index = 0; index < inputs.Length; index++)
        {
            Assert.That(subject.Invoke(inputs[index]), Is.EqualTo(expected[index]));
            Assert.That(subject.InvokeRoot(inputs[index]), Is.EqualTo(expected[index]));
            var value = factory.CreateIntegerValue(entryType, inputs[index]);
            var execution = new IrProgramInterpreter(factory).Execute(total.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = value });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected[index])));
        }
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.ClaimId, Is.EqualTo(publicClaim.ClaimId));
        Assert.That(result.Outcome, Is.EqualTo(matchingClaim ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }
}
