using System.Collections.Immutable;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SemanticOracleMetamorphicTests
    {
        [Test]
        public async Task Ps0002_InvokedLocalFunctionWrapper_PreservesEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        void Log()
        {
            Console.WriteLine(""impure"");
        }

        Log();
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Log"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UnusedLocalFunctionWrapper_DoesNotContaminateContainingMethod()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        void Log()
        {
            Console.WriteLine(""impure"");
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0010_InvokedLambdaWrapper_PreservesExceptionType()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Action thrower = () => throw new InvalidOperationException();
        thrower();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_UnusedLambdaWrapper_DoesNotContaminateContainingMethod()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Action thrower = () => throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }
    }
}
