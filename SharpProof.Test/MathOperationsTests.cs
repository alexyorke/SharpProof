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
    public class MathOperationsTests
    {
        [Test]
        public async Task ComplexNestedExpressions_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public double TestMethod(double x, double y, double z)
    {
        var a = Math.Sin(x) * Math.Cos(y);
        var b = Math.Pow(Math.E, z) / Math.PI; // Pure: Math.E/PI allowed
        var c = Math.Sqrt(Math.Abs(a * b));
        return Math.Max(a, Math.Min(b, c));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SimpleMathMethod_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public double TestMethod(double x)
    {
        return Math.Sin(x);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathConstant_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public double TestMethod()
    {
        return Math.PI; // Pure: Math.PI allowed
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathMethodChain_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public double TestMethod(double x)
    {
        return Math.Sin(Math.Cos(x));
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathDoubleHelpers_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double CeilingMethod(double x)
    {
        return Math.Ceiling(x);
    }

    [EnforcePure]
    public double FloorMethod(double x)
    {
        return Math.Floor(x);
    }

    [EnforcePure]
    public double TruncateMethod(double x)
    {
        return Math.Truncate(x);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathSignDecimal_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(decimal x)
    {
        return Math.Sign(x);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathCeilingDecimal_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal TestMethod(decimal x)
    {
        return Math.Ceiling(x);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathAbsOverloads_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal TestDecimal(decimal x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public double TestDouble(double x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public float TestFloat(float x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public int TestInt(int x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public long TestLong(long x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public nint TestNInt(nint x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public sbyte TestSByte(sbyte x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public short TestShort(short x)
    {
        return Math.Abs(x);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


