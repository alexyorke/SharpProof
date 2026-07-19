using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class PureStateInteractionTests
{
    [Test]
    public async Task PureInteractionsWithState_MissingAttributeDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;
using System;

public abstract class Shape
{
    public int Id { get; }
    protected Shape(int id) { Id = id; }

    [EnforcePure]
    public abstract double CalculateArea();

    [EnforcePure]
    public int GetId() => Id;
}

public class Circle : Shape
{
    public double Radius { get; }
    private static readonly double PI = Math.PI;

    public Circle(int id, double radius) : base(id)
    {
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        Radius = radius;
    }

    [EnforcePure]
    public override double CalculateArea() => PI * Radius * Radius;

    [EnforcePure]
    public Circle {|SP0002:CreateScaledCopy|}(double factor)
    {
        if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        return new Circle(this.Id, this.Radius * factor);
    }

    [EnforcePure]
    public static double GetPi() => PI;
}

public class TestClass
{
    [EnforcePure]
    public double GetCircleArea(Circle c) => c.CalculateArea();

    [EnforcePure]
    public double {|SP0002:GetScaledArea|}(Circle c, double factor)
    {
        Circle scaled = c.CreateScaledCopy(factor);
        return scaled.CalculateArea();
    }

     [EnforcePure]
    public double GetStaticPi() => Circle.GetPi();
}
";

        var expectedGetId = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 16, 7, 18).WithArguments("get_Id");
        var expectedShapeCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(8, 15, 8, 20).WithArguments(".ctor");
        var expectedGetRadius = VerifyCS.Diagnostic("SP0004")
            .WithSpan(19, 19, 19, 25).WithArguments("get_Radius");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedGetId,
            expectedShapeCtor,
            expectedGetRadius
        );
    }
}