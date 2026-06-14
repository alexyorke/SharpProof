using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class LocalFunctionTests
    {
        [Test]
        public async Task PureLocalFunction_NoDiagnostic()
        {
            var code = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int PureMethod()
    {
        int LocalFunc(int a)
        {
            return a + 1;
        }
        return LocalFunc(5);
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(code);
        }

        [Test]
        public async Task UnusedImpureLocalFunction_NoDiagnostic()
        {
            var code = @"
using PurelySharp.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int PureMethod()
    {
        int Unused()
        {
            _val = 1;
            return _val;
        }

        return 0;
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(code);
        }

        [Test]
        public async Task ImpureLocalFunction_ReportsDiagnostic()
        {
            var code = @"
using PurelySharp.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int ImpureMethod()
    {
        int LocalFunc()
        {
            _val = 1; // Impure: modifies outer state
            return 1;
        }
        return LocalFunc();
    }
}";
            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                .WithSpan(9, 16, 9, 28)
                                .WithArguments("ImpureMethod");
            await VerifyCS.VerifyAnalyzerAsync(code, expected);
        }

        [Test]
        public async Task EscapingLocalFunctionDelegateCapturingFreshMutableObject_Diagnostic()
        {
            var code = @"
using System;
using PurelySharp.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Func<int> {|PS0002:TestMethod|}()
    {
        var box = new Box();

        int Local()
        {
            return box.Value;
        }

        return Local;
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(code);
        }
    }
}
