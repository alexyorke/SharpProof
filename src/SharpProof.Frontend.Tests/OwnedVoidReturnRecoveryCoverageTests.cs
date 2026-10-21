using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OwnedVoidReturnRecoveryCoverageTests
{
    private static readonly ImmutableArray<string> RecoveryErrorIds = ["CS0127"];

    [TestCase("invalid-value", "return 1;", true)]
    [TestCase("bare-return", "return;", false)]
    [TestCase("empty-body", "", false)]
    public void PublicBindingRetainsActualVoidReturnShape(string shape, string body, bool compilerError)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(shape);
        var source = "public static class Subject { public static void Target() { " + body + " } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "owned-void-return.cs");
        var compilation = CSharpCompilation.Create("OwnedVoidReturn_" + shape, [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)).ToImmutableArray();
        if (compilerError)
        {
            Assert.That(errors, Is.EqualTo(RecoveryErrorIds));
        }
        else
        {
            Assert.That(errors, Is.Empty);
        }

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.EqualTo(!compilerError));
        var emitErrors = emitted.Diagnostics.Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)).ToImmutableArray();
        if (compilerError)
        {
            Assert.That(emitErrors, Is.EqualTo(RecoveryErrorIds));
        }
        else
        {
            Assert.That(emitErrors, Is.Empty);
        }

        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        Assert.That(target.ReturnsVoid, Is.True);
        Assert.That(target.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Void));
        var graph = ControlFlowGraph.Create(syntax, model)!;
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var returns = graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return))
            .ToImmutableArray();
        if (compilerError)
        {
            var returnSyntax = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            Assert.That(model.GetOperation(returnSyntax), Is.InstanceOf<IReturnOperation>());
            var returned = ((IReturnOperation)model.GetOperation(returnSyntax)!).ReturnedValue;
            Assert.That(returned, Is.InstanceOf<ILiteralOperation>());
            Assert.That(returned!.ConstantValue.HasValue, Is.True);
            Assert.That(returned.ConstantValue.Value, Is.EqualTo(1));
            Assert.That(returned.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(returns, Has.Length.EqualTo(1));
            Assert.That(returns[0].IsReachable, Is.True);
            Assert.That(returns[0].BranchValue, Is.InstanceOf<ILiteralOperation>());
            Assert.That(returns[0].BranchValue!.ConstantValue.Value, Is.EqualTo(1));
        }
        else
        {
            Assert.That(returns, Is.Empty);
            Assert.That(graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue)).Where(value => value != null), Is.Empty);
        }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.Factory, Is.SameAs(factory));
        Assert.That(SymbolEqualityComparer.Default.Equals(context.Target, target), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.Result, Is.Null);
        Assert.That(context.Parameters, Is.Empty);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var binding = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.True);
        Assert.That(binding.Clauses, Is.Empty);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(context.Compilation, Is.SameAs(compilation));
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var irReturns = lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrReturnInstruction>().ToImmutableArray();
        Assert.That(irReturns, Is.Not.Empty);
        Assert.That(irReturns.All(instruction => instruction.Value == null), Is.True);
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue, Is.Null);

        if (!compilerError)
        {
            var load = new AssemblyLoadContext("OwnedVoidReturn_" + shape, isCollectible: true);
            try
            {
                image.Position = 0;
                var runtime = load.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
                Assert.That(runtime.ReturnType, Is.EqualTo(typeof(void)));
                Assert.That(runtime.Invoke(null, null), Is.Null);
            }
            finally
            {
                load.Unload();
            }
        }
    }
}
