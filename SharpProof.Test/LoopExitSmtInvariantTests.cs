using System;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test
{
    [TestFixture]
    [Category("SmtHeavy")]
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
        public void SymbolicSourceQueryService_ProvesLoopExitConditionWhenBodyThrowDoesNotReachAfterLoop()
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
                throw new System.InvalidOperationException();
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
        public void SymbolicSourceQueryService_ProvesMultipleTopLevelGuardedBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        for (;;)
        {
            if (value < 0)
            {
                break;
            }

            if (value > 10)
            {
                break;
            }

            value++;
        }

        return value;
    }
}";

            var proof = ProveAtMarker(source, "return value;", "value < 0 || value > 10");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveMultipleGuardedBreakExitWhenGuardValueMutates()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        for (;;)
        {
            if (value < 0)
            {
                break;
            }

            value = 100;
            if (value > 10)
            {
                break;
            }
        }

        return value;
    }
}";

            var proof = ProveAtMarker(source, "return value;", "value < 0 || value > 10");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesGuardedContinueBeforeBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready)
    {
        for (;;)
        {
            if (!ready)
            {
                continue;
            }

            break;
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesNestedGuardedContinueBeforeBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool blocked)
    {
        for (;;)
        {
            if (ready)
            {
                if (blocked)
                {
                    continue;
                }
            }

            break;
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "!ready || !blocked");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesMultipleGuardedContinuesBeforeBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool blocked)
    {
        for (;;)
        {
            if (!ready)
            {
                continue;
            }

            if (blocked)
            {
                continue;
            }

            break;
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready && !blocked");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesGuardedContinuesBeforeGuardedBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool blocked, bool done)
    {
        for (;;)
        {
            if (!ready)
            {
                continue;
            }

            if (blocked)
            {
                continue;
            }

            if (done)
            {
                break;
            }
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready && !blocked && done");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveGuardedContinueBeforeGuardedBreakWhenGuardMutates()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool done)
    {
        for (;;)
        {
            if (!ready)
            {
                continue;
            }

            ready = false;
            if (done)
            {
                break;
            }
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready && done");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveGuardedContinueConditionWhenInterveningStatementMutatesGuard()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready)
    {
        for (;;)
        {
            if (!ready)
            {
                continue;
            }

            ready = false;
            break;
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesNestedGuardedBreakExitCondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool done)
    {
        for (;;)
        {
            if (ready)
            {
                if (done)
                {
                    break;
                }
            }
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready && done");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveNestedGuardedBreakExitWhenGuardMutates()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool ready, bool done)
    {
        for (;;)
        {
            if (ready)
            {
                ready = false;
                if (done)
                {
                    break;
                }
            }
        }

        return ready ? 1 : 0;
    }
}";

            var proof = ProveAtMarker(source, "return ready ? 1 : 0;", "ready && done");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesForLoopBodyLowerBoundWhenConditionLocalMutatesBeforeMarker()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int count)
    {
        var i = -1;
        for (i = 0; i < count; i++)
        {
            count = -100;
            return i;
        }

        return -1;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesConditionlessForLoopBodyLowerBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var i = -1;
        for (i = 0;; i++)
        {
            return i;
        }
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesGuardedBreakForLoopExitLowerBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool done)
    {
        var i = -1;
        for (i = 0;; i++)
        {
            if (done)
            {
                break;
            }
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotProveGuardedBreakForLoopExitLowerBoundWhenBodyMutatesIterator()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool done)
    {
        var i = 0;
        for (i = 0;; i++)
        {
            i = -1;
            if (done)
            {
                break;
            }
        }

        return i;
    }
}";

            var proof = ProveAtMarker(source, "return i;", "i >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
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
