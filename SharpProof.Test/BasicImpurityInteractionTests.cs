using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class BasicImpurityInteractionTests
{
    [Test]
    public async Task ImpureMethodModifyingInstanceState_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System;

public abstract class Shape
{
    public int Id { get; protected set; }
    private static int _nextId = 1;

    // [EnforcePure] // Base ctor is impure
    protected Shape() // Line 9
    {
        Id = _nextId++;
    }

    [EnforcePure]
    public abstract double CalculateArea();

    // [EnforcePure] // Keep base pure for this test variant
    public virtual void Scale(double factor) { }

    [EnforcePure]
    public int GetId() => Id;
}

public class Circle : Shape
{
    public double Radius { get; private set; }
    private static readonly double PI = 3.14159;

    [EnforcePure] // Marked, but calls impure base ctor
    public Circle(double radius) : base() // Line 29
    {
        Radius = radius;
    }

    [EnforcePure]
    public override double CalculateArea() => PI * Radius * Radius;

    [EnforcePure] // Marked, impure override
    public override void Scale(double factor) // Line 38
    {
        this.Radius *= factor;
    }

    [EnforcePure] // Marked, impure method
    public void SetRadius(double newRadius) // Line 44
    {
        this.Radius = newRadius;
    }

    // SetCenter method removed as it wasn't relevant to original test intent

    [EnforcePure]
    public static double GetPi() => PI;
}

public class TestClass
{
    [EnforcePure] // Marked, calls impure SetRadius
    public void ProcessShape(Circle c) // Line 62
    {
        c.SetRadius(10.0);
    }

    [EnforcePure] // Marked, calls impure Scale
    public double CalculateAndScale(Circle c, double factor) // Line 68
    {
       double area = c.CalculateArea();
       c.Scale(factor);
       return area;
    }

    [EnforcePure]
    public double GetCircleArea(Circle c) => c.CalculateArea();

    [EnforcePure]
    public double GetStaticPi() => Circle.GetPi();
}
";

        var expectedGetId = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(7, 16, 7, 18).WithArguments("get_Id");
        var expectedGetRadius = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(28, 19, 28, 25).WithArguments("get_Radius");
        var expectedCtorCircle = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(32, 12, 32, 18)
            .WithArguments(".ctor");
        var expectedScaleCircle = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(41, 26, 41, 31).WithArguments("Scale");
        var expectedSetRadiusCircle = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(47, 17, 47, 26).WithArguments("SetRadius");
        var expectedProcessShape = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(61, 17, 61, 29).WithArguments("ProcessShape");
        var expectedCalculateAndScale = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(67, 19, 67, 36).WithArguments("CalculateAndScale");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedGetId,
            expectedGetRadius,
            expectedCtorCircle,
            expectedScaleCircle,
            expectedSetRadiusCircle,
            expectedProcessShape,
            expectedCalculateAndScale
        );
    }

    [Test]
    public async Task ImpureMethodCall_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class ConfigData
{
    private string _name = ""Default"";
    public string Name { get => _name; [EnforcePure] set { _name = value; } }

    [EnforcePure] // Method itself is impure
    public void Configure(string newName) // Line 10
    {
        this.Name = newName; // Line 12 - Calls impure setter
    }
}

public class TestClass
{
    [EnforcePure] // Method itself is impure
    public void ImpureMethodCall(ConfigData data) // Line 19
    {
        data.Configure(""NewName""); // Line 21 - Calls impure Configure
    }
}
";

        var expectedSetName = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(7, 19, 7, 23)
            .WithArguments("set_Name");
        var expectedGetName = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(7, 19, 7, 23).WithArguments("get_Name");
        var expectedConfigure = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 17, 10, 26)
            .WithArguments("Configure");
        var expectedImpureMethodCall = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(19, 17, 19, 33).WithArguments("ImpureMethodCall");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedSetName,
            expectedGetName,
            expectedConfigure,
            expectedImpureMethodCall
        );
    }
}