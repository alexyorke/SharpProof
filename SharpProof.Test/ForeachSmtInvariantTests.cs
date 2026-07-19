using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using static SharpProof.Test.SemanticOracleSmtTestBase;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class ForeachSmtInvariantTests
{
    [Test]
    public void SymbolicQueryExecutor_ProvesFiniteForeachReferenceElementNonNull()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new[] { ""safe"", ""fallback"" })
        {
            return value.Length;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "FiniteForeachReferenceElementNonNull.cs",
            FindLine(source, "return value.Length;"),
            20,
            "value != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_DoesNotProveFiniteForeachReferenceElementNonNullWhenNullIsPossible()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new string[] { null, ""fallback"" })
        {
            return value == null ? 0 : value.Length;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "FiniteForeachReferenceElementMaybeNull.cs",
            FindLine(source, "return value == null ? 0 : value.Length;"),
            20,
            "value != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesPriorAssignedFiniteForeachReferenceElementNonNull()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { new object(), new object() };
        foreach (var value in values)
        {
            return value.GetHashCode();
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "PriorAssignedFiniteForeachReferenceElementNonNull.cs",
            FindLine(source, "return value.GetHashCode();"),
            20,
            "value != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesPriorAssignedFiniteForeachArrayElementAtomsNonZero()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var source = new[] { 1, 2 };
        var values = new[] { source[0], source[^1] };
        foreach (var value in values)
        {
            return value;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "PriorAssignedFiniteForeachArrayElementAtomsNonZero.cs",
            FindLine(source, "return value;"),
            20,
            "value != 0",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesPriorAssignedFiniteForeachTupleElementAtomsNonZero()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (left: 1, right: 2);
        var values = new[] { pair.left, pair.right };
        foreach (var value in values)
        {
            return value;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "PriorAssignedFiniteForeachTupleElementAtomsNonZero.cs",
            FindLine(source, "return value;"),
            20,
            "value != 0",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_DoesNotReevaluatePriorAssignedForeachCapturedLocalAfterMutation()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var seed = 1;
        var values = new[] { seed };
        seed = 0;
        foreach (var value in values)
        {
            return value;
        }

        return seed;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "PriorAssignedFiniteForeachCapturedLocalAfterMutation.cs",
            FindLine(source, "return value;"),
            20,
            "value == 0",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesCompletedForeachReceiverNonNull()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        foreach (var value in values)
        {
        }

        return values.Length;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "CompletedForeachReceiverNonNull.cs",
            FindLine(source, "return values.Length;"),
            16,
            "values != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_DoesNotProveCompletedForeachReceiverNonNullWhenBodyReassignsReceiver()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        foreach (var value in values)
        {
            values = null;
        }

        return values == null ? 0 : values.Length;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "CompletedForeachReceiverMaybeNull.cs",
            FindLine(source, "return values == null ? 0 : values.Length;"),
            16,
            "values != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesForeachCoalesceThrowReceiverNonNullInBody()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        foreach (var value in values ?? throw new System.InvalidOperationException())
        {
            return values.Length;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "ForeachCoalesceThrowReceiverNonNull.cs",
            FindLine(source, "return values.Length;"),
            20,
            "values != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_ProvesForeachConditionalThrowReceiverGuardInBody()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(bool enabled, string[] values)
    {
        foreach (var value in enabled ? values : throw new System.InvalidOperationException())
        {
            return enabled ? values.Length : 0;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "ForeachConditionalThrowReceiverGuard.cs",
            FindLine(source, "return enabled ? values.Length : 0;"),
            20,
            "enabled",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void SymbolicQueryExecutor_ForeachCoalesceThrowReceiverFactYieldsToBodyMutation()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        foreach (var value in values ?? throw new System.InvalidOperationException())
        {
            values = null;
            return values == null ? 0 : values.Length;
        }

        return 0;
    }
}";

        var proof = new SymbolicQueryExecutor().ProveConditionAtSource(
            source,
            "ForeachCoalesceThrowReceiverMutation.cs",
            FindLine(source, "return values == null ? 0 : values.Length;"),
            20,
            "values != null",
            new SmtAnalysisService(SmtAnalysisOptions.Default),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse));
    }

    [Test]
    public async Task Sp0010_FiniteForeachNonNullElementContradictoryNullDereference_DoesNotReport()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            @"
public class TestClass
{
    public int TestMethod()
    {
        foreach (var value in new[] { ""safe"", ""fallback"" })
        {
            if (value == null)
            {
                return value.Length;
            }
        }

        return 0;
    }
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

        Assert.That(diagnostics.Any(d => d.Id == "SP0010"), Is.False);
    }

}
