using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ContractsCoverageBoundaryTests
{
    [Test]
    public void UnrecognizedCompilerAttributesKeepTheExactNameAccessFailure()
    {
        var compilation = TestCompilation.Create("ContractsAttributeRecognitionBoundary",
            """
            using SharpProof.Attributes;
            public static class Subject {
                [System.Obsolete]
                public static int Read([Positive] int value) => value;
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var parameter = method.Parameters.Single();
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var unrecognized = ClosedContractAttributeValidator.Validate(
            method.GetAttributes().Single(), parameter.Type, parameter.RefKind, selections);
        Assert.That(unrecognized.IsRecognized, Is.False);
        Assert.That(unrecognized.IsValid, Is.False);
        Assert.That(unrecognized.Kind, Is.EqualTo(ClosedContractAttributeKind.None));
        var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
            _ = unrecognized.AttributeName));
        Assert.That(exception!.Message, Is.EqualTo("The attribute is not a closed contract."));

        var recognized = ClosedContractAttributeValidator.Validate(
            parameter.GetAttributes().Single(), parameter.Type, parameter.RefKind, selections);
        Assert.That(recognized.IsRecognized, Is.True);
        Assert.That(recognized.IsValid, Is.True);
        Assert.That(recognized.Kind, Is.EqualTo(ClosedContractAttributeKind.Positive));
        Assert.That(recognized.AttributeName, Is.EqualTo("[Positive]"));
        var result = new ContractBinder(compilation, new IrFactory()).Bind(method);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Contracts.Clauses[0].Evidence, Is.EqualTo(BoundContractEvidence.ClosedAttribute));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExpressionBodiedPropertyDeclarationsRetainTheirCompilerExpression(bool indexer)
    {
        var compilation = TestCompilation.Create("ContractsExpressionBodyBoundary",
            """
            public sealed class Subject {
                public int Value => 42;
                public int this[int index] => index;
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        BasePropertyDeclarationSyntax declaration = indexer
            ? root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Single()
            : root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var expression = indexer
            ? ((IndexerDeclarationSyntax)declaration).ExpressionBody!.Expression
            : ((PropertyDeclarationSyntax)declaration).ExpressionBody!.Expression;
        var body = ContractClauseInventoryBuilder.GetBody(declaration);
        Assert.That(body, Is.SameAs(expression));
        var model = compilation.GetSemanticModel(tree);
        var operation = model.GetOperation(body!);
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.Kind, Is.EqualTo(indexer
            ? OperationKind.ParameterReference
            : OperationKind.Literal));
        var property = (IPropertySymbol)model.GetDeclaredSymbol(declaration)!;
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(property.GetMethod!);
        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.HasRejectedContractApiUsage, Is.False);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    [Test]
    public void CompanionResolutionRejectsAnAbsentIndexAndTheBinderSuppliesItsRealIndex()
    {
        var compilation = TestCompilation.Create("ContractsRequiredIndexBoundary",
            """
            using SharpProof.Attributes;
            public interface Target { int Read(int value); }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(Target receiver, int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        var target = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var exception = Assert.Throws<ArgumentNullException>(new Action(() =>
            ContractForSymbolMatcher.ResolveCompanion(target, null!)));
        Assert.That(exception!.ParamName, Is.EqualTo("targetIndex"));

        var result = new ContractBinder(compilation, new IrFactory()).Bind(target);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Contracts.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task FieldAndPropertyInitializersUseOnlyAvailableContractIntrinsics(
        bool includeContractApi, bool resultIntrinsic)
    {
        var initializer = resultIntrinsic ? "SharpProof.Attributes.Contract.Result<int>()" : "0";
        var compilation = TestCompilation.Create("ContractsInitializerBoundary",
            $$"""
            public sealed class Subject {
                public int Field = {{initializer}};
                public int Value { get; } = {{initializer}};
            }
            """);
        var tree = compilation.SyntaxTrees.Single();
        var ordinaryOptions = ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(Array.Empty<string>());
        compilation = compilation.RemoveAllSyntaxTrees()
            .AddSyntaxTrees(tree.WithRootAndOptions(await tree.GetRootAsync(), ordinaryOptions));
        Assert.That(ordinaryOptions.PreprocessorSymbolNames, Is.Empty);
        if (!includeContractApi)
        {
            compilation = compilation.WithReferences(TestMetadataReferences.WithoutSharpProof);
            TestCompilation.AssertNoErrors(compilation);
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var options = new AnalyzerOptions([], new ContractOptionsProvider());
        var diagnostics = await compilation.WithAnalyzers([new SharpProofAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics, Has.Length.EqualTo(resultIntrinsic ? 2 : 0),
            string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Id + ": " +
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
        foreach (var diagnostic in diagnostics)
        {
            Assert.That(diagnostic.Id, Is.EqualTo("SP0024"));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(
                "SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataBindingRequiresTheRuntimeContractApi(bool includeContractApi)
    {
        var compilation = CreateMetadataConsumer(
            "public static class Library { public static int Target(int value) => value; }",
            includeContractApi);
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(includeContractApi
            ? ContractBindingFailure.None
            : ContractBindingFailure.ContractApiUnavailable));
        Assert.That(result.Clauses, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataBindingDoesNotBorrowSourceAttributeEvidence(bool metadata)
    {
        const string source = "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => value; }";
        var compilation = metadata
            ? CreateMetadataConsumer(source, includeContractApi: true)
            : TestCompilation.Create("ContractsSourceBoundary", source);
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(metadata
            ? ContractBindingFailure.None
            : ContractBindingFailure.UnsupportedTarget));
        Assert.That(result.Clauses, Has.Length.EqualTo(metadata ? 1 : 0));
        if (metadata)
        {
            Assert.That(result.Clauses[0].Attribute.ApplicationSyntaxReference, Is.Null);
            Assert.That(result.Clauses[0].ParameterOrdinal, Is.Zero);
        }
    }

    [TestCase("int", true)]
    [TestCase("ref int", false)]
    public void MetadataBindingRejectsReferencesWithoutSourceWriteEvidence(string parameterType, bool byValue)
    {
        var compilation = CreateMetadataConsumer(
            $"public static class Library {{ public static int Target({parameterType} value) => 0; }}",
            includeContractApi: true);
        var method = compilation.GetTypeByMetadataName("Library")!
            .GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.Parameters[0].RefKind, Is.EqualTo(byValue ? RefKind.None : RefKind.Ref));
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(byValue
            ? ContractBindingFailure.None
            : ContractBindingFailure.UnsupportedTarget));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static TotalMetadataRequiresBindingResult Bind(CSharpCompilation compilation)
    {
        var method = compilation.GetTypeByMetadataName("Library")!
            .GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        return new ContractBinder(compilation, factory)
            .BindTotalMetadataRequires(new TotalLoweringContext(factory, method));
    }

    private static CSharpCompilation CreateMetadataConsumer(string source, bool includeContractApi)
    {
        var library = TestCompilation.Create("ContractsMetadataBoundaryLibrary", source);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var references = includeContractApi
            ? TestMetadataReferences.WithSharpProof
            : TestMetadataReferences.WithoutSharpProof;
        var compilation = CSharpCompilation.Create("ContractsMetadataBoundaryConsumer",
            [CSharpSyntaxTree.ParseText("public static class Consumer { }")],
            references.Add(MetadataReference.CreateFromImage(image.ToArray())),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private sealed class ContractOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new ContractOptions();
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _options;
        }
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _options;
        }
    }

    private sealed class ContractOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = key switch
            {
                "sharpproof_profile" => "strict",
                "sharpproof_features" => "contracts",
                _ => string.Empty
            };
            return value.Length != 0;
        }
    }
}
