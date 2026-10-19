using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    [TestCase("Min")]
    [TestCase("Max")]
    public void ReviewedMetadataCostsAreConditionalAndCarryProvenance(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var result = Analyze($"for(int i=0;i<n;i++) {{ int x = System.Math.{method}(i,m); }}");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("max(0, n)"));
        Assert.That(result.Bounds.Arrays.Text, Is.EqualTo("0"));
        Assert.That(result.Bounds.Program.Children.Single().Kind, Is.EqualTo(SharpProof.Ir.ResourceProgramKind.MetadataCall));
        Assert.That(result.Assumptions, Has.Length.EqualTo(1));
        Assert.That(result.Assumptions[0], Does.Contain(ResourceMetadataCostCatalog.ContentSha256));
        Assert.That(result.Assumptions[0], Does.Contain(ResourceMetadataCostCatalog.EvidenceSource));
        Assert.That(result.Assumptions[0], Does.Contain("net8.0.32"));
    }

    [TestCase("int value=n.GetHashCode();", "OpaqueCall")]
    [TestCase("int value=System.Math.Max(n++,0);", "UnstableInputOrCounter")]
    public void UnsupportedMetadataReceiversAndArgumentsHaveNoPartialCosts(string body, string reason)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.Assumptions, Is.Empty);
    }
    [Test]
    public void SourceLookalikeDoesNotReceiveMetadataCosts()
    {
        var result = AnalyzeSource("""
            namespace System {
                public static class Math {
                    public static int Max(int a,int b) { while(true) {} }
                }
            }
            public static class C { public static void Root() { int value=System.Math.Max(1,2); } }
            """);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo("UnsupportedLoop"));
        Assert.That(result.Assumptions, Is.Empty);
    }

    [Test]
    public async Task ConditionalMetadataEvidenceIsExplicitInDiagnostics()
    {
        const string source = "public static class C { public static int M(int n) => System.Math.Max(n,0); }";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, null, ["SP0060"]);
        var result = diagnostics.Single();
        Assert.That(result.Properties["ResourceProofBasis"], Is.EqualTo("ConditionalOnMetadataSpecifications"));
        Assert.That(result.Properties["ResourceAssumptions"], Does.Contain("bcl.math.max.int32-int32"));
        Assert.That(result.Properties["ResourceMetadataCatalogSha256"], Has.Length.EqualTo(64));
        Assert.That(result.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("under recorded metadata assumptions"));
    }

    [Test]
    public void AGapDoesNotPublishPartialMetadataProofEvidence()
    {
        var result = Analyze("int x = System.Math.Max(n,m); System.Console.WriteLine(x);");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.Assumptions, Is.Empty);
        Assert.That(result.UnknownReason, Is.EqualTo("OpaqueCall"));
    }
}
