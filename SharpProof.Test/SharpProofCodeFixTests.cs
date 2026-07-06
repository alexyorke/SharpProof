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
#pragma warning disable SP0004
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
#pragma warning disable SP0004
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
                .WithSpan(11, 27, 11, 29)
                .WithArguments("Id");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0002_RemovesPurityAttributes()
        {
            var source = @"
#pragma warning disable SP0004
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
#pragma warning disable SP0004
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
                .WithSpan(10, 27, 10, 30)
                .WithArguments("Bad");
            await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
        }

        [Test]
        public async Task SP0002_RemovesPurityAttributesFromConversionOperator()
        {
            var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }

    [EnforcePure]
    public static explicit operator int(Temperature value)
    {
        System.Console.WriteLine(value._celsius);
        return value._celsius;
    }
}
";
            var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }
    public static explicit operator int(Temperature value)
    {
        System.Console.WriteLine(value._celsius);
        return value._celsius;
    }
}
";
            var expectedImpure = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(15, 37, 15, 40)
                .WithArguments("op_Explicit");
            await VerifyCF.VerifyCodeFixAsync(
                source,
                expectedImpure,
                fixedSource,
                codeActionEquivalenceKey: "RemoveAttributesMatchingAsyncSP0002");
        }

        [Test]
        public async Task SP0002_RemovesPurityAttributesFromExpressionBodiedProperty()
        {
            var source = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;

    [Pure]
    public int Value => _counter++;
}
";
            var fixedSource = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;
    public int Value => _counter++;
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(9, 16, 9, 21)
                .WithArguments("get_Value");
            await VerifyCF.VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                codeActionEquivalenceKey: "RemoveAttributesMatchingAsyncSP0002");
        }

        [Test]
        public async Task SP0002_RemovesNameMatchedPureAttributeFromAnyNamespace()
        {
            var source = @"
using System;

namespace External
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PureAttribute : Attribute
    {
    }
}

public static class TestClass
{
    [External.Pure]
    public static int Bad()
    {
        System.Console.WriteLine(1);
        return 0;
    }
}
";
            var fixedSource = @"
using System;

namespace External
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PureAttribute : Attribute
    {
    }
}

public static class TestClass
{
    public static int Bad()
    {
        System.Console.WriteLine(1);
        return 0;
    }
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(15, 23, 15, 26)
                .WithArguments("Bad");
            await VerifyCF.VerifyCodeFixAsync(
                source,
                expected,
                fixedSource,
                codeActionEquivalenceKey: "RemoveAttributesMatchingAsyncSP0002");
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
        public async Task SP0003_RemovesMisplacedEnforcePureOnEventField()
        {
            var source = @"
using System;
using SharpProof.Attributes;

public sealed class C
{
    [EnforcePure]
    public event Action E;
}
";
            var fixedSource = @"
using System;
using SharpProof.Attributes;

public sealed class C
{
    public event Action E;
}
";
            var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MisplacedAttributeId)
                .WithSpan(7, 6, 7, 17);
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
