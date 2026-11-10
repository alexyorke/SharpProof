using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static bool CheckExceptionMessageIntrinsic(CSharpCompilation references, string id, bool wrongClaim, bool resourceOnly = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(id);
        foreach (var message in resourceOnly ? ImmutableArray.Create("(string)null") : ImmutableArray.Create("(string)null", "\"\"", "\"message\""))
        {
            var expression = resourceOnly ? ResourceConstructorExpression(id) : id switch
            {
                "M:System.ArgumentException.#ctor(System.String)" => "new System.ArgumentException((marker = 1) == 1 ? " + message + " : \"unreachable\")",
                "M:System.ArgumentException.#ctor(System.String,System.String)" => "new System.ArgumentException(paramName: (marker = 1) == 1 ? \"parameter\" : \"unreachable\", message: (marker = 2) == 2 ? " + message + " : \"unreachable\")",
                "M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)" => "new System.Collections.Generic.KeyNotFoundException((marker = 1) == 1 ? " + message + " : \"unreachable\")",
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            };
            var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { int marker = 0; _ = " + expression + "; return marker; } }");
            var compilation = references.AddSyntaxTrees(tree);
            using var image = new MemoryStream();
            if (!compilation.Emit(image).Success)
            { return false; }
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
                static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
            if (!lowered.IsExact)
            { return false; }
            if (resourceOnly && lowered.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions).OfType<IrLockInstruction>().Count() != 1)
            { return false; }
            // Select a normal constructor path only for this differential probe.
            // This approximation is not native replay authority or a proof premise.
            var allocations = 0;
            var options = new IrProgramReplayOptions(request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
            })
            {
                AllocationPrefixObserver = (instruction, _) =>
                {
                    ArgumentNullException.ThrowIfNull(instruction);
                    allocations++;
                }
            };
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
            var runtime = new AssemblyLoadContext("ExceptionMessageInventoryOracle", isCollectible: true);
            try
            {
                image.Position = 0;
                var actual = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
                var resourceMessage = resourceOnly || id == "M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)" && message == "(string)null";
                if (replay.Status != IrProgramExecutionStatus.Returned || replay.ConsumedApproximation != resourceMessage || allocations != 1 ||
                    replay.ReturnValue!.Integer != actual + (wrongClaim ? 1 : 0))
                { return false; }
            }
            finally { runtime.Unload(); }
        }
        return true;
    }

    private static string ResourceConstructorExpression(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id switch
        {
            "M:System.ArgumentException.#ctor" => "new System.ArgumentException()",
            "M:System.ArgumentNullException.#ctor" => "new System.ArgumentNullException()",
            "M:System.ArgumentNullException.#ctor(System.String)" => "new System.ArgumentNullException(\"parameter\")",
            "M:System.ArgumentOutOfRangeException.#ctor" => "new System.ArgumentOutOfRangeException()",
            "M:System.ArgumentOutOfRangeException.#ctor(System.String)" => "new System.ArgumentOutOfRangeException(\"parameter\")",
            "M:System.InvalidOperationException.#ctor" => "new System.InvalidOperationException()",
            "M:System.InvalidOperationException.#ctor(System.String)" => "new System.InvalidOperationException((string)null)",
            "M:System.DivideByZeroException.#ctor" => "new System.DivideByZeroException()",
            "M:System.IO.IOException.#ctor" => "new System.IO.IOException()",
            "M:System.ObjectDisposedException.#ctor(System.String)" => "new System.ObjectDisposedException(\"object\")",
            "M:System.AggregateException.#ctor" => "new System.AggregateException()",
            "M:System.AggregateException.#ctor(System.String)" => "new System.AggregateException((string)null)",
            "M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)" => "new System.Collections.Generic.KeyNotFoundException((string)null)",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
}
