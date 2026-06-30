using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class AuthoringRuntimeHazardDiagnosticTests
    {
        [TestCaseSource(nameof(ProvableRuntimeHazardCases))]
        public async Task Ps0011_AuthoringRuntimeHazards_ReportWithoutEnforcePure(
            string source,
            string operationText,
            string exceptionType,
            string category)
        {
            var diagnostics = await GetAuthoringHazardDiagnosticsAsync(source);

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            var diagnostic = SingleRuntimeHazardDiagnostic(diagnostics);

            Assert.That(diagnostic.Id, Is.EqualTo(PurelySharpDiagnostics.UncaughtExceptionSiteId));
            Assert.That(diagnostic.GetMessage(), Does.Contain(operationText));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo(category));
        }

        [TestCaseSource(nameof(GuardedSafeOrUnknownRuntimeHazardCases))]
        public async Task Ps0011_AuthoringRuntimeHazards_DoNotReportGuardedSafeOrUnknownCases(string source)
        {
            var diagnostics = await GetAuthoringHazardDiagnosticsAsync(source);

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId),
                Is.False,
                FormatDiagnostics(diagnostics));
        }

        private static IEnumerable<TestCaseData> ProvableRuntimeHazardCases()
        {
            yield return new TestCaseData(
                @"
public class TestClass
{
    public int NullDereference()
    {
        string value = null!;
        return value.Length;
    }
}",
                "value.Length",
                "System.NullReferenceException",
                "definite_null_dereference")
                .SetName("Ps0011_AuthoringRuntimeHazards_ReportNullDereferenceWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int DivideByZero(int value)
    {
        int divisor = 0;
        return value / divisor;
    }
}",
                "value / divisor",
                "System.DivideByZeroException",
                "definite_divide_by_zero")
                .SetName("Ps0011_AuthoringRuntimeHazards_ReportDivideByZeroWithoutEnforcePure");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int IndexOutOfRange()
    {
        var values = new int[0];
        return values[^1];
    }
}",
                "values[^1]",
                "System.IndexOutOfRangeException",
                "definite_index_out_of_range")
                .SetName("Ps0011_AuthoringRuntimeHazards_ReportIndexOutOfRangeWithoutEnforcePure");
        }

        private static IEnumerable<TestCaseData> GuardedSafeOrUnknownRuntimeHazardCases()
        {
            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedNullDereference(string value)
    {
        if (value != null)
        {
            return value.Length;
        }

        return 0;
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressGuardedNullDereference");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownNullDereference(string value)
    {
        return value.Length;
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressUnknownNullDereference");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedDivideByZero(int value, int divisor)
    {
        if (divisor != 0)
        {
            return value / divisor;
        }

        return 0;
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressGuardedDivideByZero");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownDivideByZero(int value, int divisor)
    {
        return value / divisor;
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressUnknownDivideByZero");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int GuardedIndexOutOfRange(int[] values, int index)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressGuardedIndexOutOfRange");

            yield return new TestCaseData(
                @"
public class TestClass
{
    public int UnknownIndexOutOfRange(int[] values, int index)
    {
        return values[index];
    }
}")
                .SetName("Ps0011_AuthoringRuntimeHazards_SuppressUnknownIndexOutOfRange");
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAuthoringHazardDiagnosticsAsync(string source)
        {
            return await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_runtime_hazard_mode", "sites"),
                allowUnsafe: false,
                additionalFiles: ImmutableArray<AdditionalText>.Empty);
        }

        private static Diagnostic SingleRuntimeHazardDiagnostic(ImmutableArray<Diagnostic> diagnostics)
        {
            var siteDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                .ToImmutableArray();

            Assert.That(siteDiagnostics, Has.Length.EqualTo(1), FormatDiagnostics(diagnostics));
            return siteDiagnostics[0];
        }

        private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        {
            return string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage()));
        }
    }
}
