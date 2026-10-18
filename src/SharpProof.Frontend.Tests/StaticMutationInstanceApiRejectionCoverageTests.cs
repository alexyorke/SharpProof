using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class StaticMutationInstanceApiRejectionCoverageTests
{
    [Test]
    public void ActualStaticIncrementStaysOutsideTheInstanceOnlyApi()
    {
        const string source = "public static class Subject { public static int State; public static int Target() { State++; return 0; } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("StaticMutationInstanceApi", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var load = new AssemblyLoadContext("StaticMutationInstanceApi", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = load.LoadFromStream(image).GetType("Subject")!;
            Assert.That(runtime.GetField("State")!.GetValue(null), Is.EqualTo(0));
            Assert.That(runtime.GetMethod("Target")!.Invoke(null, []), Is.EqualTo(0));
            Assert.That(runtime.GetField("State")!.GetValue(null), Is.EqualTo(1));
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var incrementSyntax = syntax.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Single();
            var operation = model.GetOperation(incrementSyntax);
            Assert.That(operation, Is.InstanceOf<IIncrementOrDecrementOperation>());
            var increment = (IIncrementOrDecrementOperation)operation!;
            Assert.That(increment.Kind, Is.EqualTo(OperationKind.Increment));
            Assert.That(increment.IsPostfix, Is.True);
            Assert.That(increment.IsLifted, Is.False);
            Assert.That(increment.OperatorMethod, Is.Null);
            Assert.That(increment.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(increment.Target, Is.InstanceOf<IFieldReferenceOperation>());
            var field = (IFieldReferenceOperation)increment.Target;
            Assert.That(field.Field.IsStatic, Is.True);
            Assert.That(field.Instance, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(field.Field.ContainingAssembly, compilation.Assembly), Is.True);
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(syntax)!);
            var graph = ControlFlowGraph.Create(syntax, model)!;
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            CheckStaticWorkflow();

            // This genuine static SDK operation is a deliberately wrong-family
            // input to the instance-only expression API, never a normal routing claim.
            var builder = new IrProgramBuilder(factory);
            var entry = builder.CreateBlock("instance-api-negative");
            var rejected = new RoslynTotalExpressionLowerer(context, builder)
                .LowerDiscardedInstanceFieldMutation(increment, entry);
            Assert.That(rejected.Classification.IsExact, Is.False);
            Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
            Assert.That(rejected.Continuation, Is.EqualTo(entry));
            Assert.That(rejected.Value, Is.InstanceOf<IrVariableTerm>());
            Assert.That(rejected.Value.Type, Is.EqualTo(factory.IntegerType));
            CheckStaticWorkflow();

            void CheckStaticWorkflow()
            {
                var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
                Assert.That(lowered.IsExact, Is.True);
                Assert.That(lowered.ConstructionLimitExceeded, Is.False);
                var writes = lowered.Program.Blocks.SelectMany(b => b is null ? throw new System.ArgumentNullException(nameof(b)) : (b.Instructions)).OfType<IrWriteInstruction>().ToArray();
                Assert.That(writes, Has.Length.EqualTo(1));
                Assert.That(writes[0].Region, Is.EqualTo(IrWriteRegion.Static));
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
            }
        }
        finally
        {
            load.Unload();
        }
    }
}
