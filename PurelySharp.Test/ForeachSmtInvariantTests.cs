using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ForeachSmtInvariantTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesFiniteForeachReferenceElementNonNull()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public void SymbolicSourceQueryService_DoesNotProveFiniteForeachReferenceElementNonNullWhenNullIsPossible()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public void SymbolicSourceQueryService_ProvesPriorAssignedFiniteForeachReferenceElementNonNull()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public void SymbolicSourceQueryService_ProvesPriorAssignedFiniteForeachArrayElementAtomsNonZero()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public void SymbolicSourceQueryService_ProvesPriorAssignedFiniteForeachTupleElementAtomsNonZero()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public void SymbolicSourceQueryService_DoesNotReevaluatePriorAssignedForeachCapturedLocalAfterMutation()
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

            var proof = new SymbolicSourceQueryService().ProveConditionAtSource(
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
        public async Task Ps0010_FiniteForeachNonNullElementContradictoryNullDereference_DoesNotReport()
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
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        private static int FindLine(string source, string text)
        {
            var lines = source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(text, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            throw new InvalidOperationException("Text was not found in source.");
        }
    }
}
