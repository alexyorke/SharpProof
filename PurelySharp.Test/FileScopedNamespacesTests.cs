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
    public class FileScopedNamespacesTests
    {
        [Test]
        public async Task FileScopedNamespace_PureMethod_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

// Using file-scoped namespace (C# 10 feature)
namespace TestNamespace;



// Class in file-scoped namespace
public class Calculator
{
    [EnforcePure]
    public int Add(int a, int b)
    {
        // Pure operation in a file-scoped namespace
        return a + b;
    }
    
    [EnforcePure]
    public double CalculateCircleArea(double radius)
    {
        // Pure: Math.PI read is now allowed
        return Math.PI * radius * radius;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FileScopedNamespace_WithNestedTypes_PureMethod_ExpectsDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

namespace MyCompany.Geometry; // File-scoped

// Record removed [EnforcePure]
public record struct Point(double X, double Y);

// Class removed [EnforcePure]
public class Circle
{
    public Point Center { get; } // PS0004 expected
    public double Radius { get; } // PS0004 expected

    public Circle(Point center, double radius) // PS0004 expected
    {
        Center = center;
        Radius = radius;
    }

    [EnforcePure]
    public double CalculateArea()
    {
        return System.Math.PI * Radius * Radius; // Pure calculation
    }

    [EnforcePure]
    public bool Contains(Point p)
    {
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;
        return dx * dx + dy * dy <= Radius * Radius; // Pure calculation
    }
}

public static class GeometryUtils
{
    [EnforcePure]
    public static Point Midpoint(Point p1, Point p2)
    {
        return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }
}
";

            var expectedGetCenter = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId).WithSpan(14, 18, 14, 24).WithArguments("get_Center");
            var expectedGetRadius = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId).WithSpan(15, 19, 15, 25).WithArguments("get_Radius");
            var expectedCircleCtor = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId).WithSpan(17, 12, 17, 18).WithArguments(".ctor");

            await VerifyCS.VerifyAnalyzerAsync(test,
                                             expectedGetCenter,
                                             expectedGetRadius,
                                             expectedCircleCtor
                                             );
        }

        [Test]
        public async Task FileScopedNamespace_WithMultipleClasses_PureMethods_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Linq;
using System.Collections.Generic;

// File-scoped namespace with multiple classes
namespace TestUtilities;



// First class in file-scoped namespace
public class StringUtils
{
    [EnforcePure]
    public string ReverseString(string input)
    {
        // Pure: transient char[] materialization is consumed immediately by the string constructor.
        return new string(input.Reverse().ToArray());
    }
}

// Second class in same file-scoped namespace
public class MathUtils
{
    [EnforcePure]
    public int Factorial(int n)
    {
        if (n <= 1) return 1;
        return n * Factorial(n - 1);
    }
}

// Third class in same file-scoped namespace
public static class Extensions
{
    [EnforcePure]
    public static bool IsEven(this int number)
    {
        // Pure extension method
        return number % 2 == 0;
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FileScopedNamespace_ImpureMethod_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.IO;

// File-scoped namespace with impure method
namespace TestImpure;



public class FileManager
{
    [EnforcePure]
    public void WriteToFile(string content)
    {
        // Impure operation: file system access
        File.WriteAllText(""test.txt"", content);
    }

    [EnforcePure]
    public string ReadCurrentTime()
    {
        // Impure operation: accessing current time
        return DateTime.Now.ToString();
    }
}";


            var expected = new[] {
                VerifyCS.Diagnostic("PS0002").WithSpan(14, 17, 14, 28).WithArguments("WriteToFile"),
                VerifyCS.Diagnostic("PS0002").WithSpan(21, 19, 21, 34).WithArguments("ReadCurrentTime")
            };
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task FileScopedNamespace_InterfaceImplementation_MissingAttributeDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

// File-scoped namespace with interface implementation
namespace TestInterfaces;

public interface ICalculator
{
    int Add(int a, int b);
}

public class Calculator : ICalculator
{
    [EnforcePure]
    public int Add(int a, int b)
    {
        return a + b;
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FileScopedNamespace_Record_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

// File-scoped namespace with record
namespace TestRecords;

public record Point(double X, double Y)
{
    [EnforcePure]
    public double DistanceFromOrigin()
    {
        // Pure operation (Math.Sqrt is pure, property reads are pure)
        return Math.Sqrt(X * X + Y * Y);
    }
}";


            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
