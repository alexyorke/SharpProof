using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class StackStateOracleTests
{
    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public void StackScalarBoundariesAgreeWithCompiledClr(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Compare("var data = new System.Collections.Generic.Stack<" + type + ">(0); var alias = data; data.Push(" + type + ".MinValue); alias.Push(" + type + ".MaxValue); return data.Pop() == " + type + ".MaxValue && alias.Pop() == " + type + ".MinValue && data.Count == 0 ? 1 : 0;");
    }

    [TestCase("var data = new System.Collections.Generic.Stack<int>() { }; data.Push(17); return data.Count + data.Pop();")]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); data.Push(17); data.Push(data.Pop() + 1); return data.Pop() + data.Count;")]
    [TestCase("var first = new System.Collections.Generic.Stack<int>(); var second = new System.Collections.Generic.Stack<int>(); second.Push(23); var data = first; data.Push((data = second).Pop()); return first.Pop() * 100 + data.Count;")]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); data.Push(17); var alias = data; alias.Push(23); var before = data.Pop(); alias.Push(31); return before * 10000 + data.Pop() * 100 + data.Pop();")]
    [TestCase("var data = new System.Collections.Generic.Stack<bool>(4); data.Push(false); data.Push(true); return data.Pop() && !data.Pop() && data.Count == 0 ? 1 : 0;")]
    [TestCase("var data = new System.Collections.Generic.Stack<string>(); data.Push(null); data.Push(\"value\"); return object.ReferenceEquals(data.Pop(), \"value\") && data.Pop() == null && data.Count == 0 ? 1 : 0;")]
    public void StackOrderingAndIdentityAgreeWithCompiledClr(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare(body);
    }

    [Test]
    public void EmptyIncomingStackCountHasAnExactMaterializableEntryWitness()
    {
        const string source = "using SharpProof.Attributes; public static class Probe { public static int Target(System.Collections.Generic.Stack<int> data) { Contract.Requires(data != null); Contract.Ensures(true); return data.Count; } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var count = total.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrAssignInstruction>().Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Value)
            .OfType<IrBinaryTerm>().Where(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Operator == IrBinaryOperator.BitwiseAnd)
            .Select(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Left).OfType<IrOpaqueTerm>().First();
        var parameter = total.Parameters.Single().Entry;
        var inputBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
        inputBuilder[parameter] = factory.CreateReferenceValue(factory.GetVariableInfo(parameter).Type,
            new IrObjectState().WithField(count.Member, factory.CreateIntegerValue(0)));
        var inputs = inputBuilder.ToImmutable();
        var result = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 1000);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ConsumedApproximation, Is.False, "An incoming empty Stack needs no constructor/growth choice.");
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(new Stack<int>().Count));
        Assert.That(result.ReturnValue.Integer, Is.Zero, "This is a concrete counterexample to strict Count positivity.");
    }

    private static void Compare(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class Probe { public static int Target() { Contract.Ensures(true); " + body + " } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var choices = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var interpreted = new IrProgramInterpreter(factory).Execute(total.Program, null, 10000, choices);
        Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), interpreted.Unsupported?.Detail);
        Assert.That(interpreted.ConsumedApproximation, Is.True);
        var compilation = TestCompilation.CreateExact("StackStateOracle", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, ImmutableArray.Create(("Probe.cs", source)));
        var syntax = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(syntax,
            syntax.WithRootAndOptions(syntax.GetRoot(), ((CSharpParseOptions)syntax.Options).WithPreprocessorSymbols(ImmutableArray<string>.Empty)));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new AssemblyLoadContext("StackStateOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null);
            Assert.That(interpreted.ReturnValue!.Integer, Is.EqualTo(actual));
        }
        finally
        { runtime.Unload(); }
    }
}
