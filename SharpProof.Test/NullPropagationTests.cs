using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class NullPropagationTests
{
    private static readonly ImmutableArray<MetadataReference> NullPropagationFrameworkReferences =
        AnalyzerTestHost.GetMinimalFrameworkReferences();

    [Test]
    public async Task PureMethodWithNullPropagation_ReportsMutablePropertyDiagnostics()
    {
        var test = """
                   #nullable enable
                   using System;
                   using SharpProof.Attributes;

                   public class Person
                   {
                       public string Name { get; set; } = "";
                       public int    Age  { get; set; }
                   }

                   public class TestClass
                   {
                       [EnforcePure]
                       public string TestMethod(Person? person)
                       {
                           // Null-propagation itself is pure; analyzer flags this due to setter on Name.
                           return person?.Name ?? "Unknown";
                       }
                   }
                   """;
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            test,
            frameworkReferences: NullPropagationFrameworkReferences,
            concurrentAnalysis: true);
        AssertExpectedNullPropagationDiagnostics(diagnostics, "TestMethod");
    }

    [Test]
    public async Task ImpureMethodWithNullPropagation_Diagnostic()
    {
        var test = """
                   #nullable enable
                   using System;
                   using SharpProof.Attributes;

                   public class Person
                   {
                       public string Name { get; set; } = "";
                       public int    Age  { get; set; }

                       public void LogToConsole() => Console.WriteLine(Name);
                   }

                   public class TestClass
                   {
                       [EnforcePure]
                       public void TestMethod(Person? person)
                       {
                           // Null-propagation followed by an impure operation.
                           person?.LogToConsole();
                       }
                   }
                   """;
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            test,
            frameworkReferences: NullPropagationFrameworkReferences,
            concurrentAnalysis: true);
        AssertExpectedNullPropagationDiagnostics(diagnostics, "TestMethod");
    }

    [Test]
    public async Task PureMethodWithNullPropagationAndImpureOperation_Diagnostic()
    {
        var test = """
                   #nullable enable
                   using System;
                   using SharpProof.Attributes;

                   public class Person
                   {
                       public string Name { get; set; } = "";
                       public int    Age  { get; set; }
                   }

                   public class TestClass
                   {
                       private int _counter;

                       [EnforcePure]
                       public string TestMethod(Person? person)
                       {
                           // Pure null-propagation.
                           var name = person?.Name ?? "Unknown";

                           // Impure state modification.
                           _counter++;

                           return name;
                       }
                   }
                   """;
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            test,
            frameworkReferences: NullPropagationFrameworkReferences,
            concurrentAnalysis: true);
        AssertExpectedNullPropagationDiagnostics(diagnostics, "TestMethod");
    }

    private static void AssertExpectedNullPropagationDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        string methodName)
    {
        var sp0004Messages = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();
        var sp0002Messages = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();

        Assert.That(sp0004Messages, Has.Length.EqualTo(2));
        Assert.That(sp0004Messages, Has.Some.Contains("get_Name"));
        Assert.That(sp0004Messages, Has.Some.Contains("get_Age"));
        Assert.That(sp0002Messages, Has.Length.EqualTo(1));
        Assert.That(sp0002Messages[0], Does.Contain("'" + methodName + "'"));
        Assert.That(diagnostics, Has.Length.EqualTo(3));
    }
}