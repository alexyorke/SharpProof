using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
internal sealed class ClosedContractAttributeBindingAdditionalDomainTests
{
    private static readonly ImmutableArray<long> PredicateInputs = [-1L, 2L, 4L];
    private static readonly ImmutableArray<string> MarkerNames = ["MarkerAttribute", "PositiveAttribute"];

    private static readonly ImmutableArray<string> MetadataIntegerDiagnostics =
    [
        "[Positive] count",
        "[InRange(1, 3)] count"
    ];

    [TestCase("NotNull", "string", IrExecutionSemantics.Legacy, ContractBindingFailure.InvalidClosedAttribute)]
    [TestCase("NotNull", "string", IrExecutionSemantics.Total, ContractBindingFailure.UnsupportedExpression)]
    [TestCase("Positive", "int", IrExecutionSemantics.Legacy, ContractBindingFailure.InvalidClosedAttribute)]
    [TestCase("Positive", "int", IrExecutionSemantics.Total, ContractBindingFailure.UnsupportedExpression)]
    [TestCase("InRange(-2L, 2L)", "int", IrExecutionSemantics.Legacy, ContractBindingFailure.InvalidClosedAttribute)]
    [TestCase("InRange(-2L, 2L)", "int", IrExecutionSemantics.Total, ContractBindingFailure.UnsupportedExpression)]
    public void EveryRealClosedAttributeFamilyRejectsAValidBooleanIrValue(
        string attributeText,
        string sourceType,
        IrExecutionSemantics semantics,
        ContractBindingFailure expectedFailure)
    {
        ArgumentNullException.ThrowIfNull(attributeText);
        ArgumentNullException.ThrowIfNull(sourceType);
        var compilation = CreateParameterCompilation(attributeText, sourceType);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var parameter = GetMethod(compilation, "Read").Parameters.Single();
        var attributes = parameter.GetAttributes();
        Assert.That(attributes, Has.Length.EqualTo(1));
        Assert.That(attributes[0].ApplicationSyntaxReference, Is.Not.Null);

        var factory = new IrFactory(semantics);
        var variable = factory.CreateVariable("legalBoolean", factory.BooleanType);
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationCalls = 0;
        var result = ClosedContractAttributeBinding.BindValue(
            factory,
            api.Selections,
            attributes,
            parameter.Type,
            parameter.RefKind,
            () =>
            {
                valueRequests++;
                return value;
            },
            parameter.Name,
            BoundContractKind.Requires,
            total: semantics == IrExecutionSemantics.Total,
            syntax =>
            {
                operationCalls++;
                return factory.CreateOperation("held-boolean-projection");
            });

        Assert.That(factory.GetTypeInfo(value.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
        Assert.That(result.Failure, Is.EqualTo(expectedFailure));
        Assert.That(result.Clauses, Is.Empty);
        Assert.That(valueRequests, Is.EqualTo(1));
        Assert.That(operationCalls, Is.Zero);
    }

    [TestCase("NotNull", "string", IrExecutionSemantics.Legacy)]
    [TestCase("NotNull", "string", IrExecutionSemantics.Total)]
    [TestCase("Positive", "int", IrExecutionSemantics.Legacy)]
    [TestCase("Positive", "int", IrExecutionSemantics.Total)]
    [TestCase("InRange(1L, 3L)", "int", IrExecutionSemantics.Legacy)]
    [TestCase("InRange(1L, 3L)", "int", IrExecutionSemantics.Total)]
    public void MissingLegalIrValueFailsClosedForRealAnnotations(
        string attributeText,
        string sourceType,
        IrExecutionSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(attributeText);
        ArgumentNullException.ThrowIfNull(sourceType);
        var compilation = CreateParameterCompilation(attributeText, sourceType);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var parameter = GetMethod(compilation, "Read").Parameters.Single();
        var valueRequests = 0;
        var operationCalls = 0;
        var factory = new IrFactory(semantics);
        var result = ClosedContractAttributeBinding.BindValue(
            factory,
            api.Selections,
            parameter.GetAttributes(),
            parameter.Type,
            parameter.RefKind,
            () =>
            {
                valueRequests++;
                return null;
            },
            parameter.Name,
            BoundContractKind.Requires,
            total: semantics == IrExecutionSemantics.Total,
            syntax =>
            {
                operationCalls++;
                return factory.CreateOperation("held-missing-value");
            });

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(result.Clauses, Is.Empty);
        Assert.That(valueRequests, Is.EqualTo(1));
        Assert.That(operationCalls, Is.Zero);
    }

    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void OutParameterIsRejectedBeforeRequestingItsIrValue(IrExecutionSemantics semantics)
    {
        var compilation = TestCompilation.Create("ClosedAttributeOutParameter", """
            using SharpProof.Attributes;
            public static class Target {
                public static void Read([Positive] out int value) { value = 0; }
            }
            """);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var parameter = GetMethod(compilation, "Read").Parameters.Single();
        Assert.That(parameter.RefKind, Is.EqualTo(RefKind.Out));

        var factory = new IrFactory(semantics);
        var variable = factory.CreateVariable("entry", factory.IntegerType);
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationCalls = 0;
        var result = ClosedContractAttributeBinding.BindValue(
            factory,
            api.Selections,
            parameter.GetAttributes(),
            parameter.Type,
            parameter.RefKind,
            () =>
            {
                valueRequests++;
                return value;
            },
            parameter.Name,
            BoundContractKind.Requires,
            total: semantics == IrExecutionSemantics.Total,
            syntax =>
            {
                operationCalls++;
                return factory.CreateOperation("held-out-parameter");
            });

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(result.Clauses, Is.Empty);
        Assert.That(valueRequests, Is.Zero);
        Assert.That(operationCalls, Is.Zero);
    }

    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void RealNonContractMarkerIsSkippedAroundAClosedAnnotation(IrExecutionSemantics semantics)
    {
        var compilation = TestCompilation.Create("ClosedAttributeMarker", """
            using System;
            using SharpProof.Attributes;
            public sealed class MarkerAttribute : Attribute { }
            public static class Target {
                public static void Read([Marker, Positive] int value) { }
            }
            """);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var parameter = GetMethod(compilation, "Read").Parameters.Single();
        var attributes = parameter.GetAttributes();
        Assert.That(attributes.Select(static attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass!.Name)),
            Is.EqualTo(MarkerNames));

        var factory = new IrFactory(semantics);
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationSiteBuilder = ImmutableArray.CreateBuilder<SyntaxNode?>();
        var result = ClosedContractAttributeBinding.BindValue(
            factory,
            api.Selections,
            attributes,
            parameter.Type,
            parameter.RefKind,
            () =>
            {
                valueRequests++;
                return value;
            },
            parameter.Name,
            BoundContractKind.Requires,
            total: semantics == IrExecutionSemantics.Total,
            syntax =>
            {
                operationSiteBuilder.Add(syntax);
                return factory.CreateOperation("held-marker-control");
            });
        var operationSites = operationSiteBuilder.ToImmutable();

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Clauses[0].DiagnosticText, Is.EqualTo("[Positive] value"));
        Assert.That(result.Clauses[0].SourceSyntax, Is.Not.Null);
        Assert.That(valueRequests, Is.EqualTo(1));
        Assert.That(operationSites, Has.Length.EqualTo(1));
        Assert.That(operationSites[0] is not null, Is.EqualTo(semantics == IrExecutionSemantics.Total));

        var interpreter = new IrInterpreter(factory);
        var negative = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(factory.IntegerType, -1));
        var positive = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(factory.IntegerType, 2));
        Assert.That(interpreter.Evaluate(result.Clauses[0].Condition, negative).Value!.Boolean, Is.False);
        Assert.That(interpreter.Evaluate(result.Clauses[0].Condition, positive).Value!.Boolean, Is.True);
    }

    [TestCase("text", IrExecutionSemantics.Legacy)]
    [TestCase("text", IrExecutionSemantics.Total)]
    [TestCase("count", IrExecutionSemantics.Legacy)]
    [TestCase("count", IrExecutionSemantics.Total)]
    public void MetadataAttributesKeepLegacyFallbackAndRequireSourceSyntaxInTotal(
        string parameterName,
        IrExecutionSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(parameterName);
        var (consumer, method) = CreateMetadataConsumer();
        var api = ContractApiSymbols.TryCreate(consumer)!;
        var parameter = method.Parameters.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Name == parameterName));
        var attributes = parameter.GetAttributes();
        Assert.That(attributes, Is.Not.Empty);
        Assert.That(attributes.All(static attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.ApplicationSyntaxReference is null)), Is.True);

        var isText = parameterName == "text";
        var factory = new IrFactory(semantics);
        var type = isText ? factory.StringType : factory.IntegerType;
        var variable = factory.CreateVariable(parameter.Name, type);
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationCalls = 0;
        var result = ClosedContractAttributeBinding.BindValue(
            factory,
            api.Selections,
            attributes,
            parameter.Type,
            parameter.RefKind,
            () =>
            {
                valueRequests++;
                return value;
            },
            parameter.Name,
            BoundContractKind.Requires,
            total: semantics == IrExecutionSemantics.Total,
            syntax =>
            {
                operationCalls++;
                return factory.CreateOperation("held-metadata-attribute");
            });

        if (semantics == IrExecutionSemantics.Total)
        {
            Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
            Assert.That(result.Clauses, Is.Empty);
            Assert.That(valueRequests, Is.Zero);
            Assert.That(operationCalls, Is.Zero);
            return;
        }

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(valueRequests, Is.EqualTo(isText ? 1 : 2));
        Assert.That(operationCalls, Is.EqualTo(isText ? 1 : 2));
        if (isText)
        {
            Assert.That(result.Clauses, Has.Length.EqualTo(1));
            Assert.That(result.Clauses[0].DiagnosticText, Is.EqualTo("[NotNull] text"));
            var interpreter = new IrInterpreter(factory);
            Assert.That(interpreter.Evaluate(result.Clauses[0].Condition,
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateNullValue(factory.StringType))).Value!.Boolean,
                Is.False);
            Assert.That(interpreter.Evaluate(result.Clauses[0].Condition,
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateStringValue("present"))).Value!.Boolean,
                Is.True);
            return;
        }

        Assert.That(result.Clauses, Has.Length.EqualTo(2));
        Assert.That(result.Clauses.Select(static clause => clause.DiagnosticText),
            Is.EqualTo(MetadataIntegerDiagnostics));
        var integerInterpreter = new IrInterpreter(factory);
        foreach (var input in PredicateInputs)
        {
            var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(factory.IntegerType, input));
            Assert.That(integerInterpreter.Evaluate(result.Clauses[0].Condition, values).Value!.Boolean,
                Is.EqualTo(input > 0));
            Assert.That(integerInterpreter.Evaluate(result.Clauses[1].Condition, values).Value!.Boolean,
                Is.EqualTo(input is >= 1 and <= 3));
        }
    }

    private static CSharpCompilation CreateParameterCompilation(string attributeText, string sourceType)
    {
        System.ArgumentNullException.ThrowIfNull(attributeText);
        System.ArgumentNullException.ThrowIfNull(sourceType);
        return TestCompilation.Create("ClosedAttributeParameterDomain", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static void Read([{{attributeText}}] {{sourceType}} value) { }
            }
            """);
    }

    private static (CSharpCompilation Consumer, IMethodSymbol Method) CreateMetadataConsumer()
    {
        var library = TestCompilation.Create("ClosedAttributeMetadataLibrary", """
            using SharpProof.Attributes;
            public static class Library {
                public static void Read([NotNull] string text, [Positive, InRange(1L, 3L)] int count) { }
            }
            """);
        using var image = new MemoryStream();
        var emit = library.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join(System.Environment.NewLine,
            emit.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))));

        var consumer = TestCompilation.Create("ClosedAttributeMetadataConsumer", "public static class Consumer {}")
            .AddReferences(MetadataReference.CreateFromImage(ImmutableArray.Create(image.GetBuffer(), 0, checked((int)image.Length))));
        TestCompilation.AssertNoErrors(consumer);
        var method = consumer.GetTypeByMetadataName("Library")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        return (consumer, method);
    }

    private static IMethodSymbol GetMethod(CSharpCompilation compilation, string name)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(name);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var method = compilation.GetTypeByMetadataName("Target")!
            .GetMembers(name).OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        return method;
    }
}
