using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ComprehensiveAsyncTests
    {
        [Test]
    public async Task PureAsyncMethod_WithFromResult_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task<int> {|PS0002:PureAsyncMethod|}()
    {
        return await Task.FromResult(42);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureAsyncMethod_WithCompletedTask_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task PureAsyncMethod()
    {
        await Task.CompletedTask;
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureAsyncMethod_WithValueTask_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async ValueTask<int> PureAsyncMethod()
    {
        return await new ValueTask<int>(42);
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpureAsyncMethod_WithTaskDelay_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task ImpureAsyncMethod()
    {
        await Task.Delay(100); // Impure operation
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(9, 23, 9, 40).WithArguments("ImpureAsyncMethod"));
        }

        [Test]
        public async Task ImpureAsyncMethod_WithStateModification_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    private int _state;

    [EnforcePure]
    public async Task<int> ImpureAsyncMethod()
    {
        _state++; // State modification is impure
        return await Task.FromResult(_state);
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(11, 28, 11, 45).WithArguments("ImpureAsyncMethod"));
        }

        [Test]
    public async Task AsyncMethod_AwaitingImpureHelper_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task<int> {|PS0002:Helper|}()
    {
        return await Task.FromResult(42);
    }

    [EnforcePure]
    public async Task<int> {|PS0002:PureAsyncMethod|}()
    {
        return await Helper();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AsyncMethod_AwaitingImpureMethod_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    public async Task<int> ImpureHelper()
    {
        Console.WriteLine(""Impure operation"");
        return await Task.FromResult(42);
    }

    [EnforcePure]
    public async Task<int> ImpureAsyncMethod()
    {
        return await ImpureHelper(); // Awaiting an impure method
    }
}";


            var diag1 = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(15, 28, 15, 45).WithArguments("ImpureAsyncMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, new[] { diag1 });
        }

        [Test]
        public async Task TaskRunMethod_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task ImpureTaskRunMethod()
    {
        await Task.Run(() => Console.WriteLine(""Impure operation"")); // Task.Run is impure
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(9, 23, 9, 42).WithArguments("ImpureTaskRunMethod"));
        }

        [Test]
    public async Task AsyncMethod_ReturnWithoutAwait_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task<int> PureAsyncMethod()
    {
        // No await, but returns a Task directly
        if (true)
            return 42;
        else
            return await Task.FromResult(42);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
    public async Task AsyncMethod_ConditionalAwait_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading.Tasks;

class Program
{
    [EnforcePure]
    public async Task<int> {|PS0002:PureAsyncMethod|}(bool condition)
    {
        if (condition)
        {
            return await Task.FromResult(42);
        }
        return 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
