using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;
using SharpProof.Testing;
using SharpProof.Verify;

namespace SharpProof.Fuzz.Test;

public sealed class PartialNativeObservationControls
{
    [Test]
    public async Task ActualClrAndNativeQueriesFeedTheObservationProjection()
    {
        const string source = "public static class NativeObservationSample { " +
            "public static bool Target() => true; " +
            "public static bool Control() => false; " +
            "public static bool Unsupported(bool value) => value is var _; }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("NativeObservationSample", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var methods = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText), StringComparer.Ordinal);
        Assert.That(methods, Has.Count.EqualTo(3));
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = LowerActual("Target");
        var control = LowerActual("Control");
        var unsupported = LowerActual("Unsupported");
        Assert.That(target.IsExact, Is.True);
        Assert.That(control.IsExact, Is.True);
        Assert.That(unsupported.IsExact, Is.False);
        Assert.That(unsupported.Term, Is.TypeOf<IrOpaqueTerm>());
        bool targetClr;
        bool controlClr;
        var context = new AssemblyLoadContext("NativeObservationSample", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("NativeObservationSample");
            Assert.That(runtime, Is.Not.Null);
            targetClr = (bool)runtime!.GetMethod("Target")!.Invoke(null, [])!;
            controlClr = (bool)runtime.GetMethod("Control")!.Invoke(null, [])!;
            Assert.That((bool)runtime.GetMethod("Unsupported")!.Invoke(null, [true])!, Is.True);
        }
        finally
        {
            context.Unload();
        }
        Assert.That(targetClr, Is.True);
        Assert.That(controlClr, Is.False);
        var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
        var interpreter = new IrInterpreter(factory);
        var targetIr = interpreter.Evaluate(target.Term, scenario);
        var controlIr = interpreter.Evaluate(control.Term, scenario);
        Assert.That(targetIr.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(controlIr.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(targetIr.Value!.Boolean, Is.EqualTo(targetClr));
        Assert.That(controlIr.Value!.Boolean, Is.EqualTo(controlClr));
        Assert.That(interpreter.Evaluate(unsupported.Term, scenario).Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        var expected = targetClr ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse;
        using var session = FuzzSmtSession.Create(factory);
        var kernel = new ProofKernel(session);
        var positive = await kernel.VerifyAsync(Query(target.Term, 0), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var negative = await kernel.VerifyAsync(Query(control.Term, 1), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var unknown = await kernel.VerifyAsync(Query(unsupported.Term, 2), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(positive, Is.TypeOf<ProvenOutcome>());
        Assert.That(negative, Is.TypeOf<RefutedOutcome>());
        Assert.That(unknown, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)unknown).Reason, Is.EqualTo(AbstentionReason.UnsupportedEncoding));
        Assert.That(PartialTermSmtDifferentialOracle.ObserveBackend(expected,
            PartialTermSmtDifferentialOracle.Classify(positive), positive, 0), Is.Null);
        // Deliberately project another query's real refutation against Target's
        // actual reference. This is component rejection, not an oracle mismatch.
        var rejected = PartialTermSmtDifferentialOracle.ObserveBackend(expected,
            PartialTermSmtDifferentialOracle.Classify(negative), negative, 0);
        Assert.That(rejected, Is.Not.Null);
        Assert.That(rejected!.Value.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(rejected.Value.Detail, Is.EqualTo(
            "The interpreter reported DefinedTrue while the backend reported DefinedFalse for partial-term scenario 0."));
        var abstained = PartialTermSmtDifferentialOracle.ObserveBackend(expected,
            PartialTermSmtDifferentialOracle.Classify(unknown), unknown, 0);
        Assert.That(abstained, Is.Not.Null);
        Assert.That(abstained!.Value.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(abstained.Value.Detail, Is.EqualTo(
            "The backend returned Unknown(UnsupportedEncoding) for partial-term scenario 0."));
        await TestContext.Out.WriteLineAsync("Real same-query proof accepted; other-query refutation rejected by component; real Unknown abstained.");

        VerificationQuery Query(IrTerm term, int index)
        {
            System.ArgumentNullException.ThrowIfNull(term);
            return new(factory, [], new Goal(factory, term,
                ProofDiagnosticKind.InternalConsistency, new SourceLocationId(index)));
        }

        FrontendLoweringResult LowerActual(string name)
        {
            System.ArgumentNullException.ThrowIfNull(name);
            var syntax = methods[name];
            var symbol = model.GetDeclaredSymbol(syntax);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.IsStatic, Is.True);
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            var operation = model.GetOperation(syntax.ExpressionBody!.Expression);
            Assert.That(operation, Is.Not.Null);
            var lowered = new RoslynOperationLowerer(factory).Lower(operation!);
            Assert.That(lowered.Variables, Is.Empty);
            return lowered;
        }
    }
}
