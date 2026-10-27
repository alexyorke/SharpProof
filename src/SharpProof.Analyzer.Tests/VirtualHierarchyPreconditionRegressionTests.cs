using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class VirtualHierarchyPreconditionRegressionTests
{
    [TestCase(false, false, 1)]
    [TestCase(true, false, 1)]
    [TestCase(false, true, 0)]
    [TestCase(true, true, 0)]
    public async Task EveryDispatchContractMustPermitLocalRequires(bool useBase, bool allMatching, int expected)
    {
        var open = allMatching ? "[Positive] " : "";
        var ancestor = useBase
            ? "public class Open { public virtual void Run(" + open + "int value) {} }"
            : "public interface Open { void Run(" + open + "int value); }";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; " + ancestor + """
            public interface Strict { void Run([Positive] int value); }
            public class Service : Open, Strict {
                public
            """ + (useBase ? " override " : " ") + "void Run([Positive] int value) {} }",
            "contracts", []);
        Assert.That(diagnostics.Count(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "SP0024")), Is.EqualTo(expected));
        Assert.That(diagnostics.Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "AD0001")), Is.False);
    }

    [TestCase("v99999999999999999999", "v99999999999999999999", 0)]
    [TestCase("v3", "v6", 1)]
    [TestCase("same", "same", 0)]
    public async Task RequiresNormalizationPreservesStringLiterals(string inherited, string local, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(inherited);
        System.ArgumentNullException.ThrowIfNull(local);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public class Base { public virtual void Run(string original) { " +
            "Contract.Requires(original != \"" + inherited + "\"); } } " +
            "public class Derived : Base { public override void Run(string renamed) { " +
            "Contract.Requires(renamed != \"" + local + "\"); } }",
            "contracts", []);
        Assert.That(diagnostics.Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "AD0001")), Is.False);
        Assert.That(diagnostics.Count(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "SP0024")), Is.EqualTo(expected));
    }
}
