using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
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
        public void SymbolicSourceQueryService_ProvesCollectionExpressionSpreadFixedLengthLowerBound()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input, 1, 2];
        return values.Length;
    }
}";

            var marker = FindMarker(source, "return values.Length;");
            var proof = ProveAtMarker(source, marker, "values.Length >= 2");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_CollectionExpressionSpreadLowerBoundSurvivesSourceReassignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input, 1];
        input = null;
        return values.Length;
    }
}";

            var marker = FindMarker(source, "return values.Length;");
            var proof = ProveAtMarker(source, marker, "values.Length >= 1");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_CollectionExpressionSpreadFixedLengthIsNotExact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input, 1];
        return values.Length;
    }
}";

            var marker = FindMarker(source, "return values.Length;");
            var proof = ProveAtMarker(source, marker, "values.Length == 1");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
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
        public void SymbolicSourceQueryService_ProvesSwitchStatementPositionalPatternPartialStructuralFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod((int, object) pair)
    {
        switch (pair)
        {
            case (> 0, string text):
                return 10 / pair.Item1;
            default:
                return 0;
        }
    }
}";

            var marker = FindMarker(source, "return 10 / pair.Item1;");
            var proof = ProveAtMarker(source, marker, "pair.Item1 > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesSwitchExpressionListElementPartialStructuralFact()
        {
            const string source = @"
public sealed class Entry
{
    public int Count { get; init; }

    public object Tag { get; init; }
}

public class TestClass
{
    public int TestMethod(Entry[] values)
    {
        return values switch
        {
            [ { Count: > 0, Tag: string text }, ..] => 10 / values[0].Count,
            _ => 0
        };
    }
}";

            var marker = FindMarker(source, "10 / values[0].Count");
            var proof = ProveAtMarker(source, marker, "values[0].Count > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_SwitchStatementDefaultExcludesTranslatedGuardedCase()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0 when value >= 0:
                return 0;
            default:
                return 10 / value;
        }
    }
}";

            var marker = FindMarker(source, "return 10 / value;");
            var proof = ProveAtMarker(source, marker, "value != 0");

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

        [Test]
        public async Task Ps0010_IfElseRangeGuardsMergeAtJoin_DoesNotReportDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value < 10)
        {
            if (value <= 0)
            {
                return 0;
            }
        }
        else
        {
            if (value == 10)
            {
                return 1;
            }
        }

        return 10 / value;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardedContinueBeforeGuardedBreakExit_DoesNotReportDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(bool stop, int divisor)
    {
        for (;;)
        {
            if (divisor == 0)
            {
                continue;
            }

            if (stop)
            {
                break;
            }
        }

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NestedGuardedContinueBeforeBreakExit_DoesNotReportDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        var guard = true;
        for (;;)
        {
            if (guard)
            {
                if (divisor == 0)
                {
                    continue;
                }
            }

            break;
        }

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NestedGuardedBreakExit_DoesNotReportDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(bool stop, int divisor)
    {
        for (;;)
        {
            if (divisor != 0)
            {
                if (stop)
                {
                    break;
                }
            }
        }

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesOneSidedReassignedLocalFactAfterJoin()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool replace, int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var divisor = value;
        if (replace)
        {
            divisor = 1;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesOneSidedReassignedArrayLengthFactAfterJoin()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool replace, int[] input)
    {
        if (input == null || input.Length < 2)
        {
            return 0;
        }

        var values = input;
        if (replace)
        {
            values = new[] { 1, 2, 3 };
        }

        return values.Length;
    }
}";

            var marker = FindMarker(source, "return values.Length;");
            var proof = ProveAtMarker(source, marker, "values.Length >= 2");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public async Task Ps0010_OneSidedReassignedLocalFactMergesAtJoin_DoesNotReportDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(bool replace, int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var divisor = value;
        if (replace)
        {
            divisor = 1;
        }

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesBranchDiscriminatorAssignmentRelationAfterJoin()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool choose)
    {
        var divisor = 0;
        if (choose)
        {
            divisor = 1;
        }
        else
        {
            divisor = 2;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var query = AnalyzeAtPosition(source, marker.Position);
            var proof = ProveAtMarker(
                source,
                marker,
                "(choose && divisor == 1) || (!choose && divisor == 2)");

            Assert.That(query.MergedInvariantText, Does.Contain("choose"));
            Assert.That(query.MergedInvariantText, Does.Contain("divisor"));
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesExhaustiveBooleanSwitchAssignmentRelationAfterJoin()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool choose)
    {
        var divisor = 0;
        switch (choose)
        {
            case true:
                divisor = 1;
                break;
            case false:
                divisor = 2;
                break;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var query = AnalyzeAtPosition(source, marker.Position);
            var proof = ProveAtMarker(
                source,
                marker,
                "(choose && divisor == 1) || (!choose && divisor == 2)");

            Assert.That(query.MergedInvariantText, Does.Contain("choose"));
            Assert.That(query.MergedInvariantText, Does.Contain("divisor"));
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ProvesExhaustiveBooleanSwitchExpressionAssignmentRelationAfterJoin()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool choose)
    {
        var divisor = choose switch
        {
            true => 1,
            false => 2
        };

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var query = AnalyzeAtPosition(source, marker.Position);
            var proof = ProveAtMarker(
                source,
                marker,
                "(choose && divisor == 1) || (!choose && divisor == 2)");

            Assert.That(query.MergedInvariantText, Does.Contain("choose"));
            Assert.That(query.MergedInvariantText, Does.Contain("divisor"));
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_ExcludesThrowingSwitchExpressionArmAfterNormalAssignmentCompletion()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = value switch
        {
            0 => throw new System.InvalidOperationException(),
            _ => value
        };

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "value != 0 && divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void SymbolicSourceQueryService_DoesNotTreatEnumSwitchWithoutDefaultAsExhaustive()
        {
            const string source = @"
public enum Choice
{
    First,
    Second
}

public class TestClass
{
    public int TestMethod(Choice choose)
    {
        var divisor = 0;
        switch (choose)
        {
            case Choice.First:
                divisor = 1;
                break;
            case Choice.Second:
                divisor = 2;
                break;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public async Task Ps0002_BranchDiscriminatorValueJoinPrunesImpossibleImpureCall()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool choose)
    {
        var value = 0;
        if (choose)
        {
            value = 1;
        }
        else
        {
            value = 2;
        }

        if (choose && value != 1)
        {
            Console.WriteLine(value);
        }
    }
}");

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpuritySymbolProperty, out var symbol) &&
                    symbol?.Contains("System.Console.WriteLine", StringComparison.Ordinal) == true),
                Is.False);
        }

        [Test]
        public async Task Ps0010_ExhaustiveBooleanSwitchValueJoinPrunesDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(bool choose)
    {
        var divisor = 0;
        switch (choose)
        {
            case true:
                divisor = 1;
                break;
            case false:
                divisor = 2;
                break;
        }

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ExhaustiveBooleanSwitchExpressionValueJoinPrunesDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(bool choose)
    {
        var divisor = choose switch
        {
            true => 1,
            false => 2
        };

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ThrowingSwitchExpressionArmPrunesDivideByZero()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var divisor = value switch
        {
            0 => throw new System.InvalidOperationException(),
            _ => value
        };

        return 10 / divisor;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(
                diagnostics.Any(diagnostic =>
                    diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId &&
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionTypesProperty, out var exceptionTypes) &&
                    exceptionTypes?.Contains("System.DivideByZeroException", StringComparison.Ordinal) == true),
                Is.False);
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
