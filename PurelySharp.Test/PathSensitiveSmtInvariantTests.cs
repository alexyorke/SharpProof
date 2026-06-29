using System;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class PathSensitiveSmtInvariantTests
    {
        [Test]
        public void SymbolicSourceQueryService_ProvesRelationalPatternSnapshotAfterSourceReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value is > 0 and < 10)
        {
            var divisor = value;
            value = 0;
            return 10 / divisor;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var query = AnalyzeAtPosition(source, marker.Position);
            var proof = ProveAtMarker(source, marker, "divisor > 0 && divisor < 10");

            Assert.That(query.MergedInvariantText, Does.Contain("divisor"));
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesListSliceLengthSnapshotAfterSourceReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [_, .., _])
        {
            var copy = values;
            values = null;
            return copy.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return copy.Length;");
            var query = AnalyzeAtPosition(source, marker.Position);
            var proof = ProveAtMarker(source, marker, "copy != null && copy.Length >= 2");

            Assert.That(query.MergedInvariantText, Does.Contain("copy"));
            Assert.That(query.MergedInvariantText, Does.Contain("Length"));
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesNullPatternSnapshotAfterSourceReassignment()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string text)
    {
        if (text is null)
        {
            var copy = text;
            text = ""fallback"";
            return copy;
        }

        return text;
    }
}";

            var marker = FindMarker(source, "return copy;");
            var proof = ProveAtMarker(source, marker, "copy == null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesTupleDeconstructionSnapshotAfterSourceReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        pair = (0, 0);
        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 1");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesSwitchStatementPropertyPatternStructuralFact()
        {
            const string source = @"
public sealed class Box
{
    public int Count { get; init; }

    public object Tag { get; init; }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        switch (box)
        {
            case { Count: > 0, Tag: string text }:
                return 10 / box.Count;
            default:
                return 0;
        }
    }
}";

            var marker = FindMarker(source, "return 10 / box.Count;");
            var proof = ProveAtMarker(source, marker, "box != null && box.Count > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesSwitchExpressionPropertyPatternStructuralFact()
        {
            const string source = @"
public sealed class Box
{
    public int Count { get; init; }

    public object Tag { get; init; }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        return box switch
        {
            { Count: > 0, Tag: string text } => 10 / box.Count,
            _ => 0
        };
    }
}";

            var marker = FindMarker(source, "10 / box.Count");
            var proof = ProveAtMarker(source, marker, "box != null && box.Count > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_SwitchStatementFallbackUnknownGuardDoesNotExcludeCase()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0 when RuntimeGuard(value):
                return 0;
            default:
                return 10 / value;
        }
    }

    private static bool RuntimeGuard(int value)
    {
        return value.ToString() == ""0"";
    }
}";

            var marker = FindMarker(source, "return 10 / value;");
            var proof = ProveAtMarker(source, marker, "value != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_SwitchExpressionFallbackUnknownGuardDoesNotExcludeArm()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            0 when RuntimeGuard(value) => 0,
            _ => 10 / value
        };
    }

    private static bool RuntimeGuard(int value)
    {
        return value.ToString() == ""0"";
    }
}";

            var marker = FindMarker(source, "10 / value");
            var proof = ProveAtMarker(source, marker, "value != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        private static SymbolicProgramPointQueryResult AnalyzeAtPosition(string source, int position)
        {
            return new SymbolicSourceQueryService().AnalyzeSourceAtPosition(
                source,
                "PathSensitiveSmtInvariantTests.cs",
                position,
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis: new SmtAnalysisService(SmtAnalysisOptions.Default));
        }

        private static SymbolicConditionProofResult ProveAtMarker(
            string source,
            (int Line, int Column, int Position) marker,
            string condition)
        {
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "PathSensitiveSmtInvariantTests.cs",
                marker.Line,
                marker.Column,
                condition,
                new SmtAnalysisService(SmtAnalysisOptions.Default),
                AnalyzerTestHost.GetTrustedPlatformReferences());
        }

        private static (int Line, int Column, int Position) FindMarker(string source, string marker)
        {
            var position = source.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Marker was not found in source.");
            }

            var lines = source.Split('\n');
            var currentPosition = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var nextPosition = currentPosition + lines[index].Length + 1;
                if (position < nextPosition)
                {
                    return (index + 1, position - currentPosition + 1, position);
                }

                currentPosition = nextPosition;
            }

            throw new InvalidOperationException("Marker line was not found in source.");
        }
    }
}
