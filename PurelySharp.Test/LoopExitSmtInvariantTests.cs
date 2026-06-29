using System;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class LoopExitSmtInvariantTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesDoWhileNormalExitConditionFalse()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var observed = 0;
        do
        {
            observed++;
        }
        while (value < 0);

        return value;
    }
}";

            var proof = ProveAtMarker(source, "return value;", "value >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesForLoopExitMonotonicLowerBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int count)
    {
        var i = -1;
        for (i = 0; i < count; i++)
        {
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesForLoopExitMonotonicUpperBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int limit)
    {
        var i = 0;
        for (i = limit - 1; i >= 0; i--)
        {
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i < limit");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesForLoopExitMonotonicSourceLowerBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int start, int count)
    {
        var i = 0;
        for (i = start; i < count; i++)
        {
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= start");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesReverseForLoopExitInclusiveInitialUpperBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int limit)
    {
        var i = 0;
        for (i = limit; i >= 0; i--)
        {
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i <= limit");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesLoopExitConditionWhenBodyReturnDoesNotReachAfterLoop()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        while (index < values.Length)
        {
            if (index < 0)
            {
                return -1;
            }

            index++;
        }

        return index;
    }
}";

            var proof = ProveAtMarker(source, "return index;", "index >= values.Length");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesSingleGuardedBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready)
    {
        for (;;)
        {
            if (ready)
            {
                break;
            }
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveForLoopExitBoundWhenBreakCanExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int count)
    {
        var i = -1;
        for (i = 0; i < count; i++)
        {
            break;
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveForLoopExitUpperBoundWhenBodyMutatesBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int limit)
    {
        var i = 0;
        for (i = limit - 1; i >= 0; i--)
        {
            limit = -100;
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i < limit");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        private static SymbolicConditionProofResult ProveAtMarker(
            string source,
            string marker,
            string condition)
        {
            var location = FindMarker(source, marker);
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "LoopExitSmtInvariantTests.cs",
                location.Line,
                location.Column,
                condition,
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
        }

        private static (int Line, int Column) FindMarker(string source, string marker)
        {
            var lines = source.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var column = lines[index].IndexOf(marker, StringComparison.Ordinal);
                if (column >= 0)
                {
                    return (index + 1, column + 1);
                }
            }

            throw new InvalidOperationException("Marker was not found in source.");
        }
    }
}
