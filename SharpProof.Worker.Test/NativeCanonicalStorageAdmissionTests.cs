using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeCanonicalStorageAdmissionTests
{
    [TestCase("assign", "entry", false)]
    [TestCase("assign", "current", true)]
    [TestCase("assign", "local", true)]
    [TestCase("allocate", "entry", false)]
    [TestCase("allocate", "current", true)]
    [TestCase("allocate", "local", true)]
    [TestCase("havoc", "entry", false)]
    [TestCase("havoc", "current", true)]
    [TestCase("havoc", "local", true)]
    [TestCase("input-havoc", "entry", true)]
    [TestCase("input-havoc", "current", true)]
    [TestCase("input-havoc", "local", false)]
    [TestCase("array-allocate", "entry", false)]
    [TestCase("array-allocate", "current", true)]
    [TestCase("array-allocate", "local", true)]
    [TestCase("initialized-array-allocate", "entry", false)]
    [TestCase("initialized-array-allocate", "current", true)]
    [TestCase("initialized-array-allocate", "local", true)]
    public async Task CanonicalEntryStorageIsReadOnlyAtNativeAdmission(string operation, string storage, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = operation switch
        {
            "allocate" => factory.ObjectType,
            "array-allocate" or "initialized-array-allocate" => factory.GetOrCreateSequenceType(factory.IntegerType),
            _ => factory.IntegerType
        };
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", type),
            factory.CreateVariable("current", type), factory.CreateVariable("old", type));
        var local = factory.CreateVariable("local", type);
        var target = storage switch { "entry" => parameter.Entry, "current" => parameter.Current, _ => local };
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("canonical-storage-admission");
        switch (operation)
        {
            case "assign":
                builder.Assign(block, site, target, factory.Integer(1));
                break;
            case "allocate":
                builder.Allocate(block, site, type, target);
                break;
            case "havoc":
                builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, target);
                break;
            case "input-havoc":
                builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Input, target);
                break;
            case "array-allocate":
                builder.Allocate(block, site, type, target, factory.Integer(1));
                break;
            case "initialized-array-allocate":
                builder.Allocate(block, site, type, target, factory.Integer(1), [factory.Integer(7)]);
                break;
        }
        builder.Return(block, site);
        // The public builder accepts this typed owned IR. Ordinary assignment,
        // allocation and approximation havoc cannot rewrite canonical Entry;
        // Input havoc initializes its binding and is allowed. These module
        // inputs do not claim compiler-produced callable ownership.
        var candidate = new PassiveCallableCandidate("canonical-storage-admission", builder.Build(), [parameter], null, [],
            [new(factory.Boolean(true), factory.Boolean(true), site)]);
        Assert.That(candidate.Program.Factory, Is.SameAs(factory));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: CancellationToken.None), Is.EqualTo(admitted));
        Assert.That(reason, Is.EqualTo(admitted ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
        Assert.That(plan == null, Is.EqualTo(!admitted));
        if (!admitted)
        { return; }
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: CancellationToken.None)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyEnsuresAsync(0, cancellationToken: CancellationToken.None);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
