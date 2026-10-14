using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class MetadataRequiresDeclaredBoundsTests
{
    [Test]
    public void EmittedValidDeclaredBoundsHaveIndependentPredicateTruth()
    {
        var (compilation, target) = Import("Valid");
        Assert.That(target.Parameters[0].GetAttributes().Single().ConstructorArguments.Select(argument => argument.Value),
            Is.EqualTo(new object[] { -2L, 2L }));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var result = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Clauses[0].ParameterOrdinal, Is.Zero);
        Assert.That(result.Clauses[0].Validation.IsRecognized, Is.True);
        Assert.That(result.Clauses[0].Validation.IsValid, Is.True);
        var variable = context.Parameters.Single().Entry;
        var type = factory.GetVariableInfo(variable).Type;
        var interpreter = new IrInterpreter(factory);
        foreach (var input in new[] { -3, -2, 0, 2, 3 })
        {
            var values = new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValue(type, (long)input)
            };
            Assert.That(interpreter.Evaluate(result.Clauses[0].Value, values).Value!.Boolean,
                Is.EqualTo(input >= -2 && input <= 2));
            Assert.That(interpreter.Evaluate(result.Clauses[0].SafeCondition, values).Value!.Boolean, Is.True);
        }
    }

    [TestCase("Invalid", 1)]
    [TestCase("LateInvalid", 2)]
    public void EmittedInvalidDeclaredBoundsRejectWithoutPublishingPrefix(string methodName, int parameterCount)
    {
        var (compilation, target) = Import(methodName);
        Assert.That(target.Parameters.Length, Is.EqualTo(parameterCount));
        var invalid = target.Parameters.Last().GetAttributes().Single();
        Assert.That(invalid.ConstructorArguments.Select(argument => argument.Value),
            Is.EqualTo(new object[] { 2L, 1L }));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var result = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(result.Clauses, Is.Empty);
        if (parameterCount == 2)
        {
            Assert.That(target.Parameters[0].GetAttributes().Single().ConstructorArguments.Select(argument => argument.Value),
                Is.EqualTo(new object[] { -2L, 2L }));
        }
    }

    private static (Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation, IMethodSymbol Target) Import(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        var library = TestCompilation.Create("DeclaredMetadataBoundsLibrary", """
            using SharpProof.Attributes;
            public static class DeclaredBounds {
                public static bool Valid([InRange(-2L, 2L)] int value) => value >= -2 && value <= 2;
                public static bool Invalid([InRange(2L, 1L)] int value) => false;
                public static bool LateInvalid([InRange(-2L, 2L)] int first, [InRange(2L, 1L)] int second) => false;
            }
            """);
        TestCompilation.AssertNoErrors(library);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var compilation = TestCompilation.Create("DeclaredMetadataBoundsConsumer", "public static class Consumer {}")
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        TestCompilation.AssertNoErrors(compilation);
        using var consumerImage = new MemoryStream();
        var consumerEmit = compilation.Emit(consumerImage);
        Assert.That(consumerEmit.Success, Is.True, string.Join(Environment.NewLine, consumerEmit.Diagnostics));
        var target = compilation.GetTypeByMetadataName("DeclaredBounds")!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(target.IsStatic, Is.True);
        Assert.That(target.Parameters.All(parameter => parameter.RefKind == RefKind.None &&
            parameter.Type.SpecialType == SpecialType.System_Int32), Is.True);
        return (compilation, target);
    }
}
