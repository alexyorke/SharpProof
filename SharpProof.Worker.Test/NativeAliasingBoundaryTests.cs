using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeAliasingBoundaryTests
{
    [TestCase(false, 7, WorkerClaimOutcome.Proven)]
    [TestCase(false, 2, WorkerClaimOutcome.Refuted)]
    [TestCase(true, 1, WorkerClaimOutcome.Proven)]
    [TestCase(true, 7, WorkerClaimOutcome.Refuted)]
    public async Task ConditionalOldOwnerUsesSelectedHeap(bool chooseOld, int expected, WorkerClaimOutcome outcome)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public sealed class Node { public int Value; }
            public static class C {
                public static int Target(Node a, Node b, bool choose) {
                    Contract.Requires(a != null && b != null && a != b);
                    Contract.Requires(a.Value == 1 && b.Value == 2 && choose == {{(chooseOld ? "true" : "false")}});
                    Contract.Ensures((choose ? Contract.Old(a) : b).Value == {{expected}});
                    b.Value = 7;
                    a.Value = 9;
                    return 0;
                }
            }
            """;
        Assert.That(await Verify(source), Is.EqualTo(outcome));
    }

    [TestCase("ref", "cell.Value = 5;")]
    [TestCase("in", "cell.Value = 5;")]
    [TestCase("ref", "Change(cell);")]
    [TestCase("in", "Change(cell);")]
    [TestCase("ref", "try { cell.Mutate(); } catch (System.Exception) { }")]
    [TestCase("in", "try { cell.Mutate(); } catch (System.Exception) { }")]
    public async Task ReadonlyReferenceMayAliasWrittenField(string modifier, string body)
    {
        var source = "using SharpProof.Attributes; public class Cell { public int Value; " +
            "public virtual void Mutate() { Value = 5; throw new System.InvalidOperationException(); } } public static class C { " +
            "public static int Target(" + modifier + " int value, Cell cell) { Contract.Requires(cell != null); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(value)); " + body + " return value; } " +
            "private static void Change(Cell cell) { cell.Value = 5; } }";
        Assert.That(await Verify(source), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("ref")]
    [TestCase("in")]
    public async Task ReadonlyReferenceMayAliasWrittenElement(string modifier)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int Target(" + modifier +
            " int value, int[] values) { Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(value)); values[0] = 5; return value; } }";
        Assert.That(await Verify(source), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("ref")]
    [TestCase("in")]
    public async Task MultipleReadonlyReferencesMayAliasReferenceCell(string modifier)
    {
        var source = "using SharpProof.Attributes; public sealed class Item { } public sealed class Cell { public Item Value; } " +
            "public static class C { public static Item Target(" + modifier + " Item first, " + modifier +
            " Item second, Cell cell, Item replacement) { Contract.Requires(cell != null); " +
            "Contract.Ensures(Contract.Result<Item>() == Contract.Old(second)); cell.Value = replacement; return second; } }";
        Assert.That(await Verify(source), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("ref")]
    [TestCase("in")]
    public async Task ReadonlyReferenceWithoutWritesRemainsExact(string modifier)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int Target(" + modifier +
            " int value) { Contract.Ensures(Contract.Result<int>() == Contract.Old(value)); int local = value; return local; } }";
        Assert.That(await Verify(source), Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static async Task<WorkerClaimOutcome> Verify(string source)
    {
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        return response.ClaimResults.Single().Outcome;
    }
}
