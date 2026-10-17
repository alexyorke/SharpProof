using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Fuzz.Test;

public sealed class PartialDiscardSourceUnsupportedControls
{
    [TestCase("int.TryParse(\"0\", out _)", true, true)]
    [TestCase("int.TryParse(\"invalid\", out _)", false, true)]
    [TestCase("0 == 0", true, false)]
    [TestCase("0 != 0", false, false)]
    public async Task EmittedSourcePreservesItsActualReferenceAndReportsUnsupportedInterpretation(
        string expression, bool expectedReference, bool unsupported)
    {
        var source = $$"""
            public static class DiscardTryParsePartialReference
            {
                public static bool Predicate() => {{expression}};
                public static bool Completion()
                {
                    try { _ = Predicate(); return true; }
                    catch (System.Exception) { return false; }
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "discard-try-parse-partial-reference.cs");
        var compilation = CSharpCompilation.Create("DiscardTryParsePartialReference", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Predicate");
        var symbol = model.GetDeclaredSymbol(declaration);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(symbol.Parameters, Is.Empty);
        Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        var operation = model.GetOperation(declaration.ExpressionBody!.Expression);
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.Syntax.SyntaxTree, Is.SameAs(tree));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lowering = new RoslynOperationLowerer(factory).Lower(operation);
        Assert.That(lowering.IsExact, Is.EqualTo(!unsupported));
        if (unsupported)
        {
            Assert.That(lowering.Term, Is.TypeOf<IrOpaqueTerm>());
        }
        Assert.That(lowering.Term.Type, Is.EqualTo(factory.BooleanType));
        Assert.That(IrTermAnalysis.CollectVariables(lowering.Term), Is.Empty);
        await TestContext.Progress.WriteLineAsync("Actual TryParse lowering abstention: " + lowering.Classification.Abstention);

        var context = new AssemblyLoadContext("DiscardTryParsePartialReference", isCollectible: true);
        bool completed;
        bool reference;
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("DiscardTryParsePartialReference");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime!.Assembly.GetName().Name, Is.EqualTo(compilation.AssemblyName));
            var predicate = runtime.GetMethod("Predicate", BindingFlags.Public | BindingFlags.Static);
            var completion = runtime.GetMethod("Completion", BindingFlags.Public | BindingFlags.Static);
            Assert.That(predicate, Is.Not.Null);
            Assert.That(completion, Is.Not.Null);
            completed = (bool)completion!.Invoke(null, [])!;
            Assert.That(completed, Is.True);
            reference = (bool)predicate!.Invoke(null, [])!;
            Assert.That(reference, Is.EqualTo(expectedReference));
        }
        finally
        {
            context.Unload();
        }
        var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
        var evaluated = new IrInterpreter(factory).Evaluate(lowering.Term, scenario);
        Assert.That(evaluated.Status,
            Is.EqualTo(unsupported ? IrEvaluationStatus.Unsupported : IrEvaluationStatus.Value));
        if (!unsupported)
        {
            Assert.That(evaluated.Value!.Kind, Is.EqualTo(IrValueKind.Boolean));
            Assert.That(evaluated.Value.Boolean, Is.EqualTo(reference));
        }
        using var session = FuzzSmtSession.Create(factory);
        var query = new VerificationQuery(factory, [],
            new Goal(factory, lowering.Term, ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0)));
        var proof = await new ProofKernel(session).VerifyAsync(query, cancellationToken: CancellationToken.None);
        if (unsupported)
        {
            Assert.That(proof, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)proof).Reason, Is.EqualTo(AbstentionReason.UnsupportedEncoding));
        }
        else if (reference)
        {
            Assert.That(proof, Is.TypeOf<ProvenOutcome>());
        }
        else
        {
            Assert.That(proof, Is.TypeOf<RefutedOutcome>());
        }
        var generated = new PartialTermSmtCase(lowering.Term, factory.Boolean(completed), [scenario],
            [reference ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse]);
        var result = await PartialTermSmtDifferentialOracle.CompareAsync(factory, generated, cancellationToken: CancellationToken.None);
        Assert.That(result.Status,
            Is.EqualTo(unsupported ? FuzzOracleStatus.Mismatch : FuzzOracleStatus.Agreement));
        Assert.That(result.ScenarioCount, Is.EqualTo(1));
        Assert.That(result.DefinedTrueCount, Is.EqualTo(!unsupported && reference ? 1 : 0));
        Assert.That(result.DefinedFalseCount, Is.EqualTo(!unsupported && !reference ? 1 : 0));
        Assert.That(result.UndefinedCount, Is.Zero);
        Assert.That(result.Detail, Is.EqualTo(unsupported
            ? "The guarded interpreter disagrees with C# execution in scenario 0."
            : ""));
    }
}
