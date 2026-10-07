using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class GeneratedLocalWorkflowControlTests
{
    [Test]
    public async Task GeneratedLocalEffectsHonorSelection(
        [Values("advisory", "strict")] string profile,
        [Values("handwritten", "filename", "symbol")] string generatedBy,
        [Values("selected-mutation", "selected-pure", "unselected-mutation")] string scenario)
    {
        var selected = scenario != "unselected-mutation";
        var mutates = scenario != "selected-pure";
        var attribute = selected ? "[EnforcePure] " : string.Empty;
        var body = mutates ? "State++;" : string.Empty;
        var nested = attribute + "static void Nested() { " + body + " } Nested();";
        var generatedAttribute = generatedBy == "symbol"
            ? "[System.CodeDom.Compiler.GeneratedCode(\"audit\", \"1\")]"
            : string.Empty;
        var source = $$"""
            using SharpProof.Attributes;
            {{generatedAttribute}}
            public static class Subject {
                public static int State;
                [EnforcePure] public static int SelectedControl() => 1;
                public static void Run() {
                    {{nested}}
                }
            }
            """;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source, mode: null, enabledIds: ["SP0002"],
            profile: profile, features: "effects",
            filePath: generatedBy == "filename" ? "Subject.g.cs" : "Subject.cs");
        if (selected && mutates)
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
            Assert.That(diagnostics.Single().Location.SourceTree?.FilePath,
                Is.EqualTo(generatedBy == "filename" ? "Subject.g.cs" : "Subject.cs"));
        }
        else
        {
            Assert.That(diagnostics, Is.Empty);
        }
    }

    [TestCase("local")]
    [TestCase("lambda")]
    public async Task GeneratedSelectedMalformedEffectAttributeIsDiagnosed(string callable)
    {
        const string attribute = "[EnforcePure, EffectContract((SharpProofEffect)(1L << 40), Complete = true)] ";
        var nested = callable == "local"
            ? attribute + "static void Nested() { } Nested();"
            : "var nested = " + attribute + "() => { }; nested();";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { public static void Run() { " + nested + " } }",
            mode: null, enabledIds: ["SP0024"], profile: "advisory", features: "effects",
            filePath: "Subject.g.cs");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
    }

    [TestCase("local")]
    [TestCase("lambda")]
    public async Task GeneratedRejectedEffectAttributeIsVisiblyIncomplete(string callable)
    {
        var nested = callable == "local"
            ? "[SharpProof.Attributes.EnforcePure] static void Nested() { } Nested();"
            : "var nested = [SharpProof.Attributes.EnforcePure] () => { }; nested();";
        var source = $$"""
            namespace SharpProof.Attributes {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class EnforcePureAttribute : System.Attribute { }
            }
            public static class Subject {
                public static void Run() { {{nested}} }
            }
            """;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source, mode: null, enabledIds: ["SP0047"], profile: "advisory", features: "effects",
            filePath: "Subject.g.cs");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
        AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "ContractApiIdentityRejected");
    }
}
