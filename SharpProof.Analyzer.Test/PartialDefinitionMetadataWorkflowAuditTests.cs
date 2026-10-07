using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class PartialDefinitionMetadataWorkflowAuditTests
{
    [TestCase("advisory", false)]
    [TestCase("advisory", true)]
    [TestCase("strict", false)]
    [TestCase("strict", true)]
    public async Task HandwrittenPartialContractSurvivesGeneratedImplementation(string profile, bool generatedImplementation)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public static partial class Subject {
                [EffectContract((SharpProofEffect)(1L << 40), Complete = true)]
                public static partial void Run();
            }
            """, ["SP0024"], filePath: "Subject.Definition.cs");
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static partial class Subject { public static partial void Run() { } }",
            new CSharpParseOptions(LanguageVersion.Preview),
            generatedImplementation ? "Subject.Implementation.g.cs" : "Subject.Implementation.cs"));
        var definition = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var implementation = definition.PartialImplementationPart;
        Assert.That(implementation, Is.Not.Null);
        var attribute = implementation!.GetAttributes().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
            compilation.GetTypeByMetadataName("SharpProof.Attributes.EffectContractAttribute")), Is.True);
        Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo(1L << 40));
        Assert.That(attribute.ApplicationSyntaxReference!.SyntaxTree.FilePath, Is.EqualTo("Subject.Definition.cs"));
        RuntimeAssemblyTestHost.WithRuntimeAssembly("PartialDefinitionMetadataWorkflowAudit",
            AnalyzerTestHost.EmitImage(compilation), assembly =>
        {
            var method = assembly.GetType("Subject", throwOnError: true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
            var emittedAttribute = method.CustomAttributes.Single(item =>
                item.AttributeType == typeof(SharpProof.Attributes.EffectContractAttribute));
            Assert.That(emittedAttribute.ConstructorArguments.Single().Value, Is.EqualTo(1L << 40));
        });
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: profile, features: "effects");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        Assert.That(diagnostics.Single().Location.SourceTree?.FilePath, Is.EqualTo("Subject.Definition.cs"));
    }
}
