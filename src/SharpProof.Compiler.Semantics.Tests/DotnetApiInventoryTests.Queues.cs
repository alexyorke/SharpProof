using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void QueueStateRejectsSourceDefinedFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public class Queue<T> { public Queue() {} public Queue(int capacity) {} public void Enqueue(T value) {} public int Count => -1; public T Dequeue() => default(T); } }");
        var compilation = CSharpCompilation.Create("UserQueue", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.Queue`1")!;
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        { Assert.That(CSharpOperationSemantics.IsQueueStateMember(member), Is.False); }
    }
    private static bool CheckQueueStateIntrinsic(CSharpCompilation references, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { var data = new System.Collections.Generic.Queue<int>(1); var alias = data; data.Enqueue(17); alias.Enqueue(23); var first = data.Dequeue(); data.Enqueue(29); var other = new System.Collections.Generic.Queue<object>(); var item = new object(); other.Enqueue(item); other.Enqueue(null); return first * 10000 + alias.Dequeue() * 100 + data.Dequeue() + data.Count + (object.ReferenceEquals(other.Dequeue(), item) && other.Dequeue() == null ? 1 : 0); } }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var runtime = new AssemblyLoadContext("QueueInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
            return result.Status == IrProgramExecutionStatus.Returned && result.ConsumedApproximation && result.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
        }
        finally { runtime.Unload(); }
    }
}
