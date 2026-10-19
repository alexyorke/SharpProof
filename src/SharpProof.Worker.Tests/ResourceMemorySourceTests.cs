using NUnit.Framework;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("int[] Target() => new int[1];", 32, 0, 32, 1)]
    [TestCase("int[] Target() { var value = new int[1]; var alias = value; return alias; }", 32, 0, 32, 1)]
    [TestCase("int[] Target() { var dead = new int[1]; return new int[3]; }", 72, 32, 40, 3)]
    public void SourceOwnedArraysPreserveAllocationAndReturnedAliasCosts(string source, int cumulative, int auxiliary, int retained, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        source = "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.ArrayAllocations, 2)] public static " + source + " }";
        var actual = ObserveMemoryArrayReturn(source);
        Assert.That(actual.Length, Is.EqualTo(length));
        var preparation = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(preparation.Total, Is.Not.Null);
        var result = ResourceMemoryProgram.Evaluate(preparation.Total!.Program, new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        if (!result.Verified)
        {
            var printer = new IrPrinter(preparation.Total.Program.Factory);
            foreach (var instruction in preparation.Total.Program.Blocks.SelectMany(static block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions).Take(64))
            {
                var detail = instruction switch
                {
                    IrBranchInstruction branch => printer.Print(branch.Condition),
                    IrAssignInstruction assignment => assignment.Target + "=" + printer.Print(assignment.Value),
                    IrAllocationInstruction { Length: { } size } => printer.Print(size),
                    _ => string.Empty
                };
                TestContext.Out.WriteLine(instruction.Kind + ": " + detail);
            }
        }
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(cumulative, cumulative, auxiliary, auxiliary, retained, retained)));
    }

    [Test]
    public void BorrowedReturnCannotBecomeOwnedRetainedStorage()
    {
        var source = "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)] public static int[] Target(int[] input) => input; }";
        var preparation = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(preparation.Total, Is.Not.Null);
        var result = ResourceMemoryProgram.Evaluate(preparation.Total!.Program, new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
    }
    private static int[] ObserveMemoryArrayReturn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("MemorySource", ("Subject.cs", source));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("MemorySource", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (int[])assembly.GetType("C")!.GetMethod("Target")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }
}
