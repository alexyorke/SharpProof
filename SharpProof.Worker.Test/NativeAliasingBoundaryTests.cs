using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeAliasingBoundaryTests
{
    [TestCase(7, WorkerClaimOutcome.Proven)]
    [TestCase(3, WorkerClaimOutcome.Refuted)]
    public async Task GenericReceiverContractsShareFieldIdentity(int expected, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public class Outer<T> { public class Cell<U> { public int Value; " +
            "public void Set() { Contract.Requires(Value == 3); Value = 7; } } } public static class C { " +
            "public static int Target(Outer<int>.Cell<string> cell) { Contract.Requires(cell != null && cell.Value == 3); " +
            "Contract.Ensures(cell.Value == " + expected + " && Contract.Old(cell).Value == 3); cell.Set(); return cell.Value; } }";
        Assert.That(await Verify(source), Is.EqualTo(outcome));
    }

    [TestCase(73, WorkerClaimOutcome.Proven)]
    [TestCase(77, WorkerClaimOutcome.Refuted)]
    public async Task GenericFieldIdentityPreservesDistinctReceivers(int expected, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public class Cell<T> { public int Value; " +
            "public void Set() { Value = 7; } } public static class C { public static int Target(Cell<int> a, Cell<int> b) { " +
            "Contract.Requires(a != null && b != null && a != b && a.Value == 1 && b.Value == 3); " +
            "Contract.Ensures(Contract.Result<int>() == " + expected + "); a.Set(); return a.Value * 10 + b.Value; } }";
        Assert.That(await Verify(source), Is.EqualTo(outcome));
    }

    [TestCase("int", "3", "7")]
    [TestCase("string", "\"before\"", "\"after\"")]
    public async Task GenericDependentFieldRetainsConservativeCallBoundary(string type, string before, string after)
    {
        var source = "using SharpProof.Attributes; public class Cell<T> { public T Value; " +
            "public void Set(T value) { Value = value; } } public static class C { public static " + type + " Target() { " +
            "Contract.Ensures(Contract.Result<" + type + ">() == " + after + "); var cell = new Cell<" + type +
            ">(); cell.Value = " + before + "; cell.Set(" + after + "); return cell.Value; } }";
        Assert.That(await Verify(source), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("bool", "false", "true", "cell.Value ? 7 : 3", 7, WorkerClaimOutcome.Proven)]
    [TestCase("bool", "false", "true", "cell.Value ? 7 : 3", 3, WorkerClaimOutcome.Refuted)]
    [TestCase("string", "\"before\"", "\"after\"", "cell.Value == \"after\" ? 7 : 3", 7, WorkerClaimOutcome.Proven)]
    [TestCase("string", "\"before\"", "\"after\"", "cell.Value == \"after\" ? 7 : 3", 3, WorkerClaimOutcome.Refuted)]
    [TestCase("Node", "first", "second", "cell.Value.Score", 7, WorkerClaimOutcome.Proven)]
    [TestCase("Node", "first", "second", "cell.Value.Score", 3, WorkerClaimOutcome.Refuted)]
    [TestCase("int[]", "new int[3]", "new int[7]", "cell.Value.Length", 7, WorkerClaimOutcome.Proven)]
    [TestCase("int[]", "new int[3]", "new int[7]", "cell.Value.Length", 3, WorkerClaimOutcome.Refuted)]
    public async Task NestedGenericReceiverWritesTheSameFieldSlot(string type, string before, string after,
        string result, int expected, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public class Node { public int Score; } " +
            "public class Outer<T> { public class Cell<U> { public " + type + " Value; public void Set(" + type +
            " value) { Value = value; } } } public static class C { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() == " + expected + "); var first = new Node(); first.Score = 3; " +
            "var second = new Node(); second.Score = 7; var cell = new Outer<int>.Cell<string>(); " +
            "cell.Value = " + before + "; cell.Set(" + after + "); return " + result + "; } }";
        Assert.That(await Verify(source), Is.EqualTo(outcome));
    }

    [TestCase("cell.Set();", 7, WorkerClaimOutcome.Proven)]
    [TestCase("cell.Set();", 3, WorkerClaimOutcome.Refuted)]
    [TestCase("cell.Property = 7;", 7, WorkerClaimOutcome.Proven)]
    [TestCase("cell.Property = 7;", 3, WorkerClaimOutcome.Refuted)]
    public async Task GenericReceiverWritesTheSameFieldSlot(string mutation, int expected, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public class Cell<T> { public int Value; " +
            "public void Set() { Value = 7; } public int Property { get { return Value; } set { Value = value; } } } " +
            "public static class C { public static int Target() { Contract.Ensures(Contract.Result<int>() == " + expected +
            "); var cell = new Cell<int>(); cell.Value = 3; " + mutation + " return cell.Value; } }";
        Assert.That(await Verify(source), Is.EqualTo(outcome));
    }

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
