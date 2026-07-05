using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCF = SharpProof.Test.CSharpCodeFixVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer,
    SharpProof.SharpProofCodeFixProvider>;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class SharpProofCodeFixTests
    {
        [Test]
        public async Task SP0004_AddEnforcePure_InsertsFullyQualifiedAttribute()
        {
            var source = @"
namespace N
{
    public static class C
    {
        public static int Add(int a, int b) => a + b;
    }
}
";
            var fixedSource = @"
namespace N
{
    public static class C
    {
        [global::SharpProof.Attributes.EnforcePure]
        public static int Add(int a, int b) => a + b;
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(6, 27, 6, 30)
                .WithArguments("Add");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0005_RemovesPure_KeepsEnforcePure()
        {
            var source = @"
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        [Pure]
        public static int Id(int x) => x;
    }
}
";
            var fixedSource = @"
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        public static int Id(int x) => x;
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.ConflictingPurityAttributesId)
                .WithSpan(10, 27, 10, 29)
                .WithArguments("Id");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0002_RemovesPurityAttributes()
        {
            var source = @"
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        public static int Bad()
        {
            System.Console.Write(1);
            return 0;
        }
    }
}
";
            var fixedSource = @"
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        public static int Bad()
        {
            System.Console.Write(1);
            return 0;
        }
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(9, 27, 9, 30)
                .WithArguments("Bad");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0003_RemovesMisplacedEnforcePureOnClass()
        {
            var source = @"
using SharpProof.Attributes;

[EnforcePure]
public class C
{
}
";
            var fixedSource = @"
using SharpProof.Attributes;
public class C
{
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MisplacedAttributeId)
                .WithSpan(4, 2, 4, 13);
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0006_RemoveAllowSynchronization_LeavesImpureMethodWithoutExtraDiagnostics()
        {
            var source = @"
using SharpProof.Attributes;
using System;

namespace N
{
    public class C
    {
        [AllowSynchronization]
        public void M() { Console.Write(1); }
    }
}
";
            var fixedSource = @"
using SharpProof.Attributes;
using System;

namespace N
{
    public class C
    {
        public void M() { Console.Write(1); }
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId)
                .WithSpan(10, 21, 10, 22)
                .WithArguments("M");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource, "RemoveAttributesMatchingAsyncSP0006b");
        }

        [Test]
        public async Task SP0008_RemovesRedundantAllowSynchronization()
        {
            var source = @"
using SharpProof.Attributes;

namespace N
{
    public class C
    {
        [EnforcePure]
        [AllowSynchronization]
        public int M() => 1;
    }
}
";
            var fixedSource = @"
using SharpProof.Attributes;

namespace N
{
    public class C
    {
        [EnforcePure]
        public int M() => 1;
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.RedundantAllowSynchronizationId)
                .WithSpan(10, 20, 10, 21)
                .WithArguments("M");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }
    }
}
