using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    [TestCase("public class Cell { public int value; public Cell(int n) { value=n; for(int i=0;i<n;i++) { var a=new int[2]; } } }", "var cell=new Cell(n);", "max(0, n)", "max(0, n)", "(max(0, n) * 2)")]
    [TestCase("public class Cell { public int[] data=new int[3]; }", "var cell=new Cell();", "0", "1", "3")]
    [TestCase("public class Cell { public int[] Data { get; } = new int[3]; }", "var cell=new Cell();", "0", "1", "3")]
    [TestCase("public class Cell { public int[] data; }", "var cell=new Cell { data=new int[3] };", "0", "1", "3")]
    [TestCase("public class Cell { public int[] data; public Cell(int[] a) { data=a; } }", "var cell=new Cell(new int[3]);", "0", "1", "3")]
    [TestCase("public class Cell { public int[] Data { get; } = new int[3]; }", "var cell=new Cell(); var values=cell.Data;", "0", "1", "3")]
    public void ManagedConstructorsAndInitializersRetainTheirResourceEffects(string declarations, string body, string loops, string arrays, string elements)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(arrays);
        ArgumentNullException.ThrowIfNull(elements);
        var result = AnalyzeSource(declarations + " public static class Subject { public static void Root(int n) { " + body + " } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo(loops));
        Assert.That(result.Bounds.Arrays.Text, Is.EqualTo(arrays));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo(elements));
    }

    [TestCase("public class Base {} public class Cell : Base {}", "UnsupportedManagedConstructor")]
    [TestCase("public class Cell { static Cell() { var a=new int[3]; } }", "ConstructorTypeInitializationUnknown")]
    [TestCase("public class Cell { public Cell() { var cell=new Cell(); } }", "RecursiveConstructorCostUnknown")]
    [TestCase("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size=1024)] public class Cell {}", "UnsupportedManagedLayout")]
    [TestCase("public class Cell { public decimal value; }", "UnsupportedManagedFieldStorage")]
    [TestCase("public class Cell { ~Cell() { var a=new int[3]; } }", "UnsupportedManagedLayout")]
    [TestCase("public class Cell { public int Data => 3; }", "UnsupportedManagedLayout")]
    public void UnsupportedObjectStorageAndConstructionStayUnknown(string declarations, string reason)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(reason);
        var result = AnalyzeSource(declarations + " public static class Subject { public static void Root() { var cell=new Cell(); } }");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
    }
    [TestCase("var value=new string('x', 0);")]
    [TestCase("var value=new string('x', 3);")]
    [TestCase("var value=new string('x', n);")]
    public void RepeatedStringsPreserveArrayCountUnits(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("0"));
    }
    [TestCase("return new System.Action(Callbacks.Run);")]
    [TestCase("_ = new System.Action(Callbacks.Run); return null;")]
    public void DelegateCreationKeepsTargetBodiesOutsideCreationCounts(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = AnalyzeSource("public static class Subject { public static System.Action Root(int n) { " + body +
            " } } public static class Callbacks { public static void Run() { for(int i=0;i<3;i++) { var data=new int[2]; } } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Arrays.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("0"));
    }

    [Test]
    public void DiscardAssignmentsPreserveTheirArrayAllocationCosts()
    {
        var result = Analyze("_ = new int[3];");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("1"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("3"));
    }
    [TestCase("object value=n;")]
    [TestCase("object value=(long)n;")]
    [TestCase("object value=true;")]
    [TestCase("object value=(char)n;")]
    [TestCase("for(int i=0;i<3;i++) { object value=i; }")]
    public void ScalarBoxesPreserveCountUnitsAndCarryTypedStorage(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Program.Kind, Is.Not.EqualTo(SharpProof.Ir.ResourceProgramKind.Zero));
    }
}
