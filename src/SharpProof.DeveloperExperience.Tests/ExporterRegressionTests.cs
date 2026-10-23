using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class ExporterRegressionTests
{
    [TestCase("value = 5;")]
    [TestCase("value++;")]
    [TestCase("value += 5;")]
    [TestCase("Mutate(ref value);")]
    [TestCase("void Change() { value++; } Change();")]
    [TestCase("unsafe { int* pointer = &value; *pointer = 5; }")]
    public void AssignedParameterExitValuesCannotBeReplacedWithEntryInputs(string mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(mutation);
        var error = Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            _ = Rewrite("Contract.Result<int>() != value", mutation);
        }));
        Assert.That(error!.Message, Does.Contain("parameter's exit value"));
    }

    [Test]
    public void OldStillNamesAnAssignedParametersEntryValue()
    {
        var assertion = Rewrite("Contract.Result<int>() != Contract.Old(value)", "value++;");
        Assert.That(assertion, Does.Contain("_sharpProofResult").And.Contain("_argument0"));
    }

    [Test]
    public void UnchangedParametersRemainObservable()
    {
        var assertion = Rewrite("Contract.Result<int>() != value", "");
        Assert.That(assertion, Does.Contain("_sharpProofResult").And.Contain("_argument0"));
    }

    [TestCase("(value > 0) && Contract.Result<int>() > Limit")]
    [TestCase("checked(Contract.Result<int>() + 1) > value")]
    [TestCase("unchecked(Contract.Result<int>() + int.MaxValue) > value")]
    [TestCase("(byte)Contract.Result<int>() == (byte)value")]
    [TestCase("checked((byte)Contract.Result<int>()) == 0")]
    [TestCase("~Contract.Result<int>() == -value")]
    [TestCase("Contract.Result<int>() == (value > 0 ? 1 : 2)")]
    [TestCase("Contract.Result<int>() == Marker")]
    [TestCase("Contract.Result<int>() == Contract.Old(value)")]
    [TestCase("(bool)(Contract.Result<int>() > value)")]
    public void ScalarRewritingProducesACompilableBooleanAssertion(string condition)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        var assertion = Rewrite(condition, "");
        Assert.That(assertion, Does.Not.Contain("Contract.").And.Contain("_sharpProofResult"));
        var result = CompanionScaffoldingTests.Compilation("public static class Replay { public static bool Check(int _argument0, int _sharpProofResult) => " + assertion + "; }");
        Assert.That(result.GetDiagnostics().Where(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error), Is.Empty, assertion);
    }

    [TestCase("System.Math.Abs(value) > 0", "")]
    [TestCase("Property > value", "")]
    [TestCase("StaticValue > value", "")]
    [TestCase("Contract.Old(value + 1) > 0", "")]
    [TestCase("nameof(value) == \"value\"", "")]
    public void UnsupportedPostconditionOperationsFailBeforeWritingATest(string condition, string mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        ArgumentNullException.ThrowIfNull(mutation);
        var error = Assert.Throws<InvalidOperationException>(new Action(() => { _ = Rewrite(condition, mutation); }));
        Assert.That(error!.Message, Does.Contain("does not support operation").And.Contain("No test was written"));
    }

    [Test]
    public void ExpressionsWithoutMethodBodiesCannotBeRewritten()
    {
        var compilation = CompanionScaffoldingTests.Compilation("public static class Api { public static bool Run(int value) => value > 0; }");
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        var error = Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            _ = new NUnitExporter.ScalarContractRewriter(model, symbol).Rewrite(method.ExpressionBody!.Expression);
        }));
        Assert.That(error!.Message, Does.Contain("requires a method body"));
    }

    [Test]
    public void ContractRecognitionRequiresTheBoundSharpProofContractType()
    {
        var compilation = CompanionScaffoldingTests.Compilation("using SharpProof.Attributes; public static class Api { public static int Run(int value) { Contract.Ensures(value > 0); Fake.Ensures(value > 0); return value; } } public static class Fake { public static void Ensures(bool value) { } }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(NUnitExporter.IsContract(model, invocations[0], "Ensures"), Is.True);
            Assert.That(NUnitExporter.IsContract(model, invocations[0], "Result"), Is.False);
            Assert.That(NUnitExporter.IsContract(model, invocations[1], "Ensures"), Is.False);
        }
    }

    [Test]
    public void TypeQualifierRegionsCannotBecomeObservableParameterStorage()
    {
        var compilation = CompanionScaffoldingTests.Compilation("public static class Api { public static int Run(int value) { return int.MaxValue; } }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var qualifier = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single().Expression;
        var invalidRegion = model.AnalyzeDataFlow(qualifier)!;
        Assert.That(invalidRegion.Succeeded, Is.False);
        var error = Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            _ = NUnitExporter.ScalarContractRewriter.GetUnobservableStorage(invalidRegion);
        }));
        Assert.That(error!.Message, Does.Contain("Parameter storage could not be analyzed"));
    }

    [Test]
    public void MissingRoslynAnalysisCannotBecomeObservableParameterStorage()
    {
        Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            _ = NUnitExporter.ScalarContractRewriter.GetUnobservableStorage(null);
        }));
    }

    [Test]
    public void SuccessfulRoslynRegionsExposeExactlyWrittenOrAddressTakenStorage()
    {
        var compilation = CompanionScaffoldingTests.Compilation("public static class Api { public static int Run(int first, int second) { first++; return first + second; } }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var body = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
        var region = model.AnalyzeDataFlow(body);
        var storage = NUnitExporter.ScalarContractRewriter.GetUnobservableStorage(region).Select(static symbol =>
            (symbol ?? throw new ArgumentNullException(nameof(symbol))).Name).ToArray();
        Assert.That(storage, Is.EqualTo(ImmutableArray.Create("first")));
    }

    [TestCase(".")]
    [TestCase("Regressions")]
    [TestCase("nested/Regressions")]
    public void OutputInsideConsumerWouldBeIncludedByItsCompileGlob(string relativeOutput)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeOutput);
        var directory = Path.Combine(Path.GetTempPath(), "sharpproof-export-layout", "Consumer");
        var project = Path.Combine(directory, "Consumer.csproj");
        var error = Assert.Throws<IOException>(new Action(() =>
        {
            NUnitExporter.RequireIndependentOutputDirectory(project, Path.Combine(directory, relativeOutput));
        }));
        Assert.That(error!.Message, Does.Contain("sibling directory"));
    }

    [Test]
    public void OutputInASiblingDirectoryKeepsConsumerCompileGlobsIndependent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sharpproof-export-layout", "Consumer");
        NUnitExporter.RequireIndependentOutputDirectory(Path.Combine(directory, "Consumer.csproj"),
            Path.Combine(directory, "..", "Consumer.Regressions"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ProjectInputsBindToTheSelectedConsumerEvenWhenOriginalFilesExist(bool consumerInputExists)
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpproof-export-binding-" + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(root, "original");
        var consumer = Path.Combine(root, "consumer");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(consumer);
        try
        {
            var originalInput = Path.Combine(original, "Api.cs");
            var consumerInput = Path.Combine(consumer, "Api.cs");
            File.WriteAllText(originalInput, "original source");
            if (consumerInputExists)
            {
                File.WriteAllText(consumerInput, "changed consumer source");
                Assert.That(NUnitExporter.Remap(originalInput, original, consumer), Is.EqualTo(consumerInput));
            }
            else
            {
                Assert.Throws<FileNotFoundException>(new Action(() =>
                {
                    _ = NUnitExporter.Remap(originalInput, original, consumer);
                }));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ExistingExternalInputsRemainAvailableButProjectTraversalIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpproof-export-remap-" + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(root, "original");
        var consumer = Path.Combine(root, "consumer");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(consumer);
        try
        {
            var external = Path.Combine(root, "external.cs");
            File.WriteAllText(external, "external source");
            Assert.That(NUnitExporter.Remap(external, original, consumer), Is.EqualTo(external));
            Assert.Throws<FileNotFoundException>(new Action(() =>
            {
                _ = NUnitExporter.Remap(Path.Combine(original, "..", "external.cs"), original, consumer);
            }));
            Assert.Throws<FileNotFoundException>(new Action(() =>
            {
                _ = NUnitExporter.Remap(Path.Combine(root, "missing.cs"), original, consumer);
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public void RecordedNugetReferencesCanMapIntoTheCurrentPackageCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpproof-export-nuget-" + Guid.NewGuid().ToString("N"));
        var previousPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var current = Path.Combine(root, "packages", "library", "1.0.0", "lib", "Library.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(current, "captured binary");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(root, "packages"));
            Assert.That(NUnitExporter.Remap("/missing/.nuget/packages/library/1.0.0/lib/Library.dll", "/missing/project", root), Is.EqualTo(current));
            Assert.Throws<FileNotFoundException>(new Action(() =>
            {
                _ = NUnitExporter.Remap("/missing/.nuget/packages/library/2.0.0/lib/Library.dll", "/missing/project", root);
            }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousPackages);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Rewrite(string condition, string mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        ArgumentNullException.ThrowIfNull(mutation);
        var compilation = CompanionScaffoldingTests.Compilation($$"""
            using SharpProof.Attributes;
            public static class Api
            {
                public const int Limit = 3;
                public const char Marker = 'z';
                private static int Property => 1;
                private static int StaticValue = 1;
                public static int Run(int value)
                {
                    Contract.Ensures({{condition}});
                    {{mutation}}
                    return value;
                }

                private static void Mutate(ref int value) { value++; }
            }
            """);
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
        {
            global::SharpProof.Internal.InputValidation.Require(diagnostic, nameof(diagnostic));
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(static declaration =>
        {
            global::SharpProof.Internal.InputValidation.Require(declaration, nameof(declaration));
            return declaration.Identifier.ValueText == "Run";
        });
        var model = compilation.GetSemanticModel(tree);
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        var ensures = (InvocationExpressionSyntax)((ExpressionStatementSyntax)method.Body!.Statements[0]).Expression;
        return new NUnitExporter.ScalarContractRewriter(model, symbol).Rewrite(ensures.ArgumentList.Arguments[0].Expression);
    }
}
