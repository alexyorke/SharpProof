using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RepeatedRequiresDefaultWorkApiCoverageTests
{
    [TestCase(8, 2, false)]
    [TestCase(100, 24, true)]
    public void ActualRepeatedRequiresImportsRetainTheDefaultWorkBoundary(int clauses, int calls, bool limited)
    {
        var requirements = string.Join(" ", Enumerable.Range(0, clauses).Select(i =>
            "SharpProof.Attributes.Contract.Requires(value >= " + i.ToString(CultureInfo.InvariantCulture) + ");"));
        var invocations = string.Join(" ", Enumerable.Repeat("value = Helper(value);", calls));
        var source = "public static class Subject { public static int Target(int value) { " + invocations +
            " return value; } public static int Helper(int value) { " + requirements + " return value + 1; } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "requires-import-work.cs");
        var compilation = CSharpCompilation.Create("RequiresImportWorkSubject", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var load = new AssemblyLoadContext("RequiresImportWorkSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = load.LoadFromStream(image).GetType("Subject")!;
            Assert.That(runtime.GetMethod("Target")!.Invoke(null, [5000]), Is.EqualTo(5000 + calls));
            Assert.That(runtime.GetMethod("Helper")!.Invoke(null, [5000]), Is.EqualTo(5001));
            var model = compilation.GetSemanticModel(tree);
            var declarations = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var target = declarations.Single(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Identifier.ValueText == "Target"));
            var helper = declarations.Single(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Identifier.ValueText == "Helper"));
            Assert.That(target.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(), Is.EqualTo(calls));
            Assert.That(helper.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(), Is.EqualTo(clauses));
            Assert.That(helper.DescendantNodesAndSelf().Count(), Is.LessThan(2048));
            Assert.That(compilation.GetTypeByMetadataName("Subject")!.StaticConstructors, Is.Empty);
            var targetGraph = ControlFlowGraph.Create(target, model)!;
            var helperGraph = ControlFlowGraph.Create(helper, model)!;
            var helperSymbol = (IMethodSymbol)model.GetDeclaredSymbol(helper)!;
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(target)!);
            var preparedCount = 0;
            bool Prepare(TotalLoweringContext candidate)
            {
                System.ArgumentNullException.ThrowIfNull(candidate);
                preparedCount++;
                Assert.That(candidate.Factory, Is.SameAs(factory));
                Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, helperSymbol), Is.True);
                var binding = new ContractBinder(compilation, factory).BindTotal(candidate);
                Assert.That(binding.IsSuccess, Is.True);
                Assert.That(binding.Clauses, Has.Length.EqualTo(clauses));
                Assert.That(binding.Clauses.All(c => c is null ? throw new System.ArgumentNullException(nameof(c)) : (c.Kind == BoundContractKind.Requires)), Is.True);
                candidate.SourceCallPreconditions = [.. binding.Clauses.Select(c =>
                    c is null ? throw new System.ArgumentNullException(nameof(c)) : (new TotalSourcePrecondition(c.Value, c.SafeCondition, c.SourceOperation)))];
                var exactHelper = new RoslynProgramLowerer(factory).LowerCandidate(helperGraph, candidate);
                Assert.That(candidate.Compilation, Is.SameAs(compilation));
                Assert.That(exactHelper.IsExact, Is.True);
                Assert.That(exactHelper.ConstructionLimitExceeded, Is.False);
                var replay = new IrProgramInterpreter(factory).Execute(exactHelper.Program,
                    candidate.Parameters.ToDictionary(p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (p.Entry), p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (factory.CreateIntegerValue(factory.GetVariableInfo(p.Entry).Type, 5000))));
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(5001)));
                return exactHelper.IsExact;
            }
            var session = new TotalSourceCallSession(compilation, Prepare, null, default);
            var lowered = new RoslynTotalProgramLowerer(context, default, session).Lower(targetGraph);
            Assert.That(preparedCount, Is.GreaterThan(0));
            Assert.That(session.IsActive(context.Target), Is.False);
            Assert.That(session.IsActive(helperSymbol), Is.False);
            Assert.That(session.ConstructionLimitExceeded, Is.EqualTo(limited));
            Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(limited));
            Assert.That(lowered.IsExact, Is.EqualTo(!limited));
            TestContext.WriteLine($"actual work result clauses={clauses}; calls={calls}; prepared={preparedCount}; reason={lowered.Classification.Abstention}; markers={lowered.CallPreconditions.Count}");
            if (!limited)
            {
                Assert.That(preparedCount, Is.EqualTo(calls));
                Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
                Assert.That(lowered.CallPreconditions, Has.Count.EqualTo(clauses * calls));
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    context.Parameters.ToDictionary(p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (p.Entry), p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (factory.CreateIntegerValue(factory.GetVariableInfo(p.Entry).Type, 5000))));
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(5000 + calls)));
            }
            // The resource-limited result is never replayed. Every distinct
            // real Requires and call participates in this raw importer API;
            // the production4096 work allowance is unchanged.
        }
        finally
        {
            load.Unload();
        }
    }
}
