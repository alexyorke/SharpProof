using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ExpressionTreeTests
{
    [Test]
    public async Task Expression_Building_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using System.Linq.Expressions;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public Expression<Func<int, int>> TestMethod()
    {
        // Pure: Building in-memory representation of code
        ParameterExpression param = Expression.Parameter(typeof(int), ""x"");
        ConstantExpression constOne = Expression.Constant(1, typeof(int));
        BinaryExpression addExpr = Expression.Add(param, constOne);
        return Expression.Lambda<Func<int, int>>(addExpr, param);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task IQueryableExpressionProperty_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Linq;
using System.Linq.Expressions;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Expression {|SP0002:TestMethod|}(IQueryable<int> query)
    {
        return query.Expression;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}