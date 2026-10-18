using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeSequenceLocalAdmissionTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task LegalSequenceLocalDefinitionsRespectTheNativeModuleDomain(bool nested, bool havoc)
    {
        await CheckLocalDefinition("integer", nested, havoc);
    }

    [TestCase("boolean", false, false)]
    [TestCase("boolean", false, true)]
    [TestCase("boolean", true, false)]
    [TestCase("boolean", true, true)]
    [TestCase("reference", false, false)]
    [TestCase("reference", false, true)]
    [TestCase("reference", true, false)]
    [TestCase("reference", true, true)]
    [TestCase("string", false, false)]
    [TestCase("string", false, true)]
    [TestCase("string", true, false)]
    [TestCase("string", true, true)]
    public async Task RemainingLegalSequenceLeafTypesKeepTheSameLocalAdmissionBoundary(string leaf, bool nested, bool havoc)
    {
        await CheckLocalDefinition(leaf, nested, havoc);
    }

    private static async Task CheckLocalDefinition(string leaf, bool nested, bool havoc)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var element = leaf switch
        {
            "integer" => factory.IntegerType,
            "boolean" => factory.BooleanType,
            "reference" => factory.ObjectType,
            "string" => factory.StringType,
            _ => throw new InvalidOperationException("Undefined fixture leaf.")
        };
        var flat = factory.GetOrCreateSequenceType(element);
        var type = nested ? factory.GetOrCreateSequenceType(flat) : flat;
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var site = factory.CreateOperation("sequence-local-definition");
        var local = factory.CreateVariable("local", type);
        if (havoc)
        { builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, local); }
        else
        { builder.Assign(block, site, local, factory.Null(type)); }
        builder.Return(block, site);
        var program = builder.Build();
        Assert.That(program.GetBlock(block).Instructions.First(),
            havoc ? Is.TypeOf<IrHavocInstruction>() : Is.TypeOf<IrAssignInstruction>());
        var candidate = new PassiveCallableCandidate("sequence-local-admission", program, [], null, [], []);
        var accepted = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.EqualTo(!nested));
            Assert.That(plan == null, Is.EqualTo(nested));
            Assert.That(failure, Is.EqualTo(nested ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        }
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate,
            WorkerEffectCapabilitySet.None, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, nested ? Is.Null : Is.TypeOf<ProvenOutcome>());
            Assert.That(result.Reason, Is.EqualTo(nested ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
            Assert.That(result.LockWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
            Assert.That(result.AllocationWitness, Is.Null);
        }
    }
}
