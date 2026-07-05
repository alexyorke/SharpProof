using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class AsyncOperationsTests
    {
        [Test]
        public async Task MethodWithAsyncOperation_NoDiagnostic()
        {




            var test = @"
using System;
using SharpProof.Attributes;
using System.Threading.Tasks;



class Program
{
    [EnforcePure]
    public async Task<int> TestMethod()
    {
        return 1 + 2;
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AsyncMethodWithAwait_Diagnostic()
        {

            var test = @"
using System.Threading.Tasks;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task<int> {|SP0002:TestMethod|}()
        {
            // Await Task.Delay, which is treated as impure.
            await Task.Delay(10);
            return 42;
        }
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


