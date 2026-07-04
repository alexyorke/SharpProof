using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicProgramPointFactTests
    {
        [Test]
        public void ProgramPointFacts_ReplayNestedElseIfGuardFactsAfterOuterExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value < 0)
        {
            throw new System.InvalidOperationException();
        }
        else if (value == 0)
        {
            return 0;
        }

        return 10 / value;
    }
}";

            var marker = FindMarker(source, "return 10 / value;");
            var proof = ProveAtMarker(source, marker, "value > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ReplaySurvivingElseAssignmentAfterTrueBranchExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool useFallback, int input)
    {
        var divisor = 0;
        if (useFallback)
        {
            return 0;
        }
        else
        {
            divisor = input < 0 ? 1 : 2;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ReplaySurvivingTrueAssignmentAfterFalseBranchExit()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool usePrimary, int input)
    {
        var divisor = 0;
        if (usePrimary)
        {
            divisor = input == 0 ? 3 : input;
        }
        else
        {
            throw new System.InvalidOperationException();
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor != 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotReplaySurvivingBranchGuardAfterReferenceMutation()
        {
            const string source = @"
public sealed class Box
{
    public int Value;

    public void MaybeMutate()
    {
    }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        if (box.Value > 0)
        {
            return 0;
        }
        else
        {
            box.MaybeMutate();
        }

        return box.Value;
    }
}";

            var marker = FindMarker(source, "return box.Value;");
            var proof = ProveAtMarker(source, marker, "box.Value <= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotMergeIfElseWithReferenceMutatedCondition()
        {
            const string source = @"
public sealed class Box
{
    public int Value;

    public void MaybeMutate()
    {
    }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        var divisor = 0;
        if (box.Value > 0)
        {
            box.MaybeMutate();
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
            var proof = ProveAtMarker(source, marker, "box.Value > 0 || divisor == 2");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotMergeImplicitElseWithReferenceMutatedCondition()
        {
            const string source = @"
public sealed class Box
{
    public int Value;

    public void MaybeMutate()
    {
    }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        var divisor = 1;
        if (box.Value > 0)
        {
            box.MaybeMutate();
            divisor = 2;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "box.Value > 0 || divisor == 1");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_FilterBranchLocalSymbolsWhenReplayingSingleSurvivingBranch()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(bool stop)
    {
        var divisor = 0;
        if (stop)
        {
            return 0;
        }
        else
        {
            var hidden = 5;
            divisor = hidden;
        }

        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var snapshot = GetSnapshotAtStatement(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 5");

            Assert.That(snapshot.Facts.Any(fact => fact.Contains("hidden#", StringComparison.Ordinal)), Is.False);
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_InlineAssignmentComparisonProvesAssignedValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        if ((divisor = 0) == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_InlineAssignmentComparisonInvalidatesPriorValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        if ((divisor = 0) == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 5");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_RightHandInlineAssignmentComparisonPreservesDirection()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        if (0 < (divisor = 1))
        {
            return 10 / divisor;
        }

        return 1;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor > 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_RightHandInlineAssignmentReferencingAssignedSymbolRemainsConservative()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        if (divisor == (divisor = 0))
        {
            return divisor;
        }

        return 1;
    }
}";

            var marker = FindMarker(source, "return divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_InlineAssignmentBranchBlockInvalidatesPriorValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        if ((divisor = 0) == 0)
        {
            return 10 / divisor;
        }

        return 1;
    }
}";

            var snapshot = GetSnapshotAtBlockContainingStatement(source, "return 10 / divisor;");

            Assert.That(snapshot.Facts.Any(fact => fact.Contains("Value = 5", StringComparison.Ordinal)), Is.False);
            Assert.That(snapshot.Facts.Count(fact => fact.Contains("Value = 0", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ProgramPointFacts_SelfReferentialInlineAssignmentRemainsConservative()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int input)
    {
        var divisor = input;
        if ((divisor = divisor + 1) == 0)
        {
            return divisor;
        }

        return 1;
    }
}";

            var marker = FindMarker(source, "return divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NullableCoalesceAssignmentPreservesValueParts()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? left)
    {
        if (left.HasValue && left.Value == 5)
        {
            int? result = left ?? 9;
            return result.Value;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return result.Value;");
            var proof = ProveAtMarker(source, marker, "result.HasValue && result.Value == 5");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ConditionalAccessAssignmentProvesNoNullableValueOnNullReceiver()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        int? length = text?.Length;
        if (text is null)
        {
            return length.Value;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return length.Value;");
            var proof = ProveAtMarker(source, marker, "!length.HasValue");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_FiniteArrayElementAssignmentUsesElementTerm()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 7, 11 };
        var divisor = values[0];
        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 7");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DeconstructionDeclarationDiscardPreservesVisibleTupleSlotFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (5, 0);
        (int divisor, _) = pair;
        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 5");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_TupleAssignmentDiscardPreservesVisibleTupleSlotFact()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (0, 7);
        var divisor = 1;
        (_, divisor) = pair;
        return 10 / divisor;
    }
}";

            var marker = FindMarker(source, "return 10 / divisor;");
            var proof = ProveAtMarker(source, marker, "divisor == 7");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ArrayCreationNormalCompletionProvesLengthNonNegative()
        {
            const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        var values = new int[length];
        if (length < 0)
        {
            return new int[0];
        }

        return values;
    }
}";

            var marker = FindMarker(source, "if (length < 0)");
            var proof = ProveAtMarker(source, marker, "length >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ArrayCreationNormalCompletionDoesNotSurviveLengthReassignment()
        {
            const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        var values = new int[length];
        length = -1;
        if (length < 0)
        {
            return new int[0];
        }

        return values;
    }
}";

            var marker = FindMarker(source, "if (length < 0)");
            var proof = ProveAtMarker(source, marker, "length >= 0");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullParameterNormalCompletionProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void Require([NotNull] object? value)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        Guard.Require(value);
        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullParameterNormalCompletionDoesNotSurviveArgumentReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void Require([NotNull] object? value)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        Guard.Require(value);
        value = null;
        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullNormalCompletionProvesFieldNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    private string? _value;

    [MemberNotNull(nameof(_value))]
    private void EnsureValue()
    {
        _value = string.Empty;
    }

    public int TestMethod()
    {
        EnsureValue();
        return _value.Length;
    }
}";

            var marker = FindMarker(source, "return _value.Length;");
            var proof = ProveAtMarker(source, marker, "_value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullNormalCompletionProvesPropertyNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    public string? Value { get; private set; }

    [MemberNotNull(nameof(Value))]
    private void EnsureValue()
    {
        Value = string.Empty;
    }

    public int TestMethod()
    {
        this.EnsureValue();
        return this.Value.Length;
    }
}";

            var marker = FindMarker(source, "return this.Value.Length;");
            var proof = ProveAtMarker(source, marker, "this.Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullFactDoesNotSurviveMemberReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    public string? Value { get; private set; }

    [MemberNotNull(nameof(Value))]
    private void EnsureValue()
    {
        Value = string.Empty;
    }

    public int TestMethod()
    {
        EnsureValue();
        Value = null;
        return Value.Length;
    }
}";

            var marker = FindMarker(source, "return Value.Length;");
            var proof = ProveAtMarker(source, marker, "Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullWhenTrueBranchProvesPropertyNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    public string? Value { get; private set; }

    [MemberNotNullWhen(true, nameof(Value))]
    private bool HasValue()
    {
        return Value is not null;
    }

    public int TestMethod()
    {
        if (HasValue())
        {
            return Value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return Value.Length;");
            var proof = ProveAtMarker(source, marker, "Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullWhenFalseComparisonBranchProvesFieldNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    private string? _value;

    [MemberNotNullWhen(false, nameof(_value))]
    private bool MissingValue()
    {
        return _value is null;
    }

    public int TestMethod()
    {
        if (MissingValue() == false)
        {
            return _value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return _value.Length;");
            var proof = ProveAtMarker(source, marker, "_value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MemberNotNullWhenFactDoesNotSurviveMemberReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    public string? Value { get; private set; }

    [MemberNotNullWhen(true, nameof(Value))]
    private bool HasValue()
    {
        return Value is not null;
    }

    public int TestMethod()
    {
        if (HasValue())
        {
            Value = null;
            return Value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return Value.Length;");
            var proof = ProveAtMarker(source, marker, "Value != null");

            Assert.That(proof.TruthValue, Is.Not.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenTrueBranchProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (Guard.IsPresent(value))
        {
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_SwitchStatementNotNullWhenGuardProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        switch (value)
        {
            case var candidate when Guard.IsPresent(candidate):
                return value.Length;
            default:
                return 0;
        }
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_SwitchExpressionNotNullWhenGuardProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        return value switch
        {
            var candidate when Guard.IsPresent(candidate) => value.Length,
            _ => 0,
        };
    }
}";

            var marker = FindMarker(source, "value.Length");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_SwitchStatementMemberNotNullWhenGuardProvesMemberNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public class TestClass
{
    public string? Value { get; private set; }

    [MemberNotNullWhen(true, nameof(Value))]
    private bool HasValue()
    {
        return Value is not null;
    }

    public int TestMethod()
    {
        switch (this)
        {
            case _ when HasValue():
                return Value.Length;
            default:
                return 0;
        }
    }
}";

            var marker = FindMarker(source, "return Value.Length;");
            var proof = ProveAtMarker(source, marker, "Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_SwitchStatementPropertyPatternNotNullWhenGuardSubstitutesBinding()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public sealed class Box
{
    public string? Value { get; init; }
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        switch (box)
        {
            case { Value: var candidate } when Guard.IsPresent(candidate):
                return box.Value.Length;
            default:
                return 0;
        }
    }
}";

            var marker = FindMarker(source, "return box.Value.Length;");
            var proof = ProveAtMarker(source, marker, "box.Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_SwitchExpressionListPatternNotNullWhenGuardSubstitutesBinding()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public class TestClass
{
    public int TestMethod(string?[] values)
    {
        return values switch
        {
            [var first] when Guard.IsPresent(first) => values[0].Length,
            _ => 0,
        };
    }
}";

            var marker = FindMarker(source, "values[0].Length");
            var proof = ProveAtMarker(source, marker, "values[0] != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenFalseNegatedBranchProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsMissing([NotNullWhen(false)] string? value)
    {
        return value is null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (!Guard.IsMissing(value))
        {
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenBoolComparisonBranchProvesArgumentNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsMissing([NotNullWhen(false)] string? value)
    {
        return value is null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (Guard.IsMissing(value) == false)
        {
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenMemberArgumentRemainsConservative()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public interface IGuard
{
    bool IsPresent([NotNullWhen(true)] string? value);
}

public sealed class Box
{
    public string? Value;
}

public class TestClass
{
    public int TestMethod(IGuard guard, Box box)
    {
        if (guard.IsPresent(box.Value))
        {
            return box.Value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return box.Value.Length;");
            var proof = ProveAtMarker(source, marker, "box.Value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullIfNotNullAssignedMethodReturnProvesLocalNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Echo(string? value)
    {
        return value;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        var copy = Guard.Echo(value);
        if (value != null)
        {
            return copy.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return copy.Length;");
            var proof = ProveAtMarker(source, marker, "copy != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullIfNotNullAssignedNullSourceRemainsUnknown()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Echo(string? value)
    {
        return value;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        var copy = Guard.Echo(value);
        if (value == null)
        {
            return copy == null ? 0 : copy.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return copy == null");
            var proof = ProveAtMarker(source, marker, "copy != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullIfNotNullAssignedFactDoesNotSurviveReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Echo(string? value)
    {
        return value;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        var copy = Guard.Echo(value);
        if (value != null)
        {
            copy = null;
            return copy == null ? 0 : copy.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return copy == null");
            var proof = ProveAtMarker(source, marker, "copy != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullIfNotNullAssignedMemberSourceRemainsConservative()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Echo(string? value)
    {
        return value;
    }
}

public sealed class Box
{
    public string? Value;
}

public class TestClass
{
    public int TestMethod(Box box)
    {
        var copy = Guard.Echo(box.Value);
        if (box.Value != null)
        {
            return copy == null ? 0 : copy.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return copy == null");
            var proof = ProveAtMarker(source, marker, "copy != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenOutArgumentProvesAssignedLocalNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool TryRead([NotNullWhen(true)] out string? value)
    {
        value = null;
        return true;
    }
}

public class TestClass
{
    public int TestMethod()
    {
        string? value;
        if (Guard.TryRead(out value))
        {
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenOutVarArgumentProvesDeclaredLocalNonNull()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool TryRead([NotNullWhen(true)] out string? value)
    {
        value = null;
        return true;
    }
}

public class TestClass
{
    public int TestMethod()
    {
        if (Guard.TryRead(out var value))
        {
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_NotNullWhenBranchFactDoesNotSurviveArgumentReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static bool IsPresent([NotNullWhen(true)] string? value)
    {
        return value is not null;
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (Guard.IsPresent(value))
        {
            value = null;
            return value.Length;
        }

        return 0;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotReturnIfTrueNormalCompletionProvesFalseCondition()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void ThrowIf([DoesNotReturnIf(true)] bool condition)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        Guard.ThrowIf(value is null);
        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotReturnIfFalseNormalCompletionProvesTrueCondition()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void Require([DoesNotReturnIf(false)] bool condition)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        Guard.Require(value is not null);
        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotReturnIfFactDoesNotSurviveArgumentReassignment()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    public static void ThrowIf([DoesNotReturnIf(true)] bool condition)
    {
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        Guard.ThrowIf(value is null);
        value = null;
        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenFalse), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_DoesNotReturnCallInTrueBranchProvesFalseCondition()
        {
            const string source = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;

public static class Guard
{
    [DoesNotReturn]
    public static void Fail()
    {
        throw new System.Exception();
    }
}

public class TestClass
{
    public int TestMethod(string? value)
    {
        if (value is null)
        {
            Guard.Fail();
        }

        return value.Length;
    }
}";

            var marker = FindMarker(source, "return value.Length;");
            var proof = ProveAtMarker(source, marker, "value != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_UsingExpressionThrowGuardNormalCompletionProvesResourceNonNull()
        {
            const string source = @"
#nullable enable
using System;

public sealed class Resource : IDisposable
{
    public void Dispose()
    {
    }
}

public class TestClass
{
    public int TestMethod(Resource? resource)
    {
        using (resource ?? throw new InvalidOperationException())
        {
        }

        return resource.GetHashCode();
    }
}";

            var marker = FindMarker(source, "return resource.GetHashCode();");
            var proof = ProveAtMarker(source, marker, "resource != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_UsingDeclarationInitializerThrowGuardNormalCompletionProvesInputNonNull()
        {
            const string source = @"
#nullable enable
using System;

public sealed class Resource : IDisposable
{
    public void Dispose()
    {
    }
}

public class TestClass
{
    public int TestMethod(Resource? resource)
    {
        using (var disposable = resource ?? throw new InvalidOperationException())
        {
        }

        return resource.GetHashCode();
    }
}";

            var marker = FindMarker(source, "return resource.GetHashCode();");
            var proof = ProveAtMarker(source, marker, "resource != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_UsingExpressionThrowGuardFactDoesNotSurviveBodyReassignment()
        {
            const string source = @"
#nullable enable
using System;

public sealed class Resource : IDisposable
{
    public void Dispose()
    {
    }
}

public class TestClass
{
    public int TestMethod(Resource? resource)
    {
        using (resource ?? throw new InvalidOperationException())
        {
            resource = null;
        }

        return resource.GetHashCode();
    }
}";

            var marker = FindMarker(source, "return resource.GetHashCode();");
            var proof = ProveAtMarker(source, marker, "resource != null");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown), proof.Reason);
        }

        [Test]
        public void ProgramPointFacts_MultidimensionalArrayCreationAssignsDimensionLengths()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int rows, int columns)
    {
        var values = new int[rows, columns];
        return values[0, 0];
    }
}";

            var marker = FindMarker(source, "return values[0, 0];");
            var rowProof = ProveAtMarker(source, marker, "values.GetLength(0) == rows");
            var columnProof = ProveAtMarker(source, marker, "values.GetLength(1) == columns");

            Assert.That(rowProof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), rowProof.Reason);
            Assert.That(columnProof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), columnProof.Reason);
        }

        [Test]
        public void ProgramPointFacts_ObjectErasedArrayCastAliasProvesLength()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int length)
    {
        var values = new int[length];
        object boxed = values;
        var alias = (int[])boxed;
        return alias.Length;
    }
}";

            var marker = FindMarker(source, "return alias.Length;");
            var proof = ProveAtMarker(source, marker, "alias.Length == length");

            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue), proof.Reason);
        }

        private static SymbolicInvariantSnapshot GetSnapshotAtStatement(string source, string statementPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "SymbolicProgramPointFactTests.cs");
            var compilation = CSharpCompilation.Create(
                "SymbolicProgramPointFactTests",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var statement = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<StatementSyntax>()
                .Single(node => node.ToString().StartsWith(statementPrefix, StringComparison.Ordinal));

            return new SymbolicInvariantService().GetInvariantsAt(statement, semanticModel);
        }

        private static SymbolicInvariantSnapshot GetSnapshotAtBlockContainingStatement(string source, string statementPrefix)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                "SymbolicProgramPointFactTests.cs");
            var compilation = CSharpCompilation.Create(
                "SymbolicProgramPointFactTests",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var block = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<BlockSyntax>()
                .Single(node => node.Statements.Any(statement =>
                    statement.ToString().StartsWith(statementPrefix, StringComparison.Ordinal)));

            return new SymbolicInvariantService().GetInvariantsAt(block, semanticModel);
        }

        private static SymbolicConditionProofResult ProveAtMarker(
            string source,
            (int Line, int Column, int Position) marker,
            string condition)
        {
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            return new SymbolicSourceQueryService().ProveConditionAtSource(
                source,
                "SymbolicProgramPointFactTests.cs",
                marker.Line,
                marker.Column,
                condition,
                smtAnalysis,
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
