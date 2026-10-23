using Microsoft.CodeAnalysis;
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

    private static string Rewrite(string condition, string mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        ArgumentNullException.ThrowIfNull(mutation);
        var compilation = CompanionScaffoldingTests.Compilation($$"""
            using SharpProof.Attributes;
            public static class Api
            {
                public static int Run(int value)
                {
                    Contract.Ensures({{condition}});
                    {{mutation}}
                    return value;
                }

                private static void Mutate(ref int value) { value++; }
            }
            """);
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
