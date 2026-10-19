using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class ClosedClassIdentityTests
{
    [TestCase("public class Box<T> { }", "Box<int>", "Box<string>", "", WorkerClaimOutcome.Proven)]
    [TestCase("public class A { } public class B { }", "A", "B", "", WorkerClaimOutcome.Proven)]
    [TestCase("public class A { } public class B : A { }", "A", "B", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class A<T> { } public class B<T> : A<T> { }", "A<int>", "B<int>", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class Box<T> { }", "Box<T>", "Box<U>", "<T, U>", WorkerClaimOutcome.Unknown)]
    [TestCase("public class Box<T> { }", "Box<Box<T>>", "Box<Box<int>>", "<T>", WorkerClaimOutcome.Unknown)]
    [TestCase("public class A<T> { } public class B<T> { }", "A<T>", "B<U>", "<T, U>", WorkerClaimOutcome.Unknown)]
    [TestCase("public interface A { } public interface B { } public class Both : A, B { }", "A", "B", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class Box<T> { }", "Box<dynamic>", "Box<object>", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class Box<T> { }", "Box<string?>", "Box<string>", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class Box<T> { }", "Box<nint>", "Box<System.IntPtr>", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class Box<T> { }", "Box<(int x, int y)>", "Box<(int a, int b)>", "", WorkerClaimOutcome.Refuted)]
    public async Task EntryClassIdentityRetainsOnlyRuntimeCompatibleAliases(string declarations, string first, string second,
        string generics, WorkerClaimOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(generics);
        Assert.That(await Verify("using SharpProof.Attributes; " + declarations + " public static class C { public static bool Target" + generics +
            "(" + first + " a, " + second + " b) { Contract.Requires(a != null && b != null); Contract.Ensures(Contract.Result<bool>()); return !object.ReferenceEquals(a, b); } }"), Is.EqualTo(expected));
    }

    [TestCase("public class Box<T> { }", "Box<int>", "Box<string>", "", WorkerClaimOutcome.Unknown)]
    [TestCase("public class A { } public class B : A { }", "A", "B", "", WorkerClaimOutcome.Refuted)]
    [TestCase("public class A<T> { } public class B<T> { }", "A<T>", "B<U>", "<T, U>", WorkerClaimOutcome.Unknown)]
    public async Task FieldClassIdentityRequiresPositiveAliasCompatibility(string declarations, string first, string second,
        string generics, WorkerClaimOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(generics);
        Assert.That(await Verify("using SharpProof.Attributes; " + declarations + " public class Holder" + generics + " { public " + first + " First; public " + second +
            " Second; } public static class C { public static bool Target" + generics + "(Holder" + generics + " holder) { Contract.Requires(holder != null && holder.First != null && holder.Second != null); " +
            "Contract.Ensures(Contract.Result<bool>()); return !object.ReferenceEquals(holder.First, holder.Second); } }"), Is.EqualTo(expected));
    }

    [Test]
    public async Task CovariantArrayViewsCannotBeProvedDisjoint()
    {
        Assert.That(await Verify("using SharpProof.Attributes; public static class C { public static bool Target(string[] a, object[] b) { Contract.Requires(a != null && b != null); " +
            "Contract.Ensures(Contract.Result<bool>()); return !object.ReferenceEquals(a, b); } }"), Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task IncompatibleClassesCanBothBeNull()
    {
        Assert.That(await Verify("using SharpProof.Attributes; public class A { } public class B { } public static class C { public static bool Target(A a, B b) { " +
            "Contract.Requires(a == null && b == null); Contract.Ensures(Contract.Result<bool>()); return object.ReferenceEquals(a, b); } }"), Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase(false, WorkerClaimOutcome.Refuted)]
    [TestCase(true, WorkerClaimOutcome.Unknown)]
    public async Task UnrelatedFieldViewsNeedAConcreteEntryWitness(bool requireAlias, WorkerClaimOutcome expected)
    {
        Assert.That(await Verify("using SharpProof.Attributes; public class A { } public class B { } " +
            "public class Holder { public A First; public B Second; public int Value; } public static class C { " +
            "public static int Target(Holder holder) { Contract.Requires(holder != null && holder.First != null && holder.Second != null && holder.Value == 0); " +
            (requireAlias ? "Contract.Requires(object.ReferenceEquals(holder.First, holder.Second)); " : "") +
            "Contract.Ensures(Contract.Result<int>() == 0); holder.Value = 1; return holder.Value; } }"), Is.EqualTo(expected));
    }

    [TestCase("public class A { } public class B : A { }", "A", "B")]
    [TestCase("public interface A { } public interface B { } public class Both : A, B { }", "A", "B")]
    [TestCase("public interface A { } public class B : A { }", "A", "B")]
    public async Task NominalWitnessSearchRetainsCertifiedAliasOverlaps(string declarations, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        Assert.That(await Verify("using SharpProof.Attributes; " + declarations + " public class Other { } public class Holder { public " + first +
            " First; public " + second + " Second; public Other Unrelated; public int Value; } public static class C { " +
            "public static int Target(Holder holder) { Contract.Requires(holder != null && holder.First != null && holder.Second != null && holder.Unrelated != null && holder.Value == 0); " +
            "Contract.Requires(object.ReferenceEquals(holder.First, holder.Second)); Contract.Ensures(Contract.Result<int>() == 0); holder.Value = 1; return holder.Value; } }"),
            Is.EqualTo(WorkerClaimOutcome.Refuted));
    }
    private static async Task<WorkerClaimOutcome> Verify(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var result = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Errors, Is.Empty);
        return result.ClaimResults.Single().Outcome;
    }
}
