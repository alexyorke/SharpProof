using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicDefaultLocalNearbyTests
{
    [TestCase("signed-wide", "Long", "Dim value As Long\nReturn value", 0)]
    [TestCase("unsigned-narrow", "Byte", "Dim value As Byte\nReturn value", 0)]
    [TestCase("unsigned-wide", "ULong", "Dim value As ULong\nReturn value", 0)]
    [TestCase("multiple-defaults", "Integer", "Dim left, right As Integer\nReturn left + right", 0)]
    [TestCase("explicit-initializer", "Integer", "Dim value As Integer = 17\nReturn value", 17)]
    [TestCase("loop-local-persistence", "Integer", "Dim total As Integer = 0\nFor i As Integer = 1 To 4\nDim value As Integer\nvalue += 1\ntotal += value\nNext\nReturn total", 10)]
    [TestCase("false-default", "Integer", "Dim value As Boolean\nReturn If(value, 1, 0)", 0)]
    [TestCase("null-string-default", "Integer", "Dim value As String\nReturn If(value Is Nothing, 17, 0)", 17)]
    [TestCase("null-object-default", "Integer", "Dim value As Object\nReturn If(value Is Nothing, 17, 0)", 17)]
    [TestCase("function-result-fallthrough", "Integer", "", 0)]
    [TestCase("function-result-assignment", "Integer", "Target = 17", 17)]
    public void OrdinaryDefaultStorageAndAssignmentsMatchTheCompiledProgram(string name, string returnType, string body, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(returnType);
        var source = "Public Class Subject\nPublic Shared Function Target() As " + returnType + "\n" + body + "\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: name + ".vb");
        var compilation = VisualBasicCompilation.Create("DefaultLocal_" + name, [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty, name);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True, name);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext(name, isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.Invoke(null, null);
            Assert.That(actual, Is.EqualTo(expected), name);
            var model = compilation.GetSemanticModel(tree);
            var graph = ControlFlowGraph.Create(tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single(), model)!;
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True, name);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), name);
            Assert.That(replay.ConsumedApproximation, Is.False, name);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)), name);
        }
        finally { runtime.Unload(); }
    }

    [Test]
    public void PersistentStaticLocalIsNotModeledAsFreshPerInvocationStorage()
    {
        const string source = "Public Class Subject\nPublic Shared Function Target() As Integer\nStatic value As Integer\nvalue += 1\nReturn value\nEnd Function\nEnd Class";
        var tree = VisualBasicSyntaxTree.ParseText(source, path: "static-local.vb");
        var compilation = VisualBasicCompilation.Create("StaticLocalSubject", [tree], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("StaticLocalSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(method.Invoke(null, null), Is.EqualTo(1));
            Assert.That(method.Invoke(null, null), Is.EqualTo(2));
            var model = compilation.GetSemanticModel(tree);
            var graph = ControlFlowGraph.Create(tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single(), model)!;
            var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, new TotalLoweringContext(factory, target));
            Assert.That(lowered.IsExact, Is.False);
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        }
        finally { runtime.Unload(); }
    }

    [TestCase("int Target() { int value; value = 17; return value; }", 17)]
    [TestCase("int Target() { int value; if (true) value = 9; else value = 7; return value; }", 9)]
    public void CSharpDefiniteAssignmentControlsKeepTheirExistingSemantics(string source, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([]), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowered, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
