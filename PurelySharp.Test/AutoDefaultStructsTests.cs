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
    public class AutoDefaultStructsTests
    {
        [Test]
        public async Task AutoDefaultStruct_MissingAttributeAndUnknownPurityDiagnostics()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public struct Point
    {
        public double X { get; set; }
        public double Y { get; set; }
        
        [EnforcePure]
        public double CalculateDistance(Point other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
    
    public class PointTest
    {
        [EnforcePure]
        public static void ProcessPoint()
        {
            Point p1 = default;
            Point p2 = new Point { X = 3, Y = 4 };
            double distance = p1.CalculateDistance(p2);
        }
    }
}";


            var expectedGetX = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(9, 23, 9, 24).WithArguments("get_X");
            var expectedGetY = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(10, 23, 10, 24).WithArguments("get_Y");
            var expectedProcessPoint = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(24, 28, 24, 40).WithArguments("ProcessPoint");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetX, expectedGetY, expectedProcessPoint });
        }

        [Test]
        public async Task AutoDefaultStruct_WithConstructor_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public struct Vector3D
    {
        public double X;
        public double Y;
        public double Z;
        
        // Parameterless constructor allowed in C# 11
        public Vector3D()
        {
            // Only initialize Z, leaving X and Y with their default values
            Z = 1.0;
        }
        
        [EnforcePure]
        public double CalculateMagnitude()
        {
            // Pure method using auto-default values and manually set values
            return Math.Sqrt(X * X + Y * Y + Z * Z);
        }
    }
    
    public class VectorTest
    {
        public static double TestVector()
        {
            Vector3D v = new Vector3D(); // Will call the parameterless constructor
            return v.CalculateMagnitude(); // Should return 1.0 (Z=1, X=0, Y=0)
        }
    }
}";


            var expectedCtor = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(14, 16, 14, 24).WithArguments(".ctor");
            var expectedTestVector = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(30, 30, 30, 40).WithArguments("TestVector");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedCtor, expectedTestVector });
        }

        [Test]
        public async Task AutoDefaultStruct_WithReadonlyFields_PureMethod_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public readonly struct Temperature
    {
        public readonly double Celsius; // Will be auto-defaulted to 0
        public readonly double Fahrenheit => (Celsius * 9 / 5) + 32;
        
        [EnforcePure]
        public bool IsFreezing()
        {
            // Pure method using auto-default values
            return Celsius <= 0;
        }
    }
    
    public class TemperatureTest
    {
        public static void TestTemperature()
        {
            Temperature temp = new Temperature(); // Auto-default readonly struct
            Console.WriteLine(temp.IsFreezing()); // Should output True
        }
    }
}";




            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AutoDefaultStruct_WithProperties_MissingAttributeDiagnostics()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public struct Rectangle
    {
        // Auto-properties will be auto-defaulted to 0
        public int Width { get; set; }
        public int Height { get; set; }
        
        [EnforcePure]
        public int CalculateArea()
        {
            // Pure method using auto-default property values
            return Width * Height;
        }
    }
    
    public class RectangleTest
    {
        public static void TestRectangle()
        {
            Rectangle rect = new Rectangle(); // Auto-default struct with properties
            Console.WriteLine(rect.CalculateArea()); // Should output 0
        }
    }
}";


            var expectedGetWidth = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(10, 20, 10, 25).WithArguments("get_Width");
            var expectedGetHeight = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 20, 11, 26).WithArguments("get_Height");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetWidth, expectedGetHeight });
        }

        [Test]
        public async Task AutoDefaultStruct_ImpureMethod_Diagnostic()
        {
            var test = @"
using System;
using System.IO;
using PurelySharp.Attributes;

namespace TestNamespace
{
    // Define a struct with an impure method marked as pure
    public struct Logger
    {
        public string LogPath { get; set; }

        [EnforcePure]
        public void WriteLog(string message)
        {
            // Impure operation
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
    }

    public class TestClass
    {
        public void LogSomething()
        {
            Logger logger = default; // Auto-default struct instantiation
            logger.LogPath = ""app.log""; // State modification
            logger.WriteLog(""Test log entry""); // Call impure method
        }
    }
}";



            var expectedGetLogPath = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 23, 11, 30).WithArguments("get_LogPath");
            var expectedWriteLog = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(14, 21, 14, 29).WithArguments("WriteLog");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetLogPath, expectedWriteLog });
        }

        [Test]
        public async Task AutoDefaultStruct_WithNestedStructs_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public struct Coordinate
    {
        public double Latitude; // Auto-defaulted to 0
        public double Longitude; // Auto-defaulted to 0
    }
    
    public struct GeoLocation
    {
        public Coordinate Position; // Nested struct is also auto-defaulted
        public string Name; // Auto-defaulted to null
        
        [EnforcePure]
        public bool IsOrigin()
        {
            // Pure method using auto-default values in nested struct
            return Position.Latitude == 0 && Position.Longitude == 0;
        }
    }
    
    public class GeoTest
    {
        public static void TestGeo()
        {
            GeoLocation location = new GeoLocation(); // Auto-default with nested struct
            Console.WriteLine(location.IsOrigin()); // Should output True
        }
    }
}";


            var expectedIsOrigin = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(19, 21, 19, 29).WithArguments("IsOrigin");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedIsOrigin });
        }
    }
}


