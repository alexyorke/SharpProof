using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;

namespace SharpProof.Test
{
    [TestFixture]
    public class PureAsyncTests
    {

        private const string MinimalEnforcePureAttributeSource = @"
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }";

        [Test]
        public async Task PureAsyncMethod_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Threading.Tasks;

public class TestClass
{
    [EnforcePure]
    public async Task<int> {|SP0002:PureAsyncMethod|}()
    {
        // Task.FromResult now follows reviewed runtime evidence.
        return await Task.FromResult(42);
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpureAsyncMethod_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Threading.Tasks;

class Program
{
    private int _counter = 0;

    [EnforcePure]
    public async Task<int> ImpureAsyncMethod()
    {
        // Task.Delay is impure, _counter++ is impure
        await Task.Delay(1);
        _counter++;
        return _counter;
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                    .WithSpan(11, 28, 11, 45)
                                    .WithArguments("ImpureAsyncMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ImpureAsyncVoidMethod_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;

// --- Attribute Definition ---
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }
// --- End Attribute Definition ---

class TestClass
{
    private int _count = 0;

    [EnforcePure]
    public async void TestMethod()
    {
        await Task.Delay(1); // Impure
        _count++;            // Impure
    }
}
";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(15, 23, 15, 33)
                                   .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithAsyncOperation_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;

// --- Attribute Definition ---
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }
// --- End Attribute Definition ---

class TestClass
{
    private int _field = 0;

    [EnforcePure]
    public async Task TestMethod()
    {
        _field = 1; // Impure assignment
        await Task.Yield(); // Yield is okay
    }
}
";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(15, 23, 15, 33)
                                   .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ImpureMethodWithAsyncLocalFunction_Diagnostic()
        {
            var test = @"
using System;
using System.Threading.Tasks;

// --- Attribute Definition ---
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }
// --- End Attribute Definition ---

class TestClass
{
    private static int counter = 0;

    [EnforcePure]
    public async Task OuterMethod()
    {
        // Calling impure local function makes outer method impure
        await ImpureLocalAsync();
        [EnforcePure]
        async Task ImpureLocalAsync()
        {
            await Task.Delay(1); // Impure
            counter++;           // Impure
        }
    }
}
";

            var expectedOuter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                        .WithSpan(15, 23, 15, 34)
                                        .WithArguments("OuterMethod");
            var expectedLocal = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                        .WithSpan(20, 20, 20, 36)
                                        .WithArguments("ImpureLocalAsync");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedOuter, expectedLocal);
        }
    }
}


