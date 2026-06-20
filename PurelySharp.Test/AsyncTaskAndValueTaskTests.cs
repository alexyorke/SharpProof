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
    public class AsyncTaskAndValueTaskTests
    {
        [Test]
        public async Task Task_CompletedTask_NoDiagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Task PureMethod()
        {
            return Task.CompletedTask;
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Task_FromResult_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Task<int> {|PS0002:PureMethod|}()
        {
            return Task.FromResult(42);
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ValueTask_AsTask_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Task<int> {|PS0002:PureMethod|}()
        {
            return new ValueTask<int>(42).AsTask();
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ValueTask_Constructor_NoDiagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public ValueTask<int> PureMethod()
        {
            return new ValueTask<int>(42);
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TaskRun_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using System.IO;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Task {|PS0002:ImpureMethod|}()
        {
            return Task.Run(() => File.WriteAllText(""log.txt"", ""Task run executed""));
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalReturn_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task<int> {|PS0002:PureMethod|}(bool condition)
        {
            if (condition)
            {
                return await Task.FromResult(1);
            }
            else
            {
                return await new ValueTask<int>(2);
            }
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AwaitingPureMethodParameter_NoDiagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task<int> PureMethod(Task<int> taskToAwait)
        {
            return await taskToAwait;
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TaskResult_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public int {|PS0002:ImpureMethod|}(Task<int> task)
        {
            return task.Result;
        }
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
