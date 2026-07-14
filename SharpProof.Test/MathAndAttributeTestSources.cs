namespace SharpProof.Test;

internal static class MathAndAttributeTestSources
{
    internal const string MinimalEnforcePureAttribute = @"
namespace SharpProof.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class EnforcePureAttribute : System.Attribute { }
}
";

    internal const string MemberEnforcePureAttribute = @"
namespace SharpProof.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Property | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
    public sealed class EnforcePureAttribute : System.Attribute { }
}
";

    internal const string ComplexNestedExpressions = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(double x, double y, double z)
    {
        var a = Math.Sin(x) * Math.Cos(y);
        var b = Math.Pow(Math.E, z) / Math.PI;
        var c = Math.Sqrt(Math.Abs(a * b));
        return Math.Max(a, Math.Min(b, c));
    }
}";

    internal const string SimpleMathMethod = @"
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

    internal const string MathConstant = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod()
    {
        return Math.PI;
    }
}";

    internal const string MathMethodChain = @"
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
}
