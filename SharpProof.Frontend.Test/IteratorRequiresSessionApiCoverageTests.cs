using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class IteratorRequiresSessionApiCoverageTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ContractOnlyIteratorPreparationRejectsActualBoundRequires(bool iterator, bool requires)
    {
        var clause = requires ? "SharpProof.Attributes.Contract.Requires(value > 0);" : "";
        var body = iterator ? "yield return value;" : "return new[] { value };";
        var source = "public static class Subject { public static int Target() { _ = Values(17); return 17; } " +
            "private static System.Collections.Generic.IEnumerable<int> Values(int value) { " + clause + body + " } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "iterator-requires.cs");
        var references = TestMetadataReferences.Platform.Add(
            MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.Contract).Assembly.Location));
        var compilation = CSharpCompilation.Create("IteratorRequiresSessionApi", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var loadContext = new AssemblyLoadContext("IteratorRequiresSessionApi", isCollectible: true);
        try
        {
            output.Position = 0;
            var assembly = loadContext.LoadFromStream(output);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(17));
        }
        finally
        {
            loadContext.Unload();
        }
        var model = compilation.GetSemanticModel(tree);
        var target = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Target");
        var callSyntax = target.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(callSyntax)!;
        var helper = (MethodDeclarationSyntax)invocation.TargetMethod.DeclaringSyntaxReferences.Single().GetSyntax();
        Assert.That(helper.DescendantNodes().OfType<YieldStatementSyntax>().Any(), Is.EqualTo(iterator));
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(invocation.Arguments[0].Value.ConstantValue.Value, Is.EqualTo(17));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var caller = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(target)!);
        var prepared = 0;
        bool PrepareCallee(TotalLoweringContext candidate)
        {
            Assert.That(candidate.Factory, Is.SameAs(factory));
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, invocation.TargetMethod), Is.True);
            var binding = new ContractBinder(compilation, factory).BindTotalRequires(candidate);
            Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
            Assert.That(binding.Clauses, Has.Length.EqualTo(requires ? 1 : 0));
            Assert.That(binding.Clauses.All(static value => value.Kind == BoundContractKind.Requires), Is.True);
            candidate.SourceCallPreconditions = [.. binding.Clauses.Where(static value => value.Kind == BoundContractKind.Requires)
                .Select(static value => new TotalSourcePrecondition(value.Value, value.SafeCondition, value.SourceOperation))];
            prepared++;
            return binding.IsSuccess;
        }
        var session = new TotalSourceCallSession(compilation, PrepareCallee, null, default);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accepted = session.TryPrepare(caller, invocation.TargetMethod, invocation.Instance,
                invocation.Arguments, out var frame, out var graph, contractOnly: true);
            Assert.That(accepted, Is.EqualTo(iterator && !requires));
            Assert.That(graph, Is.Null);
            if (!iterator || requires)
            {
                Assert.That(frame, Is.Null);
            }
            else
            {
                Assert.That(frame, Is.Not.Null);
                Assert.That(frame!.SourceCallPreconditions, Has.Length.EqualTo(requires ? 1 : 0));
            }
            Assert.That(session.ConstructionLimitExceeded, Is.False);
            Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
        }
        Assert.That(prepared, Is.EqualTo(iterator ? 2 : 0));
        using var control = TypedProgramSubject.Create("int Target() => 17;");
        Assert.That(control.Invoke([]), Is.EqualTo(17));
        var lowering = control.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var replay = control.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}
