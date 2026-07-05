using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public class SemanticOracleMetamorphicTests
    {
        [Test]
        public async Task Sp0002_InvokedLocalFunctionWrapper_PreservesEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

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

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Log"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Sp0002_UnusedLocalFunctionWrapper_DoesNotContaminateContainingMethod()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_InvokedLambdaWrapper_PreservesEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Action log = () => Console.WriteLine(""impure"");
        log();
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Sp0002_UnusedLambdaWrapper_DoesNotContaminateContainingMethod()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Action log = () => Console.WriteLine(""impure"");
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_DeadBranchWrapper_RemovesEffect()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        if (false)
        {
            Console.WriteLine(""impure"");
        }
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Sp0002_TempLocalWrapper_PreservesEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var value = Console.Read();
        return value;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Sp0002_ConditionalExpressionWrapper_PreservesEvidence()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(bool flag)
    {
        return flag ? Console.Read() : 0;
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Sp0010_InvokedLambdaWrapper_PreservesExceptionType()
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
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            var diagnostic = diagnostics
                .Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .First(candidate => candidate.GetMessage().Contains("'TestMethod'", System.StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Sp0010_InvokedLocalFunctionWrapper_PreservesExceptionType()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        void Thrower()
        {
            throw new InvalidOperationException();
        }

        Thrower();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            var diagnostic = diagnostics
                .Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .First(candidate => candidate.GetMessage().Contains("'TestMethod'", System.StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Sp0010_UnusedLambdaWrapper_DoesNotContaminateContainingMethod()
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
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_DeadBranchWrapper_DoesNotContaminateContainingMethod()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        if (false)
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_ExactCatchWrapper_SuppressesMatchingException()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_BaseCatchWrapper_SuppressesMatchingException()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (Exception)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_RethrowWrapper_PreservesExceptionType()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            var diagnostic = diagnostics
                .Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .First(candidate => candidate.GetMessage().Contains("'TestMethod'", System.StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Sp0010_FilterTrueWrapper_SuppressesMatchingException()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (true)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Sp0010_FilterFalseWrapper_PreservesExceptionType()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (false)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            var diagnostic = diagnostics
                .Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .First(candidate => candidate.GetMessage().Contains("'TestMethod'", System.StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }
    }
}
