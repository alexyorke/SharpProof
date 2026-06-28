using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ExceptionFlowPathFactStressTests
    {
        [Test]
        public async Task Ps0010_AndConditionZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_AndConditionNullReceiver_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(string value, bool enabled)
    {
        if (enabled && value == null)
        {
            return value.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_OrFalseBranchZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (divisor != 0 || enabled)
        {
            return 0;
        }

        return value % divisor;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_IsNotNullElseBranch_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value is not null)
        {
            return 0;
        }
        else
        {
            return value.Length;
        }
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_IsNotZeroElseBranch_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor is not 0)
        {
            return 0;
        }
        else
        {
            return value / divisor;
        }
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_AndConditionZeroDivisor_ReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor == 0)
        {
            divisor = 1;
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_OrTrueBranchZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (divisor == 0 || enabled)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_OrTrueBranchNullReceiver_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value, bool enabled)
    {
        if (value == null || enabled)
        {
            return value.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedNotEqualZero_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (!(divisor != 0))
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_NegatedEqualsZero_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (!(divisor == 0))
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegatedIsNotNull_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (!(value is not null))
        {
            return value.Length;
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_AndConditionNonZeroDivisor_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor, bool enabled)
    {
        if (enabled && divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_GuardFalsePathReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return 0;
        }

        divisor = 1;
        return value / divisor;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_BranchFactDivideByZeroCaught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        try
        {
            if (divisor == 0)
            {
                return value / divisor;
            }

            return 0;
        }
        catch (DivideByZeroException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NegativeArrayIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values[-1];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NegativeIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_UpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index >= values.Length)
        {
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_InRangeArrayIndexGuard_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayLengthNegativeGuardedIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length < 0)
        {
            return values[-1];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchExpressionArrayLengthNegativeArm_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        return values.Length switch
        {
            < 0 => values[-1],
            _ => 0
        };
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_OutOfRangeGuardReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        if (index < 0)
        {
            index = 0;
            return values[index];
        }

        return 0;
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_BranchFactIndexOutOfRangeCaught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        try
        {
            if (index >= values.Length)
            {
                return values[index];
            }

            return 0;
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationConstantUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[4];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationSymbolicUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var length = 4;
        var values = new int[length];
        return values[length];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ArrayEmptyIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        var values = Array.Empty<int>();
        return values[0];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_ArrayCollectionExpressionIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        int[] values = [1, 2, 3];
        return values[3];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_ArrayAliasUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var alias = values;
        return alias[4];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_NegativeStringIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if (index < 0)
        {
            return text[index];
        }

        return '\0';
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringUpperBoundIndexGuard_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod(string text, int index)
    {
        if (index >= text.Length)
        {
            return text[index];
        }

        return '\0';
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringLiteralUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[3];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_index_out_of_range"));
        }

        [Test]
        public async Task Ps0010_StringAliasUpperBound_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod(string input)
    {
        var text = input;
        var alias = text;
        return alias[input.Length];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringLiteralInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod()
    {
        var text = ""abc"";
        return text[2];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_StringIsNullOrEmptyNonNullIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (string.IsNullOrEmpty(text) && text != null)
        {
            return text[0];
        }

        return '\0';
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_StringIsNullOrEmptyFalseBranchIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public char TestMethod(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            return text[0];
        }

        return '\0';
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayCollectionExpressionSpreadIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        int[] values = [.. input];
        return values[0];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IndexAssignedFromArrayLength_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        var index = values.Length;
        return values[index];
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_LocalArrayCreationInRangeIndex_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        return values[3];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ArrayLengthFactRemovedAfterReassignment_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] input)
    {
        var values = new int[1];
        values = input;
        return values[1];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConditionalArrayReassignmentInvalidatesLength_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new int[1];
        if (flag)
        {
            values = new int[2];
        }

        return values[1];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SelfReferentialIndexAssignment_DoesNotReportFromUnsatisfiableFacts()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        index = index + 1;
        return values[index];
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementConstantZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                return 1 / divisor;
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_SwitchStatementNonZeroCase_DoesNotReportDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 1:
                return 1 / divisor;
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementReassignmentInvalidatesCaseFact_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        switch (divisor)
        {
            case 0:
                divisor = 1;
                return 1 / divisor;
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SwitchStatementNullCase_ReportsNullReferenceException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        switch (value)
        {
            case null:
                return value.Length;
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_SwitchStatementRelationalPatternIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        switch (index)
        {
            case < 0:
                return values[index];
            default:
                return 0;
        }
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionRelationalPatternIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            < 0 => values[index],
            _ => 0
        };
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionWhenGuardIndex_ReportsIndexOutOfRangeException()
        {
            var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            _ when index >= values.Length => values[index],
            _ => 0
        };
    }
}");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public async Task Ps0010_SwitchExpressionWhenGuardInRange_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int[] values, int index)
    {
        return index switch
        {
            _ when index >= 0 && index < values.Length => values[index],
            _ => 0
        };
    }
}");

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        private static async Task<Diagnostic> SingleExceptionDiagnosticAsync(string source)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            return diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "ExceptionFlowPathFactStressTests",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerOptions = new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_report_exceptions",
                    "true")));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
