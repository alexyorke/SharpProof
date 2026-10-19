using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void StackStateIntrinsicRejectsSourceDefinedFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public class Stack<T> { public Stack() {} public Stack(int capacity) {} public void Push(T value) {} public T Pop() => default(T); public int Count => -1; } }");
        var compilation = CSharpCompilation.Create("UserStack", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.Stack`1")!;
        foreach (var member in type.GetMembers())
        { Assert.That(CSharpOperationSemantics.IsStackStateDeclaration(member), Is.False); }
    }

    private static bool CheckStackStateIntrinsic(CSharpCompilation references, bool wrongClaim, bool incoming = false, bool opaque = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        var body = incoming ? "var before = data.Count; data.Push(17); var item = data.Pop(); return data.Count == before ? item : 0;"
            : opaque ? "var data = new System.Collections.Generic.Stack<T>(); var other = new System.Collections.Generic.Stack<T>(2); data.Push(first); other.Push(second); var alias = data; alias.Push(other.Pop()); var result = Read<T>(data); data.Pop(); return result;"
            : "var data = new System.Collections.Generic.Stack<int>(); var other = new System.Collections.Generic.Stack<int>(2); var alias = data; alias.Push(17); other.Push(23); data.Push(other.Pop()); var second = alias.Pop(); return second * 100 + data.Pop() + data.Count + other.Count;";
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static " +
            (incoming ? "int Target(System.Collections.Generic.Stack<int> data)" : opaque ? "T Target<T>(T first, T second)" : "int Target()") +
            " { " + body + " } static T Read<T>(System.Collections.Generic.Stack<T> data) => data.Pop(); }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>();
        if (incoming)
        {
            var count = lowered.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
                .OfType<IrAssignInstruction>().Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Value)
                .OfType<IrBinaryTerm>().Where(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Operator == IrBinaryOperator.BitwiseAnd)
                .Select(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Left).OfType<IrOpaqueTerm>().First();
            var parameter = context.Parameters.Single().Entry;
            inputs[parameter] = factory.CreateReferenceValue(factory.GetVariableInfo(parameter).Type,
                new IrObjectState().WithField(count.Member, factory.CreateIntegerValue(1)));
        }
        else if (opaque)
        {
            foreach (var parameter in context.Parameters)
            {
                inputs[parameter.Entry] = factory.CreateReferenceValue(factory.GetVariableInfo(parameter.Entry).Type, new IrObjectState());
            }
        }
        var normal = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 10000, normal);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var runtime = new AssemblyLoadContext("StackInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!;
            if (opaque)
            {
                target = target.MakeGenericMethod(typeof(decimal));
                var expected = new decimal(12300, 0, 0, false, 4);
                var actual = (decimal)target.Invoke(null, new object[] { decimal.MinValue, expected })!;
                return decimal.GetBits(actual).SequenceEqual(decimal.GetBits(expected)) && result.Status == IrProgramExecutionStatus.Returned &&
                    result.ConsumedApproximation && result.ReturnValue!.Kind == IrValueKind.Reference &&
                    ReferenceEquals(result.ReturnValue.Reference, inputs[context.Parameters[wrongClaim ? 0 : 1].Entry].Reference);
            }
            var observed = new Stack<int>();
            observed.Push(23);
            var numeric = (int)target.Invoke(null, incoming ? [observed] : null)!;
            return result.Status == IrProgramExecutionStatus.Returned && result.ConsumedApproximation && result.ReturnValue!.Integer == numeric + (wrongClaim ? 1 : 0);
        }
        finally
        { runtime.Unload(); }
    }
}
