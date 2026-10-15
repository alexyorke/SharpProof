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

public sealed class PartialSourceCompletionControls
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ActualCorrectCompletionKeepsItsLegacyCompilerAndNativeBoundary(bool fault, bool opaque)
    {
        var parameters = fault ? "long value" : "bool value";
        var targetBody = fault ? "1L / (value - value) == 0L" : "value";
        var completionBody = opaque ? fault ? "!(value is var _)" : "value is var _" : fault ? "false" : "true";
        var source = "public static class PartialCompletionSourcePremise { " +
            "public static bool Target(" + parameters + ") => " + targetBody + "; " +
            "public static bool NormalCompletion(" + parameters + ") => " + completionBody + "; }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("PartialCompletionSourcePremise", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true));
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var methods = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText, StringComparer.Ordinal);
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory();
        var bindings = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
        var formula = LowerActual("Target");
        var completion = LowerActual("NormalCompletion");
        Assert.That(formula.IsExact, Is.True);
        Assert.That(completion.IsExact, Is.EqualTo(!opaque));
        Assert.That(completion.Variables, Is.Empty);
        if (opaque)
        {
            Assert.That(completion.Term, Is.TypeOf<IrOpaqueTerm>());
        }
        bool? returned = null;
        Exception? actualFault = null;
        bool actualCompletion;
        var context = new AssemblyLoadContext("PartialCompletionSourcePremise", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("PartialCompletionSourcePremise");
            Assert.That(runtime, Is.Not.Null);
            object?[] arguments = [fault ? (object)7L : true];
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
            Assert.That(actualFault, Is.TypeOf<DivideByZeroException>());
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
            Assert.That(formulaResult.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        }
        var completionResult = interpreter.Evaluate(completion.Term, bindings);
        Assert.That(completionResult.Status, Is.EqualTo(opaque ? IrEvaluationStatus.Unsupported : IrEvaluationStatus.Value));
        if (!opaque)
        {
            Assert.That(completionResult.Value!.Boolean, Is.EqualTo(actualCompletion));
        }
        var generated = new PartialTermSmtCase(formula.Term, completion.Term, [bindings], [reference]);
        Func<Task> compare = async () => await PartialTermSmtDifferentialOracle.CompareAsync(factory, generated);
        var exception = Assert.ThrowsAsync<ArgumentException>(compare);
        Assert.That(exception!.ParamName, Is.EqualTo("factory"));
        Assert.That(exception.Message, Does.StartWith("A callable bitvector session requires Total IR semantics."));
        await TestContext.Out.WriteLineAsync("Actual Legacy admission: " + exception.Message);
        FrontendLoweringResult LowerActual(string name)
        {
            var syntax = methods[name];
            var symbol = model.GetDeclaredSymbol(syntax);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.IsStatic, Is.True);
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.Parameters.Single().Type.SpecialType,
                Is.EqualTo(fault ? SpecialType.System_Int64 : SpecialType.System_Boolean));
            var operation = model.GetOperation(syntax.ExpressionBody!.Expression);
            Assert.That(operation, Is.Not.Null);
            var lowered = new RoslynOperationLowerer(factory).Lower(operation!);
            foreach (var binding in lowered.Variables)
            {
                Assert.That(binding.Symbol, Is.InstanceOf<IParameterSymbol>());
                var parameter = (IParameterSymbol)binding.Symbol;
                Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, symbol), Is.True);
                Assert.That(parameter.Ordinal, Is.Zero);
                bindings = bindings.Add(binding.Variable, fault
                    ? factory.CreateIntegerValue(factory.GetVariableInfo(binding.Variable).Type, 7L)
                    : factory.CreateBooleanValue(true));
            }
            return lowered;
        }
    }
}
