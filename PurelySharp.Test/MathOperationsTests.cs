using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class MathOperationsTests
    {
        [Test]
        public async Task ComplexNestedExpressions_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public double {|PS0002:TestMethod|}(double x, double y, double z)
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
        public async Task SimpleMathMethod_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public double {|PS0002:TestMethod|}(double x)
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
using PurelySharp.Attributes;



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
        public async Task MathMethodChain_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    [EnforcePure]
    public double {|PS0002:TestMethod|}(double x)
    {
        return Math.Sin(Math.Cos(x));
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MathDoubleHelpers_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|PS0002:CeilingMethod|}(double x)
    {
        return Math.Ceiling(x);
    }

    [EnforcePure]
    public double {|PS0002:FloorMethod|}(double x)
    {
        return Math.Floor(x);
    }

    [EnforcePure]
    public double {|PS0002:TruncateMethod|}(double x)
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
using PurelySharp.Attributes;

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
    }
}


