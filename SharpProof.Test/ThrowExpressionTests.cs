using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ThrowExpressionTests
{
    [Test]
    public async Task MethodWithThrowExpression_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public int {|SP0002:TestMethod|}(int value)
        {
            return value >= 0 ? value : throw new ArgumentException(""Invalid value"");
        }
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DirectThrowExpression_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}() => throw new ArgumentException(""Invalid value"");
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LocalFunctionWithDirectThrowExpression_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        int Local() => throw new ArgumentException(""Invalid value"");
        return Local();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LambdaWithDirectThrowExpression_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        Func<int> projector = () => throw new ArgumentException(""Invalid value"");
        return projector();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}