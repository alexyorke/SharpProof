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

namespace SharpProof.Fuzz.Test;

public sealed class FuzzTotalSourceCompletionControls
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ActualCorrectCompletionKeepsItsOwnedTotalSourceAndNativeBoundary(bool fault, bool opaque)
    {
        const string parameters = "bool value";
        var targetBody = fault ? "((string)null!).StartsWith(\"x\")" : "value";
        var completionBody = opaque ? fault ? "!(value is var _)" : "value is var _" : fault ? "false" : "true";
        var members = "public static bool Target(" + parameters + ") => " + targetBody + "; " +
            "public static bool NormalCompletion(" + parameters + ") => " + completionBody + ";";
        var source = "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var methods = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText), StringComparer.Ordinal);
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var bindings = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
        var formula = LowerActual("Target");
        var completion = LowerActual("NormalCompletion");
        Assert.That(formula.IsExact, Is.EqualTo(!fault));
        Assert.That(completion.IsExact, Is.EqualTo(!opaque));
        Assert.That(completion.Variables, Is.Empty);
        if (opaque)
        {
            Assert.That(completion.Term, Is.TypeOf<IrOpaqueTerm>());
        }
        bool? returned = null;
        Exception? actualFault = null;
        bool actualCompletion;
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            Assert.That(runtime, Is.Not.Null);
            object?[] arguments = [true];
            try
            {
                returned = (bool)runtime!.GetMethod("Target")!.Invoke(null, arguments)!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                actualFault = exception.InnerException;
            }
            actualCompletion = (bool)runtime!.GetMethod("NormalCompletion")!.Invoke(null, arguments)!;
        }
        finally
        {
            context.Unload();
        }
        Assert.That(actualCompletion, Is.EqualTo(actualFault == null));
        if (fault)
        {
            Assert.That(actualFault, Is.TypeOf<NullReferenceException>());
        }
        else
        {
            Assert.That(actualFault, Is.Null);
            Assert.That(returned, Is.True);
        }
        var reference = actualFault != null ? PartialTermSemanticOutcome.Undefined
            : returned == true ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse;
        var interpreter = new IrInterpreter(factory);
        var formulaResult = interpreter.Evaluate(formula.Term, bindings);
        Assert.That(formulaResult.Status, Is.EqualTo(fault ? IrEvaluationStatus.Exception : IrEvaluationStatus.Value));
        if (fault)
        {
            Assert.That(formulaResult.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
        }
        var completionResult = interpreter.Evaluate(completion.Term, bindings);
        Assert.That(completionResult.Status, Is.EqualTo(opaque ? IrEvaluationStatus.Unsupported : IrEvaluationStatus.Value));
        if (!opaque)
        {
            Assert.That(completionResult.Value!.Boolean, Is.EqualTo(actualCompletion));
        }
        Assert.That(reference, Is.EqualTo(fault ? PartialTermSemanticOutcome.Undefined : PartialTermSemanticOutcome.DefinedTrue));
        Assert.That(bindings.Count, Is.EqualTo(fault ? 0 : 1));
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        const string finite = "public static bool Target(bool value) => value;";
        var campaign = await FuzzRunner.ReplaySourceCasesAsync([new(positive, finite, [true], members, [[true]])], cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(campaign.Cases, Is.EqualTo(1));
        Assert.That(campaign.FrontendAgreements, Is.EqualTo(1));
        Assert.That(campaign.SmtAgreements, Is.EqualTo(1));
        Assert.That(campaign.PartialSmtAgreements, Is.EqualTo(opaque ? 0 : 1));
        Assert.That(campaign.Agreements, Is.EqualTo(opaque ? 0 : 1));
        Assert.That(campaign.Abstentions, Is.EqualTo(opaque ? 1 : 0));
        Assert.That(campaign.Failures.Length, Is.EqualTo(opaque ? 1 : 0));
        var result = campaign.PartialResults.Single();
        await TestContext.Out.WriteLineAsync("fault=" + fault + "; opaque=" + opaque + "; status=" + result.Status + "; detail=" + result.Detail);
        Assert.That(result.Status, Is.EqualTo(opaque ? FuzzOracleStatus.Abstained : FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.ScenarioCount, Is.EqualTo(1));
        Assert.That(result.UndefinedCount, Is.EqualTo(actualFault != null ? 1 : 0));
        Assert.That(result.DefinedTrueCount, Is.EqualTo(returned == true ? 1 : 0));
        Assert.That(result.DefinedFalseCount, Is.Zero);
        Assert.That(result.Detail, opaque ? Does.Contain("Unknown(UnsupportedEncoding)") : Is.Empty);

        FrontendLoweringResult LowerActual(string name)
        {
            System.ArgumentNullException.ThrowIfNull(name);
            var syntax = methods[name];
            var symbol = model.GetDeclaredSymbol(syntax);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.IsStatic, Is.True);
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.Parameters.Single().Type.SpecialType,
                Is.EqualTo(SpecialType.System_Boolean));
            var operation = model.GetOperation(syntax.ExpressionBody!.Expression);
            Assert.That(operation, Is.Not.Null);
            var lowered = new RoslynOperationLowerer(factory).Lower(operation!);
            foreach (var binding in lowered.Variables)
            {
                Assert.That(binding.Symbol, Is.InstanceOf<IParameterSymbol>());
                var parameter = (IParameterSymbol)binding.Symbol;
                Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, symbol), Is.True);
                Assert.That(parameter.Ordinal, Is.Zero);
                bindings = bindings.Add(binding.Variable, factory.CreateBooleanValue(true));
            }
            return lowered;
        }
    }
}
