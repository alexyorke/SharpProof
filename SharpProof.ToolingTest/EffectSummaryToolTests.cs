using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
[Explicit("Effect summary JSON artifacts are removed from the active repo flow.")]
public partial class EffectSummaryToolTests
{
    [Test]
    public async Task EffectSummaryTool_CollectsCommonDirectExceptionTypes()
    {
        var source = """
                     using System;

                     public static class ExceptionFixture
                     {
                         public static void ThrowIndexOutOfRange() => throw new IndexOutOfRangeException();

                         public static void ThrowInvalidCast() => throw new InvalidCastException();

                         public static void ThrowObjectDisposed() => throw new ObjectDisposedException("stream");

                         public static void ThrowFormat() => throw new FormatException();

                         public static void ThrowOverflow() => throw new OverflowException();
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCommonExceptions", source);
        using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, true);

        AssertThrownExceptions(summary, "ExceptionFixture.ThrowIndexOutOfRange()", "System.IndexOutOfRangeException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowInvalidCast()", "System.InvalidCastException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowObjectDisposed()", "System.ObjectDisposedException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowFormat()", "System.FormatException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowOverflow()", "System.OverflowException");
    }

    [Test]
    public async Task EffectSummaryTool_SuppressesCaughtThrows_And_PreservesRethrowAndTransitiveExceptions()
    {
        var source = """
                     using System;

                     public class FixtureBaseException : Exception
                     {
                     }

                     public sealed class FixtureDerivedException : FixtureBaseException
                     {
                     }

                     public static class ExceptionFixture
                     {
                         public static void ThrowDirect()
                         {
                             throw new InvalidOperationException("boom");
                         }

                         public static void ThrowViaLocal()
                         {
                             var ex = new ObjectDisposedException("stream");
                             throw ex;
                         }

                         private static InvalidOperationException CreateInvalidOperation()
                         {
                             return new InvalidOperationException("boom");
                         }

                         private static InvalidOperationException CreateInvalidOperation<T>()
                         {
                             return new InvalidOperationException("boom");
                         }

                         private static ObjectDisposedException CreateObjectDisposed()
                         {
                             return new ObjectDisposedException("stream");
                         }

                         private static Exception CreateBaseException()
                         {
                             return new InvalidOperationException("boom");
                         }

                         private static Exception? MaybeNullException()
                         {
                             return null;
                         }

                         private static Exception CreateVariantException(bool first)
                         {
                             return first
                                 ? new InvalidOperationException("boom")
                                 : new ObjectDisposedException("stream");
                         }

                         public static void ThrowViaFactoryReturn()
                         {
                             throw CreateInvalidOperation();
                         }

                         public static void ThrowViaGenericFactory()
                         {
                             throw CreateInvalidOperation<int>();
                         }

                         public static void ThrowViaFactoryLocal()
                         {
                             var ex = CreateObjectDisposed();
                             throw ex;
                         }

                         public static void ThrowViaBaseFactory()
                         {
                             throw CreateBaseException();
                         }

                         public static void ThrowViaMaybeNullFactory()
                         {
                             throw MaybeNullException();
                         }

                         public static void ThrowViaVariantFactory(bool first)
                         {
                             throw CreateVariantException(first);
                         }

                         public static void ThrowViaCallee()
                         {
                             ThrowDirect();
                         }

                         private static void ThrowDerived()
                         {
                             throw new FixtureDerivedException();
                         }

                         public static int CatchTransitiveDerivedAsBase()
                         {
                             try
                             {
                                 ThrowDerived();
                                 return 0;
                             }
                             catch (FixtureBaseException)
                             {
                                 return 1;
                             }
                         }

                         public static int HandleLocally()
                         {
                             try
                             {
                                 throw new FormatException();
                             }
                             catch (FormatException)
                             {
                                 return 1;
                             }
                         }

                         public static void RethrowOverflow()
                         {
                             try
                             {
                                 throw new OverflowException();
                             }
                             catch (OverflowException)
                             {
                                 throw;
                             }
                         }

                         public static int RethrowFormatCaughtByOuter()
                         {
                             try
                             {
                                 try
                                 {
                                     throw new FormatException();
                                 }
                                 catch (Exception)
                                 {
                                     throw;
                                 }
                             }
                             catch (FormatException)
                             {
                                 return 0;
                             }
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryControlFlow", source);
        using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, true);

        AssertThrownExceptions(summary, "ExceptionFixture.ThrowDirect()", "System.InvalidOperationException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaLocal()", "System.ObjectDisposedException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaFactoryReturn()", "System.InvalidOperationException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaGenericFactory()",
            "System.InvalidOperationException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaFactoryLocal()", "System.ObjectDisposedException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaBaseFactory()", "System.InvalidOperationException");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaMaybeNullFactory()");
        AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaVariantFactory(bool)");
        AssertTransitiveExceptions(summary, "ExceptionFixture.ThrowViaCallee()", "System.InvalidOperationException");
        AssertTransitiveExceptionEdges(
            summary,
            "ExceptionFixture.ThrowViaCallee()",
            ("System.InvalidOperationException", "ExceptionFixture.ThrowDirect()->void",
                "ExceptionFixture.ThrowViaCallee() -> ExceptionFixture.ThrowDirect()", 1));
        AssertTransitiveExceptions(summary, "ExceptionFixture.CatchTransitiveDerivedAsBase()");
        AssertTransitiveExceptionEdges(summary, "ExceptionFixture.CatchTransitiveDerivedAsBase()");
        AssertThrownExceptions(summary, "ExceptionFixture.HandleLocally()");
        AssertTransitiveExceptionEdges(summary, "ExceptionFixture.HandleLocally()");
        AssertThrownExceptions(summary, "ExceptionFixture.RethrowOverflow()", "System.OverflowException");
        AssertThrownExceptions(summary, "ExceptionFixture.RethrowFormatCaughtByOuter()");
    }

    [Test]
    public async Task EffectSummaryTool_FinallyThrow_ShadowsEarlierEscapingDirectAndTransitiveExceptions()
    {
        var source = """
                     using System;

                     public static class ExceptionFixture
                     {
                         private static void ThrowDirect()
                         {
                             throw new InvalidOperationException("boom");
                         }

                         public static void DirectThrowShadowedByFinally()
                         {
                             try
                             {
                                 throw new InvalidOperationException("boom");
                             }
                             finally
                             {
                                 throw new FormatException();
                             }
                         }

                         public static void TransitiveCallShadowedByFinally()
                         {
                             try
                             {
                                 ThrowDirect();
                             }
                             finally
                             {
                                 throw new FormatException();
                             }
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryFinallyShadowing", source);
        using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, true);

        AssertThrownExceptions(summary, "ExceptionFixture.DirectThrowShadowedByFinally()", "System.FormatException");
        AssertThrownExceptions(summary, "ExceptionFixture.TransitiveCallShadowedByFinally()", "System.FormatException");
        AssertTransitiveExceptions(summary, "ExceptionFixture.TransitiveCallShadowedByFinally()",
            "System.FormatException");
        AssertTransitiveExceptionEdges(summary, "ExceptionFixture.TransitiveCallShadowedByFinally()");
    }

    [Test]
    public async Task EffectSummaryTool_CycleTransitiveExceptions_DoNotMemoizePartialResults()
    {
        var source = """
                     using System;

                     public static class CycleFixture
                     {
                         public static void A()
                         {
                             B();
                             C();
                         }

                         public static void B()
                         {
                             A();
                         }

                         public static void C()
                         {
                             throw new InvalidOperationException();
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCycleTransitiveExceptions", source);
        using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, true);

        AssertTransitiveExceptionsContain(summary, "CycleFixture.B()", "System.InvalidOperationException");
    }

    [Test]
    public async Task EffectSummaryTool_FrameworkNetMoniker_ReportsUnsupportedFrameworkMoniker()
    {
        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-framework-parse-" + Guid.NewGuid().ToString("N") + ".json");

        var result = await RunEffectSummaryProcessAsync(
            "--framework",
            "net",
            "--runtime-assembly",
            "DefinitelyMissing.Runtime.dll",
            "--output",
            outputPath);

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.StandardError, Does.Contain("Unsupported framework moniker"));
    }

    [Test]
    public async Task EffectSummaryTool_FilteredSummary_UnboundedDepth_PreservesDeepTransitiveExceptions()
    {
        var source = """
                     using System;

                     public static class ExceptionFixture
                     {
                         public static string Outer(string value) => Middle(value);

                         private static string Middle(string value) => Inner(value);

                         private static string Inner(string value) => Leaf(value);

                         private static string Leaf(string value)
                         {
                             if (string.IsNullOrWhiteSpace(value))
                             {
                                 throw new InvalidOperationException();
                             }

                             return value;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDeepFilteredExceptions", source);
        using var boundedSummary = await RunFilteredEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            1,
            "ExceptionFixture.Outer");
        using var unboundedSummary = await RunFilteredEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            -1,
            "ExceptionFixture.Outer");

        AssertTransitiveExceptions(boundedSummary, "ExceptionFixture.Outer(string)",
            "System.InvalidOperationException");
        AssertTransitiveExceptionSourcePaths(
            boundedSummary,
            "ExceptionFixture.Outer(string)",
            ("System.InvalidOperationException",
                "ExceptionFixture.Outer(string) -> ExceptionFixture.Middle(string) -> ExceptionFixture.Inner(string) -> ExceptionFixture.Leaf(string)"));
        AssertTransitiveExceptionEdges(
            boundedSummary,
            "ExceptionFixture.Outer(string)",
            ("System.InvalidOperationException", "ExceptionFixture.Leaf(string)->string",
                "ExceptionFixture.Outer(string) -> ExceptionFixture.Middle(string) -> ExceptionFixture.Inner(string) -> ExceptionFixture.Leaf(string)",
                3));
        Assert.That(
            FindMethod(boundedSummary, "ExceptionFixture.Outer(string)")
                .GetProperty("TransitiveThrownExceptionEdges")
                .GetArrayLength(),
            Is.EqualTo(1));
        Assert.That(
            FindMethodsByPrefix(boundedSummary, "ExceptionFixture.")
                .Select(method => method.GetProperty("DisplayName").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToArray(),
            Is.EqualTo(new[]
            {
                "ExceptionFixture.Middle(string)",
                "ExceptionFixture.Outer(string)"
            }));

        AssertThrownExceptions(unboundedSummary, "ExceptionFixture.Leaf(string)", "System.InvalidOperationException");
        AssertTransitiveExceptions(unboundedSummary, "ExceptionFixture.Outer(string)",
            "System.InvalidOperationException");
        AssertTransitiveExceptionSourcePaths(
            unboundedSummary,
            "ExceptionFixture.Outer(string)",
            ("System.InvalidOperationException",
                "ExceptionFixture.Outer(string) -> ExceptionFixture.Middle(string) -> ExceptionFixture.Inner(string) -> ExceptionFixture.Leaf(string)"));
        AssertTransitiveExceptionEdges(
            unboundedSummary,
            "ExceptionFixture.Outer(string)",
            ("System.InvalidOperationException", "ExceptionFixture.Leaf(string)->string",
                "ExceptionFixture.Outer(string) -> ExceptionFixture.Middle(string) -> ExceptionFixture.Inner(string) -> ExceptionFixture.Leaf(string)",
                3));
        Assert.That(
            FindMethod(unboundedSummary, "ExceptionFixture.Outer(string)")
                .GetProperty("TransitiveThrownExceptionEdges")
                .GetArrayLength(),
            Is.EqualTo(1));
        Assert.That(
            FindMethodsByPrefix(unboundedSummary, "ExceptionFixture.")
                .Select(method => method.GetProperty("DisplayName").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToArray(),
            Is.EqualTo(new[]
            {
                "ExceptionFixture.Inner(string)",
                "ExceptionFixture.Leaf(string)",
                "ExceptionFixture.Middle(string)",
                "ExceptionFixture.Outer(string)"
            }));
    }

    [Test]
    public async Task EffectSummaryTool_FilteredSummary_WithoutIncludeCallees_EmitsOnlyRootMethods()
    {
        var source = """
                     using System;

                     public static class FilterFixture
                     {
                         public static int Outer(int value) => Middle(value) + 1;

                         private static int Middle(int value) => Inner(value) + 1;

                         private static int Inner(int value)
                         {
                             if (value < 0)
                             {
                                 throw new InvalidOperationException();
                             }

                             return value;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryFilteredRootOnly", source);
        using var summary = await RunFilteredEffectSummaryAsync(
            fixture.AssemblyPath,
            false,
            1,
            false,
            "FilterFixture.Outer");

        Assert.That(
            FindMethodsByPrefix(summary, "FilterFixture.")
                .Select(method => method.GetProperty("DisplayName").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToArray(),
            Is.EqualTo(new[]
            {
                "FilterFixture.Outer(int)"
            }));
    }

    [Test]
    public async Task EffectSummaryTool_Produces_ReportOnly_Purity_Classifications()
    {
        var source = """
                     using System;

                     public interface IWorker
                     {
                         int Get();
                     }

                     public abstract class AbstractWorker
                     {
                         public abstract int Get();
                     }

                     public static class PurityFixture
                     {
                         private static int _state;

                         public static int PureLeaf() => 42;

                         public static int PureViaCallee() => PureLeaf();

                         public static int ImpureWrite()
                         {
                             _state++;
                             return _state;
                         }

                         public static int ImpureViaCallee() => ImpureWrite();

                         public static int UnknownViaInterface(IWorker worker) => worker.Get();

                         public static byte[] PureFreshArray()
                         {
                             var bytes = new byte[4];
                             bytes[0] = 1;
                             return bytes;
                         }

                         public static void MutateCallerArray(byte[] bytes)
                         {
                             bytes[0] = 1;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryPurityClassification", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            true);

        Assert.That(summary.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(5));
        Assert.That(summary.RootElement.GetProperty("EvidenceSchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(summary.RootElement.TryGetProperty("EvidenceSchemaCompatibility", out _), Is.False);
        AssertPurityClassification(summary, "PurityFixture.PureLeaf()", "pure");
        AssertPurityClassification(summary, "PurityFixture.PureViaCallee()", "pure");
        AssertPurityClassification(summary, "PurityFixture.ImpureWrite()", "impure", "global_state_write");
        AssertPurityClassification(summary, "PurityFixture.ImpureViaCallee()", "impure", "impure_callee");
        AssertPurityClassification(summary, "PurityFixture.UnknownViaInterface(IWorker)", "conservative_unknown",
            "unknown_callee");
        AssertPurityClassification(summary, "AbstractWorker.Get()", "conservative_unknown",
            "metadata_only_or_external");
        AssertPurityClassification(summary, "PurityFixture.PureFreshArray()", "pure");
        AssertPurityClassification(summary, "PurityFixture.MutateCallerArray(byte[])", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "PurityFixture.PureLeaf()", "none");
        AssertEffectVisibilityClassification(summary, "PurityFixture.PureViaCallee()", "none");
        AssertEffectVisibilityClassification(summary, "PurityFixture.ImpureWrite()", "caller_visible");
        AssertEffectVisibilityClassification(summary, "PurityFixture.ImpureViaCallee()", "caller_visible");
        AssertEffectVisibilityClassification(summary, "PurityFixture.UnknownViaInterface(IWorker)", "unknown");
        AssertEffectVisibilityClassification(summary, "AbstractWorker.Get()", "unknown");
        AssertEffectVisibilityClassification(summary, "PurityFixture.PureFreshArray()", "internal_only");
        AssertEffectVisibilityClassification(summary, "PurityFixture.MutateCallerArray(byte[])", "caller_visible");
        AssertFreshnessClassification(summary, "PurityFixture.PureFreshArray()", "fresh_owned_array_write");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(5));
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThanOrEqualTo(8));
        Assert.That(report.GetProperty("PureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));
        Assert.That(report.GetProperty("ImpureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
    }

    [Test]
    public async Task EffectSummaryTool_CrossAssemblyPureChain_SelfBootstrapsGeneratedPurity()
    {
        const string leafSource = """
                                  public static class CrossAssemblyLeaf
                                  {
                                      public static int AddOne(int value) => value + 1;
                                  }
                                  """;

        const string middleSource = """
                                    public static class CrossAssemblyMiddle
                                    {
                                        public static int AddOne(int value) => CrossAssemblyLeaf.AddOne(value);
                                    }
                                    """;

        const string rootSource = """
                                  public static class CrossAssemblyRoot
                                  {
                                      public static int AddTwo(int value) => CrossAssemblyMiddle.AddOne(value) + 1;
                                  }
                                  """;

        await using var leaf = await CreateFixtureAssemblyAsync("CrossAssemblyLeaf", leafSource);
        await using var middle = await CreateFixtureAssemblyAsync(
            "CrossAssemblyMiddle",
            middleSource,
            MetadataReference.CreateFromFile(leaf.AssemblyPath));
        await using var root = await CreateFixtureAssemblyAsync(
            "CrossAssemblyRoot",
            rootSource,
            MetadataReference.CreateFromFile(middle.AssemblyPath));

        using var summary = await RunEffectSummaryAsync(
            new[] { root.AssemblyPath, middle.AssemblyPath, leaf.AssemblyPath },
            true,
            true);

        AssertPurityClassification(summary, "CrossAssemblyRoot.AddTwo(int)", "pure");
        AssertEffectVisibilityClassification(summary, "CrossAssemblyRoot.AddTwo(int)", "none");
    }

    [Test]
    public async Task EffectSummaryTool_CrossAssemblyImpureChain_SelfBootstrapsGeneratedPurity()
    {
        const string leafSource = """
                                  public static class CrossAssemblyImpureLeaf
                                  {
                                      private static int s_value;

                                      public static int Increment()
                                      {
                                          s_value++;
                                          return s_value;
                                      }
                                  }
                                  """;

        const string middleSource = """
                                    public static class CrossAssemblyImpureMiddle
                                    {
                                        public static int Increment() => CrossAssemblyImpureLeaf.Increment();
                                    }
                                    """;

        const string rootSource = """
                                  public static class CrossAssemblyImpureRoot
                                  {
                                      public static int IncrementPlusOne() => CrossAssemblyImpureMiddle.Increment() + 1;
                                  }
                                  """;

        await using var leaf = await CreateFixtureAssemblyAsync("CrossAssemblyImpureLeaf", leafSource);
        await using var middle = await CreateFixtureAssemblyAsync(
            "CrossAssemblyImpureMiddle",
            middleSource,
            MetadataReference.CreateFromFile(leaf.AssemblyPath));
        await using var root = await CreateFixtureAssemblyAsync(
            "CrossAssemblyImpureRoot",
            rootSource,
            MetadataReference.CreateFromFile(middle.AssemblyPath));

        using var summary = await RunEffectSummaryAsync(
            new[] { root.AssemblyPath, middle.AssemblyPath, leaf.AssemblyPath },
            true,
            true);

        AssertPurityClassification(summary, "CrossAssemblyImpureRoot.IncrementPlusOne()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "CrossAssemblyImpureRoot.IncrementPlusOne()", "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_UnresolvedNonInteropCallee_IsConservativeUnknown()
    {
        const string dependencySource = """
                                        public static class MissingDependency
                                        {
                                            public static int Read() => 1;
                                        }
                                        """;
        const string rootSource = """
                                  public static class UnresolvedCallRoot
                                  {
                                      public static int Read() => MissingDependency.Read();
                                  }
                                  """;

        await using var dependency = await CreateFixtureAssemblyAsync(
            "MissingEffectSummaryDependency",
            dependencySource);
        await using var root = await CreateFixtureAssemblyAsync(
            "UnresolvedEffectSummaryCall",
            rootSource,
            MetadataReference.CreateFromFile(dependency.AssemblyPath));

        using var summary = await RunEffectSummaryAsync(
            new[] { root.AssemblyPath },
            true,
            true);

        AssertPurityClassification(
            summary,
            "UnresolvedCallRoot.Read()",
            "conservative_unknown",
            "unknown_callee");
        AssertEffectVisibilityClassification(summary, "UnresolvedCallRoot.Read()", "unknown");
    }

    [Test]
    public async Task EffectSummaryTool_CapturesDeterministicStringComparisonArgumentEvidence()
    {
        var source = """
                     using System;

                     public static class StringComparisonFixture
                     {
                         public static bool Deterministic(string left, string right)
                         {
                             return left.Equals(right, StringComparison.OrdinalIgnoreCase);
                         }

                         public static bool CurrentCulture(string left, string right)
                         {
                             return left.Equals(right, StringComparison.CurrentCulture);
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryStringComparisonCallsites", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            true);

        var deterministicCallSite = FindMethod(summary, "StringComparisonFixture.Deterministic(string, string)")
            .GetProperty("CallSites")
            .EnumerateArray()
            .Single(callSite => string.Equals(
                FormatStructuralIdentity(callSite.GetProperty("Identity"), includeReturnType: true),
                "string.Equals(string, System.StringComparison)->bool",
                StringComparison.Ordinal));
        var deterministicEvidence = deterministicCallSite.GetProperty("ArgumentEvidence")
            .EnumerateArray()
            .Single(evidence => string.Equals(
                evidence.GetProperty("Type").GetString(),
                "System.StringComparison",
                StringComparison.Ordinal));
        Assert.That(deterministicEvidence.GetProperty("Value").GetString(),
            Is.EqualTo("System.StringComparison.OrdinalIgnoreCase"));

        var currentCultureCallSite = FindMethod(summary, "StringComparisonFixture.CurrentCulture(string, string)")
            .GetProperty("CallSites")
            .EnumerateArray()
            .Single(callSite => string.Equals(
                FormatStructuralIdentity(callSite.GetProperty("Identity"), includeReturnType: true),
                "string.Equals(string, System.StringComparison)->bool",
                StringComparison.Ordinal));
        var currentCultureEvidence = currentCultureCallSite.GetProperty("ArgumentEvidence")
            .EnumerateArray()
            .Single(evidence => string.Equals(
                evidence.GetProperty("Type").GetString(),
                "System.StringComparison",
                StringComparison.Ordinal));
        Assert.That(currentCultureEvidence.GetProperty("Value").GetString(),
            Is.EqualTo("System.StringComparison.CurrentCulture"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitConverterSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.GetBytes", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        Assert.That(generatedCatalog.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(5));
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("DisplayName").GetString()
                    ?.StartsWith("System.BitConverter.GetBytes", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(generatedRows, Has.Length.EqualTo(11));
        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.BitConverter.GetBytes(bool)",
                "System.BitConverter.GetBytes(char)",
                "System.BitConverter.GetBytes(short)",
                "System.BitConverter.GetBytes(int)",
                "System.BitConverter.GetBytes(long)",
                "System.BitConverter.GetBytes(ushort)",
                "System.BitConverter.GetBytes(uint)",
                "System.BitConverter.GetBytes(ulong)",
                "System.BitConverter.GetBytes(System.Half)",
                "System.BitConverter.GetBytes(float)",
                "System.BitConverter.GetBytes(double)"
            }));

        foreach (var row in generatedRows)
        {
            Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("fresh_owned_array_write"));
            Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("internal_only"));
            Assert.That(row.GetProperty("HasFreshArrayAllocationEvidence").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitOperationsIsPow2Slice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations.IsPow2", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("DisplayName").GetString()
                    ?.StartsWith("System.Numerics.BitOperations.IsPow2", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(generatedRows, Has.Length.EqualTo(6));
        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.Numerics.BitOperations.IsPow2(int)",
                "System.Numerics.BitOperations.IsPow2(uint)",
                "System.Numerics.BitOperations.IsPow2(long)",
                "System.Numerics.BitOperations.IsPow2(ulong)",
                "System.Numerics.BitOperations.IsPow2(nint)",
                "System.Numerics.BitOperations.IsPow2(nuint)"
            }));

        foreach (var row in generatedRows)
        {
            Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBinaryPrimitivesReadSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Buffers.Binary.BinaryPrimitives.Read", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("DisplayName").GetString()?.StartsWith("System.Buffers.Binary.BinaryPrimitives.Read",
                    StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(generatedRows.Length, Is.GreaterThanOrEqualTo(12));
        var representativeSymbols = new[]
        {
            "System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadHalfBigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadHalfLittleEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(System.ReadOnlySpan`1<byte>)"
        };

        foreach (var symbol in representativeSymbols)
            Assert.That(
                generatedRows.Any(row =>
                    string.Equals(row.GetProperty("DisplayName").GetString(), symbol, StringComparison.Ordinal)),
                Is.True,
                symbol);

        var freshOwnedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Buffers.Binary.BinaryPrimitives.ReadHalfBigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadInt128BigEndian(System.ReadOnlySpan`1<byte>)",
            "System.Buffers.Binary.BinaryPrimitives.ReadUInt128BigEndian(System.ReadOnlySpan`1<byte>)"
        };

        foreach (var row in generatedRows)
        {
            var symbol = row.GetProperty("DisplayName").GetString();
            Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(
                row.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo(freshOwnedSymbols.Contains(symbol!) ? "fresh_owned_object_write" : "none"),
                symbol);
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBinaryPrimitivesWriteAndTryWriteSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.Buffers.Binary.BinaryPrimitives.Write",
            "System.Buffers.Binary.BinaryPrimitives.TryWrite");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var expectedSymbols = new[]
        {
            "System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(System.Span`1<byte>, double)",
            "System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(System.Span`1<byte>, double)",
            "System.Buffers.Binary.BinaryPrimitives.WriteHalfBigEndian(System.Span`1<byte>, System.Half)",
            "System.Buffers.Binary.BinaryPrimitives.WriteHalfLittleEndian(System.Span`1<byte>, System.Half)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt128BigEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt128LittleEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(System.Span`1<byte>, short)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(System.Span`1<byte>, short)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(System.Span`1<byte>, int)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(System.Span`1<byte>, int)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(System.Span`1<byte>, long)",
            "System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(System.Span`1<byte>, long)",
            "System.Buffers.Binary.BinaryPrimitives.WriteIntPtrBigEndian(System.Span`1<byte>, nint)",
            "System.Buffers.Binary.BinaryPrimitives.WriteIntPtrLittleEndian(System.Span`1<byte>, nint)",
            "System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(System.Span`1<byte>, float)",
            "System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(System.Span`1<byte>, float)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt128BigEndian(System.Span`1<byte>, System.UInt128)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt128LittleEndian(System.Span`1<byte>, System.UInt128)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(System.Span`1<byte>, ushort)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(System.Span`1<byte>, ushort)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(System.Span`1<byte>, uint)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(System.Span`1<byte>, uint)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(System.Span`1<byte>, ulong)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(System.Span`1<byte>, ulong)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUIntPtrBigEndian(System.Span`1<byte>, nuint)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUIntPtrLittleEndian(System.Span`1<byte>, nuint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteDoubleBigEndian(System.Span`1<byte>, double)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteDoubleLittleEndian(System.Span`1<byte>, double)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteHalfBigEndian(System.Span`1<byte>, System.Half)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteHalfLittleEndian(System.Span`1<byte>, System.Half)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt128BigEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt128LittleEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt16BigEndian(System.Span`1<byte>, short)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt16LittleEndian(System.Span`1<byte>, short)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt32BigEndian(System.Span`1<byte>, int)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt32LittleEndian(System.Span`1<byte>, int)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt64BigEndian(System.Span`1<byte>, long)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt64LittleEndian(System.Span`1<byte>, long)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteIntPtrBigEndian(System.Span`1<byte>, nint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteIntPtrLittleEndian(System.Span`1<byte>, nint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteSingleBigEndian(System.Span`1<byte>, float)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteSingleLittleEndian(System.Span`1<byte>, float)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt128BigEndian(System.Span`1<byte>, System.UInt128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt128LittleEndian(System.Span`1<byte>, System.UInt128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt16BigEndian(System.Span`1<byte>, ushort)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt16LittleEndian(System.Span`1<byte>, ushort)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt32BigEndian(System.Span`1<byte>, uint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt32LittleEndian(System.Span`1<byte>, uint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt64BigEndian(System.Span`1<byte>, ulong)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt64LittleEndian(System.Span`1<byte>, ulong)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUIntPtrBigEndian(System.Span`1<byte>, nuint)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUIntPtrLittleEndian(System.Span`1<byte>, nuint)"
        };

        var generatedRows = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
            {
                var symbol = row.GetProperty("DisplayName").GetString();
                return !string.IsNullOrWhiteSpace(symbol) &&
                       (symbol.StartsWith("System.Buffers.Binary.BinaryPrimitives.Write", StringComparison.Ordinal) ||
                        symbol.StartsWith("System.Buffers.Binary.BinaryPrimitives.TryWrite", StringComparison.Ordinal));
            })
            .ToArray();

        Assert.That(generatedRows, Has.Length.EqualTo(expectedSymbols.Length));
        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(expectedSymbols));

        foreach (var symbol in expectedSymbols)
        {
            AssertPurityClassification(summary, symbol, "pure");
            AssertEffectVisibilityClassification(summary, symbol, "internal_only");
        }

        var freshOwnedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Buffers.Binary.BinaryPrimitives.WriteInt128BigEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.WriteUInt128BigEndian(System.Span`1<byte>, System.UInt128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteInt128BigEndian(System.Span`1<byte>, System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.TryWriteUInt128BigEndian(System.Span`1<byte>, System.UInt128)"
        };

        foreach (var row in generatedRows)
        {
            var symbol = row.GetProperty("DisplayName").GetString();
            Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(
                row.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo(freshOwnedSymbols.Contains(symbol!) ? "fresh_owned_object_write" : "none"),
                symbol);
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeBinaryPrimitivesReverseEndiannessSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsync("System.Buffers.Binary.BinaryPrimitives.ReverseEndianness", 40);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedPureRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("DisplayName").GetString()
                    ?.StartsWith("System.Buffers.Binary.BinaryPrimitives.ReverseEndianness",
                        StringComparison.Ordinal) == true &&
                string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedPureRows, Has.Length.EqualTo(13));
        Assert.That(
            generatedPureRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(sbyte)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(short)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(int)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(long)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(nint)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.Int128)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(byte)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(ushort)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(char)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(uint)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(ulong)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(nuint)",
                "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.UInt128)"
            }));

        var freshOwnedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.Int128)",
            "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.UInt128)"
        };

        foreach (var row in generatedPureRows)
        {
            var symbol = row.GetProperty("DisplayName").GetString();
            Assert.That(
                row.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo(freshOwnedSymbols.Contains(symbol!) ? "fresh_owned_object_write" : "none"),
                symbol);
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitOperationsFastHelpersSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();
        Assert.That(knownPureRows.Any(row =>
                string.Equals(row.GetProperty("DisplayName").GetString(), "System.Numerics.BitOperations.PopCount(uint)",
                    StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(), "System.Numerics.BitOperations.PopCount(ulong)",
                    StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(), "System.Numerics.BitOperations.PopCount(nuint)",
                    StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateLeft(uint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateLeft(ulong, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateLeft(nuint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateRight(uint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateRight(ulong, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.Numerics.BitOperations.RotateRight(nuint, int)", StringComparison.Ordinal)),
            Is.False);

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedPureRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                (row.GetProperty("DisplayName").GetString()
                     ?.StartsWith("System.Numerics.BitOperations.PopCount", StringComparison.Ordinal) == true ||
                 row.GetProperty("DisplayName").GetString()?.StartsWith("System.Numerics.BitOperations.RotateLeft",
                     StringComparison.Ordinal) == true ||
                 row.GetProperty("DisplayName").GetString()?.StartsWith("System.Numerics.BitOperations.RotateRight",
                     StringComparison.Ordinal) == true) &&
                string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedPureRows, Has.Length.EqualTo(9));
        Assert.That(
            generatedPureRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.Numerics.BitOperations.PopCount(uint)",
                "System.Numerics.BitOperations.PopCount(ulong)",
                "System.Numerics.BitOperations.PopCount(nuint)",
                "System.Numerics.BitOperations.RotateLeft(uint, int)",
                "System.Numerics.BitOperations.RotateLeft(ulong, int)",
                "System.Numerics.BitOperations.RotateLeft(nuint, int)",
                "System.Numerics.BitOperations.RotateRight(uint, int)",
                "System.Numerics.BitOperations.RotateRight(ulong, int)",
                "System.Numerics.BitOperations.RotateRight(nuint, int)"
            }));

        foreach (var row in generatedPureRows)
        {
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMathSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Math", 120);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedPureRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("Classification").GetString() == "pure" &&
                row.GetProperty("DisplayName").GetString()?.StartsWith("System.Math.", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(generatedPureRows.Length, Is.GreaterThanOrEqualTo(16));

        var representativePureSymbols = new[]
        {
            "System.Math.Abs(double)",
            "System.Math.Clamp(byte, byte, byte)",
            "System.Math.Clamp(double, double, double)",
            "System.Math.ILogB(double)",
            "System.Math.Max(double, double)",
            "System.Math.Min(double, double)",
            "System.Math.MaxMagnitude(double, double)",
            "System.Math.ReciprocalEstimate(double)"
        };

        foreach (var symbol in representativePureSymbols)
            Assert.That(
                generatedPureRows.Any(row =>
                    string.Equals(row.GetProperty("DisplayName").GetString(), symbol, StringComparison.Ordinal)),
                Is.True,
                symbol);

        AssertPurityClassification(summary, "System.Math.Ceiling(double)", "conservative_unknown",
            "metadata_only_or_external");
        AssertPurityClassification(summary, "System.Math.Floor(double)", "conservative_unknown",
            "metadata_only_or_external");
        AssertPurityClassification(summary, "System.Math.Sin(double)", "conservative_unknown",
            "metadata_only_or_external");
        AssertPurityClassification(summary, "System.Math.Sqrt(double)", "conservative_unknown",
            "metadata_only_or_external");
        AssertPurityClassification(summary, "System.Math.Truncate(double)", "conservative_unknown", "unknown_callee");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMemoryExtensionsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.MemoryExtensions", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedPureRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("Classification").GetString() == "pure" &&
                row.GetProperty("DisplayName").GetString()
                    ?.StartsWith("System.MemoryExtensions.", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(generatedPureRows.Length, Is.GreaterThanOrEqualTo(16));

        var representativePureSymbols = new[]
        {
            "System.MemoryExtensions.AsSpan(string)",
            "System.MemoryExtensions.AsSpan(string, int)",
            "System.MemoryExtensions.ContainsAny(System.ReadOnlySpan`1<!!0>, System.Buffers.SearchValues`1<!!0>)",
            "System.MemoryExtensions.ContainsAnyExcept(System.Span`1<!!0>, System.Buffers.SearchValues`1<!!0>)",
            "System.MemoryExtensions.IndexOfAnyExcept(System.ReadOnlySpan`1<!!0>, System.Buffers.SearchValues`1<!!0>)",
            "System.MemoryExtensions.LastIndexOfAnyExcept(System.Span`1<!!0>, System.Buffers.SearchValues`1<!!0>)"
        };

        foreach (var symbol in representativePureSymbols)
            Assert.That(
                generatedPureRows.Any(row =>
                    string.Equals(row.GetProperty("DisplayName").GetString(), symbol, StringComparison.Ordinal)),
                Is.True,
                symbol);

        AssertPurityClassification(summary, "System.MemoryExtensions.Contains(System.ReadOnlySpan`1<!!0>, !!0)",
            "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.MemoryExtensions.Contains(System.ReadOnlySpan`1<!!0>, !!0)", "caller_visible");
        AssertPurityClassification(summary,
            "System.MemoryExtensions.SequenceEqual(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.MemoryExtensions.SequenceEqual(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)", "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMemoryExtensionsAsSpanSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.MemoryExtensions.AsSpan", 40);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.MemoryExtensions.AsSpan(string)", "pure");
        AssertPurityClassification(summary, "System.MemoryExtensions.AsSpan(!!0[])", "pure");
        AssertEffectVisibilityClassification(summary, "System.MemoryExtensions.AsSpan(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.MemoryExtensions.AsSpan(!!0[])", "none");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.MemoryExtensions.AsSpan", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.MemoryExtensions.AsSpan(string)"));
        Assert.That(symbols, Does.Contain("System.MemoryExtensions.AsSpan(!!0[])"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitOperationsDeBruijnHelpersSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();
        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("Classification").GetString() == "pure" &&
                (
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.LeadingZeroCount(uint)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.LeadingZeroCount(ulong)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(), "System.Numerics.BitOperations.Log2(uint)",
                        StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(), "System.Numerics.BitOperations.Log2(ulong)",
                        StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.TrailingZeroCount(int)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.TrailingZeroCount(uint)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.TrailingZeroCount(long)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.TrailingZeroCount(ulong)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)", StringComparison.Ordinal) ||
                    string.Equals(row.GetProperty("DisplayName").GetString(),
                        "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)", StringComparison.Ordinal)))
            .ToArray();

        Assert.That(generatedRows, Has.Length.EqualTo(10));
        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                "System.Numerics.BitOperations.Log2(uint)",
                "System.Numerics.BitOperations.Log2(ulong)",
                "System.Numerics.BitOperations.TrailingZeroCount(int)",
                "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                "System.Numerics.BitOperations.TrailingZeroCount(long)",
                "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)"
            }));

        foreach (var symbol in new[]
                 {
                     "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                     "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                     "System.Numerics.BitOperations.Log2(uint)",
                     "System.Numerics.BitOperations.Log2(ulong)",
                     "System.Numerics.BitOperations.TrailingZeroCount(int)",
                     "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                     "System.Numerics.BitOperations.TrailingZeroCount(long)",
                     "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
                     "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                     "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)"
                 })
            Assert.That(
                knownPureRows.Any(row =>
                    string.Equals(row.GetProperty("DisplayName").GetString(), symbol, StringComparison.Ordinal)), Is.False);

        foreach (var row in generatedRows)
        {
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeNumericsPureHelpersSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Runtime.Numerics.dll",
            8,
            "System.Numerics.BigInteger.Add(System.Numerics.BigInteger, System.Numerics.BigInteger)",
            "System.Numerics.Complex..ctor(double, double)",
            "System.Numerics.Complex.Abs(System.Numerics.Complex)");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary,
            "System.Numerics.BigInteger.Add(System.Numerics.BigInteger, System.Numerics.BigInteger)", "impure");
        AssertPurityClassification(summary, "System.Numerics.Complex..ctor(double, double)", "pure");
        AssertPurityClassification(summary, "System.Numerics.Complex.Abs(System.Numerics.Complex)", "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeVectorMathPureHelpersSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            24,
            "System.Numerics.Quaternion..ctor(float, float, float, float)",
            "System.Numerics.Vector3.Normalize(System.Numerics.Vector3)",
            "System.Runtime.Intrinsics.X86.Sse.Add(System.Runtime.Intrinsics.Vector128`1<float>, System.Runtime.Intrinsics.Vector128`1<float>)");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Numerics.Quaternion..ctor(float, float, float, float)", "pure");
        AssertPurityClassification(summary, "System.Numerics.Vector3.Normalize(System.Numerics.Vector3)",
            "conservative_unknown");
        AssertPurityClassification(summary,
            "System.Runtime.Intrinsics.X86.Sse.Add(System.Runtime.Intrinsics.Vector128`1<float>, System.Runtime.Intrinsics.Vector128`1<float>)",
            "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMetadataReaderStringSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Reflection.Metadata.dll",
            20,
            "System.Reflection.Metadata.MetadataReader.GetString(System.Reflection.Metadata.StringHandle)");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary,
            "System.Reflection.Metadata.MetadataReader.GetString(System.Reflection.Metadata.StringHandle)", "impure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeExpressionBuilderSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Linq.Expressions.dll",
            24,
            "System.Linq.Expressions.Expression.Constant(object)",
            "System.Linq.Expressions.Expression.Call(System.Reflection.MethodInfo, System.Linq.Expressions.Expression[])");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Linq.Expressions.Expression.Constant(object)", "pure");
        AssertPurityClassification(summary,
            "System.Linq.Expressions.Expression.Call(System.Reflection.MethodInfo, System.Linq.Expressions.Expression[])",
            "pure");
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_CounterSampleSlice_UsesGeneratedImpureCatalogEntry()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-countersample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory, "CounterSample.Calculate.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.Diagnostics.PerformanceCounter",
                        PackageVersion = "8.0.0",
                        PackageAssemblyRelativePath = "lib/net8.0/System.Diagnostics.PerformanceCounter.dll",
                        Limit = 20,
                        SymbolPrefixes = new[]
                        {
                            "System.Diagnostics.CounterSample.Calculate(System.Diagnostics.CounterSample, System.Diagnostics.CounterSample)"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary,
            "System.Diagnostics.CounterSample.Calculate(System.Diagnostics.CounterSample, System.Diagnostics.CounterSample)",
            "impure", "throw");
        AssertEffectVisibilityClassification(summary,
            "System.Diagnostics.CounterSample.Calculate(System.Diagnostics.CounterSample, System.Diagnostics.CounterSample)",
            "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDrawingPrimitivesPureHelpersSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Drawing.Primitives.dll",
            8,
            "System.Drawing.Color.FromArgb(int, int, int, int)",
            "System.Drawing.Point..ctor(int, int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Drawing.Color.FromArgb(int, int, int, int)", "impure");
        AssertPurityClassification(summary, "System.Drawing.Point..ctor(int, int)", "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertBase64Slice_TreatsRuntimeHelpersAsImpure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.FromBase64", 20);

        var methods = FindMethodsByPrefix(summary, "System.Convert.FromBase64");
        Assert.That(methods.Length, Is.GreaterThan(0));

        AssertPurityClassification(summary, "System.Convert.FromBase64CharArray(char[], int, int)", "impure",
            "impure_callee");
        AssertPurityClassification(summary, "System.Convert.FromBase64String(string)", "impure", "impure_callee");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertHexSlice_TreatsRuntimeHelpersAsImpure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.FromHexString", 20);

        var methods = FindMethodsByPrefix(summary, "System.Convert.FromHexString");
        Assert.That(methods.Length, Is.GreaterThan(0));

        AssertPurityClassification(summary, "System.Convert.FromHexString(System.ReadOnlySpan`1<char>)", "impure",
            "throw");
        AssertPurityClassification(summary, "System.Convert.FromHexString(string)", "impure", "impure_callee");
        AssertThrownExceptions(summary, "System.Convert.FromHexString(System.ReadOnlySpan`1<char>)",
            "System.FormatException");
        AssertThrownExceptions(summary, "System.Convert.FromHexString(string)");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSha256HashDataSlice_TreatsFreshArrayWrapperAsPure()
    {
        using var summary = await RunEffectSummaryAsync(
            typeof(SHA256).Assembly.Location,
            true,
            true,
            true);

        var methods = FindMethodsByPrefix(summary, "System.Security.Cryptography.SHA256.HashData");
        Assert.That(methods.Length, Is.GreaterThanOrEqualTo(5));

        var byteArrayOverloads = methods.Where(method =>
                method.GetProperty("DisplayName").GetString() is
                    "System.Security.Cryptography.SHA256.HashData(byte[])"
                    or "System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan`1<byte>)")
            .ToArray();

        Assert.That(byteArrayOverloads, Has.Length.EqualTo(2));
        Assert.That(
            byteArrayOverloads.All(method =>
                method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"),
            Is.True);
        Assert.That(
            byteArrayOverloads.All(method =>
                method.GetProperty("PurityClassification").GetProperty("FreshnessClassification").GetString() ==
                "fresh_owned_array_write"),
            Is.True);
        Assert.That(
            byteArrayOverloads.All(method =>
                method.GetProperty("PurityClassification").GetProperty("EffectVisibilityClassification").GetString() ==
                "internal_only"),
            Is.True);
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeCryptographicOperationsFixedTimeEqualsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Security.Cryptography.dll",
            20,
            "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan`1<byte>, System.ReadOnlySpan`1<byte>)",
            "pure");
        AssertFreshnessClassification(
            summary,
            "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan`1<byte>, System.ReadOnlySpan`1<byte>)",
            "none");
        AssertEffectVisibilityClassification(
            summary,
            "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan`1<byte>, System.ReadOnlySpan`1<byte>)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(
            generatedSymbols,
            Does.Contain(
                "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan`1<byte>, System.ReadOnlySpan`1<byte>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUtf8ParserInt32Slice_UsesGeneratedImpureCatalogEntries()
    {
        const string symbol =
            "System.Buffers.Text.Utf8Parser.TryParse(System.ReadOnlySpan`1<byte>, ref int, ref int, char)";

        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            60,
            symbol);

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, symbol, "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, symbol, "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_Crc32HashSlice_UsesGeneratedPurityCatalogEntry()
    {
        const string symbol = "System.IO.Hashing.Crc32.Hash(System.ReadOnlySpan`1<byte>)";
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-crc32-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory, "Crc32.Hash.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.IO.Hashing",
                        PackageVersion = "8.0.0",
                        PackageAssemblyRelativePath = "lib/net8.0/System.IO.Hashing.dll",
                        Limit = 20,
                        SymbolPrefixes = new[]
                        {
                            "System.IO.Hashing.Crc32.Hash(System.ReadOnlySpan`1<byte>)"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));

        var report = summary.RootElement.GetProperty("PurityReport");
        Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, symbol, "impure");
        AssertEffectVisibilityClassification(summary, symbol, "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayPredicateSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Array.Exists(",
            "System.Array.Find(",
            "System.Array.FindIndex(",
            "System.Array.TrueForAll(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Array.Find(!!0[], System.Predicate`1<!!0>)",
            "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.Find(!!0[], System.Predicate`1<!!0>)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Array.FindIndex(!!0[], System.Predicate`1<!!0>)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.FindIndex(!!0[], System.Predicate`1<!!0>)",
            "none");
        AssertPurityClassification(
            summary,
            "System.Array.Exists(!!0[], System.Predicate`1<!!0>)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.Exists(!!0[], System.Predicate`1<!!0>)",
            "none");
        AssertPurityClassification(
            summary,
            "System.Array.TrueForAll(!!0[], System.Predicate`1<!!0>)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.TrueForAll(!!0[], System.Predicate`1<!!0>)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.Exists(!!0[], System.Predicate`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Find(!!0[], System.Predicate`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.FindIndex(!!0[], System.Predicate`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.FindIndex(!!0[], int, System.Predicate`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.FindIndex(!!0[], int, int, System.Predicate`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.TrueForAll(!!0[], System.Predicate`1<!!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayIndexOfLengthSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Array.GetLength",
            "System.Array.IndexOf(System.Array, object)",
            "System.Array.get_Length");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Array.IndexOf(System.Array, object)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.IndexOf(System.Array, object)",
            "none");
        AssertPurityClassification(
            summary,
            "System.Array.get_Length()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.get_Length()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Array.GetLength(int)",
            "impure",
            "throw");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.GetLength(int)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.IndexOf(System.Array, object)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.get_Length()"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.GetLength(int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayGetEnumeratorSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.Array.GetEnumerator()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Array.GetEnumerator()", "pure");
        AssertFreshnessClassification(summary, "System.Array.GetEnumerator()", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Array.GetEnumerator()", "internal_only");
        AssertPurityClassification(summary, "System.ArrayEnumerator..ctor(System.Array)", "pure");
        AssertFreshnessClassification(summary, "System.ArrayEnumerator..ctor(System.Array)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ArrayEnumerator..ctor(System.Array)", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.GetEnumerator()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeContractSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            40,
            "System.Diagnostics.Contracts.Contract.Ensures",
            "System.Diagnostics.Contracts.Contract.Requires");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var contractMethods = FindMethodsByPrefix(summary, "System.Diagnostics.Contracts.Contract.")
            .Where(method =>
            {
                var symbol = method.GetProperty("DisplayName").GetString();
                return symbol is not null &&
                       (symbol.StartsWith("System.Diagnostics.Contracts.Contract.Ensures(", StringComparison.Ordinal) ||
                        symbol.StartsWith("System.Diagnostics.Contracts.Contract.Requires(", StringComparison.Ordinal));
            })
            .ToArray();
        Assert.That(contractMethods, Is.Not.Empty);

        foreach (var method in contractMethods)
        {
            var classification = method.GetProperty("PurityClassification");
            Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(
                classification.GetProperty("Categories").EnumerateArray().Select(category => category.GetString())
                    .ToArray(),
                Is.Empty);
            Assert.That(classification.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
        }

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.Contracts.Contract.Ensures(bool)"));
        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.Contracts.Contract.Requires(bool)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayBinarySearchSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Array.BinarySearch(System.Array");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Array.BinarySearch(System.Array, object)",
            "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Array.BinarySearch(System.Array, object)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Array.BinarySearch(System.Array, object, System.Collections.IComparer)",
            "impure");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.BinarySearch(System.Array, object)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.BinarySearch(System.Array, int, int, object)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Array.BinarySearch(System.Array, object, System.Collections.IComparer)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Array.BinarySearch(System.Array, int, int, object, System.Collections.IComparer)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedSetGetViewBetweenSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            30,
            "System.Collections.Generic.SortedSet`1.GetViewBetween");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedSet`1.GetViewBetween(!0, !0)",
            "impure",
            "throw");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedSet`1.GetViewBetween(!0, !0)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedSet`1.GetViewBetween(!0, !0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedListAndLinkedListNodeReadSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            20,
            "System.Collections.Generic.LinkedListNode`1.get_Value()",
            "System.Collections.Generic.SortedList`2.IndexOfKey(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.LinkedListNode`1.get_Value()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.LinkedListNode`1.get_Value()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedList`2.IndexOfKey(!0)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedList`2.IndexOfKey(!0)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.LinkedListNode`1.get_Value()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedList`2.IndexOfKey(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeLinkedListMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            40,
            "System.Collections.Generic.LinkedList`1.AddFirst",
            "System.Collections.Generic.LinkedListNode`1.set_Value");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.LinkedList<T>.AddFirst(T)",
                StringComparison.Ordinal)),
            Is.False,
            "LinkedList<T>.AddFirst(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.LinkedListNode<T>.Value.set",
                StringComparison.Ordinal)),
            Is.False,
            "LinkedListNode<T>.Value.set should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.LinkedList`1.AddFirst(!0)", "impure",
            "impure_callee", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.LinkedList`1.AddFirst(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.LinkedListNode`1.set_Value(!0)", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.LinkedListNode`1.set_Value(!0)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.LinkedList`1.AddFirst(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.LinkedListNode`1.set_Value(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePriorityQueueMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            30,
            "System.Collections.Generic.PriorityQueue`2.Enqueue",
            "System.Collections.Generic.PriorityQueue`2.Dequeue");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)",
                StringComparison.Ordinal)),
            Is.False,
            "PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.PriorityQueue<TElement, TPriority>.Dequeue()",
                StringComparison.Ordinal)),
            Is.False,
            "PriorityQueue<TElement, TPriority>.Dequeue() should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.PriorityQueue`2.Enqueue(!0, !1)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.PriorityQueue`2.Enqueue(!0, !1)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.PriorityQueue`2.Dequeue()", "impure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.PriorityQueue`2.Dequeue()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.PriorityQueue`2.Enqueue(!0, !1)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.PriorityQueue`2.Dequeue()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConcurrentQueueMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            30,
            "System.Collections.Concurrent.ConcurrentQueue`1.Enqueue",
            "System.Collections.Concurrent.ConcurrentQueue`1.TryDequeue");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.ConcurrentQueue<T>.Enqueue(T)",
                StringComparison.Ordinal)),
            Is.False,
            "ConcurrentQueue<T>.Enqueue(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.ConcurrentQueue<T>.TryDequeue(out T)",
                StringComparison.Ordinal)),
            Is.False,
            "ConcurrentQueue<T>.TryDequeue(out T) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Concurrent.ConcurrentQueue`1.Enqueue(!0)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Concurrent.ConcurrentQueue`1.Enqueue(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Concurrent.ConcurrentQueue`1.TryDequeue(ref !0)",
            "impure", "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Concurrent.ConcurrentQueue`1.TryDequeue(ref !0)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Concurrent.ConcurrentQueue`1.Enqueue(!0)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Concurrent.ConcurrentQueue`1.TryDequeue(ref !0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAdditionalConcurrentCollectionMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.Concurrent.dll",
            80,
            "System.Collections.Concurrent.ConcurrentDictionary`2.TryAdd",
            "System.Collections.Concurrent.BlockingCollection`1.Add",
            "System.Collections.Concurrent.BlockingCollection`1.Take",
            "System.Collections.Concurrent.ConcurrentBag`1.Add",
            "System.Collections.Concurrent.ConcurrentBag`1.TryTake");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)",
                StringComparison.Ordinal)),
            Is.False,
            "ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.BlockingCollection<T>.Add(T)",
                StringComparison.Ordinal)),
            Is.False,
            "BlockingCollection<T>.Add(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.BlockingCollection<T>.Take()",
                StringComparison.Ordinal)),
            Is.False,
            "BlockingCollection<T>.Take() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.ConcurrentBag<T>.Add(T)",
                StringComparison.Ordinal)),
            Is.False,
            "ConcurrentBag<T>.Add(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Concurrent.ConcurrentBag<T>.TryTake(out T)",
                StringComparison.Ordinal)),
            Is.False,
            "ConcurrentBag<T>.TryTake(out T) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Concurrent.ConcurrentDictionary`2.TryAdd(!0, !1)",
            "impure", "caller_visible_memory_write", "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Concurrent.ConcurrentDictionary`2.TryAdd(!0, !1)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Concurrent.BlockingCollection`1.Add(!0)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Concurrent.BlockingCollection`1.Add(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Concurrent.BlockingCollection`1.Take()", "impure",
            "throw");
        AssertEffectVisibilityClassification(summary, "System.Collections.Concurrent.BlockingCollection`1.Take()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Concurrent.ConcurrentBag`1.Add(!0)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Concurrent.ConcurrentBag`1.Add(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Concurrent.ConcurrentBag`1.TryTake(ref !0)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Concurrent.ConcurrentBag`1.TryTake(ref !0)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Concurrent.ConcurrentDictionary`2.TryAdd(!0, !1)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Concurrent.BlockingCollection`1.Add(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Concurrent.BlockingCollection`1.Take()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Concurrent.ConcurrentBag`1.Add(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Concurrent.ConcurrentBag`1.TryTake(ref !0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDiagnosticsAssertAndStackFrameSlice_UsesGeneratedEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Diagnostics.Debug.Assert(bool)",
            "System.Diagnostics.StackFrame.GetMethod()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Diagnostics.Debug.Assert(bool)",
                StringComparison.Ordinal)),
            Is.False,
            "Debug.Assert(bool) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Diagnostics.StackFrame.GetMethod()",
                StringComparison.Ordinal)),
            Is.False,
            "StackFrame.GetMethod() should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Diagnostics.Debug.Assert(bool)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Debug.Assert(bool)", "internal_only");
        AssertPurityClassification(summary, "System.Diagnostics.StackFrame.GetMethod()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.StackFrame.GetMethod()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.Debug.Assert(bool)"));
        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.StackFrame.GetMethod()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDiagnosticListenerConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Diagnostics.DiagnosticSource.dll",
            20,
            "System.Diagnostics.DiagnosticListener..ctor(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Diagnostics.DiagnosticListener.DiagnosticListener(string)",
                StringComparison.Ordinal)),
            Is.False,
            "DiagnosticListener(string) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Diagnostics.DiagnosticListener..ctor(string)", "impure",
            "global_state_read", "global_state_write", "impure_callee", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.DiagnosticListener..ctor(string)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.DiagnosticListener..ctor(string)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeFileVersionInfoGetterSlice_UsesGeneratedEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Diagnostics.FileVersionInfo.dll",
            20,
            "System.Diagnostics.FileVersionInfo.get_FileVersion()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Diagnostics.FileVersionInfo.FileVersion.get",
                StringComparison.Ordinal)),
            Is.False,
            "FileVersionInfo.FileVersion.get should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Diagnostics.FileVersionInfo.get_FileVersion()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.FileVersionInfo.get_FileVersion()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Diagnostics.FileVersionInfo.get_FileVersion()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedCollectionAndBitArrayMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            40,
            "System.Collections.BitArray.Set",
            "System.Collections.Generic.SortedDictionary`2.Add",
            "System.Collections.Generic.SortedSet`1.Add");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.BitArray.Set(int, bool)",
                StringComparison.Ordinal)),
            Is.False,
            "BitArray.Set(int, bool) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.SortedDictionary<TKey, TValue>.Add(TKey, TValue)",
                StringComparison.Ordinal)),
            Is.False,
            "SortedDictionary<TKey, TValue>.Add(TKey, TValue) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.SortedSet<T>.Add(T)",
                StringComparison.Ordinal)),
            Is.False,
            "SortedSet<T>.Add(T) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.BitArray.Set(int, bool)", "impure",
            "caller_visible_memory_write", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.BitArray.Set(int, bool)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.SortedDictionary`2.Add(!0, !1)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.SortedDictionary`2.Add(!0, !1)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.SortedSet`1.Add(!0)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.SortedSet`1.Add(!0)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.BitArray.Set(int, bool)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedDictionary`2.Add(!0, !1)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedSet`1.Add(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayConvertAllAndComparerSortSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Array.ConvertAll(!!0[], System.Converter`2<!!0, !!1>)",
            "System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)",
            "System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.ConvertAll<TInput, TOutput>(TInput[], System.Converter<TInput, TOutput>)",
                StringComparison.Ordinal)),
            Is.False,
            "Array.ConvertAll<TInput, TOutput>(TInput[], System.Converter<TInput, TOutput>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Sort<T>(T[], System.Collections.Generic.IComparer<T>?)",
                StringComparison.Ordinal)),
            Is.False,
            "Array.Sort<T>(T[], IComparer<T>?) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Sort<T>(T[], int, int, System.Collections.Generic.IComparer<T>?)",
                StringComparison.Ordinal)),
            Is.False,
            "Array.Sort<T>(T[], int, int, IComparer<T>?) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Array.ConvertAll(!!0[], System.Converter`2<!!0, !!1>)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Array.ConvertAll(!!0[], System.Converter`2<!!0, !!1>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)",
            "impure", "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.ConvertAll(!!0[], System.Converter`2<!!0, !!1>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStaticCustomAttributeHelperSlice_UsesGeneratedEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            30,
            "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
            "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)",
            "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)",
            "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
                StringComparison.Ordinal)),
            Is.False,
            "Attribute.GetCustomAttributes(MemberInfo) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)",
                StringComparison.Ordinal)),
            Is.False,
            "Attribute.GetCustomAttribute(MemberInfo, Type) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)",
                StringComparison.Ordinal)),
            Is.False,
            "Attribute.IsDefined(MemberInfo, Type) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)",
                StringComparison.Ordinal)),
            Is.False,
            "CustomAttributeData.GetCustomAttributes(MemberInfo) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
            "impure", "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)", "caller_visible");
        AssertPurityClassification(summary, "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)",
            "impure", "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)", "pure");
        AssertEffectVisibilityClassification(summary,
            "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols,
            Does.Contain("System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeKeyValuePairCtorAndAccessorsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Collections.Generic.KeyValuePair`2..ctor(!0, !1)",
            "System.Collections.Generic.KeyValuePair`2.get_Key()",
            "System.Collections.Generic.KeyValuePair`2.get_Value()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Collections.Generic.KeyValuePair`2..ctor(!0, !1)", "pure");
        AssertFreshnessClassification(summary, "System.Collections.Generic.KeyValuePair`2..ctor(!0, !1)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.KeyValuePair`2..ctor(!0, !1)",
            "internal_only");
        AssertPurityClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Key()", "pure");
        AssertFreshnessClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Key()", "none");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Key()", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Value()", "pure");
        AssertFreshnessClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Value()", "none");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.KeyValuePair`2.get_Value()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.Collections.Generic.KeyValuePair`2..ctor(!0, !1)",
            "System.Collections.Generic.KeyValuePair`2.get_Key()",
            "System.Collections.Generic.KeyValuePair`2.get_Value()"
        }));
    }

    [Test]
    public void EffectSummaryTool_RetiredManualCatalogNormalization_DoesNotReturn()
    {
        var sourcePath = Path.Combine(
            GetRepositoryRoot(),
            "Tools",
            "SharpProof.EffectSummary",
            "EffectSummaryCatalogReporting.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.That(source, Does.Not.Contain("NormalizeCatalogSymbol"));
        Assert.That(source, Does.Not.Contain("NormalizeCatalogComparisonKey"));
        Assert.That(source, Does.Not.Contain("AggregateCatalogClassification"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedDictionaryLookupSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            40,
            "System.Collections.Generic.SortedDictionary`2.ContainsKey",
            "System.Collections.Generic.SortedDictionary`2.ContainsValue",
            "System.Collections.Generic.SortedDictionary`2.TryGetValue");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.ContainsKey(!0)",
            "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.ContainsKey(!0)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.ContainsValue(!1)",
            "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.ContainsValue(!1)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.TryGetValue(!0, ref !1)",
            "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.TryGetValue(!0, ref !1)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedDictionary`2.ContainsKey(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedDictionary`2.ContainsValue(!1)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.SortedDictionary`2.TryGetValue(!0, ref !1)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeNullableComparisonSlice_UsesGeneratedConservativePurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Nullable.Compare",
            "System.Nullable.Equals");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Nullable.Compare(System.Nullable`1<!!0>, System.Nullable`1<!!0>)",
            "conservative_unknown",
            "unknown_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Nullable.Compare(System.Nullable`1<!!0>, System.Nullable`1<!!0>)",
            "unknown");
        AssertPurityClassification(
            summary,
            "System.Nullable.Equals(System.Nullable`1<!!0>, System.Nullable`1<!!0>)",
            "conservative_unknown",
            "unknown_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Nullable.Equals(System.Nullable`1<!!0>, System.Nullable`1<!!0>)",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols,
            Does.Contain("System.Nullable.Compare(System.Nullable`1<!!0>, System.Nullable`1<!!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Nullable.Equals(System.Nullable`1<!!0>, System.Nullable`1<!!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeNullableGetValueOrDefaultSlice_UsesGeneratedPureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Nullable`1.GetValueOrDefault");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Nullable`1.GetValueOrDefault()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Nullable`1.GetValueOrDefault()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Nullable`1.GetValueOrDefault(!0)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Nullable`1.GetValueOrDefault(!0)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Nullable`1.GetValueOrDefault()"));
        Assert.That(generatedSymbols, Does.Contain("System.Nullable`1.GetValueOrDefault(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeExceptionAccessorSlice_UsesGeneratedPureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Exception.GetClassName",
            "System.Exception.get_HResult",
            "System.Exception.get_InnerException",
            "System.Exception.get_Message",
            "System.Object.GetType");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Exception.GetClassName()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Exception.GetClassName()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Exception.get_HResult()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Exception.get_HResult()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Exception.get_InnerException()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Exception.get_InnerException()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Exception.get_Message()",
            "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Exception.get_Message()",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Object.GetType()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Object.GetType()",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Exception.GetClassName()"));
        Assert.That(generatedSymbols, Does.Contain("System.Exception.get_HResult()"));
        Assert.That(generatedSymbols, Does.Contain("System.Exception.get_InnerException()"));
        Assert.That(generatedSymbols, Does.Contain("System.Exception.get_Message()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeExceptionToStringSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Exception.ToString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Exception.ToString()",
                StringComparison.Ordinal)),
            Is.False,
            "System.Exception.ToString() should no longer overlap the manual impure catalog.");

        AssertPurityClassification(
            summary,
            "System.Exception.ToString()",
            "impure",
            "global_state_read",
            "global_state_write",
            "impure_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Exception.ToString()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Exception.ToString()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeFileSystemPathGetterSlice_UsesGeneratedEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            60,
            "System.IO.DirectoryInfo.get_Name",
            "System.IO.DirectoryInfo.get_Parent",
            "System.IO.FileInfo.get_DirectoryName",
            "System.IO.FileInfo.get_Name",
            "System.IO.FileSystemInfo.get_Extension");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.DirectoryInfo.get_Parent()", "pure");
        AssertEffectVisibilityClassification(summary, "System.IO.DirectoryInfo.get_Parent()", "none");
        AssertPurityClassification(summary, "System.IO.FileInfo.get_DirectoryName()", "pure");
        AssertEffectVisibilityClassification(summary, "System.IO.FileInfo.get_DirectoryName()", "none");
        AssertPurityClassification(summary, "System.IO.DirectoryInfo.get_Name()", "impure", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.DirectoryInfo.get_Name()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.FileInfo.get_Name()", "impure", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.FileInfo.get_Name()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.FileSystemInfo.get_Extension()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.FileSystemInfo.get_Extension()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.IO.DirectoryInfo.get_Name()"));
        Assert.That(generatedSymbols, Does.Contain("System.IO.DirectoryInfo.get_Parent()"));
        Assert.That(generatedSymbols, Does.Contain("System.IO.FileInfo.get_DirectoryName()"));
        Assert.That(generatedSymbols, Does.Contain("System.IO.FileInfo.get_Name()"));
        Assert.That(generatedSymbols, Does.Contain("System.IO.FileSystemInfo.get_Extension()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeInterfaceCollectionLookupSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Collections.Generic.ICollection`1.get_Count",
            "System.Collections.Generic.ICollection`1.Contains",
            "System.Collections.Generic.IList`1.IndexOf");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.ICollection`1.get_Count()",
            "conservative_unknown",
            "abstract",
            "metadata_only_or_external",
            "no_il_body");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.ICollection`1.get_Count()",
            "unknown");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.ICollection`1.Contains(!0)",
            "conservative_unknown",
            "abstract",
            "metadata_only_or_external",
            "no_il_body");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.ICollection`1.Contains(!0)",
            "unknown");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.IList`1.IndexOf(!0)",
            "conservative_unknown",
            "abstract",
            "metadata_only_or_external",
            "no_il_body");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.IList`1.IndexOf(!0)",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.ICollection`1.Contains(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.ICollection`1.get_Count()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.IList`1.IndexOf(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeInterfaceEnumeratorContractSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            12,
            "System.Collections.Generic.IEnumerable`1.GetEnumerator()",
            "System.Collections.Generic.IEnumerator`1.get_Current()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.IEnumerable`1.GetEnumerator()",
            "conservative_unknown",
            "abstract",
            "metadata_only_or_external",
            "no_il_body");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.IEnumerable`1.GetEnumerator()",
            "unknown");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.IEnumerator`1.get_Current()",
            "conservative_unknown",
            "abstract",
            "metadata_only_or_external",
            "no_il_body");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.IEnumerator`1.get_Current()",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.IEnumerable`1.GetEnumerator()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.IEnumerator`1.get_Current()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeHashtableAndCompareInfoSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Collections.Hashtable.ContainsKey(object)",
            "System.Globalization.CompareInfo.Compare(string, string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Hashtable.ContainsKey(object)",
            "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Hashtable.ContainsKey(object)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.Globalization.CompareInfo.Compare(string, string)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Globalization.CompareInfo.Compare(string, string)",
            "none");
        AssertPurityClassification(
            summary,
            "System.Globalization.CompareInfo.Compare(string, string, System.Globalization.CompareOptions)",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Globalization.CompareInfo.Compare(string, string, System.Globalization.CompareOptions)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Hashtable.ContainsKey(object)"));
        Assert.That(generatedSymbols, Does.Contain("System.Globalization.CompareInfo.Compare(string, string)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Globalization.CompareInfo.Compare(string, string, System.Globalization.CompareOptions)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedListGetKeySlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.NonGeneric.dll",
            12,
            "System.Collections.SortedList.GetKey(int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.SortedList.GetKey(int)",
            "impure",
            "throw");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.SortedList.GetKey(int)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.SortedList.GetKey(int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeKeyedCollectionContainsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ObjectModel.dll",
            20,
            "System.Collections.ObjectModel.KeyedCollection`2.Contains(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.ObjectModel.KeyedCollection`2.Contains(!0)",
            "conservative_unknown",
            "dynamic_dispatch",
            "virtual_call");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.ObjectModel.KeyedCollection`2.Contains(!0)",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.ObjectModel.KeyedCollection`2.Contains(!0)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSortedCollectionCountSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            20,
            "System.Collections.Generic.SortedDictionary`2.get_Count",
            "System.Collections.Generic.SortedSet`1.get_Count",
            "System.Collections.Generic.SortedSet`1.VersionCheck");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.get_Count()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedDictionary`2.get_Count()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedSet`1.get_Count()",
            "pure");
        AssertEffectVisibilityClassification(
            summary,
            "System.Collections.Generic.SortedSet`1.get_Count()",
            "none");
        AssertPurityClassification(
            summary,
            "System.Collections.Generic.SortedSet`1.VersionCheck(bool)",
            "pure");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedDictionary`2.get_Count()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedSet`1.get_Count()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.SortedSet`1.VersionCheck(bool)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitConverterReadSlice_TreatsIntrinsicHelpersAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToInt32", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal) &&
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.BitConverter.ToInt32(System.ReadOnlySpan`1<byte>)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.BitConverter.ToInt32(System.ReadOnlySpan`1<byte>)"
            }));

        AssertPurityClassification(summary, "System.BitConverter.ToInt32(byte[], int)", "pure");

        foreach (var row in generatedRows)
        {
            Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBitConverterDoubleSlice_TreatsIntrinsicHelpersAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToDouble", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal) &&
                string.Equals(row.GetProperty("DisplayName").GetString(),
                    "System.BitConverter.ToDouble(System.ReadOnlySpan`1<byte>)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            generatedRows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.BitConverter.ToDouble(System.ReadOnlySpan`1<byte>)"
            }));

        AssertPurityClassification(summary, "System.BitConverter.ToDouble(byte[], int)", "pure");

        foreach (var row in generatedRows)
        {
            Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayEmptySlice_TreatsSafeStaticCacheReadsAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Array.Empty", 10);

        var methods = FindMethodsByPrefix(summary, "System.Array.Empty");
        Assert.That(methods.Length, Is.GreaterThan(0));

        foreach (var method in methods)
        {
            var classification = method.GetProperty("PurityClassification");
            Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(classification.GetProperty("EffectVisibilityClassification").GetString(),
                Is.EqualTo("internal_only"));
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStaticCacheGetterSlice_TreatsSafeStaticCacheReadsAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            40,
            "System.Collections.Generic.Comparer`1.get_Default",
            "System.Collections.Generic.EqualityComparer`1.get_Default",
            "System.StringComparer.get_Ordinal",
            "System.StringComparer.get_OrdinalIgnoreCase",
            "System.Globalization.CultureInfo.get_InvariantCulture",
            "System.Text.Encoding.get_ASCII",
            "System.Threading.Tasks.Task.get_CompletedTask");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Collections.Generic.Comparer`1.get_Default()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Comparer`1.get_Default()",
            "internal_only");
        AssertPurityClassification(summary, "System.Collections.Generic.EqualityComparer`1.get_Default()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.EqualityComparer`1.get_Default()",
            "internal_only");
        AssertPurityClassification(summary, "System.StringComparer.get_Ordinal()", "pure");
        AssertEffectVisibilityClassification(summary, "System.StringComparer.get_Ordinal()", "internal_only");
        AssertPurityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "pure");
        AssertEffectVisibilityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "internal_only");
        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_InvariantCulture()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.get_InvariantCulture()",
            "internal_only");
        AssertPurityClassification(summary, "System.Text.Encoding.get_ASCII()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Text.Encoding.get_ASCII()", "internal_only");
        AssertPurityClassification(summary, "System.Threading.Tasks.Task.get_CompletedTask()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.Task.get_CompletedTask()",
            "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Comparer`1.get_Default()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.EqualityComparer`1.get_Default()"));
        Assert.That(generatedSymbols, Does.Contain("System.StringComparer.get_Ordinal()"));
        Assert.That(generatedSymbols, Does.Contain("System.StringComparer.get_OrdinalIgnoreCase()"));
        Assert.That(generatedSymbols, Does.Contain("System.Globalization.CultureInfo.get_InvariantCulture()"));
        Assert.That(generatedSymbols, Does.Contain("System.Text.Encoding.get_ASCII()"));
        Assert.That(generatedSymbols, Does.Contain("System.Threading.Tasks.Task.get_CompletedTask()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCancellationTokenNoneSlice_TreatsReturnValueInitializationAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(10, "System.Threading.CancellationToken.get_None");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Threading.CancellationToken.get_None()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Threading.CancellationToken.get_None()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Threading.CancellationToken.get_None()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeGuidToByteArraySlice_TreatsRuntimeHelpersAndEndianReadsAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Guid.ToByteArray", 20);

        var methods = FindMethodsByPrefix(summary, "System.Guid.ToByteArray");
        Assert.That(methods.Length, Is.EqualTo(2));

        foreach (var method in methods)
        {
            var symbol = method.GetProperty("DisplayName").GetString();
            var classification = method.GetProperty("PurityClassification");
            Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo("pure"), symbol);
            Assert.That(classification.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo("fresh_array_candidate_via_local_helpers"), symbol);
            Assert.That(classification.GetProperty("EffectVisibilityClassification").GetString(),
                Is.EqualTo("internal_only"), symbol);
        }
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeGuidCoreSlice_ClassifiesComparisonsParsingAndFormattingConservatively()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Guid", 80);

        AssertPurityClassification(summary, "System.Guid.Equals(System.Guid)", "pure");
        AssertPurityClassification(summary, "System.Guid.CompareTo(System.Guid)", "pure");
        AssertPurityClassification(summary, "System.Guid.Parse(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Guid.ParseExact(string, string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Guid.TryParse(string, ref System.Guid)", "impure",
            "caller_visible_memory_write", "impure_callee");
        AssertPurityClassification(summary, "System.Guid.TryParseExact(string, string, ref System.Guid)", "impure",
            "caller_visible_memory_write", "impure_callee");
        AssertPurityClassification(summary, "System.Guid.ToString()", "pure");
        AssertPurityClassification(summary, "System.Guid.ToString(string)", "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeGuidNewGuidSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Guid.NewGuid");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Guid.NewGuid()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Guid.NewGuid()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Guid.NewGuid()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.Guid.NewGuid()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePathCoreSlice_SeparatesPureAndConservativeStringWrappers()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.IO.Path", 80);

        var pathSymbols = FindMethodsByPrefix(summary, "System.IO.Path.")
            .Select(method => method.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(pathSymbols, Does.Contain("System.IO.Path.Combine(string, string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.HasExtension(string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.ChangeExtension(string, string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.GetDirectoryName(string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.GetExtension(string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.GetFileName(string)"));
        Assert.That(pathSymbols, Does.Contain("System.IO.Path.GetFileNameWithoutExtension(string)"));

        AssertPurityClassification(summary, "System.IO.Path.Combine(string, string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.HasExtension(string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.ChangeExtension(string, string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetDirectoryName(string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetDirectoryName(System.ReadOnlySpan`1<char>)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetExtension(string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetExtension(System.ReadOnlySpan`1<char>)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetFileName(string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetFileName(System.ReadOnlySpan`1<char>)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetFileNameWithoutExtension(string)", "pure");
        AssertPurityClassification(summary, "System.IO.Path.GetFileNameWithoutExtension(System.ReadOnlySpan`1<char>)",
            "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePathEnvironmentSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.IO.Path.GetFullPath",
            "System.IO.Path.GetRandomFileName",
            "System.IO.Path.GetTempFileName",
            "System.IO.Path.GetTempPath");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.Path.GetFullPath(string)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.IO.Path.GetFullPath(string)", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Path.GetRandomFileName()", "impure", "global_state_read",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.Path.GetRandomFileName()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Path.GetTempFileName()", "impure", "caller_visible_memory_write",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Path.GetTempFileName()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Path.GetTempPath()", "impure", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.Path.GetTempPath()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.IO.Path.GetFullPath(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Path.GetRandomFileName()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Path.GetTempFileName()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Path.GetTempPath()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.IO.Path.GetFullPath(string)",
            "System.IO.Path.GetRandomFileName()",
            "System.IO.Path.GetTempFileName()",
            "System.IO.Path.GetTempPath()"
        }));
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeDateTimeOffsetSlice_TreatsAddMethodsFactoriesAndDerivedHelpersDifferently()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            200,
            "System.DateTimeOffset.Add(System.TimeSpan)",
            "System.DateTimeOffset.AddDays(double)",
            "System.DateTimeOffset.AddHours(double)",
            "System.DateTimeOffset.AddMilliseconds(double)",
            "System.DateTimeOffset.AddMinutes(double)",
            "System.DateTimeOffset.AddMonths(int)",
            "System.DateTimeOffset.AddSeconds(double)",
            "System.DateTimeOffset.AddTicks(long)",
            "System.DateTimeOffset.AddYears(int)",
            "System.DateTimeOffset.Compare(System.DateTimeOffset, System.DateTimeOffset)",
            "System.DateTimeOffset.CompareTo(System.DateTimeOffset)",
            "System.DateTimeOffset.Equals(System.DateTimeOffset)",
            "System.DateTimeOffset.Equals(System.DateTimeOffset, System.DateTimeOffset)",
            "System.DateTimeOffset.Subtract(System.DateTimeOffset)",
            "System.DateTimeOffset.ToUnixTimeMilliseconds()",
            "System.DateTimeOffset.ToUnixTimeSeconds()",
            "System.DateTimeOffset.get_Offset()",
            "System.DateTimeOffset.FromUnixTimeMilliseconds(long)",
            "System.DateTimeOffset.FromUnixTimeSeconds(long)");

        AssertPurityClassification(summary, "System.DateTimeOffset.Add(System.TimeSpan)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddDays(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddHours(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMilliseconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMinutes(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMonths(int)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddSeconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddTicks(long)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddYears(int)", "pure");
        AssertPurityClassification(summary,
            "System.DateTimeOffset.Compare(System.DateTimeOffset, System.DateTimeOffset)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.CompareTo(System.DateTimeOffset)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.Equals(System.DateTimeOffset)", "pure");
        AssertPurityClassification(summary,
            "System.DateTimeOffset.Equals(System.DateTimeOffset, System.DateTimeOffset)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.Subtract(System.DateTimeOffset)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeMilliseconds()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeSeconds()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Offset()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.FromUnixTimeMilliseconds(long)", "impure", "throw");
        AssertPurityClassification(summary, "System.DateTimeOffset.FromUnixTimeSeconds(long)", "impure", "throw");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeSlice_TreatsAddAndRoundTripHelpersDifferently()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            200,
            "System.DateTime.Add(System.TimeSpan)",
            "System.DateTime.AddDays(double)",
            "System.DateTime.AddHours(double)",
            "System.DateTime.AddMilliseconds(double)",
            "System.DateTime.AddMinutes(double)",
            "System.DateTime.AddMonths(int)",
            "System.DateTime.AddSeconds(double)",
            "System.DateTime.AddTicks(long)",
            "System.DateTime.AddYears(int)",
            "System.DateTime.FromBinary(long)",
            "System.DateTime.FromOADate(double)",
            "System.DateTime.ToOADate()",
            "System.DateTime.ToBinary()",
            "System.DateTime.Compare(System.DateTime, System.DateTime)",
            "System.DateTime.CompareTo(System.DateTime)",
            "System.DateTime.Equals(System.DateTime)",
            "System.DateTime.Equals(System.DateTime, System.DateTime)",
            "System.DateTime.Equals(object)",
            "System.DateTime.Subtract(System.DateTime)",
            "System.DateTime.Subtract(System.TimeSpan)",
            "System.DateTime.DaysInMonth(int, int)");

        AssertPurityClassification(summary, "System.DateTime.Add(System.TimeSpan)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddDays(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddHours(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddMilliseconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddMinutes(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddMonths(int)", "impure", "throw");
        AssertPurityClassification(summary, "System.DateTime.AddSeconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddTicks(long)", "pure");
        AssertPurityClassification(summary, "System.DateTime.AddYears(int)", "impure", "throw");
        AssertPurityClassification(summary, "System.DateTime.FromBinary(long)", "impure", "global_state_read", "throw");
        AssertPurityClassification(summary, "System.DateTime.FromOADate(double)", "pure");
        AssertPurityClassification(summary, "System.DateTime.ToOADate()", "pure");
        AssertPurityClassification(summary, "System.DateTime.ToBinary()", "pure");
        AssertPurityClassification(summary, "System.DateTime.Compare(System.DateTime, System.DateTime)", "pure");
        AssertPurityClassification(summary, "System.DateTime.CompareTo(System.DateTime)", "pure");
        AssertPurityClassification(summary, "System.DateTime.Equals(System.DateTime)", "pure");
        AssertPurityClassification(summary, "System.DateTime.Equals(System.DateTime, System.DateTime)", "pure");
        AssertPurityClassification(summary, "System.DateTime.Equals(object)", "pure");
        AssertPurityClassification(summary, "System.DateTime.Subtract(System.DateTime)", "pure");
        AssertPurityClassification(summary, "System.DateTime.Subtract(System.TimeSpan)", "pure");
        AssertPurityClassification(summary, "System.DateTime.DaysInMonth(int, int)", "pure");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeStablePureSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            40,
            "System.DateTime..ctor(long)",
            "System.DateTime..ctor(int, int, int)",
            "System.DateTime.IsLeapYear(int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DateTime..ctor(long)", "pure");
        AssertFreshnessClassification(summary, "System.DateTime..ctor(long)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.DateTime..ctor(long)", "internal_only");
        AssertPurityClassification(summary, "System.DateTime..ctor(int, int, int)", "pure");
        AssertFreshnessClassification(summary, "System.DateTime..ctor(int, int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.DateTime..ctor(int, int, int)", "internal_only");
        AssertPurityClassification(summary, "System.DateTime.IsLeapYear(int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.DateTime.IsLeapYear(int)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => symbol != null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(generatedSymbols, Does.Contain("System.DateTime..ctor(long)"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTime..ctor(int, int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTime.IsLeapYear(int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeGetterSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.DateTime.get_Day",
            "System.DateTime.get_DayOfWeek",
            "System.DateTime.get_DayOfYear",
            "System.DateTime.get_Hour",
            "System.DateTime.get_Kind",
            "System.DateTime.get_Millisecond",
            "System.DateTime.get_Minute",
            "System.DateTime.get_Month",
            "System.DateTime.get_Second",
            "System.DateTime.get_Ticks",
            "System.DateTime.get_TimeOfDay");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var representativeSymbols = new[]
        {
            "System.DateTime.get_Day()",
            "System.DateTime.get_DayOfWeek()",
            "System.DateTime.get_Hour()",
            "System.DateTime.get_Kind()",
            "System.DateTime.get_Ticks()",
            "System.DateTime.get_TimeOfDay()"
        };

        foreach (var symbol in representativeSymbols.Where(symbol =>
                     !string.Equals(symbol, "System.DateTime.get_TimeOfDay()", StringComparison.Ordinal)))
        {
            AssertPurityClassification(summary, symbol, "pure");
            AssertEffectVisibilityClassification(summary, symbol, "none");
        }

        AssertPurityClassification(summary, "System.DateTime.get_TimeOfDay()", "pure");
        AssertFreshnessClassification(summary, "System.DateTime.get_TimeOfDay()", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.DateTime.get_TimeOfDay()", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        foreach (var symbol in representativeSymbols) Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeAmbientStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            2,
            "System.DateTime.get_Now",
            "System.DateTime.get_Today",
            "System.DateTime.ToLocalTime()",
            "System.DateTime.get_UtcNow",
            "System.DateTimeOffset.get_Now",
            "System.DateTimeOffset.get_UtcNow");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DateTime.get_Now()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTime.get_Now()", "caller_visible");
        AssertPurityClassification(summary, "System.DateTime.get_Today()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTime.get_Today()", "caller_visible");
        AssertPurityClassification(summary, "System.DateTime.ToLocalTime()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTime.ToLocalTime()", "caller_visible");
        AssertPurityClassification(summary, "System.DateTime.get_UtcNow()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTime.get_UtcNow()", "caller_visible");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Now()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTimeOffset.get_Now()", "caller_visible");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_UtcNow()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTimeOffset.get_UtcNow()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.DateTime.get_Now()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.DateTime.get_Today()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.DateTime.ToLocalTime()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.DateTime.get_UtcNow()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.DateTimeOffset.get_Now()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.DateTimeOffset.get_UtcNow()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.DateTime.ToLocalTime()",
            "System.DateTime.get_Now()",
            "System.DateTime.get_Today()",
            "System.DateTime.get_UtcNow()",
            "System.DateTimeOffset.get_Now()",
            "System.DateTimeOffset.get_UtcNow()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeOffsetStablePureSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.DateTimeOffset..ctor(long, System.TimeSpan)",
            "System.DateTimeOffset.get_DateTime",
            "System.DateTimeOffset.get_Day",
            "System.DateTimeOffset.get_DayOfWeek",
            "System.DateTimeOffset.get_DayOfYear",
            "System.DateTimeOffset.get_Hour",
            "System.DateTimeOffset.get_Millisecond",
            "System.DateTimeOffset.get_Minute",
            "System.DateTimeOffset.get_Month",
            "System.DateTimeOffset.get_Offset",
            "System.DateTimeOffset.get_Second",
            "System.DateTimeOffset.get_Ticks",
            "System.DateTimeOffset.get_UtcDateTime",
            "System.DateTimeOffset.get_UtcTicks",
            "System.DateTimeOffset.get_Year");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DateTimeOffset..ctor(long, System.TimeSpan)", "pure");
        AssertFreshnessClassification(summary, "System.DateTimeOffset..ctor(long, System.TimeSpan)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.DateTimeOffset..ctor(long, System.TimeSpan)",
            "internal_only");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_DateTime()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Day()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_DayOfWeek()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_DayOfYear()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Hour()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Millisecond()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Minute()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Month()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Offset()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Second()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Ticks()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_UtcDateTime()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_UtcTicks()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.get_Year()", "pure");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => symbol != null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset..ctor(long, System.TimeSpan)"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.get_DateTime()"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.get_Offset()"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.get_UtcDateTime()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeOffsetAdditionalPureSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.DateTimeOffset.Add(System.TimeSpan)",
            "System.DateTimeOffset.AddDays(double)",
            "System.DateTimeOffset.AddHours(double)",
            "System.DateTimeOffset.AddMilliseconds(double)",
            "System.DateTimeOffset.AddMinutes(double)",
            "System.DateTimeOffset.AddMonths(int)",
            "System.DateTimeOffset.AddSeconds(double)",
            "System.DateTimeOffset.AddTicks(long)",
            "System.DateTimeOffset.AddYears(int)",
            "System.DateTimeOffset.ToUnixTimeMilliseconds()",
            "System.DateTimeOffset.ToUnixTimeSeconds()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DateTimeOffset.Add(System.TimeSpan)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddDays(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddHours(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMilliseconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMinutes(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddMonths(int)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddSeconds(double)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddTicks(long)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.AddYears(int)", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeMilliseconds()", "pure");
        AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeSeconds()", "pure");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => symbol != null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.Add(System.TimeSpan)"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.AddMonths(int)"));
        Assert.That(generatedSymbols, Does.Contain("System.DateTimeOffset.ToUnixTimeSeconds()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeVersionSlice_TreatsIntegerConstructorsAsFreshOwnedPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Version", 40);

        AssertPurityClassification(summary, "System.Version..ctor(int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Version..ctor(int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int)", "internal_only");

        AssertPurityClassification(summary, "System.Version..ctor(int, int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Version..ctor(int, int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int, int)", "internal_only");

        AssertPurityClassification(summary, "System.Version..ctor(int, int, int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Version..ctor(int, int, int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int, int, int)", "internal_only");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeVersionPureSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.Version..ctor(int, int)",
            "System.Version..ctor(int, int, int)",
            "System.Version..ctor(int, int, int, int)",
            "System.Version.CompareTo(System.Version)",
            "System.Version.Equals(System.Version)",
            "System.Version.get_Major",
            "System.Version.get_Minor",
            "System.Version.get_Build",
            "System.Version.get_Revision",
            "System.Version.get_MajorRevision",
            "System.Version.get_MinorRevision",
            "System.Version.GetHashCode",
            "System.Version.op_Equality",
            "System.Version.op_Inequality",
            "System.Version.op_GreaterThan",
            "System.Version.op_GreaterThanOrEqual",
            "System.Version.op_LessThan",
            "System.Version.op_LessThanOrEqual");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Version..ctor(int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Version..ctor(int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int)", "internal_only");
        AssertPurityClassification(summary, "System.Version.CompareTo(System.Version)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.CompareTo(System.Version)", "none");
        AssertPurityClassification(summary, "System.Version.Equals(System.Version)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.Equals(System.Version)", "none");
        AssertPurityClassification(summary, "System.Version.get_Major()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.get_Major()", "none");
        AssertPurityClassification(summary, "System.Version.op_Equality(System.Version, System.Version)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.op_Equality(System.Version, System.Version)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Version..ctor(int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version..ctor(int, int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version..ctor(int, int, int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.CompareTo(System.Version)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.Equals(System.Version)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_Major()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_Minor()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_Build()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_Revision()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_MajorRevision()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_MinorRevision()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.GetHashCode()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_Equality(System.Version, System.Version)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_Inequality(System.Version, System.Version)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_GreaterThan(System.Version, System.Version)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_GreaterThanOrEqual(System.Version, System.Version)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_LessThan(System.Version, System.Version)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.op_LessThanOrEqual(System.Version, System.Version)",
                    StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Version..ctor(int, int)",
            "System.Version..ctor(int, int, int)",
            "System.Version..ctor(int, int, int, int)",
            "System.Version.CompareTo(System.Version)",
            "System.Version.Equals(System.Version)",
            "System.Version.GetHashCode()",
            "System.Version.get_Build()",
            "System.Version.get_Major()",
            "System.Version.get_MajorRevision()",
            "System.Version.get_Minor()",
            "System.Version.get_MinorRevision()",
            "System.Version.get_Revision()",
            "System.Version.op_Equality(System.Version, System.Version)",
            "System.Version.op_GreaterThan(System.Version, System.Version)",
            "System.Version.op_GreaterThanOrEqual(System.Version, System.Version)",
            "System.Version.op_Inequality(System.Version, System.Version)",
            "System.Version.op_LessThan(System.Version, System.Version)",
            "System.Version.op_LessThanOrEqual(System.Version, System.Version)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeVersionSlice_RemainsVerifiableFromGeneratedOutput()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            1,
            false,
            "System.Version..ctor(int, int)",
            "System.Version.CompareTo(System.Version)",
            "System.Version.get_Major");

        AssertPurityClassification(summary, "System.Version..ctor(int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Version..ctor(int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int)", "internal_only");
        AssertPurityClassification(summary, "System.Version.CompareTo(System.Version)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.CompareTo(System.Version)", "none");
        AssertPurityClassification(summary, "System.Version.get_Major()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Version.get_Major()", "none");

        var generatedSymbols = GetGeneratedPurityCatalogSymbols(summary)
            .Where(symbol =>
                string.Equals(symbol, "System.Version..ctor(int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.CompareTo(System.Version)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Version.get_Major()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Version..ctor(int, int)",
            "System.Version.CompareTo(System.Version)",
            "System.Version.get_Major()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeFrameworkNameConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            16,
            "System.Runtime.Versioning.FrameworkName..ctor(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        const string symbol = "System.Runtime.Versioning.FrameworkName..ctor(string)";
        AssertPurityClassification(summary, symbol, "impure", "object_state_write", "throw");
        AssertEffectVisibilityClassification(summary, symbol, "caller_visible");

        var generatedSymbols = GetGeneratedPurityCatalogSymbols(summary)
            .Where(candidate => string.Equals(candidate, symbol, StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { symbol }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTimeSpanSlice_TreatsConstructorAsPureAndAddAsImpure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.TimeSpan", 80);

        AssertPurityClassification(summary, "System.TimeSpan..ctor(long)", "pure");
        AssertFreshnessClassification(summary, "System.TimeSpan..ctor(long)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan..ctor(long)", "internal_only");

        AssertPurityClassification(summary, "System.TimeSpan.Add(System.TimeSpan)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.Add(System.TimeSpan)", "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTimeSpanComparisonAndFactorySlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            40,
            "System.TimeSpan.CompareTo",
            "System.TimeSpan.FromDays");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");

        var knownPureRows = catalogComparison.GetProperty("KnownPureMembers")
            .EnumerateArray()
            .Where(row => row.GetProperty("DisplayName").GetString() is string symbol &&
                          (string.Equals(symbol, "System.TimeSpan.CompareTo(System.TimeSpan)",
                               StringComparison.Ordinal) ||
                           string.Equals(symbol, "System.TimeSpan.FromDays(double)", StringComparison.Ordinal)))
            .ToArray();

        Assert.That(knownPureRows, Is.Empty);
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.TimeSpan.CompareTo(System.TimeSpan)", "pure");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.CompareTo(System.TimeSpan)", "none");
        AssertPurityClassification(summary, "System.TimeSpan.FromDays(double)", "pure");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.FromDays(double)", "internal_only");
        AssertPurityClassification(summary, "System.TimeSpan.Interval(double, double)", "pure");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.Interval(double, double)", "internal_only");
        AssertPurityClassification(summary, "System.TimeSpan.CompareTo(object)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.CompareTo(object)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.TimeSpan.", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.TimeSpan.CompareTo(object)",
            "System.TimeSpan.CompareTo(System.TimeSpan)",
            "System.TimeSpan.FromDays(double)",
            "System.TimeSpan.Interval(double, double)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTimeSpanTicksSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(20, "System.TimeSpan.get_Ticks");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.TimeSpan.get_Ticks()", "pure");
        AssertEffectVisibilityClassification(summary, "System.TimeSpan.get_Ticks()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => symbol != null)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.TimeSpan.get_Ticks()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUnsafeSlice_TreatsReadUnalignedAsPureAndWriteUnalignedAsImpure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Runtime.CompilerServices.Unsafe", 80);

        var methods = FindMethodsByPrefix(summary, "System.Runtime.CompilerServices.Unsafe");
        var readMethods = methods.Where(method =>
                method.GetProperty("DisplayName").GetString() is
                    "System.Runtime.CompilerServices.Unsafe.ReadUnaligned(ref byte)" or
                    "System.Runtime.CompilerServices.Unsafe.ReadUnaligned(void*)")
            .ToArray();
        var writeMethods = methods.Where(method =>
                method.GetProperty("DisplayName").GetString() is
                    "System.Runtime.CompilerServices.Unsafe.WriteUnaligned(ref byte, !!0)" or
                    "System.Runtime.CompilerServices.Unsafe.WriteUnaligned(void*, !!0)")
            .ToArray();

        Assert.That(readMethods.Length, Is.EqualTo(2));
        Assert.That(
            readMethods.All(method =>
                method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"),
            Is.True);
        Assert.That(writeMethods.Length, Is.EqualTo(2));
        Assert.That(
            writeMethods.All(method =>
                method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "impure"),
            Is.True);
        Assert.That(writeMethods.All(method =>
            method.GetProperty("PurityClassification")
                .GetProperty("Categories")
                .EnumerateArray()
                .Any(category => category.GetString() == "caller_visible_memory_write")), Is.True);

        var asMethods = methods.Where(method =>
                method.GetProperty("DisplayName").GetString() is "System.Runtime.CompilerServices.Unsafe.As(object)" or
                    "System.Runtime.CompilerServices.Unsafe.As(ref !!0)")
            .ToArray();
        var sizeOfMethods = methods.Where(method =>
                method.GetProperty("DisplayName").GetString() == "System.Runtime.CompilerServices.Unsafe.SizeOf()")
            .ToArray();

        Assert.That(asMethods.Length, Is.EqualTo(2));
        Assert.That(
            asMethods.All(method =>
                method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"),
            Is.True);
        Assert.That(sizeOfMethods.Length, Is.EqualTo(1));
        Assert.That(sizeOfMethods[0].GetProperty("PurityClassification").GetProperty("Classification").GetString(),
            Is.EqualTo("pure"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringSlice_TreatsToCharArrayAsGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.ToCharArray", 10);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var toCharArrayRows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(entry => string.Equals(
                                entry.GetProperty("DisplayName").GetString(),
                                "System.String.ToCharArray()",
                                StringComparison.Ordinal) ||
                            string.Equals(
                                entry.GetProperty("DisplayName").GetString(),
                                "System.String.ToCharArray(int, int)",
                                StringComparison.Ordinal))
            .ToArray();

        Assert.That(toCharArrayRows.Length, Is.EqualTo(2));
        Assert.That(toCharArrayRows.All(row => row.GetProperty("Classification").GetString() == "pure"), Is.True);
        Assert.That(
            toCharArrayRows.All(row =>
                row.GetProperty("FreshnessClassification").GetString() == "fresh_array_candidate_via_local_helpers"),
            Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringIsNullOrEmptySlice_TreatsHelperAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.IsNullOrEmpty", 10);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.IsNullOrEmpty(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.IsNullOrEmpty(string)", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.IsNullOrEmpty", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EqualTo(new[]
        {
            "System.String.IsNullOrEmpty(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringIsNullOrWhiteSpaceSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.IsNullOrWhiteSpace", 10);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.IsNullOrWhiteSpace(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.IsNullOrWhiteSpace(string)", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.IsNullOrWhiteSpace", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EqualTo(new[]
        {
            "System.String.IsNullOrWhiteSpace(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringComparerSlice_TreatsOrdinalGettersAsPure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.StringComparer", 20);

        AssertPurityClassification(summary, "System.StringComparer.get_Ordinal()", "pure");
        AssertEffectVisibilityClassification(summary, "System.StringComparer.get_Ordinal()", "internal_only");
        AssertPurityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "pure");
        AssertEffectVisibilityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "internal_only");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.StringComparer.get_Ordinal", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EquivalentTo(new[]
        {
            "System.StringComparer.get_Ordinal()",
            "System.StringComparer.get_OrdinalIgnoreCase()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringFormatSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.String.Format(string, object)",
            "System.String.Format(string, object, object)",
            "System.String.Format(string, object, object, object)",
            "System.String.Format(string, object[])");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        foreach (var symbol in new[]
                 {
                     "System.String.Format(string, object)",
                     "System.String.Format(string, object, object)",
                     "System.String.Format(string, object, object, object)",
                     "System.String.Format(string, object[])"
                 })
        {
            AssertPurityClassification(summary, symbol, "impure", "impure_callee");
            AssertEffectVisibilityClassification(summary, symbol, "caller_visible");
            AssertPrimaryCategory(summary, symbol, "impure_callee");
        }

        var generatedSymbols = summary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.String.Format(string, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.Format(string, object, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.Format(string, object, object, object)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.Format(string, object[])", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            generatedSymbols,
            Is.EquivalentTo(new[]
            {
                "System.String.Format(string, object)",
                "System.String.Format(string, object, object)",
                "System.String.Format(string, object, object, object)",
                "System.String.Format(string, object[])"
            }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringLengthSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.get_Length", 10);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.get_Length()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.get_Length()", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.get_Length", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EqualTo(new[]
        {
            "System.String.get_Length()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringTrimSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Trim", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Trim()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Trim()", "none");
        AssertPurityClassification(summary, "System.String.TrimStart()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.TrimStart()", "none");
        AssertPurityClassification(summary, "System.String.TrimEnd()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.TrimEnd()", "none");
        AssertPurityClassification(summary, "System.String.Trim(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Trim(char)", "none");
        AssertPurityClassification(summary, "System.String.TrimStart(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.TrimStart(char)", "none");
        AssertPurityClassification(summary, "System.String.TrimEnd(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.TrimEnd(char)", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Trim", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Does.Contain("System.String.Trim()"));
        Assert.That(symbols, Does.Contain("System.String.TrimStart()"));
        Assert.That(symbols, Does.Contain("System.String.TrimEnd()"));
        Assert.That(symbols, Does.Contain("System.String.Trim(char)"));
        Assert.That(symbols, Does.Contain("System.String.TrimStart(char)"));
        Assert.That(symbols, Does.Contain("System.String.TrimEnd(char)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringEqualsSlice_TreatsComparisonOverloadsAsGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Equals", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Equals(string)", "impure");
        AssertEffectVisibilityClassification(summary, "System.String.Equals(string)", "caller_visible");
        AssertPurityClassification(summary, "System.String.Equals(string, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Equals(string, string)", "none");
        AssertPurityClassification(summary, "System.String.Equals(string, System.StringComparison)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.String.Equals(string, System.StringComparison)",
            "caller_visible");
        AssertPurityClassification(summary, "System.String.Equals(string, string, System.StringComparison)", "impure",
            "throw");
        AssertEffectVisibilityClassification(summary, "System.String.Equals(string, string, System.StringComparison)",
            "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringGetHashCodeSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.GetHashCode", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.GetHashCode()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.GetHashCode()", "none");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringInvariantCasingSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var lowerSummary = await RunRuntimeEffectSummaryAsync("System.String.ToLowerInvariant", 10);
        using var upperSummary = await RunRuntimeEffectSummaryAsync("System.String.ToUpperInvariant", 10);

        var lowerReport = lowerSummary.RootElement.GetProperty("PurityReport");
        var lowerCatalogComparison = lowerReport.GetProperty("CatalogComparison");
        Assert.That(lowerCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));

        var upperReport = upperSummary.RootElement.GetProperty("PurityReport");
        var upperCatalogComparison = upperReport.GetProperty("CatalogComparison");
        Assert.That(upperCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));

        AssertPurityClassification(lowerSummary, "System.String.ToLowerInvariant()", "pure");
        AssertEffectVisibilityClassification(lowerSummary, "System.String.ToLowerInvariant()", "internal_only");
        AssertPurityClassification(upperSummary, "System.String.ToUpperInvariant()", "pure");
        AssertEffectVisibilityClassification(upperSummary, "System.String.ToUpperInvariant()", "internal_only");

        var lowerSymbols = lowerSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.ToLowerInvariant", StringComparison.Ordinal))
            .ToArray();
        Assert.That(lowerSymbols, Is.EqualTo(new[]
        {
            "System.String.ToLowerInvariant()"
        }));

        var upperSymbols = upperSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.ToUpperInvariant", StringComparison.Ordinal))
            .ToArray();
        Assert.That(upperSymbols, Is.EqualTo(new[]
        {
            "System.String.ToUpperInvariant()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringConcatSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Concat", 60);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Concat(string, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Concat(string, string)", "internal_only");
        AssertPurityClassification(summary, "System.String.Concat(string[])", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Concat(string[])", "internal_only");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Concat", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Concat(string, string)"));
        Assert.That(symbols, Does.Contain("System.String.Concat(string[])"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringSubstringSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Substring", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Substring(int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Substring(int)", "internal_only");
        AssertPurityClassification(summary, "System.String.Substring(int, int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Substring(int, int)", "internal_only");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Substring", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Substring(int)"));
        Assert.That(symbols, Does.Contain("System.String.Substring(int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringReplaceSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Replace", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Replace(char, char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Replace(char, char)", "internal_only");
        AssertPurityClassification(summary, "System.String.Replace(string, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Replace(string, string)", "internal_only");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Replace", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Replace(char, char)"));
        Assert.That(symbols, Does.Contain("System.String.Replace(string, string)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringIndexOfSlice_TreatsDefaultStringSearchAsGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.IndexOf", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.IndexOf(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.IndexOf(char)", "internal_only");
        AssertPurityClassification(summary, "System.String.IndexOf(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.String.IndexOf(string)", "caller_visible");
        AssertPurityClassification(summary, "System.String.IndexOf(string, System.StringComparison)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.String.IndexOf(string, System.StringComparison)",
            "caller_visible");
        AssertPurityClassification(summary, "System.String.IndexOf(string, int, int, System.StringComparison)",
            "impure", "throw");
        AssertEffectVisibilityClassification(summary,
            "System.String.IndexOf(string, int, int, System.StringComparison)", "caller_visible");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.IndexOf", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.IndexOf(char)"));
        Assert.That(symbols, Does.Contain("System.String.IndexOf(string)"));
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeStringLastIndexEnumeratorAndSpanViewSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            60,
            "System.String.LastIndexOf",
            "System.String.GetEnumerator",
            "System.String.op_Implicit");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.LastIndexOf(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.LastIndexOf(char)", "none");
        AssertPurityClassification(summary, "System.String.GetEnumerator()", "pure");
        AssertFreshnessClassification(summary, "System.String.GetEnumerator()", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.String.GetEnumerator()", "internal_only");
        AssertPurityClassification(summary, "System.String.op_Implicit(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.op_Implicit(string)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.String.LastIndexOf(char)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.GetEnumerator()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.op_Implicit(string)", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.String.GetEnumerator()",
            "System.String.LastIndexOf(char)",
            "System.String.op_Implicit(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringCloneCompareToAndToStringSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            40,
            "System.String.Clone",
            "System.String.CompareTo",
            "System.String.ToString");

        AssertPurityClassification(summary, "System.String.Clone()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Clone()", "internal_only");
        AssertPurityClassification(summary, "System.String.CompareTo(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.CompareTo(string)", "internal_only");
        AssertPurityClassification(summary, "System.String.ToString()", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.ToString()", "none");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.String.Clone()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.CompareTo(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.ToString()", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Clone()"));
        Assert.That(symbols, Does.Contain("System.String.CompareTo(string)"));
        Assert.That(symbols, Does.Contain("System.String.ToString()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringInsertPadLeftAndRemoveSlice_UsesGeneratedPurityAndImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            60,
            "System.String.Insert",
            "System.String.PadLeft",
            "System.String.Remove");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Insert(int, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Insert(int, string)", "none");
        AssertPurityClassification(summary, "System.String.PadLeft(int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.PadLeft(int)", "none");
        AssertPurityClassification(summary, "System.String.Remove(int)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.String.Remove(int)", "caller_visible");
        AssertPurityClassification(summary, "System.String.Remove(int, int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Remove(int, int)", "internal_only");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.String.Insert(int, string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.PadLeft(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.Remove(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.String.Remove(int, int)", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Insert(int, string)"));
        Assert.That(symbols, Does.Contain("System.String.PadLeft(int)"));
        Assert.That(symbols, Does.Contain("System.String.Remove(int)"));
        Assert.That(symbols, Does.Contain("System.String.Remove(int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringBuilderToStringSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Text.StringBuilder.ToString", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Text.StringBuilder.ToString()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder.ToString()", "caller_visible");
        AssertPurityClassification(summary, "System.Text.StringBuilder.ToString(int, int)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder.ToString(int, int)", "caller_visible");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Text.StringBuilder.ToString", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.Text.StringBuilder.ToString()"));
        Assert.That(symbols, Does.Contain("System.Text.StringBuilder.ToString(int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringBuilderLengthSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Text.StringBuilder.get_Length");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Text.StringBuilder.get_Length()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder.get_Length()", "none");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.Text.StringBuilder.get_Length()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringBuilderConstructorSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Text.StringBuilder..ctor()",
            "System.Text.StringBuilder..ctor(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Text.StringBuilder..ctor()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder..ctor()", "internal_only");
        AssertPurityClassification(summary, "System.Text.StringBuilder..ctor(string)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder..ctor(string)", "caller_visible");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.Text.StringBuilder..ctor()"));
        Assert.That(symbols, Does.Contain("System.Text.StringBuilder..ctor(string)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDateTimeToFileTimeAndMemberwiseCloneSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            "System.DateTime.ToFileTime()",
            "System.Object.MemberwiseClone()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DateTime.ToFileTime()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.DateTime.ToFileTime()", "caller_visible");
        AssertPurityClassification(summary, "System.Object.MemberwiseClone()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Object.MemberwiseClone()", "caller_visible");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.DateTime.ToFileTime()"));
        Assert.That(symbols, Does.Contain("System.Object.MemberwiseClone()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeHttpResponseMessageStatusCodeSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Net.Http.dll",
            20,
            "System.Net.Http.HttpResponseMessage.get_IsSuccessStatusCode()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Net.Http.HttpResponseMessage.get_IsSuccessStatusCode()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Net.Http.HttpResponseMessage.get_IsSuccessStatusCode()",
            "none");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.Net.Http.HttpResponseMessage.get_IsSuccessStatusCode()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringSplitSlice_UsesGeneratedFreshArrayEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Split", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Split(char[])", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Split(char[])", "internal_only");
        AssertFreshnessClassification(summary, "System.String.Split(char[])", "none");
        AssertPurityClassification(summary, "System.String.Split(char[], System.StringSplitOptions)", "pure");
        AssertFreshnessClassification(summary, "System.String.Split(char[], System.StringSplitOptions)", "none");
        AssertPurityClassification(summary, "System.String.Split(string[], System.StringSplitOptions)", "pure");
        AssertFreshnessClassification(summary, "System.String.Split(string[], System.StringSplitOptions)", "none");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Split", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Split(char[])"));
        Assert.That(symbols, Does.Contain("System.String.Split(char[], System.StringSplitOptions)"));
        Assert.That(symbols, Does.Contain("System.String.Split(string[], System.StringSplitOptions)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringJoinSlice_ReflectsCurrentIncludeCalleesClassification()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Join", 80);

        AssertPurityClassification(summary, "System.String.Join(string, string[])", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Join(string, string[])", "internal_only");
        AssertPurityClassification(summary, "System.String.Join(string, string[], int, int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Join(string, string[], int, int)",
            "internal_only");
        AssertPurityClassification(summary,
            "System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>)", "pure");
        AssertEffectVisibilityClassification(summary,
            "System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>)", "internal_only");

        var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.String.Join", StringComparison.Ordinal))
            .ToArray();
        Assert.That(symbols, Does.Contain("System.String.Join(string, string[])"));
        Assert.That(symbols, Does.Contain("System.String.Join(string, string[], int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringPrefixSuffixSlice_TreatsStartsWithAndEndsWithAsImpure()
    {
        using var startsWithSummary = await RunRuntimeEffectSummaryAsync("System.String.StartsWith", 20);
        using var endsWithSummary = await RunRuntimeEffectSummaryAsync("System.String.EndsWith", 20);

        AssertPurityClassification(startsWithSummary, "System.String.StartsWith(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(startsWithSummary, "System.String.StartsWith(string)", "caller_visible");

        AssertPurityClassification(endsWithSummary, "System.String.EndsWith(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(endsWithSummary, "System.String.EndsWith(string)", "caller_visible");
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeStringContainsSlice_DistinguishesPureAndParameterizedStringComparisonOverloads()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.String.Contains", 20);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.String.Contains(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Contains(string)", "internal_only");
        AssertPurityClassification(summary, "System.String.Contains(char)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Contains(char)", "internal_only");
        AssertPurityClassification(summary, "System.String.Contains(char, System.StringComparison)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Contains(char, System.StringComparison)",
            "internal_only");
        AssertPurityClassification(summary, "System.String.Contains(string, System.StringComparison)", "pure");
        AssertEffectVisibilityClassification(summary, "System.String.Contains(string, System.StringComparison)",
            "internal_only");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var rows = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(row =>
                row.GetProperty("DisplayName").GetString()?.StartsWith("System.String.Contains", StringComparison.Ordinal) ==
                true)
            .ToArray();

        Assert.That(rows, Has.Length.EqualTo(4));
        Assert.That(
            rows.Select(row => row.GetProperty("DisplayName").GetString()),
            Is.EquivalentTo(new[]
            {
                "System.String.Contains(char)",
                "System.String.Contains(char, System.StringComparison)",
                "System.String.Contains(string)",
                "System.String.Contains(string, System.StringComparison)"
            }));
        Assert.That(rows.Count(row => row.GetProperty("Classification").GetString() == "pure"), Is.EqualTo(4));
        Assert.That(rows.Count(row => row.GetProperty("Classification").GetString() == "impure"), Is.EqualTo(0));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBooleanAndCharToStringSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(40, "System.Boolean.ToString", "System.Char.ToString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Boolean.ToString()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Boolean.ToString()", "none");
        AssertPurityClassification(summary, "System.Char.ToString()", "impure");
        AssertEffectVisibilityClassification(summary, "System.Char.ToString()", "caller_visible");
        AssertPurityClassification(summary, "System.Char.ToString(char)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Char.ToString(char)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             (symbol.StartsWith("System.Boolean.ToString", StringComparison.Ordinal) ||
                              symbol.StartsWith("System.Char.ToString", StringComparison.Ordinal)))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.Boolean.ToString()",
            "System.Boolean.ToString(System.IFormatProvider)",
            "System.Char.ToString()",
            "System.Char.ToString(System.IFormatProvider)",
            "System.Char.ToString(char)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeBooleanCompareAndCharHelperSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            140,
            "System.Boolean.CompareTo(bool)",
            "System.Char.ConvertFromUtf32(int)",
            "System.Char.ConvertToUtf32(char, char)",
            "System.Char.GetNumericValue(char)",
            "System.Char.IsControl(char)",
            "System.Char.IsDigit(char)",
            "System.Char.IsLetter(char)",
            "System.Char.IsLower(char)",
            "System.Char.IsNumber(char)",
            "System.Char.IsPunctuation(char)",
            "System.Char.IsSeparator(char)",
            "System.Char.IsSymbol(char)",
            "System.Char.IsUpper(char)",
            "System.Char.IsWhiteSpace(char)",
            "System.Char.ToLowerInvariant(char)",
            "System.Char.ToUpperInvariant(char)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Char.ConvertFromUtf32(int)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Char.ConvertFromUtf32(int)", "caller_visible");
        AssertPurityClassification(summary, "System.Char.ConvertToUtf32(char, char)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Char.ConvertToUtf32(char, char)", "caller_visible");

        var representativeSymbols = new[]
        {
            "System.Boolean.CompareTo(bool)",
            "System.Char.GetNumericValue(char)",
            "System.Char.IsControl(char)",
            "System.Char.IsUpper(char)",
            "System.Char.IsWhiteSpace(char)",
            "System.Char.ToLowerInvariant(char)",
            "System.Char.ToUpperInvariant(char)"
        };

        foreach (var symbol in representativeSymbols)
        {
            AssertPurityClassification(summary, symbol, "pure");
            AssertEffectVisibilityClassification(summary, symbol, "none");
        }

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        foreach (var symbol in representativeSymbols) Assert.That(generatedSymbols, Does.Contain(symbol));

        Assert.That(generatedSymbols, Does.Contain("System.Char.ConvertFromUtf32(int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Char.ConvertToUtf32(char, char)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeIndexAndHashCodeSlice_ReflectsCurrentIncludeCalleesClassification()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            120,
            "System.HashCode.Combine",
            "System.HashCode.ToHashCode()",
            "System.Index.get_End",
            "System.Index.get_Start");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var knownImpureMembers = catalogComparison.GetProperty("KnownImpureMembers")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        Assert.That(knownImpureMembers, Is.EqualTo(new[] { "object.GetHashCode()" }));

        AssertPurityClassification(summary, "System.HashCode.Combine(!!0, !!1)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.HashCode.Combine(!!0, !!1)", "caller_visible");
        AssertPurityClassification(summary, "System.HashCode.ToHashCode()", "pure");
        AssertEffectVisibilityClassification(summary, "System.HashCode.ToHashCode()", "none");
        AssertPurityClassification(summary, "System.Index.get_End()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Index.get_End()", "none");
        AssertPurityClassification(summary, "System.Index.get_Start()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Index.get_Start()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        foreach (var symbol in new[]
                 {
                     "System.HashCode.Combine(!!0, !!1)",
                     "System.HashCode.ToHashCode()",
                     "System.Index.get_End()",
                     "System.Index.get_Start()"
                 })
            Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeSpanAndMemoryMarshalSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            140,
            "System.ReadOnlySpan`1.get_Length",
            "System.ReadOnlySpan`1.get_IsEmpty",
            "System.ReadOnlySpan`1.Slice(int, int)",
            "System.Span`1.get_Length",
            "System.Span`1.get_IsEmpty",
            "System.Runtime.InteropServices.MemoryMarshal.AsBytes");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var representativeSymbols = new[]
        {
            "System.ReadOnlySpan`1.get_Length()",
            "System.ReadOnlySpan`1.get_IsEmpty()",
            "System.ReadOnlySpan`1.Slice(int, int)",
            "System.Span`1.get_Length()",
            "System.Span`1.get_IsEmpty()",
            "System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.ReadOnlySpan`1<!!0>)",
            "System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Span`1<!!0>)"
        };

        foreach (var symbol in representativeSymbols)
        {
            AssertPurityClassification(summary, symbol, "pure");
            AssertEffectVisibilityClassification(summary, symbol, "none");
        }

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        foreach (var symbol in representativeSymbols) Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStringInfoSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            20,
            "System.Globalization.StringInfo.ParseCombiningCharacters");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Globalization.StringInfo.ParseCombiningCharacters(string)",
            "impure");
        AssertEffectVisibilityClassification(summary,
            "System.Globalization.StringInfo.ParseCombiningCharacters(string)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Globalization.StringInfo.ParseCombiningCharacters(string)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDelegateRemoveSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            20,
            "System.Delegate.Remove(System.Delegate, System.Delegate)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Delegate.Remove(System.Delegate, System.Delegate)", "impure",
            "throw");
        AssertEffectVisibilityClassification(summary, "System.Delegate.Remove(System.Delegate, System.Delegate)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Delegate.Remove(System.Delegate, System.Delegate)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDelegateCombineSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            20,
            "System.Delegate.Combine(System.Delegate, System.Delegate)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Delegate.Combine(System.Delegate, System.Delegate)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Delegate.Combine(System.Delegate, System.Delegate)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Delegate.Combine(System.Delegate, System.Delegate)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMarshalSizeOfSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            20,
            "System.Runtime.InteropServices.Marshal.SizeOf");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Runtime.InteropServices.Marshal.SizeOf()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Runtime.InteropServices.Marshal.SizeOf()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Runtime.InteropServices.Marshal.SizeOf()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePipeConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.IO.Pipelines.dll",
            20,
            "System.IO.Pipelines.Pipe..ctor(System.IO.Pipelines.PipeOptions)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.Pipelines.Pipe..ctor(System.IO.Pipelines.PipeOptions)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.IO.Pipelines.Pipe..ctor(System.IO.Pipelines.PipeOptions)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.IO.Pipelines.Pipe..ctor(System.IO.Pipelines.PipeOptions)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMemorySlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            20,
            "System.Memory`1.Slice(int, int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Memory`1.Slice(int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Memory`1.Slice(int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Memory`1.Slice(int, int)", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Memory`1.Slice(int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeReadOnlySequenceSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Memory.dll",
            60,
            "System.Buffers.ReadOnlySequence`1.get_End",
            "System.Buffers.ReadOnlySequence`1.get_IsEmpty",
            "System.Buffers.ReadOnlySequence`1.get_Length",
            "System.Buffers.ReadOnlySequence`1.get_Start",
            "System.Buffers.ReadOnlySequence`1.Slice(long)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var representativeSymbols = new[]
        {
            "System.Buffers.ReadOnlySequence`1.get_End()",
            "System.Buffers.ReadOnlySequence`1.get_IsEmpty()",
            "System.Buffers.ReadOnlySequence`1.get_Length()",
            "System.Buffers.ReadOnlySequence`1.get_Start()",
            "System.Buffers.ReadOnlySequence`1.Slice(long)"
        };

        AssertPurityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_End()", "pure");
        AssertFreshnessClassification(summary, "System.Buffers.ReadOnlySequence`1.get_End()",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_End()", "internal_only");
        AssertPurityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_IsEmpty()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_IsEmpty()", "none");
        AssertPurityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_Length()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_Length()", "none");
        AssertPurityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_Start()", "pure");
        AssertFreshnessClassification(summary, "System.Buffers.ReadOnlySequence`1.get_Start()",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Buffers.ReadOnlySequence`1.get_Start()", "internal_only");
        AssertPurityClassification(summary, "System.Buffers.ReadOnlySequence`1.Slice(long)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Buffers.ReadOnlySequence`1.Slice(long)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Buffers.ReadOnlySequence`1.", StringComparison.Ordinal))
            .ToArray();

        foreach (var symbol in representativeSymbols) Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEmailAddressAttributeSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.Annotations.dll",
            20,
            "System.ComponentModel.DataAnnotations.EmailAddressAttribute..ctor()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        const string symbol = "System.ComponentModel.DataAnnotations.EmailAddressAttribute..ctor()";
        AssertPurityClassification(summary, symbol, "pure");
        AssertEffectVisibilityClassification(summary, symbol, "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCoreComponentModelAttributeSlices_UseGeneratedPurityCatalogEntries()
    {
        using var componentSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.Primitives.dll",
            40,
            "System.ComponentModel.BrowsableAttribute..ctor(bool)",
            "System.ComponentModel.DescriptionAttribute..ctor(string)");
        using var conditionalSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Diagnostics.ConditionalAttribute..ctor(string)");

        var componentReport = componentSummary.RootElement.GetProperty("PurityReport");
        var componentCatalogComparison = componentReport.GetProperty("CatalogComparison");
        Assert.That(componentCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(componentCatalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(componentCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(componentSummary, "System.ComponentModel.BrowsableAttribute..ctor(bool)", "pure");
        AssertEffectVisibilityClassification(componentSummary, "System.ComponentModel.BrowsableAttribute..ctor(bool)",
            "internal_only");
        AssertPurityClassification(componentSummary, "System.ComponentModel.DescriptionAttribute..ctor(string)",
            "pure");
        AssertEffectVisibilityClassification(componentSummary,
            "System.ComponentModel.DescriptionAttribute..ctor(string)", "internal_only");

        var componentGeneratedSymbols = componentSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(componentGeneratedSymbols, Does.Contain("System.ComponentModel.BrowsableAttribute..ctor(bool)"));
        Assert.That(componentGeneratedSymbols,
            Does.Contain("System.ComponentModel.DescriptionAttribute..ctor(string)"));

        var conditionalReport = conditionalSummary.RootElement.GetProperty("PurityReport");
        var conditionalCatalogComparison = conditionalReport.GetProperty("CatalogComparison");
        Assert.That(conditionalCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(conditionalCatalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(conditionalCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(conditionalSummary, "System.Diagnostics.ConditionalAttribute..ctor(string)", "pure");
        AssertEffectVisibilityClassification(conditionalSummary,
            "System.Diagnostics.ConditionalAttribute..ctor(string)", "internal_only");

        var conditionalGeneratedSymbols = conditionalSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(conditionalGeneratedSymbols, Does.Contain("System.Diagnostics.ConditionalAttribute..ctor(string)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCancelEventArgsSetter_UsesSdkDerivedPurity()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.dll",
            12,
            "System.ComponentModel.CancelEventArgs.set_Cancel");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.ComponentModel.CancelEventArgs.set_Cancel(bool)", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.ComponentModel.CancelEventArgs.set_Cancel(bool)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.ComponentModel.CancelEventArgs.set_Cancel(bool)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAddingNewEventArgsConstructor_UsesSdkDerivedPurity()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.TypeConverter.dll",
            12,
            "System.ComponentModel.AddingNewEventArgs..ctor(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.ComponentModel.AddingNewEventArgs..ctor()", "pure");
        AssertFreshnessClassification(summary, "System.ComponentModel.AddingNewEventArgs..ctor()", "none");
        AssertEffectVisibilityClassification(summary, "System.ComponentModel.AddingNewEventArgs..ctor()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.ComponentModel.AddingNewEventArgs..ctor()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeRegularExpressionAttributeSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.Annotations.dll",
            80,
            "System.ComponentModel.DataAnnotations.RegularExpressionAttribute..ctor(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        const string symbol = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute..ctor(string)";
        AssertPurityClassification(summary, symbol, "impure", "global_state_read", "global_state_write",
            "impure_callee", "object_state_write");
        AssertEffectVisibilityClassification(summary, symbol, "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCoreDataAnnotationsConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.ComponentModel.Annotations.dll",
            120,
            "System.ComponentModel.DataAnnotations.RequiredAttribute..ctor()",
            "System.ComponentModel.DataAnnotations.StringLengthAttribute..ctor(int)",
            "System.ComponentModel.DataAnnotations.RangeAttribute..ctor(double, double)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.ComponentModel.DataAnnotations.RequiredAttribute..ctor()", "impure",
            "global_state_read", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.ComponentModel.DataAnnotations.RequiredAttribute..ctor()",
            "caller_visible");
        AssertPurityClassification(summary, "System.ComponentModel.DataAnnotations.StringLengthAttribute..ctor(int)",
            "impure", "global_state_read", "global_state_write", "object_state_write");
        AssertEffectVisibilityClassification(summary,
            "System.ComponentModel.DataAnnotations.StringLengthAttribute..ctor(int)", "caller_visible");
        AssertPurityClassification(summary,
            "System.ComponentModel.DataAnnotations.RangeAttribute..ctor(double, double)", "impure", "impure_callee",
            "object_state_write");
        AssertEffectVisibilityClassification(summary,
            "System.ComponentModel.DataAnnotations.RangeAttribute..ctor(double, double)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.ComponentModel.DataAnnotations.RequiredAttribute..ctor()"));
        Assert.That(generatedSymbols,
            Does.Contain("System.ComponentModel.DataAnnotations.StringLengthAttribute..ctor(int)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.ComponentModel.DataAnnotations.RangeAttribute..ctor(double, double)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDecimalNegateSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Decimal.Negate");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        const string symbol = "System.Decimal.Negate(decimal)";
        AssertPurityClassification(summary, symbol, "pure");
        AssertEffectVisibilityClassification(summary, symbol, "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDecimalComparisonAndConversionsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Decimal.Compare(decimal, decimal)",
            "System.Decimal.ToDouble(decimal)",
            "System.Decimal.ToInt32(decimal)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Decimal.Compare(decimal, decimal)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Decimal.Compare(decimal, decimal)", "none");
        AssertPurityClassification(summary, "System.Decimal.ToDouble(decimal)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Decimal.ToDouble(decimal)", "none");
        AssertPurityClassification(summary, "System.Decimal.ToInt32(decimal)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Decimal.ToInt32(decimal)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Decimal.Compare(decimal, decimal)"));
        Assert.That(generatedSymbols, Does.Contain("System.Decimal.ToDouble(decimal)"));
        Assert.That(generatedSymbols, Does.Contain("System.Decimal.ToInt32(decimal)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnumTryParseSlice_UsesSemanticHandlingInsteadOfManualCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(80, "System.Enum.TryParse");

        var knownPureRows = summary.RootElement.GetProperty("PurityReport")
            .GetProperty("CatalogComparison")
            .GetProperty("KnownPureMembers")
            .EnumerateArray()
            .Where(row => row.GetProperty("DisplayName").GetString() is string symbol &&
                          symbol.StartsWith("System.Enum.TryParse", StringComparison.Ordinal))
            .ToArray();

        Assert.That(knownPureRows, Is.Empty);

        AssertPurityClassification(summary, "System.Enum.TryParse(string, ref !!0)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Enum.TryParse(string, bool, ref !!0)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Enum.TryParse(System.ReadOnlySpan`1<char>, ref !!0)", "impure",
            "impure_callee");
        AssertPurityClassification(summary, "System.Enum.TryParse(System.ReadOnlySpan`1<char>, bool, ref !!0)",
            "impure", "impure_callee");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnumParseSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(80, "System.Enum.Parse");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Enum.Parse(System.Type, string)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Enum.Parse(System.Type, string)", "caller_visible");
        AssertPurityClassification(summary, "System.Enum.Parse(System.Type, string, bool)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Enum.Parse(System.Type, string, bool)", "caller_visible");
        AssertPurityClassification(summary, "System.Enum.Parse(string)", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Enum.Parse(string)", "caller_visible");
        AssertPurityClassification(summary, "System.Enum.Parse(string, bool)", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Enum.Parse(string, bool)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Enum.Parse", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.Enum.Parse(System.ReadOnlySpan`1<char>)",
            "System.Enum.Parse(System.ReadOnlySpan`1<char>, bool)",
            "System.Enum.Parse(System.Type, System.ReadOnlySpan`1<char>)",
            "System.Enum.Parse(System.Type, System.ReadOnlySpan`1<char>, bool)",
            "System.Enum.Parse(System.Type, string)",
            "System.Enum.Parse(System.Type, string, bool)",
            "System.Enum.Parse(string)",
            "System.Enum.Parse(string, bool)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUriIsWellFormedSlice_ReflectsCurrentIncludeCalleesClassification()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Private.Uri.dll", 40,
                "System.Uri.IsWellFormedUriString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Uri.IsWellFormedUriString(string, System.UriKind)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Uri.IsWellFormedUriString(string, System.UriKind)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Uri.IsWellFormedUriString", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Uri.IsWellFormedUriString(string, System.UriKind)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUriEscapeDataStringSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Private.Uri.dll", 40, "System.Uri.EscapeDataString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Uri.EscapeDataString(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Uri.EscapeDataString(string)", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Uri.EscapeDataString", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Uri.EscapeDataString(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUriUnescapeDataStringSlice_ReflectsCurrentIncludeCalleesClassification()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Private.Uri.dll", 40,
                "System.Uri.UnescapeDataString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Uri.UnescapeDataString(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Uri.UnescapeDataString(string)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Uri.UnescapeDataString", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Uri.UnescapeDataString(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeUriToStringSlice_UsesGeneratedImpureEvidence()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Private.Uri.dll", 40, "System.Uri.ToString");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Uri.ToString()", "impure", "impure_callee", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Uri.ToString()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Uri.ToString()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.Uri.ToString()" }));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_GeneratesMultipleOutputFiles()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var versionOutputPath = Path.Combine(workingDirectory, "Version.SharpProof.EffectSummary.json");
        var environmentOutputPath = Path.Combine(workingDirectory, "Environment.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Private.CoreLib.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = versionOutputPath,
                        Limit = 40,
                        SymbolPrefixes = new[]
                        {
                            "System.Version..ctor(int, int)",
                            "System.Version.get_Major"
                        }
                    },
                    new
                    {
                        OutputPath = environmentOutputPath,
                        Limit = 20,
                        SymbolPrefixes = new[]
                        {
                            "System.Environment.get_NewLine",
                            "System.Environment.get_Is64BitProcess"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        Assert.That(File.Exists(versionOutputPath), Is.True);
        Assert.That(File.Exists(environmentOutputPath), Is.True);

        using var versionSummary = JsonDocument.Parse(await File.ReadAllTextAsync(versionOutputPath));
        using var environmentSummary = JsonDocument.Parse(await File.ReadAllTextAsync(environmentOutputPath));

        var versionAssemblySource = versionSummary.RootElement.GetProperty("Assemblies")[0]
            .GetProperty("ArtifactSource");
        Assert.That(versionAssemblySource.GetProperty("Kind").GetString(), Is.EqualTo("framework"));
        Assert.That(versionAssemblySource.GetProperty("Framework").GetString(), Is.EqualTo("net8.0"));

        Assert.That(
            versionSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Any(entry => string.Equals(
                    entry.GetProperty("DisplayName").GetString(),
                    "System.Version..ctor(int, int)",
                    StringComparison.Ordinal)),
            Is.True);
        var versionCatalogSource = versionSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")[0]
            .GetProperty("ArtifactSource");
        Assert.That(versionCatalogSource.GetProperty("Kind").GetString(), Is.EqualTo("framework"));
        Assert.That(versionCatalogSource.GetProperty("Framework").GetString(), Is.EqualTo("net8.0"));
        Assert.That(
            versionSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Any(entry => string.Equals(
                    entry.GetProperty("DisplayName").GetString(),
                    "System.Version.get_Major()",
                    StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            environmentSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Any(entry => string.Equals(
                    entry.GetProperty("DisplayName").GetString(),
                    "System.Environment.get_NewLine()",
                    StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            environmentSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Any(entry => string.Equals(
                    entry.GetProperty("DisplayName").GetString(),
                    "System.Environment.get_Is64BitProcess()",
                    StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpecDependencies_WriteStableResolvedManifests()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-dependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        const string source = "public static class DependencyFixture { public static int Value() => 42; }";
        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDependencyFixture", source);
        var sourceSummaryPath = Path.Combine(workingDirectory, "source-summary.json");
        await File.WriteAllTextAsync(
            sourceSummaryPath,
            """
            {
              "GeneratedPurityCatalog": {
                "Entries": [
                  {
                    "DisplayName": "DependencyFixture.Value()",
                    "ExactSymbolKey": "DependencyFixture.Value()->int"
                  }
                ]
              }
            }
            """);

        var outputRoot = Path.Combine(workingDirectory, "generated");
        var inputManifestPath = Path.Combine(workingDirectory, "inputs.txt");
        var outputManifestPath = Path.Combine(workingDirectory, "outputs.txt");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");
        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Artifacts = new[]
                {
                    new
                    {
                        OutputPath = "DependencyFixture.SharpProof.EffectSummary.json",
                        SourceSummaryPath = sourceSummaryPath,
                        AssemblyPaths = new[] { fixture.AssemblyPath },
                        Limit = 5
                    }
                }
            },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        var arguments = new[]
        {
            "--artifact-spec-dependencies", artifactSpecPath,
            "--input-manifest", inputManifestPath,
            "--output-manifest", outputManifestPath,
            "--dependency-output-root", outputRoot
        };
        await RunEffectSummaryToolAsync(arguments);

        var inputPaths = await File.ReadAllLinesAsync(inputManifestPath);
        var outputPaths = await File.ReadAllLinesAsync(outputManifestPath);
        Assert.That(inputPaths, Does.Contain(Path.GetFullPath(artifactSpecPath)));
        Assert.That(inputPaths, Does.Contain(Path.GetFullPath(fixture.AssemblyPath)));
        Assert.That(inputPaths, Does.Contain(Path.GetFullPath(sourceSummaryPath)));
        Assert.That(inputPaths, Does.Contain(Path.GetFullPath(GetEffectSummaryToolDllPath())));
        Assert.That(
            outputPaths,
            Is.EqualTo(new[]
            {
                Path.GetFullPath(Path.Combine(outputRoot, "DependencyFixture.SharpProof.EffectSummary.json"))
            }));
        Assert.That(await File.ReadAllTextAsync(inputManifestPath), Does.Not.Contain("\r"));
        Assert.That(await File.ReadAllTextAsync(outputManifestPath), Does.Not.Contain("\r"));
        Assert.That(File.ReadAllBytes(inputManifestPath).Take(3),
            Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));

        var fixedTimestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(inputManifestPath, fixedTimestamp);
        File.SetLastWriteTimeUtc(outputManifestPath, fixedTimestamp);

        await RunEffectSummaryToolAsync(arguments);

        Assert.That(File.GetLastWriteTimeUtc(inputManifestPath), Is.EqualTo(fixedTimestamp));
        Assert.That(File.GetLastWriteTimeUtc(outputManifestPath), Is.EqualTo(fixedTimestamp));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_Resolves_NuGetPackageAssembly()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory, "ImmutableCollections.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.Collections.Immutable",
                        PackageVersion = "9.0",
                        PackageAssemblyRelativePath = "lib/net8.0/System.Collections.Immutable.dll",
                        Limit = 40,
                        SymbolPrefixes = new[]
                        {
                            "System.Collections.Immutable.ImmutableList`1.get_Count",
                            "System.Collections.Immutable.ImmutableList`1.get_Item",
                            "System.Collections.Immutable.ImmutableDictionary.CreateRange",
                            "System.Collections.Immutable.ImmutableHashSet.CreateRange",
                            "System.Collections.Immutable.ImmutableQueue`1.Enqueue",
                            "System.Collections.Immutable.ImmutableQueue`1.Dequeue",
                            "System.Collections.Immutable.ImmutableStack`1.Push",
                            "System.Collections.Immutable.ImmutableStack`1.Pop"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var methods = summary.RootElement.GetProperty("Assemblies")[0].GetProperty("Methods").EnumerateArray()
            .ToArray();

        var assemblySource = summary.RootElement.GetProperty("Assemblies")[0].GetProperty("ArtifactSource");
        Assert.That(assemblySource.GetProperty("Kind").GetString(), Is.EqualTo("package"));
        Assert.That(assemblySource.GetProperty("PackageId").GetString(), Is.EqualTo("System.Collections.Immutable"));
        Assert.That(assemblySource.GetProperty("PackageVersion").GetString(), Is.EqualTo("9.0"));
        Assert.That(
            assemblySource.GetProperty("PackageAssemblyRelativePath").GetString(),
            Is.EqualTo("lib/net8.0/System.Collections.Immutable.dll"));
        var catalogSource = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")[0]
            .GetProperty("ArtifactSource");
        Assert.That(catalogSource.GetProperty("Kind").GetString(), Is.EqualTo("package"));

        Assert.That(
            Path.GetFileName(summary.RootElement.GetProperty("Assemblies")[0].GetProperty("AssemblyPath").GetString()),
            Is.EqualTo("System.Collections.Immutable.dll"));
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableList`1.get_Count()", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableList`1.get_Item(int)", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableDictionary.CreateRange(System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<!!0, !!1>>)",
                    StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableHashSet.CreateRange(System.Collections.Generic.IEnumerable`1<!!0>)",
                    StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableQueue`1.Enqueue(!0)", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableQueue`1.Enqueue(!0)", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableQueue`1.Dequeue()", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "impure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableStack`1.Push(!0)", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "pure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Collections.Immutable.ImmutableStack`1.Pop()", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "impure", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_Resolves_ConfigurationManagerPackageAssembly()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-configurationmanager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory,
            "ConfigurationManager.AppSettings.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.Configuration.ConfigurationManager",
                        PackageVersion = "10.0.8",
                        PackageAssemblyRelativePath = "lib/net8.0/System.Configuration.ConfigurationManager.dll",
                        Limit = 40,
                        SymbolPrefixes = new[]
                        {
                            "System.Configuration.ConfigurationManager.get_AppSettings"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var methods = summary.RootElement.GetProperty("Assemblies")[0].GetProperty("Methods").EnumerateArray()
            .ToArray();

        Assert.That(
            Path.GetFileName(summary.RootElement.GetProperty("Assemblies")[0].GetProperty("AssemblyPath").GetString()),
            Is.EqualTo("System.Configuration.ConfigurationManager.dll"));
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Configuration.ConfigurationManager.get_AppSettings()", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "impure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Configuration.ConfigurationManager.get_AppSettings()", StringComparison.Ordinal) &&
                method.GetProperty("PurityClassification").GetProperty("Categories")
                    .EnumerateArray()
                    .Any(category =>
                        string.Equals(category.GetString(), "global_state_read", StringComparison.Ordinal))),
            Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_Resolves_ConfigurationManagerConnectionStringsPackageAssembly()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-configurationmanager-connectionstrings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory,
            "ConfigurationManager.ConnectionStrings.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.Configuration.ConfigurationManager",
                        PackageVersion = "10.0.8",
                        PackageAssemblyRelativePath = "lib/net8.0/System.Configuration.ConfigurationManager.dll",
                        Limit = 40,
                        SymbolPrefixes = new[]
                        {
                            "System.Configuration.ConfigurationManager.get_ConnectionStrings"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var methods = summary.RootElement.GetProperty("Assemblies")[0].GetProperty("Methods").EnumerateArray()
            .ToArray();

        Assert.That(
            Path.GetFileName(summary.RootElement.GetProperty("Assemblies")[0].GetProperty("AssemblyPath").GetString()),
            Is.EqualTo("System.Configuration.ConfigurationManager.dll"));
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Configuration.ConfigurationManager.get_ConnectionStrings()", StringComparison.Ordinal) &&
                string.Equals(method.GetProperty("PurityClassification").GetProperty("Classification").GetString(),
                    "impure", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            methods.Any(method =>
                string.Equals(method.GetProperty("DisplayName").GetString(),
                    "System.Configuration.ConfigurationManager.get_ConnectionStrings()", StringComparison.Ordinal) &&
                method.GetProperty("PurityClassification").GetProperty("Categories")
                    .EnumerateArray()
                    .Any(category =>
                        string.Equals(category.GetString(), "global_state_read", StringComparison.Ordinal))),
            Is.True);
    }

    [Test]
    public void EffectSummaryTool_ArtifactSpec_Rejects_Rooted_NuGetPackageAssemblyPath()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-package-rooted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var outputPath = Path.Combine(workingDirectory, "ImmutableCollections.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var rootedRelativePath = OperatingSystem.IsWindows()
            ? "\\lib\\net8.0\\System.Collections.Immutable.dll"
            : "/lib/net8.0/System.Collections.Immutable.dll";
        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        PackageId = "System.Collections.Immutable",
                        PackageVersion = "9.0.0",
                        PackageAssemblyRelativePath = rootedRelativePath,
                        Limit = 10,
                        SymbolPrefixes = new[]
                        {
                            "System.Collections.Immutable.ImmutableQueue`1.Enqueue"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        File.WriteAllText(artifactSpecPath, artifactSpecJson);

        Assert.That(
            async () => await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath),
            Throws.TypeOf<AssertionException>().With.Message.Contains("must be a relative path"));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_ReusesReviewedSymbolSet()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var regeneratedOutputPath = Path.Combine(workingDirectory, "regenerated.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        await RunEffectSummaryToolAsync(
            "--framework",
            "net8.0",
            "--runtime-assembly",
            "System.Private.CoreLib.dll",
            "--symbol-prefix",
            "System.Version..ctor(int, int)",
            "--symbol-prefix",
            "System.Version.get_Major",
            "--include-callees",
            "--classify-purity",
            "--compare-manual-catalogs",
            "--limit",
            "20",
            "--output",
            seedOutputPath);

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Private.CoreLib.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = regeneratedOutputPath,
                        SourceSummaryPath = seedOutputPath,
                        Limit = 20
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var seedSummary = JsonDocument.Parse(await File.ReadAllTextAsync(seedOutputPath));
        using var regeneratedSummary = JsonDocument.Parse(await File.ReadAllTextAsync(regeneratedOutputPath));

        var seedSymbols = seedSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();
        var regeneratedSymbols = regeneratedSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(regeneratedSymbols, Is.EqualTo(seedSymbols));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_ReusesAdHocGeneratedOnlyCatalog()
    {
        var source = """
                     using System;

                     public static class PurityFixture
                     {
                         private static int _state;

                         public static int PureLeaf() => 42;

                         public static int PureViaCallee() => PureLeaf();

                         public static int ImpureWrite()
                         {
                             _state++;
                             return _state;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryArtifactSpecGeneratedOnly", source);

        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-generated-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed-generated-only.SharpProof.EffectSummary.json");
        var regeneratedOutputPath = Path.Combine(workingDirectory, "regenerated.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");
        string[] seedSymbols;

        using (var seedSummary = await RunEffectSummaryAsync(
                   fixture.AssemblyPath,
                   true,
                   true,
                   false))
        {
            seedSymbols = GetGeneratedPurityCatalogSymbols(seedSummary);
            await File.WriteAllTextAsync(seedOutputPath, CreateGeneratedOnlySummaryDocument(seedSummary));
        }

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = regeneratedOutputPath,
                        SourceSummaryPath = seedOutputPath,
                        AssemblyPaths = new[]
                        {
                            fixture.AssemblyPath
                        },
                        IncludeCallees = true,
                        IncludePurityClassification = true
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var regeneratedSummary = JsonDocument.Parse(await File.ReadAllTextAsync(regeneratedOutputPath));
        Assert.That(GetGeneratedPurityCatalogSymbols(regeneratedSummary), Is.EqualTo(seedSymbols));
        AssertPurityClassification(regeneratedSummary, "PurityFixture.PureLeaf()", "pure");
        AssertEffectVisibilityClassification(regeneratedSummary, "PurityFixture.PureLeaf()", "none");
        AssertPurityClassification(regeneratedSummary, "PurityFixture.PureViaCallee()", "pure");
        AssertEffectVisibilityClassification(regeneratedSummary, "PurityFixture.PureViaCallee()", "none");
        AssertPurityClassification(regeneratedSummary, "PurityFixture.ImpureWrite()", "impure", "global_state_write");
        AssertEffectVisibilityClassification(regeneratedSummary, "PurityFixture.ImpureWrite()", "caller_visible");
    }

    [Test]
    public async Task
        EffectSummaryTool_ArtifactSpec_SourceSummaryPath_ReusesDistinctExactKeys_ForDuplicateDisplaySymbols()
    {
        var source = """
                     public readonly struct ConversionFixture
                     {
                         private readonly int _value;

                         public ConversionFixture(int value)
                         {
                             _value = value;
                         }

                         public static explicit operator int(ConversionFixture value) => value._value;

                         public static explicit operator long(ConversionFixture value) => value._value;
                     }
                     """;

        await using var fixture =
            await CreateFixtureAssemblyAsync("EffectSummaryArtifactSpecDuplicateDisplaySymbols", source);

        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-source-duplicate-symbols-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var regeneratedOutputPath = Path.Combine(workingDirectory, "regenerated.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        using (var seedSummary = await RunEffectSummaryAsync(
                   fixture.AssemblyPath,
                   true,
                   true,
                   false))
        {
            await File.WriteAllTextAsync(seedOutputPath, seedSummary.RootElement.GetRawText());
        }

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = regeneratedOutputPath,
                        SourceSummaryPath = seedOutputPath,
                        AssemblyPaths = new[]
                        {
                            fixture.AssemblyPath
                        },
                        IncludeCallees = true,
                        IncludePurityClassification = true
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var regeneratedSummary = JsonDocument.Parse(await File.ReadAllTextAsync(regeneratedOutputPath));
        var operatorEntries = regeneratedSummary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Where(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "ConversionFixture.op_Explicit(ConversionFixture)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(operatorEntries.Length, Is.EqualTo(2));
        Assert.That(
            operatorEntries
                .Select(entry => entry.GetProperty("ExactSymbolKey").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(2),
            "Artifact-spec regeneration should preserve both exact symbol keys when SourceSummaryPath reuses a reviewed symbol set with duplicate display symbols.");
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_DeduplicatesDuplicateExactKeys()
    {
        var source = """
                     public static class DuplicateReviewedSeedFixture
                     {
                         public static int Root()
                         {
                             return Callee();
                         }

                         public static int Callee()
                         {
                             return 1;
                         }
                     }
                     """;

        await using var fixture =
            await CreateFixtureAssemblyAsync("EffectSummaryArtifactSpecDuplicateExactKey", source);

        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-source-duplicate-exact-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var regeneratedOutputPath = Path.Combine(workingDirectory, "regenerated.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        var duplicateSeedJson = JsonSerializer.Serialize(
            new
            {
                Assemblies = new object[]
                {
                    new
                    {
                        Methods = new object[]
                        {
                            new
                            {
                                Symbol = "DuplicateReviewedSeedFixture.Root()",
                                ExactSymbolKey = "DuplicateReviewedSeedFixture.Root()->int",
                                Calls = new[]
                                {
                                    "DuplicateReviewedSeedFixture.Callee()->int"
                                }
                            },
                            new
                            {
                                Symbol = "DuplicateReviewedSeedFixture.Root()",
                                ExactSymbolKey = "DuplicateReviewedSeedFixture.Root()->int",
                                Calls = Array.Empty<string>()
                            },
                            new
                            {
                                Symbol = "DuplicateReviewedSeedFixture.Callee()",
                                ExactSymbolKey = "DuplicateReviewedSeedFixture.Callee()->int",
                                Calls = Array.Empty<string>()
                            }
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(seedOutputPath, duplicateSeedJson);

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = regeneratedOutputPath,
                        SourceSummaryPath = seedOutputPath,
                        AssemblyPaths = new[]
                        {
                            fixture.AssemblyPath
                        },
                        SymbolPrefixes = new[]
                        {
                            "DuplicateReviewedSeedFixture.Root"
                        },
                        IncludeCallees = true,
                        IncludePurityClassification = true,
                        Limit = 10
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var regeneratedSummary = JsonDocument.Parse(await File.ReadAllTextAsync(regeneratedOutputPath));
        var generatedSymbols = regeneratedSummary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("DuplicateReviewedSeedFixture.Root()"));
        Assert.That(generatedSymbols, Does.Contain("DuplicateReviewedSeedFixture.Callee()"));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_ExcludesReflectionSensitiveReviewedMembers()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-source-exclusions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var metadataSeedOutputPath = Path.Combine(workingDirectory, "metadata-seed.SharpProof.EffectSummary.json");
        var metadataOutputPath = Path.Combine(workingDirectory, "metadata-regenerated.SharpProof.EffectSummary.json");
        var environmentSeedOutputPath =
            Path.Combine(workingDirectory, "environment-seed.SharpProof.EffectSummary.json");
        var environmentOutputPath =
            Path.Combine(workingDirectory, "environment-regenerated.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        await RunEffectSummaryToolAsync(
            "--framework",
            "net8.0",
            "--runtime-assembly",
            "System.Private.CoreLib.dll",
            "--symbol-prefix",
            "System.Exception.get_Message",
            "--symbol-prefix",
            "System.Object.GetType",
            "--symbol-prefix",
            "System.Type.ToString",
            "--include-callees",
            "--classify-purity",
            "--compare-manual-catalogs",
            "--limit",
            "80",
            "--output",
            metadataSeedOutputPath);

        await RunEffectSummaryToolAsync(
            "--framework",
            "net8.0",
            "--runtime-assembly",
            "System.Private.CoreLib.dll",
            "--symbol-prefix",
            "System.Environment.get_CommandLine",
            "--symbol-prefix",
            "System.Environment.get_Version",
            "--include-callees",
            "--classify-purity",
            "--compare-manual-catalogs",
            "--limit",
            "24",
            "--output",
            environmentSeedOutputPath);

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Private.CoreLib.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true,
                    ExcludedSymbolPrefixes = new[]
                    {
                        "System.Reflection.MemberInfo.get_Name",
                        "System.Type.GetTypeFromHandle",
                        "System.Type.ToString"
                    }
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = metadataOutputPath,
                        SourceSummaryPath = metadataSeedOutputPath,
                        Limit = 80,
                        SymbolPrefixes = new[]
                        {
                            "System.Exception.get_Message",
                            "System.Object.GetType"
                        }
                    },
                    new
                    {
                        OutputPath = environmentOutputPath,
                        SourceSummaryPath = environmentSeedOutputPath,
                        Limit = 24,
                        SymbolPrefixes = new[]
                        {
                            "System.Environment.get_CommandLine",
                            "System.Environment.get_Version"
                        }
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var metadataSummary = JsonDocument.Parse(await File.ReadAllTextAsync(metadataOutputPath));
        var metadataGeneratedSymbols = metadataSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(metadataGeneratedSymbols, Does.Contain("System.Exception.get_Message()"));
        Assert.That(metadataGeneratedSymbols, Does.Contain("System.Object.GetType()"));
        Assert.That(metadataGeneratedSymbols, Does.Not.Contain("System.Reflection.MemberInfo.get_Name()"));
        Assert.That(metadataGeneratedSymbols, Does.Not.Contain("System.Type.ToString()"));

        using var environmentSummary = JsonDocument.Parse(await File.ReadAllTextAsync(environmentOutputPath));
        var environmentGeneratedSymbols = environmentSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(environmentGeneratedSymbols, Does.Contain("System.Environment.get_CommandLine()"));
        Assert.That(environmentGeneratedSymbols, Does.Contain("System.Environment.get_Version()"));
        Assert.That(environmentGeneratedSymbols,
            Does.Not.Contain("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)"));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_Classifies_DirectoryCurrentDirectory_AsImpure()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-directory-current-directory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var outputPath = Path.Combine(workingDirectory, "Directory.CurrentDirectory.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        await GenerateReviewedSourceSummaryAsync(
            seedOutputPath,
            "System.Private.CoreLib.dll",
            80,
            "System.IO.Directory.GetCurrentDirectory",
            "System.IO.Directory.SetCurrentDirectory");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Private.CoreLib.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        SourceSummaryPath = seedOutputPath,
                        SymbolPrefixes = new[]
                        {
                            "System.IO.Directory.GetCurrentDirectory",
                            "System.IO.Directory.SetCurrentDirectory"
                        },
                        Limit = 80
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");

        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.Directory.GetCurrentDirectory()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.GetCurrentDirectory()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Directory.SetCurrentDirectory(string)", "impure",
            "global_state_write");
        AssertPrimaryCategory(summary, "System.IO.Directory.SetCurrentDirectory(string)", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.SetCurrentDirectory(string)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.IO.Directory.GetCurrentDirectory()"));
        Assert.That(generatedSymbols, Does.Contain("System.IO.Directory.SetCurrentDirectory(string)"));
        Assert.That(
            generatedSymbols.Any(symbol =>
                string.Equals(symbol, "System.Environment.GetEnvironmentVariables()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetEnvironmentVariablesFromRegistry(bool)",
                    StringComparison.Ordinal)),
            Is.False,
            "Directory.CurrentDirectory regeneration should not import unrelated Environment variable helpers.");

        var directorySymbols = generatedSymbols
            .Where(symbol =>
                string.Equals(symbol, "System.IO.Directory.GetCurrentDirectory()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Directory.SetCurrentDirectory(string)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(directorySymbols, Is.EqualTo(new[]
        {
            "System.IO.Directory.GetCurrentDirectory()",
            "System.IO.Directory.SetCurrentDirectory(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_ResolvesRelativePathsAgainstSpecDirectory()
    {
        var workingRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-artifact-spec-relative-" + Guid.NewGuid().ToString("N"));
        var specDirectory = Path.Combine(workingRoot, "spec");
        var invocationDirectory = Path.Combine(workingRoot, "invoke");
        var outputDirectory = Path.Combine(specDirectory, "out");
        Directory.CreateDirectory(specDirectory);
        Directory.CreateDirectory(invocationDirectory);
        Directory.CreateDirectory(outputDirectory);

        var seedOutputPath = Path.Combine(specDirectory, "seed.SharpProof.EffectSummary.json");
        var runtimeAssemblyPath = typeof(string).Assembly.Location;

        await GenerateReviewedSourceSummaryAsync(
            seedOutputPath,
            "System.Private.CoreLib.dll",
            80,
            "System.IO.Directory.GetCurrentDirectory",
            "System.IO.Directory.SetCurrentDirectory");

        var outputPath = Path.Combine(outputDirectory, "Directory.CurrentDirectory.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(specDirectory, "artifact-spec.json");
        var relativeOutputPath = Path.GetRelativePath(specDirectory, outputPath);
        var relativeSeedPath = Path.GetRelativePath(specDirectory, seedOutputPath);
        var relativeAssemblyPath = Path.GetRelativePath(specDirectory, runtimeAssemblyPath);

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = relativeOutputPath,
                        SourceSummaryPath = relativeSeedPath,
                        AssemblyPaths = new[] { relativeAssemblyPath },
                        SymbolPrefixes = new[]
                        {
                            "System.IO.Directory.GetCurrentDirectory",
                            "System.IO.Directory.SetCurrentDirectory"
                        },
                        Limit = 80
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsyncWithWorkingDirectory(invocationDirectory, "--artifact-spec", artifactSpecPath);

        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(File.Exists(Path.Combine(invocationDirectory, relativeOutputPath)), Is.False);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        AssertPurityClassification(summary, "System.IO.Directory.GetCurrentDirectory()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.GetCurrentDirectory()", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Directory.SetCurrentDirectory(string)", "impure",
            "global_state_write");
        AssertPrimaryCategory(summary, "System.IO.Directory.SetCurrentDirectory(string)", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.SetCurrentDirectory(string)",
            "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAppContextSlice_UsesGeneratedPurityAndImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            100,
            "System.AppContext.get_TargetFrameworkName",
            "System.AppContext.get_BaseDirectory",
            "System.AppContext.GetData",
            "System.AppContext.SetData",
            "System.AppContext.TryGetSwitch",
            "System.AppContext.SetSwitch");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.AppContext.get_TargetFrameworkName()", "impure",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.AppContext.get_TargetFrameworkName()", "caller_visible");
        AssertPurityClassification(summary, "System.AppContext.get_BaseDirectory()", "impure", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.AppContext.get_BaseDirectory()", "caller_visible");
        AssertPurityClassification(summary, "System.AppContext.GetData(string)", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.AppContext.GetData(string)", "caller_visible");
        AssertPurityClassification(summary, "System.AppContext.SetData(string, object)", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.AppContext.SetData(string, object)", "caller_visible");
        AssertPurityClassification(summary, "System.AppContext.TryGetSwitch(string, ref bool)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.AppContext.TryGetSwitch(string, ref bool)",
            "caller_visible");
        AssertPurityClassification(summary, "System.AppContext.SetSwitch(string, bool)", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.AppContext.SetSwitch(string, bool)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.AppContext", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.AppContext.GetBaseDirectoryCore()",
            "System.AppContext.GetData(string)",
            "System.AppContext.SetData(string, object)",
            "System.AppContext.SetSwitch(string, bool)",
            "System.AppContext.TryGetSwitch(string, ref bool)",
            "System.AppContext.get_BaseDirectory()",
            "System.AppContext.get_TargetFrameworkName()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAppDomainSlice_UsesGeneratedPurityAndImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            60,
            "System.AppDomain.get_CurrentDomain",
            "System.AppDomain.get_Id",
            "System.AppDomain.get_BaseDirectory",
            "System.AppContext.get_BaseDirectory",
            "System.AppContext.GetData");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.AppDomain.get_Id()", "pure");
        AssertEffectVisibilityClassification(summary, "System.AppDomain.get_Id()", "none");
        AssertPurityClassification(summary, "System.AppDomain.get_CurrentDomain()", "pure");
        AssertEffectVisibilityClassification(summary, "System.AppDomain.get_CurrentDomain()", "internal_only");
        AssertPurityClassification(summary, "System.AppDomain.get_BaseDirectory()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.AppDomain.get_BaseDirectory()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.AppDomain.get_CurrentDomain()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.AppDomain.get_BaseDirectory()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.AppDomain.get_Id()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.AppDomain.get_BaseDirectory()",
            "System.AppDomain.get_CurrentDomain()",
            "System.AppDomain.get_Id()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAppDomainFriendlyNameSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.AppDomain.get_FriendlyName");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.AppDomain.get_FriendlyName()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.AppDomain.get_FriendlyName()", "caller_visible");
        AssertPurityClassification(summary, "System.Reflection.Assembly.GetEntryAssembly()", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Reflection.Assembly.GetEntryAssembly()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.AppDomain.get_FriendlyName()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.AppDomain.get_FriendlyName()" }));
        Assert.That(
            FindMethodsByPrefix(summary, "System.Reflection.Assembly.GetEntryAssembly(")
                .Select(method => method.GetProperty("DisplayName").GetString())
                .ToArray(),
            Is.EqualTo(new[] { "System.Reflection.Assembly.GetEntryAssembly()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeReflectionPathMetadataSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            24,
            "System.Reflection.Assembly.get_Location",
            "System.Reflection.Module.get_FullyQualifiedName",
            "System.Reflection.Module.get_Name",
            "System.Reflection.Module.get_ScopeName");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Reflection.Assembly.get_Location()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Reflection.Assembly.get_Location()", "caller_visible");
        AssertPurityClassification(summary, "System.Reflection.Module.get_FullyQualifiedName()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Reflection.Module.get_FullyQualifiedName()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Reflection.Module.get_Name()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Reflection.Module.get_Name()", "caller_visible");
        AssertPurityClassification(summary, "System.Reflection.Module.get_ScopeName()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Reflection.Module.get_ScopeName()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Reflection.", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Reflection.Assembly.get_Location()",
            "System.Reflection.Module.get_FullyQualifiedName()",
            "System.Reflection.Module.get_Name()",
            "System.Reflection.Module.get_ScopeName()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStopwatchGetTimestampSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.Diagnostics.Stopwatch.GetTimestamp");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.GetTimestamp()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.GetTimestamp()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceCounter()",
            "conservative_unknown", "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceCounter()",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Diagnostics.Stopwatch.GetTimestamp()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.Diagnostics.Stopwatch.GetTimestamp()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStopwatchStateSlice_UsesGeneratedEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            16,
            "System.Diagnostics.Stopwatch..ctor",
            "System.Diagnostics.Stopwatch.get_Elapsed",
            "System.Diagnostics.Stopwatch.get_ElapsedMilliseconds",
            "System.Diagnostics.Stopwatch.get_ElapsedTicks",
            "System.Diagnostics.Stopwatch.get_IsRunning",
            "System.Diagnostics.Stopwatch.GetTimestamp",
            "System.Diagnostics.Stopwatch.Start",
            "System.Diagnostics.Stopwatch.Stop");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch..ctor()", "pure");
        AssertFreshnessClassification(summary, "System.Diagnostics.Stopwatch..ctor()", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch..ctor()", "internal_only");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.get_Elapsed()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.get_Elapsed()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.get_ElapsedMilliseconds()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.get_ElapsedMilliseconds()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.get_ElapsedTicks()", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.get_ElapsedTicks()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.get_IsRunning()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.get_IsRunning()", "none");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.Reset()", "impure", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.Reset()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.Start()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.Start()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.Stop()", "impure", "impure_callee",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.Stop()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.GetTimestamp()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.GetTimestamp()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceCounter()",
            "conservative_unknown", "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceCounter()",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Diagnostics.Stopwatch..ctor()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.get_Elapsed()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.get_ElapsedMilliseconds()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.get_ElapsedTicks()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.get_IsRunning()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.GetTimestamp()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.Start()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.Stop()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Diagnostics.Stopwatch..ctor()",
            "System.Diagnostics.Stopwatch.GetTimestamp()",
            "System.Diagnostics.Stopwatch.Start()",
            "System.Diagnostics.Stopwatch.Stop()",
            "System.Diagnostics.Stopwatch.get_Elapsed()",
            "System.Diagnostics.Stopwatch.get_ElapsedMilliseconds()",
            "System.Diagnostics.Stopwatch.get_ElapsedTicks()",
            "System.Diagnostics.Stopwatch.get_IsRunning()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStopwatchStaticConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            12,
            "System.Diagnostics.Stopwatch..cctor");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch..cctor()", "impure", "global_state_read",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch..cctor()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceFrequency()",
            "conservative_unknown", "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Stopwatch.QueryPerformanceFrequency()",
            "unknown");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Diagnostics.Stopwatch..cctor()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Stopwatch.QueryPerformanceFrequency()",
                    StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Diagnostics.Stopwatch..cctor()",
            "System.Diagnostics.Stopwatch.QueryPerformanceFrequency()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeOperatingSystemSlice_UsesGeneratedCurrentRuntimeEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            1,
            false,
            "System.OperatingSystem.Is",
            "System.OperatingSystem.get_Platform");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.OperatingSystem.IsWindows()", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsWindows()", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsLinux()", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsLinux()", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsAndroid()", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsAndroid()", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsMacOS()", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsMacOS()", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsOSPlatform(string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsOSPlatform(string)", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)", "none");
        AssertPurityClassification(summary, "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)",
            "none");
        AssertPurityClassification(summary,
            "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.OperatingSystem.IsOSVersionAtLeast(int, int, int, int)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.IsOSVersionAtLeast(int, int, int, int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)",
            "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.OperatingSystem.get_Platform()", "pure");
        AssertEffectVisibilityClassification(summary, "System.OperatingSystem.get_Platform()", "none");

        var isOsPlatformCalls = FindMethod(summary, "System.OperatingSystem.IsOSPlatform(string)")
            .GetProperty("Calls")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.That(isOsPlatformCalls, Does.Contain("string.Equals(string, System.StringComparison)->bool"));

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.OperatingSystem.IsAndroid()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsWindows()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsLinux()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsMacOS()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsOSPlatform(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsOSVersionAtLeast(int, int, int, int)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.OperatingSystem.get_Platform()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.OperatingSystem.IsAndroid()",
            "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)",
            "System.OperatingSystem.IsLinux()",
            "System.OperatingSystem.IsMacOS()",
            "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)",
            "System.OperatingSystem.IsOSPlatform(string)",
            "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)",
            "System.OperatingSystem.IsOSVersionAtLeast(int, int, int, int)",
            "System.OperatingSystem.IsWindows()",
            "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)",
            "System.OperatingSystem.get_Platform()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentStablePureGetterSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Environment.get_NewLine",
            "System.Environment.get_HasShutdownStarted",
            "System.Environment.get_Is64BitProcess",
            "System.Environment.get_Is64BitOperatingSystem");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.get_NewLine()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_NewLine()", "none");
        AssertPurityClassification(summary, "System.Environment.get_HasShutdownStarted()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_HasShutdownStarted()", "none");
        AssertPurityClassification(summary, "System.Environment.get_Is64BitProcess()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_Is64BitProcess()", "none");
        AssertPurityClassification(summary, "System.Environment.get_Is64BitOperatingSystem()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_Is64BitOperatingSystem()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.get_NewLine()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_HasShutdownStarted()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_Is64BitProcess()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_Is64BitOperatingSystem()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.get_HasShutdownStarted()",
            "System.Environment.get_Is64BitOperatingSystem()",
            "System.Environment.get_Is64BitProcess()",
            "System.Environment.get_NewLine()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentPathStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Environment.get_CurrentDirectory",
            "System.Environment.set_CurrentDirectory",
            "System.Environment.GetFolderPath");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.get_CurrentDirectory()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_CurrentDirectory()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.set_CurrentDirectory(string)", "impure",
            "global_state_write");
        AssertPrimaryCategory(summary, "System.Environment.set_CurrentDirectory(string)", "global_state_write");
        AssertCategoriesDoNotContain(summary, "System.Environment.set_CurrentDirectory(string)", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Environment.set_CurrentDirectory(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Environment.GetFolderPath(System.Environment+SpecialFolder)",
            "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.GetFolderPath(System.Environment+SpecialFolder)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Environment.GetFolderPath(System.Environment+SpecialFolder, System.Environment+SpecialFolderOption)",
            "impure", "impure_callee", "throw");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.GetFolderPath(System.Environment+SpecialFolder, System.Environment+SpecialFolderOption)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.get_CurrentDirectory()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.set_CurrentDirectory(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetFolderPath(System.Environment+SpecialFolder)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol,
                    "System.Environment.GetFolderPath(System.Environment+SpecialFolder, System.Environment+SpecialFolderOption)",
                    StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.GetFolderPath(System.Environment+SpecialFolder)",
            "System.Environment.GetFolderPath(System.Environment+SpecialFolder, System.Environment+SpecialFolderOption)",
            "System.Environment.get_CurrentDirectory()",
            "System.Environment.set_CurrentDirectory(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentProcessStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            "System.Environment.get_MachineName",
            "System.Environment.get_OSVersion",
            "System.Environment.get_ProcessId",
            "System.Environment.get_ProcessorCount",
            "System.Environment.get_ProcessPath",
            "System.Environment.get_SystemDirectory",
            "System.Environment.get_SystemPageSize",
            "System.Environment.get_UserDomainName",
            "System.Environment.get_WorkingSet");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.get_MachineName()", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_MachineName()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_OSVersion()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_OSVersion()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_ProcessId()", "impure", "global_state_read",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_ProcessId()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.GetProcessId()", "conservative_unknown",
            "unknown_callee");
        AssertCategoriesDoNotContain(summary, "System.Environment.GetProcessId()", "global_state_read");
        AssertCategoriesDoNotContain(summary, "System.Environment.GetProcessId()", "global_state_write");
        AssertPurityClassification(summary, "System.Environment.get_ProcessorCount()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_ProcessorCount()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_ProcessPath()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_ProcessPath()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_SystemDirectory()", "impure", "throw",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_SystemDirectory()", "caller_visible");
        AssertPurityClassification(summary, "Interop+Kernel32.GetSystemDirectoryW(ref char, uint)",
            "conservative_unknown", "unknown_callee");
        AssertEffectVisibilityClassification(summary, "Interop+Kernel32.GetSystemDirectoryW(ref char, uint)",
            "unknown");
        AssertPurityClassification(summary, "System.Environment.get_SystemPageSize()", "impure", "global_state_read",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_SystemPageSize()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.GetSystemPageSize()", "conservative_unknown",
            "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.GetSystemPageSize()", "unknown");
        AssertPurityClassification(summary, "System.Environment.get_UserDomainName()", "impure", "throw",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_UserDomainName()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_WorkingSet()", "impure",
            "caller_visible_memory_write", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_WorkingSet()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.get_MachineName()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_OSVersion()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_ProcessId()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_ProcessorCount()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_ProcessPath()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_SystemDirectory()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_SystemPageSize()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_UserDomainName()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_WorkingSet()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.get_MachineName()",
            "System.Environment.get_OSVersion()",
            "System.Environment.get_ProcessId()",
            "System.Environment.get_ProcessPath()",
            "System.Environment.get_ProcessorCount()",
            "System.Environment.get_SystemDirectory()",
            "System.Environment.get_SystemPageSize()",
            "System.Environment.get_UserDomainName()",
            "System.Environment.get_WorkingSet()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeProcessSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Diagnostics.Process.dll",
            200,
            2,
            "System.Diagnostics.Process.GetCurrentProcess",
            "System.Diagnostics.Process.get_Id",
            "System.Diagnostics.Process.get_StartInfo",
            "System.Diagnostics.Process.Start",
            "System.Diagnostics.Process.GetProcessesByName",
            "System.Diagnostics.Process.get_ExitCode");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Diagnostics.Process.GetCurrentProcess()", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.GetCurrentProcess()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Process.get_Id()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.get_Id()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Process.get_StartInfo()", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.get_StartInfo()", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Process.Start(string)", "impure", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.Start(string)", "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Process.GetProcessesByName(string)", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.GetProcessesByName(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Diagnostics.Process.get_ExitCode()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Diagnostics.Process.get_ExitCode()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Diagnostics.Process.GetCurrentProcess()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Process.get_Id()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Process.get_StartInfo()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Process.Start(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Process.GetProcessesByName(string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Diagnostics.Process.get_ExitCode()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Diagnostics.Process.GetCurrentProcess()",
            "System.Diagnostics.Process.GetProcessesByName(string)",
            "System.Diagnostics.Process.Start(string)",
            "System.Diagnostics.Process.get_ExitCode()",
            "System.Diagnostics.Process.get_Id()",
            "System.Diagnostics.Process.get_StartInfo()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentCommandLineAndVersionSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            24,
            "System.Environment.get_CommandLine",
            "System.Environment.get_Version");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.get_CommandLine()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_CommandLine()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_Version()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_Version()", "caller_visible");
        AssertPurityClassification(summary, "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
            "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.get_CommandLine()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_Version()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.get_CommandLine()",
            "System.Environment.get_Version()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentAmbientLookupSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            "System.Environment.ExpandEnvironmentVariables",
            "System.Environment.GetEnvironmentVariable",
            "System.Environment.GetEnvironmentVariables",
            "System.Environment.get_UserInteractive",
            "System.Environment.get_UserName");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.GetEnvironmentVariable(string)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.GetEnvironmentVariable(string)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Environment.GetEnvironmentVariable(string, System.EnvironmentVariableTarget)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.GetEnvironmentVariable(string, System.EnvironmentVariableTarget)", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.GetEnvironmentVariables()", "impure",
            "global_state_read", "impure_callee", "throw");
        AssertEffectVisibilityClassification(summary, "System.Environment.GetEnvironmentVariables()", "caller_visible");
        AssertPurityClassification(summary,
            "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)", "impure",
            "global_state_read", "global_state_write", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.ExpandEnvironmentVariables(string)", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.ExpandEnvironmentVariables(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_UserInteractive()", "impure",
            "caller_visible_memory_write", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_UserInteractive()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_UserName()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_UserName()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.GetUserName(ref System.Text.ValueStringBuilder)",
            "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.GetUserName(ref System.Text.ValueStringBuilder)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.ExpandEnvironmentVariables(string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetEnvironmentVariable(string)", StringComparison.Ordinal) ||
                string.Equals(symbol,
                    "System.Environment.GetEnvironmentVariable(string, System.EnvironmentVariableTarget)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetEnvironmentVariables()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_UserInteractive()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Environment.get_UserName()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.ExpandEnvironmentVariables(string)",
            "System.Environment.GetEnvironmentVariable(string)",
            "System.Environment.GetEnvironmentVariable(string, System.EnvironmentVariableTarget)",
            "System.Environment.GetEnvironmentVariables()",
            "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)",
            "System.Environment.get_UserInteractive()",
            "System.Environment.get_UserName()"
        }));

        Assert.That(
            FindMethodsByPrefix(summary, "System.Environment.GetUserName(")
                .Select(method => method.GetProperty("DisplayName").GetString()).ToArray(),
            Is.EqualTo(new[] { "System.Environment.GetUserName(ref System.Text.ValueStringBuilder)" }),
            "Including callees should keep the runtime helper in the emitted slice.");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentVariableMutationSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            2,
            "System.Environment.SetEnvironmentVariable");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.SetEnvironmentVariable(string, string)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.SetEnvironmentVariable(string, string)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Environment.SetEnvironmentVariable(string, string, System.EnvironmentVariableTarget)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Environment.SetEnvironmentVariable(string, string, System.EnvironmentVariableTarget)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.SetEnvironmentVariable(string, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol,
                    "System.Environment.SetEnvironmentVariable(string, string, System.EnvironmentVariableTarget)",
                    StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Environment.SetEnvironmentVariable(string, string)",
            "System.Environment.SetEnvironmentVariable(string, string, System.EnvironmentVariableTarget)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeEnvironmentVolatileStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            2,
            "System.Environment.get_CurrentManagedThreadId",
            "System.Environment.get_ExitCode",
            "System.Environment.Exit",
            "System.Environment.get_TickCount",
            "System.Environment.get_TickCount64",
            "System.Environment.get_StackTrace",
            "System.Threading.Thread.get_CurrentThread");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Environment.get_CurrentManagedThreadId()", "impure",
            "metadata_only_or_external");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_CurrentManagedThreadId()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_ExitCode()", "impure", "metadata_only_or_external");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_ExitCode()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.Exit(int)", "impure", "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.Exit(int)", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_TickCount()", "impure",
            "metadata_only_or_external");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_TickCount()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_TickCount64()", "impure",
            "metadata_only_or_external");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_TickCount64()", "caller_visible");
        AssertPurityClassification(summary, "System.Environment.get_StackTrace()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Environment.get_StackTrace()", "caller_visible");
        AssertPurityClassification(summary, "System.Threading.Thread.get_CurrentThread()", "impure",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Threading.Thread.get_CurrentThread()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Environment.get_StackTrace()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Threading.Thread.get_CurrentThread()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            generatedSymbols,
            Is.EqualTo(new[] { "System.Environment.get_StackTrace()", "System.Threading.Thread.get_CurrentThread()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCultureAndRegionStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            2,
            "System.Globalization.CultureInfo.get_CurrentCulture",
            "System.Globalization.CultureInfo.get_CurrentUICulture",
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentCulture",
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentUICulture",
            "System.Globalization.DateTimeFormatInfo.get_CurrentInfo",
            "System.Globalization.CultureInfo.get_InstalledUICulture",
            "System.Globalization.NumberFormatInfo.get_CurrentInfo",
            "System.Globalization.RegionInfo.get_CurrentRegion");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_CurrentCulture()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.get_CurrentCulture()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_CurrentUICulture()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.get_CurrentUICulture()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_DefaultThreadCurrentCulture()",
            "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary,
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentCulture()", "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_DefaultThreadCurrentUICulture()",
            "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary,
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentUICulture()", "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.DateTimeFormatInfo.get_CurrentInfo()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Globalization.DateTimeFormatInfo.get_CurrentInfo()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_InstalledUICulture()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.get_InstalledUICulture()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.NumberFormatInfo.get_CurrentInfo()", "impure",
            "global_state_read", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Globalization.NumberFormatInfo.get_CurrentInfo()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Globalization.RegionInfo.get_CurrentRegion()", "impure",
            "global_state_read", "global_state_write", "impure_callee", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Globalization.RegionInfo.get_CurrentRegion()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Globalization.CultureInfo.get_CurrentCulture()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.CultureInfo.get_CurrentUICulture()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.CultureInfo.get_DefaultThreadCurrentCulture()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.CultureInfo.get_DefaultThreadCurrentUICulture()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.DateTimeFormatInfo.get_CurrentInfo()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.CultureInfo.get_InstalledUICulture()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.NumberFormatInfo.get_CurrentInfo()",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Globalization.RegionInfo.get_CurrentRegion()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Globalization.CultureInfo.get_CurrentCulture()",
            "System.Globalization.CultureInfo.get_CurrentUICulture()",
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentCulture()",
            "System.Globalization.CultureInfo.get_DefaultThreadCurrentUICulture()",
            "System.Globalization.CultureInfo.get_InstalledUICulture()",
            "System.Globalization.DateTimeFormatInfo.get_CurrentInfo()",
            "System.Globalization.NumberFormatInfo.get_CurrentInfo()",
            "System.Globalization.RegionInfo.get_CurrentRegion()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCultureInfoGetCultureInfoSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            1,
            "System.Globalization.CultureInfo.GetCultureInfo(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Globalization.CultureInfo.GetCultureInfo(string)", "impure",
            "impure_callee", "object_state_write", "throw");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.GetCultureInfo(string)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Globalization.CultureInfo.GetCultureInfo(string)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Globalization.CultureInfo.GetCultureInfo(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeCultureInfoNameSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            1,
            "System.Globalization.CultureInfo.get_Name");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Globalization.CultureInfo.get_Name()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Globalization.CultureInfo.get_Name()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Globalization.CultureInfo.get_Name()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Globalization.CultureInfo.get_Name()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConsoleStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Console.dll",
            120,
            2,
            "System.Console.ReadLine()",
            "System.Console.get_Error",
            "System.Console.get_In",
            "System.Console.get_InputEncoding",
            "System.Console.get_IsErrorRedirected",
            "System.Console.get_IsInputRedirected",
            "System.Console.get_IsOutputRedirected",
            "System.Console.get_Out",
            "System.Console.get_OutputEncoding",
            "System.Console.OpenStandardError()",
            "System.Console.OpenStandardInput()",
            "System.Console.OpenStandardOutput()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Console.ReadLine()", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.ReadLine()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_Error()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_Error()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_In()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_In()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_InputEncoding()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_InputEncoding()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_IsErrorRedirected()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_IsErrorRedirected()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_IsInputRedirected()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_IsInputRedirected()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_IsOutputRedirected()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_IsOutputRedirected()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_Out()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_Out()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_OutputEncoding()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_OutputEncoding()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.OpenStandardError()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.OpenStandardError()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.OpenStandardInput()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.OpenStandardInput()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.OpenStandardOutput()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.OpenStandardOutput()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Console.ReadLine()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_Error()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_In()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_InputEncoding()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_IsErrorRedirected()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_IsInputRedirected()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_IsOutputRedirected()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_Out()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_OutputEncoding()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.OpenStandardError()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.OpenStandardInput()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.OpenStandardOutput()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Console.OpenStandardError()",
            "System.Console.OpenStandardInput()",
            "System.Console.OpenStandardOutput()",
            "System.Console.ReadLine()",
            "System.Console.get_Error()",
            "System.Console.get_In()",
            "System.Console.get_InputEncoding()",
            "System.Console.get_IsErrorRedirected()",
            "System.Console.get_IsInputRedirected()",
            "System.Console.get_IsOutputRedirected()",
            "System.Console.get_Out()",
            "System.Console.get_OutputEncoding()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConsoleObservableSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Console.dll",
            180,
            2,
            "System.Console.get_BackgroundColor",
            "System.Console.get_BufferHeight",
            "System.Console.get_BufferWidth",
            "System.Console.get_CapsLock",
            "System.Console.get_CursorLeft",
            "System.Console.get_CursorSize",
            "System.Console.get_CursorTop",
            "System.Console.get_CursorVisible",
            "System.Console.get_ForegroundColor",
            "System.Console.get_LargestWindowHeight",
            "System.Console.get_LargestWindowWidth",
            "System.Console.get_NumberLock",
            "System.Console.get_Title",
            "System.Console.get_TreatControlCAsInput",
            "System.Console.get_WindowHeight",
            "System.Console.get_WindowLeft",
            "System.Console.get_WindowTop",
            "System.Console.get_WindowWidth");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Console.get_BackgroundColor()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_BackgroundColor()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_BufferHeight()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_BufferHeight()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_BufferWidth()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_BufferWidth()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_CapsLock()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_CapsLock()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_CursorLeft()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_CursorLeft()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_CursorSize()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_CursorSize()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_CursorTop()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_CursorTop()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_CursorVisible()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_CursorVisible()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_ForegroundColor()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_ForegroundColor()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_LargestWindowHeight()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_LargestWindowHeight()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_LargestWindowWidth()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_LargestWindowWidth()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_NumberLock()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_NumberLock()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_Title()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_Title()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_TreatControlCAsInput()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_TreatControlCAsInput()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_WindowHeight()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_WindowHeight()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_WindowLeft()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_WindowLeft()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_WindowTop()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_WindowTop()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_WindowWidth()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_WindowWidth()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Console.get_BackgroundColor()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_BufferHeight()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_BufferWidth()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_CapsLock()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_CursorLeft()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_CursorSize()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_CursorTop()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_CursorVisible()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_ForegroundColor()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_LargestWindowHeight()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_LargestWindowWidth()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_NumberLock()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_Title()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_TreatControlCAsInput()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_WindowHeight()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_WindowLeft()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_WindowTop()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_WindowWidth()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Console.get_BackgroundColor()",
            "System.Console.get_BufferHeight()",
            "System.Console.get_BufferWidth()",
            "System.Console.get_CapsLock()",
            "System.Console.get_CursorLeft()",
            "System.Console.get_CursorSize()",
            "System.Console.get_CursorTop()",
            "System.Console.get_CursorVisible()",
            "System.Console.get_ForegroundColor()",
            "System.Console.get_LargestWindowHeight()",
            "System.Console.get_LargestWindowWidth()",
            "System.Console.get_NumberLock()",
            "System.Console.get_Title()",
            "System.Console.get_TreatControlCAsInput()",
            "System.Console.get_WindowHeight()",
            "System.Console.get_WindowLeft()",
            "System.Console.get_WindowTop()",
            "System.Console.get_WindowWidth()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConsoleOutputSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Console.dll",
            120,
            2,
            "System.Console.SetError(System.IO.TextWriter)",
            "System.Console.SetOut(System.IO.TextWriter)",
            "System.Console.Write(",
            "System.Console.WriteLine(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Console.SetError(System.IO.TextWriter)", "impure",
            "global_state_read", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Console.SetError(System.IO.TextWriter)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Console.SetOut(System.IO.TextWriter)", "impure",
            "global_state_read", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Console.SetOut(System.IO.TextWriter)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.Write(object)", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.Write(object)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.Write(string)", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.Write(string)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.WriteLine()", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.WriteLine()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.WriteLine(object)", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.WriteLine(object)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.WriteLine(string)", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.WriteLine(string)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.WriteLine(int)", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.WriteLine(int)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Console.SetError(System.IO.TextWriter)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.SetOut(System.IO.TextWriter)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(bool)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(char)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(char[])", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(char[], int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(decimal)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(double)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(float)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(long)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(string, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(string, object, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(string, object, object, object)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(string, object[])", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(uint)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Write(ulong)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(bool)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(char)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(char[])", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(char[], int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(decimal)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(double)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(float)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(long)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(string, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(string, object, object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(string, object, object, object)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(string, object[])", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(uint)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.WriteLine(ulong)", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Console.SetError(System.IO.TextWriter)",
            "System.Console.SetOut(System.IO.TextWriter)",
            "System.Console.Write(bool)",
            "System.Console.Write(char)",
            "System.Console.Write(char[])",
            "System.Console.Write(char[], int, int)",
            "System.Console.Write(decimal)",
            "System.Console.Write(double)",
            "System.Console.Write(float)",
            "System.Console.Write(int)",
            "System.Console.Write(long)",
            "System.Console.Write(object)",
            "System.Console.Write(string)",
            "System.Console.Write(string, object)",
            "System.Console.Write(string, object, object)",
            "System.Console.Write(string, object, object, object)",
            "System.Console.Write(string, object[])",
            "System.Console.Write(uint)",
            "System.Console.Write(ulong)",
            "System.Console.WriteLine()",
            "System.Console.WriteLine(bool)",
            "System.Console.WriteLine(char)",
            "System.Console.WriteLine(char[])",
            "System.Console.WriteLine(char[], int, int)",
            "System.Console.WriteLine(decimal)",
            "System.Console.WriteLine(double)",
            "System.Console.WriteLine(float)",
            "System.Console.WriteLine(int)",
            "System.Console.WriteLine(long)",
            "System.Console.WriteLine(object)",
            "System.Console.WriteLine(string)",
            "System.Console.WriteLine(string, object)",
            "System.Console.WriteLine(string, object, object)",
            "System.Console.WriteLine(string, object, object, object)",
            "System.Console.WriteLine(string, object[])",
            "System.Console.WriteLine(uint)",
            "System.Console.WriteLine(ulong)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConsoleControlSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Console.dll",
            160,
            2,
            "System.Console.Beep",
            "System.Console.Clear",
            "System.Console.ReadKey",
            "System.Console.SetCursorPosition",
            "System.Console.SetIn",
            "System.Console.get_KeyAvailable",
            "System.Console.set_BufferHeight",
            "System.Console.set_Title");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Console.Beep()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Console.Beep()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.Clear()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Console.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.ReadKey()", "impure", "catalog_hit");
        AssertEffectVisibilityClassification(summary, "System.Console.ReadKey()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.SetCursorPosition(int, int)", "impure", "impure_callee",
            "throw");
        AssertEffectVisibilityClassification(summary, "System.Console.SetCursorPosition(int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.SetIn(System.IO.TextReader)", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.SetIn(System.IO.TextReader)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.get_KeyAvailable()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Console.get_KeyAvailable()", "caller_visible");
        AssertPurityClassification(summary, "System.Console.set_BufferHeight(int)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Console.set_BufferHeight(int)", "caller_visible");
        AssertPurityClassification(summary, "System.Console.set_Title(string)", "impure", "impure_callee", "throw");
        AssertEffectVisibilityClassification(summary, "System.Console.set_Title(string)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Console.Beep()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Beep(int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.Clear()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.ReadKey()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.ReadKey(bool)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.SetCursorPosition(int, int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.SetIn(System.IO.TextReader)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.get_KeyAvailable()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.set_BufferHeight(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Console.set_Title(string)", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Console.Beep()",
            "System.Console.Beep(int, int)",
            "System.Console.Clear()",
            "System.Console.ReadKey()",
            "System.Console.ReadKey(bool)",
            "System.Console.SetCursorPosition(int, int)",
            "System.Console.SetIn(System.IO.TextReader)",
            "System.Console.get_KeyAvailable()",
            "System.Console.set_BufferHeight(int)",
            "System.Console.set_Title(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeObjectTypeMetadataSlice_UsesGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Object.GetType",
            "System.Type.get_DeclaringMethod",
            "System.Type.get_DeclaringType",
            "System.Type.get_IsContextful",
            "System.Type.get_IsGenericType",
            "System.Type.get_IsGenericTypeDefinition",
            "System.Type.get_IsGenericParameter",
            "System.Type.get_IsMarshalByRef",
            "System.Type.get_MemberType",
            "System.Type.get_ReflectedType");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Object.GetType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Object.GetType()", "none");
        AssertPurityClassification(summary, "System.Type.get_DeclaringMethod()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_DeclaringMethod()", "none");
        AssertPurityClassification(summary, "System.Type.get_DeclaringType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_DeclaringType()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsContextful()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsContextful()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsGenericType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsGenericType()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsGenericTypeDefinition()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsGenericTypeDefinition()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsGenericParameter()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsGenericParameter()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsMarshalByRef()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsMarshalByRef()", "none");
        AssertPurityClassification(summary, "System.Type.get_MemberType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_MemberType()", "none");
        AssertPurityClassification(summary, "System.Type.get_ReflectedType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_ReflectedType()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Object.GetType()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_DeclaringMethod()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_DeclaringType()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsContextful()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsGenericType()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsGenericTypeDefinition()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsGenericParameter()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsMarshalByRef()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_MemberType()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_ReflectedType()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Object.GetType()",
            "System.Type.get_DeclaringMethod()",
            "System.Type.get_DeclaringType()",
            "System.Type.get_IsContextful()",
            "System.Type.get_IsGenericParameter()",
            "System.Type.get_IsGenericType()",
            "System.Type.get_IsGenericTypeDefinition()",
            "System.Type.get_IsMarshalByRef()",
            "System.Type.get_MemberType()",
            "System.Type.get_ReflectedType()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTypeIsConstructedGenericType_UsesGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            10,
            "System.RuntimeType.get_IsConstructedGenericType");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.RuntimeType.get_IsConstructedGenericType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.RuntimeType.get_IsConstructedGenericType()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.RuntimeType.get_IsConstructedGenericType()",
                StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.RuntimeType.get_IsConstructedGenericType()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTypeScalarMetadataSlice_UsesGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Type.get_IsAbstract",
            "System.Type.get_IsArray",
            "System.Type.get_Attributes",
            "System.Type.get_IsClass",
            "System.Type.get_IsInterface",
            "System.Type.get_IsSealed",
            "System.Type.get_IsValueType");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Type.get_IsAbstract()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsAbstract()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsArray()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsArray()", "none");
        AssertPurityClassification(summary, "System.Type.get_Attributes()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_Attributes()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsClass()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsClass()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsInterface()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsInterface()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsSealed()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsSealed()", "none");
        AssertPurityClassification(summary, "System.Type.get_IsValueType()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.get_IsValueType()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Type.get_IsAbstract()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsArray()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_Attributes()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsClass()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsInterface()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsSealed()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.get_IsValueType()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(new[]
        {
            "System.Type.get_IsAbstract()",
            "System.Type.get_IsArray()",
            "System.Type.get_Attributes()",
            "System.Type.get_IsClass()",
            "System.Type.get_IsInterface()",
            "System.Type.get_IsSealed()",
            "System.Type.get_IsValueType()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAdditionalTypeScalarMetadataSlice_UsesGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Type.get_IsAnsiClass",
            "System.Type.get_IsAutoClass",
            "System.Type.get_IsAutoLayout",
            "System.Type.get_IsByRef",
            "System.Type.get_IsCOMObject",
            "System.Type.get_IsExplicitLayout",
            "System.Type.get_IsImport",
            "System.Type.get_IsLayoutSequential",
            "System.Type.get_IsNested",
            "System.Type.get_IsNestedAssembly",
            "System.Type.get_IsNestedFamANDAssem",
            "System.Type.get_IsNestedFamORAssem",
            "System.Type.get_IsNestedFamily",
            "System.Type.get_IsNestedPrivate",
            "System.Type.get_IsNestedPublic",
            "System.Type.get_IsNotPublic",
            "System.Type.get_IsPointer",
            "System.Type.get_IsPrimitive",
            "System.Type.get_IsPublic",
            "System.Type.get_IsSpecialName",
            "System.Type.get_IsUnicodeClass");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var symbols = new[]
        {
            "System.Type.get_IsAnsiClass()",
            "System.Type.get_IsAutoClass()",
            "System.Type.get_IsAutoLayout()",
            "System.Type.get_IsByRef()",
            "System.Type.get_IsCOMObject()",
            "System.Type.get_IsExplicitLayout()",
            "System.Type.get_IsImport()",
            "System.Type.get_IsLayoutSequential()",
            "System.Type.get_IsNested()",
            "System.Type.get_IsNestedAssembly()",
            "System.Type.get_IsNestedFamANDAssem()",
            "System.Type.get_IsNestedFamORAssem()",
            "System.Type.get_IsNestedFamily()",
            "System.Type.get_IsNestedPrivate()",
            "System.Type.get_IsNestedPublic()",
            "System.Type.get_IsNotPublic()",
            "System.Type.get_IsPointer()",
            "System.Type.get_IsPrimitive()",
            "System.Type.get_IsPublic()",
            "System.Type.get_IsSpecialName()",
            "System.Type.get_IsUnicodeClass()"
        };

        foreach (var symbol in symbols)
        {
            AssertPurityClassification(summary, symbol, "pure");
            AssertEffectVisibilityClassification(summary, symbol, "none");
        }

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbols.Contains(symbol, StringComparer.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray()));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTypeIdentitySlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            1,
            false,
            "System.Type.GetTypeFromHandle",
            "System.Type.Equals",
            "System.Type.GetHashCode");

        AssertPurityClassification(summary, "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
            "none");
        AssertPurityClassification(summary, "System.Type.Equals(System.Type)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.Equals(System.Type)", "none");
        AssertPurityClassification(summary, "System.Type.Equals(object)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.Equals(object)", "none");
        AssertPurityClassification(summary, "System.Type.GetHashCode()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Type.GetHashCode()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.Equals(System.Type)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.Equals(object)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Type.GetHashCode()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Type.Equals(System.Type)",
            "System.Type.Equals(object)",
            "System.Type.GetHashCode()",
            "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeRuntimeTypeMetadataSlice_UsesGeneratedPurityEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.RuntimeType.get_IsEnum",
            "System.RuntimeType.get_ContainsGenericParameters");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.RuntimeType.get_IsEnum()", "pure");
        AssertEffectVisibilityClassification(summary, "System.RuntimeType.get_IsEnum()", "none");
        AssertPurityClassification(summary, "System.RuntimeType.get_ContainsGenericParameters()", "pure");
        AssertEffectVisibilityClassification(summary, "System.RuntimeType.get_ContainsGenericParameters()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.RuntimeType.get_IsEnum()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.RuntimeType.get_ContainsGenericParameters()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.RuntimeType.get_ContainsGenericParameters()",
            "System.RuntimeType.get_IsEnum()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMemberInfoName_RemainsConservativeWithoutConcreteImplementationEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.Reflection.MemberInfo.get_Name");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Reflection.MemberInfo.get_Name()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Reflection.MemberInfo.get_Name()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Reflection.MemberInfo.get_Name()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeFileSystemStateSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            2,
            "System.IO.Directory.CreateDirectory(string)",
            "System.IO.Directory.CreateTempSubdirectory(string)",
            "System.IO.Directory.Exists(string)",
            "System.IO.File.Exists(string)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.Directory.CreateDirectory(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.CreateDirectory(string)", "caller_visible");
        AssertPurityClassification(summary, "System.IO.Directory.CreateTempSubdirectory(string)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.CreateTempSubdirectory(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.IO.Directory.CreateTempSubdirectoryCore(string)", "impure",
            "impure_callee", "throw");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.CreateTempSubdirectoryCore(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.IO.Directory.Exists(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.Directory.Exists(string)", "caller_visible");
        AssertPurityClassification(summary, "System.IO.File.Exists(string)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.IO.File.Exists(string)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.IO.Directory.CreateDirectory(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Directory.CreateTempSubdirectory(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.Directory.Exists(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.IO.File.Exists(string)", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.IO.Directory.CreateDirectory(string)",
            "System.IO.Directory.CreateTempSubdirectory(string)",
            "System.IO.Directory.Exists(string)",
            "System.IO.File.Exists(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArgumentGuardHelperSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            120,
            "System.ArgumentNullException.ThrowIfNull",
            "System.ArgumentException.ThrowIfNullOrEmpty",
            "System.ArgumentException.ThrowIfNullOrWhiteSpace",
            "System.ArgumentOutOfRangeException.ThrowIfNegative",
            "System.ArgumentOutOfRangeException.ThrowIfZero",
            "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero",
            "System.ArgumentOutOfRangeException.ThrowIfLessThan",
            "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual",
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThan",
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.ArgumentNullException.ThrowIfNull(object, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentNullException.ThrowIfNull(object, string)",
            "none");
        AssertPurityClassification(summary, "System.ArgumentException.ThrowIfNullOrEmpty(string, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentException.ThrowIfNullOrEmpty(string, string)",
            "none");
        AssertPurityClassification(summary, "System.ArgumentException.ThrowIfNullOrWhiteSpace(string, string)", "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentException.ThrowIfNullOrWhiteSpace(string, string)", "none");
        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfNegative(!!0, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfNegative(!!0, string)",
            "none");
        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfZero(!!0, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfZero(!!0, string)",
            "none");
        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(!!0, string)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(!!0, string)", "none");
        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfLessThan(!!0, !!0, string)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfLessThan(!!0, !!0, string)", "none");
        AssertPurityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(!!0, !!0, string)", "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(!!0, !!0, string)", "none");
        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException.ThrowIfGreaterThan(!!0, !!0, string)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThan(!!0, !!0, string)", "none");
        AssertPurityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(!!0, !!0, string)", "pure");
        AssertEffectVisibilityClassification(summary,
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(!!0, !!0, string)", "none");
        AssertPurityClassification(summary, "System.ArgumentNullException.ThrowIfNull(void*, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentNullException.ThrowIfNull(void*, string)",
            "none");
        AssertPurityClassification(summary, "System.ArgumentNullException.ThrowIfNull(nint, string)", "pure");
        AssertEffectVisibilityClassification(summary, "System.ArgumentNullException.ThrowIfNull(nint, string)", "none");
        AssertThrownExceptions(summary, "System.ArgumentException.ThrowNullOrEmptyException(string, string)",
            "System.ArgumentException");
        AssertThrownExceptions(summary, "System.ArgumentException.ThrowNullOrWhiteSpaceException(string, string)",
            "System.ArgumentException");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.ArgumentNullException.ThrowIfNull(object, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentException.ThrowIfNullOrEmpty(string, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentException.ThrowIfNullOrWhiteSpace(string, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfNegative(!!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfZero(!!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(!!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfLessThan(!!0, !!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(!!0, !!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfGreaterThan(!!0, !!0, string)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(!!0, !!0, string)",
                    StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.ArgumentException.ThrowIfNullOrEmpty(string, string)",
            "System.ArgumentException.ThrowIfNullOrWhiteSpace(string, string)",
            "System.ArgumentNullException.ThrowIfNull(object, string)",
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThan(!!0, !!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(!!0, !!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfLessThan(!!0, !!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(!!0, !!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfNegative(!!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(!!0, string)",
            "System.ArgumentOutOfRangeException.ThrowIfZero(!!0, string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeThrowHelperFactorySlice_CollectsDirectThrownExceptionTypes()
    {
        using var memorySummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Memory.dll",
            20,
            "System.ThrowHelper.ThrowStartOrEndArgumentValidationException",
            "System.ThrowHelper.CreateStartOrEndArgumentValidationException");
        using var coreLibSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.ThrowHelper.ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLessOrEqual",
            "System.ThrowHelper.GetArgumentOutOfRangeException");

        AssertThrownExceptions(
            memorySummary,
            "System.ThrowHelper.ThrowStartOrEndArgumentValidationException(long)",
            "System.ArgumentOutOfRangeException");
        AssertThrownExceptions(
            coreLibSummary,
            "System.ThrowHelper.ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLessOrEqual()",
            "System.ArgumentOutOfRangeException");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTimeProviderAndTimeZoneInfoSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            60,
            "System.TimeProvider.get_System",
            "System.TimeProvider.get_LocalTimeZone",
            "System.TimeProvider.get_TimestampFrequency",
            "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
            "System.TimeZoneInfo.get_Local",
            "System.TimeZoneInfo.FindSystemTimeZoneById",
            "System.TimeZoneInfo.ClearCachedData");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.TimeProvider.get_System()", "pure");
        AssertEffectVisibilityClassification(summary, "System.TimeProvider.get_System()", "internal_only");
        AssertPurityClassification(summary, "System.TimeProvider.get_LocalTimeZone()", "impure", "global_state_read",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.TimeProvider.get_LocalTimeZone()", "caller_visible");
        AssertPurityClassification(summary, "System.TimeProvider.get_TimestampFrequency()", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.TimeProvider.get_TimestampFrequency()", "caller_visible");
        AssertPurityClassification(summary,
            "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary,
            "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)", "caller_visible");
        AssertPurityClassification(summary, "System.TimeZoneInfo.get_Local()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.TimeZoneInfo.get_Local()", "caller_visible");
        AssertPurityClassification(summary, "System.TimeZoneInfo.FindSystemTimeZoneById(string)", "impure", "throw");
        AssertEffectVisibilityClassification(summary, "System.TimeZoneInfo.FindSystemTimeZoneById(string)",
            "caller_visible");
        AssertPurityClassification(summary, "System.TimeZoneInfo.ClearCachedData()", "impure", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.TimeZoneInfo.ClearCachedData()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.TimeProvider.get_System()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeProvider.get_LocalTimeZone()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeProvider.get_TimestampFrequency()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
                    StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeZoneInfo.get_Local()", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeZoneInfo.FindSystemTimeZoneById(string)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.TimeZoneInfo.ClearCachedData()", StringComparison.Ordinal))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.TimeProvider.get_LocalTimeZone()",
            "System.TimeProvider.get_System()",
            "System.TimeProvider.get_TimestampFrequency()",
            "System.TimeZoneInfo.ClearCachedData()",
            "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
            "System.TimeZoneInfo.FindSystemTimeZoneById(string)",
            "System.TimeZoneInfo.get_Local()"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeIPAddressParseSlice_UsesSemanticHandlingInsteadOfManualCatalogEntries()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Net.Primitives.dll", 80, "System.Net.IPAddress");

        var knownPureRows = summary.RootElement.GetProperty("PurityReport")
            .GetProperty("CatalogComparison")
            .GetProperty("KnownPureMembers")
            .EnumerateArray()
            .Where(row => row.GetProperty("DisplayName").GetString() is string symbol &&
                          symbol.StartsWith("System.Net.IPAddress.Parse", StringComparison.Ordinal))
            .ToArray();

        Assert.That(knownPureRows, Is.Empty);

        AssertPurityClassification(summary, "System.Net.IPAddress.Parse(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Net.IPAddress.Parse(System.ReadOnlySpan`1<char>)", "impure",
            "impure_callee");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeIPAddressIsLoopbackSlice_TreatsLoopbackSingletonCacheReadsAsPure()
    {
        using var summary =
            await RunRuntimeEffectSummaryAsyncForAssembly("System.Net.Primitives.dll", 20,
                "System.Net.IPAddress.IsLoopback");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Net.IPAddress.IsLoopback(System.Net.IPAddress)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Net.IPAddress.IsLoopback(System.Net.IPAddress)",
            "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Net.IPAddress.IsLoopback(System.Net.IPAddress)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeIPEndPointConstructorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Net.Primitives.dll",
            20,
            "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int)", "impure",
            "object_state_write", "throw");
        AssertEffectVisibilityClassification(summary, "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int)" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertToBase64Slice_TreatsRuntimeHelpersAsImpure()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.ToBase64String", 20);

        var methods = FindMethodsByPrefix(summary, "System.Convert.ToBase64String");
        Assert.That(methods.Length, Is.EqualTo(5));

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[])", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[], System.Base64FormattingOptions)",
            "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[], int, int)", "impure",
            "impure_callee");
        AssertPurityClassification(summary,
            "System.Convert.ToBase64String(byte[], int, int, System.Base64FormattingOptions)", "impure",
            "impure_callee");
        AssertPurityClassification(summary,
            "System.Convert.ToBase64String(System.ReadOnlySpan`1<byte>, System.Base64FormattingOptions)", "impure",
            "throw");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Convert.ToBase64String", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EqualTo(new[]
        {
            "System.Convert.ToBase64String(System.ReadOnlySpan`1<byte>, System.Base64FormattingOptions)",
            "System.Convert.ToBase64String(byte[])",
            "System.Convert.ToBase64String(byte[], System.Base64FormattingOptions)",
            "System.Convert.ToBase64String(byte[], int, int)",
            "System.Convert.ToBase64String(byte[], int, int, System.Base64FormattingOptions)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertToHexSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.ToHexString", 20);

        var methods = FindMethodsByPrefix(summary, "System.Convert.ToHexString");
        Assert.That(methods.Length, Is.EqualTo(3));

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Convert.ToHexString(byte[])", "impure");
        AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(byte[])", "caller_visible");
        AssertPurityClassification(summary, "System.Convert.ToHexString(byte[], int, int)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(byte[], int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)",
            "caller_visible");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var symbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Convert.ToHexString", StringComparison.Ordinal))
            .ToArray();

        Assert.That(symbols, Is.EqualTo(new[]
        {
            "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)",
            "System.Convert.ToHexString(byte[])",
            "System.Convert.ToHexString(byte[], int, int)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertCurrentCultureStringSlice_UsesGeneratedPurityCatalogEntries()
    {
        var symbols = new[]
        {
            "System.Convert.ToSingle(string)",
            "System.Convert.ToDouble(string)",
            "System.Convert.ToByte(string)",
            "System.Convert.ToDateTime(string)",
            "System.Convert.ToSByte(string)",
            "System.Convert.ToInt16(string)",
            "System.Convert.ToInt32(string)",
            "System.Convert.ToInt64(string)",
            "System.Convert.ToUInt16(string)",
            "System.Convert.ToUInt32(string)",
            "System.Convert.ToUInt64(string)"
        };

        using var summary = await RunRuntimeEffectSummaryAsync(120, symbols);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        foreach (var symbol in symbols)
        {
            AssertPurityClassification(summary, symbol, "impure");
            AssertEffectVisibilityClassification(summary, symbol, "caller_visible");
        }

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbols.Contains(symbol, StringComparer.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EquivalentTo(symbols));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeConvertChangeTypeTypeOverload_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            12,
            "System.Convert.ChangeType(object, System.Type)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Convert.ChangeType(object, System.Type)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Convert.ChangeType(object, System.Type)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Convert.ChangeType(object, System.Type)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Convert.ChangeType(object, System.Type)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_Classifies_MarshalPtrToStructure_AsImpure()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-marshal-ptrtostructure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var outputPath = Path.Combine(workingDirectory, "Marshal.PtrToStructure.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        await GenerateReviewedSourceSummaryAsync(
            seedOutputPath,
            "System.Private.CoreLib.dll",
            12,
            "System.Runtime.InteropServices.Marshal.PtrToStructure");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Private.CoreLib.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        SourceSummaryPath = seedOutputPath,
                        IncludeCallees = false,
                        SymbolPrefixes = new[]
                        {
                            "System.Runtime.InteropServices.Marshal.PtrToStructure"
                        },
                        Limit = 12
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Runtime.InteropServices.Marshal.PtrToStructure(nint)", "impure",
            "global_state_read", "throw");
        AssertEffectVisibilityClassification(summary, "System.Runtime.InteropServices.Marshal.PtrToStructure(nint)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Runtime.InteropServices.Marshal.PtrToStructure(nint)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Runtime.InteropServices.Marshal.PtrToStructure(nint)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_ArtifactSpec_SourceSummaryPath_Classifies_ClaimsPrincipalIsInRole_AsImpure()
    {
        var workingDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-claimsprincipal-isinrole-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        var seedOutputPath = Path.Combine(workingDirectory, "seed.SharpProof.EffectSummary.json");
        var outputPath = Path.Combine(workingDirectory, "ClaimsPrincipal.IsInRole.SharpProof.EffectSummary.json");
        var artifactSpecPath = Path.Combine(workingDirectory, "artifact-spec.json");

        await GenerateReviewedSourceSummaryAsync(
            seedOutputPath,
            "System.Security.Claims.dll",
            20,
            "System.Security.Claims.ClaimsPrincipal.IsInRole(string)");

        var artifactSpecJson = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = 1,
                Defaults = new
                {
                    Framework = "net8.0",
                    RuntimeAssemblyName = "System.Security.Claims.dll",
                    IncludeCallees = true,
                    IncludePurityClassification = true,
                    CompareManualCatalogs = true
                },
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = outputPath,
                        SourceSummaryPath = seedOutputPath,
                        SymbolPrefixes = new[]
                        {
                            "System.Security.Claims.ClaimsPrincipal.IsInRole(string)"
                        },
                        Limit = 20
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        await File.WriteAllTextAsync(artifactSpecPath, artifactSpecJson);

        await RunEffectSummaryToolAsync("--artifact-spec", artifactSpecPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Security.Claims.ClaimsPrincipal.IsInRole(string)", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary, "System.Security.Claims.ClaimsPrincipal.IsInRole(string)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Security.Claims.ClaimsPrincipal.IsInRole(string)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[]
        {
            "System.Security.Claims.ClaimsPrincipal.IsInRole(string)"
        }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeWebUtilitySlice_TreatsHelpersAsGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Net.WebUtility", 40);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Net.WebUtility.HtmlEncode(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Net.WebUtility.HtmlDecode(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Net.WebUtility.UrlEncode(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Net.WebUtility.UrlDecode(string)", "impure", "impure_callee");
        AssertPurityClassification(summary, "System.Net.WebUtility.UrlEncodeToBytes(byte[], int, int)", "impure",
            "impure_callee");
        AssertPurityClassification(summary, "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)", "impure");
        AssertEffectVisibilityClassification(summary, "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)",
            "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeListSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.Collections.Generic.List", 120);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Capacity.set",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Capacity.set should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Add(T)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Add(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.ForEach(System.Action<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.ForEach(System.Action<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Insert(int, T)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Insert(int, T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Remove(T)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Remove(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.AddRange(System.Collections.Generic.IEnumerable<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.AddRange(System.Collections.Generic.IEnumerable<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.InsertRange(int, System.Collections.Generic.IEnumerable<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.InsertRange(int, System.Collections.Generic.IEnumerable<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.RemoveAll(System.Predicate<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.RemoveAll(System.Predicate<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.RemoveAt(int)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.RemoveAt(int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.RemoveRange(int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.RemoveRange(int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Reverse()",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Reverse() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Sort()",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Sort() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Sort(System.Comparison<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Sort(System.Comparison<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Sort(System.Collections.Generic.IComparer<T>?)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Sort(System.Collections.Generic.IComparer<T>?) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.List<T>.Sort(int, int, System.Collections.Generic.IComparer<T>?)",
                StringComparison.Ordinal)),
            Is.False,
            "List<T>.Sort(int, int, System.Collections.Generic.IComparer<T>?) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.List`1.get_Capacity()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.get_Capacity()", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.set_Capacity(int)", "impure",
            "global_state_read", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.set_Capacity(int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.get_Count()", "pure");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Add(!0)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Add(!0)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable`1<!0>)", "impure",
            "global_state_read");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Clear()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Exists(System.Predicate`1<!0>)", "pure");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.FindIndex(System.Predicate`1<!0>)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.FindIndex(System.Predicate`1<!0>)", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.FindLast(System.Predicate`1<!0>)",
            "impure", "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.FindLast(System.Predicate`1<!0>)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Insert(int, !0)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Insert(int, !0)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.List`1.InsertRange(int, System.Collections.Generic.IEnumerable`1<!0>)",
            "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.InsertRange(int, System.Collections.Generic.IEnumerable`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Remove(!0)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Remove(!0)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.RemoveAll(System.Predicate`1<!0>)",
            "impure", "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.RemoveAll(System.Predicate`1<!0>)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.RemoveAt(int)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.RemoveAt(int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.RemoveRange(int, int)", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.RemoveRange(int, int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Reverse()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Reverse()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Sort()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Sort()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.Sort(System.Comparison`1<!0>)", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Sort(System.Comparison`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.List`1.Sort(System.Collections.Generic.IComparer`1<!0>)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.Sort(System.Collections.Generic.IComparer`1<!0>)", "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.List`1.Sort(int, int, System.Collections.Generic.IComparer`1<!0>)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.Sort(int, int, System.Collections.Generic.IComparer`1<!0>)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)",
            "pure");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var generatedSymbols = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                             symbol.StartsWith("System.Collections.Generic.List`1.", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.get_Capacity()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.set_Capacity(int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.get_Count()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Add(!0)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Exists(System.Predicate`1<!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.FindIndex(System.Predicate`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.FindLast(System.Predicate`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Insert(int, !0)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Collections.Generic.List`1.InsertRange(int, System.Collections.Generic.IEnumerable`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Remove(!0)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.RemoveAll(System.Predicate`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.RemoveAt(int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.RemoveRange(int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Reverse()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Sort()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Sort(System.Comparison`1<!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.Sort(System.Collections.Generic.IComparer`1<!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Collections.Generic.List`1.Sort(int, int, System.Collections.Generic.IComparer`1<!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeHashSetMutatorSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Collections.Generic.HashSet`1.Add",
            "System.Collections.Generic.HashSet`1.Clear",
            "System.Collections.Generic.HashSet`1.Remove",
            "System.Collections.Generic.HashSet`1.UnionWith");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.HashSet<T>.Add(T)",
                StringComparison.Ordinal)),
            Is.False,
            "HashSet<T>.Add(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.HashSet<T>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "HashSet<T>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.HashSet<T>.Remove(T)",
                StringComparison.Ordinal)),
            Is.False,
            "HashSet<T>.Remove(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.HashSet<T>.UnionWith(System.Collections.Generic.IEnumerable<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "HashSet<T>.UnionWith(System.Collections.Generic.IEnumerable<T>) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.HashSet`1.Add(!0)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.HashSet`1.Add(!0)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.HashSet`1.Clear()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.HashSet`1.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.HashSet`1.Remove(!0)", "impure",
            "caller_visible_memory_write", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.HashSet`1.Remove(!0)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.HashSet`1.UnionWith(System.Collections.Generic.IEnumerable`1<!0>)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.HashSet`1.UnionWith(System.Collections.Generic.IEnumerable`1<!0>)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.HashSet`1.Add(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.HashSet`1.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.HashSet`1.Remove(!0)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Collections.Generic.HashSet`1.UnionWith(System.Collections.Generic.IEnumerable`1<!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDictionaryMutatorSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Collections.Generic.Dictionary`2.Add",
            "System.Collections.Generic.Dictionary`2.Clear",
            "System.Collections.Generic.Dictionary`2.Remove",
            "System.Collections.Generic.Dictionary`2.TryAdd");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.Add(TKey, TValue)",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.Add(TKey, TValue) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.Remove(TKey)",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.Remove(TKey) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.TryAdd(TKey, TValue)",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.TryAdd(TKey, TValue) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.Add(!0, !1)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.Add(!0, !1)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.Clear()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.Clear()",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.Remove(!0)", "impure",
            "caller_visible_memory_write", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.Remove(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.TryAdd(!0, !1)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.TryAdd(!0, !1)",
            "caller_visible");
        AssertPurityClassification(summary,
            "System.Collections.Generic.Dictionary`2.TryInsert(!0, !1, System.Collections.Generic.InsertionBehavior)",
            "impure", "caller_visible_memory_write", "object_state_write");
        AssertEffectVisibilityClassification(summary,
            "System.Collections.Generic.Dictionary`2.TryInsert(!0, !1, System.Collections.Generic.InsertionBehavior)",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.Add(!0, !1)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.Remove(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.TryAdd(!0, !1)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Collections.Generic.Dictionary`2.TryInsert(!0, !1, System.Collections.Generic.InsertionBehavior)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDictionaryViewGetterSlices_UseGeneratedPurityCatalogEntries()
    {
        using var dictionarySummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Collections.Generic.Dictionary`2.get_Keys",
            "System.Collections.Generic.Dictionary`2.get_Values");
        using var sortedDictionarySummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            20,
            "System.Collections.Generic.SortedDictionary`2.get_Keys",
            "System.Collections.Generic.SortedDictionary`2.get_Values");

        var dictionaryReport = dictionarySummary.RootElement.GetProperty("PurityReport");
        var dictionaryCatalogComparison = dictionaryReport.GetProperty("CatalogComparison");
        Assert.That(dictionaryCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(dictionaryCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var dictionaryKnownImpureRows =
            dictionaryCatalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            dictionaryKnownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.Keys.get",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.Keys.get should no longer overlap the manual impure catalog.");
        Assert.That(
            dictionaryKnownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Dictionary<TKey, TValue>.Values.get",
                StringComparison.Ordinal)),
            Is.False,
            "Dictionary<TKey, TValue>.Values.get should no longer overlap the manual impure catalog.");

        AssertPurityClassification(dictionarySummary, "System.Collections.Generic.Dictionary`2.get_Keys()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(dictionarySummary, "System.Collections.Generic.Dictionary`2.get_Keys()",
            "caller_visible");
        AssertPurityClassification(dictionarySummary, "System.Collections.Generic.Dictionary`2.get_Values()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(dictionarySummary, "System.Collections.Generic.Dictionary`2.get_Values()",
            "caller_visible");

        var sortedDictionaryReport = sortedDictionarySummary.RootElement.GetProperty("PurityReport");
        var sortedDictionaryCatalogComparison = sortedDictionaryReport.GetProperty("CatalogComparison");
        Assert.That(sortedDictionaryCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(
            sortedDictionaryCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var sortedDictionaryKnownImpureRows = sortedDictionaryCatalogComparison.GetProperty("KnownImpureMembers")
            .EnumerateArray().ToArray();

        Assert.That(
            sortedDictionaryKnownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.SortedDictionary<TKey, TValue>.Keys.get",
                StringComparison.Ordinal)),
            Is.False,
            "SortedDictionary<TKey, TValue>.Keys.get should no longer overlap the manual impure catalog.");
        Assert.That(
            sortedDictionaryKnownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.SortedDictionary<TKey, TValue>.Values.get",
                StringComparison.Ordinal)),
            Is.False,
            "SortedDictionary<TKey, TValue>.Values.get should no longer overlap the manual impure catalog.");

        AssertPurityClassification(sortedDictionarySummary, "System.Collections.Generic.SortedDictionary`2.get_Keys()",
            "impure", "object_state_write");
        AssertEffectVisibilityClassification(sortedDictionarySummary,
            "System.Collections.Generic.SortedDictionary`2.get_Keys()", "caller_visible");
        AssertPurityClassification(sortedDictionarySummary,
            "System.Collections.Generic.SortedDictionary`2.get_Values()", "impure", "object_state_write");
        AssertEffectVisibilityClassification(sortedDictionarySummary,
            "System.Collections.Generic.SortedDictionary`2.get_Values()", "caller_visible");

        var dictionaryGeneratedSymbols = dictionarySummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        var sortedDictionaryGeneratedSymbols = sortedDictionarySummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(dictionaryGeneratedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.get_Keys()"));
        Assert.That(dictionaryGeneratedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.get_Values()"));
        Assert.That(sortedDictionaryGeneratedSymbols,
            Does.Contain("System.Collections.Generic.SortedDictionary`2.get_Keys()"));
        Assert.That(sortedDictionaryGeneratedSymbols,
            Does.Contain("System.Collections.Generic.SortedDictionary`2.get_Values()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeArrayCopyWriteHelperSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Array.Clear(System.Array)",
            "System.Array.Clear(System.Array, int, int)",
            "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)",
            "System.Array.Copy(System.Array, System.Array, int)",
            "System.Array.Copy(System.Array, int, System.Array, int, int)",
            "System.Array.CopyTo(System.Array, int)",
            "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)",
            "System.Array.Fill(!!0[], !!0)",
            "System.Array.Fill(!!0[], !!0, int, int)",
            "System.Array.Resize(ref !!0[], int)",
            "System.Span`1.Clear()",
            "System.Span`1.CopyTo",
            "System.Span`1.Fill(!0)",
            "System.Span`1.TryCopyTo");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();

        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Clear(System.Array)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Clear(System.Array) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Clear(System.Array, int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Clear(System.Array, int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Copy(System.Array, System.Array, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Copy(System.Array, System.Array, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Copy(System.Array, int, System.Array, int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Copy(System.Array, int, System.Array, int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.CopyTo(System.Array, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.CopyTo(System.Array, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Fill<T>(T[], T)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Fill<T>(T[], T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Fill<T>(T[], T, int, int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Fill<T>(T[], T, int, int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Array.Resize<T>(ref T[], int)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Array.Resize<T>(ref T[], int) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Span<T>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "System.Span<T>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Span<T>.CopyTo(System.Span<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Span<T>.CopyTo(System.Span<T>) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Span<T>.Fill(T)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Span<T>.Fill(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Span<T>.TryCopyTo(System.Span<T>)",
                StringComparison.Ordinal)),
            Is.False,
            "System.Span<T>.TryCopyTo(System.Span<T>) should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Array.Clear(System.Array)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Array.Clear(System.Array)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.Clear(System.Array, int, int)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Array.Clear(System.Array, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)",
            "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary,
            "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.Copy(System.Array, System.Array, int)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Array.Copy(System.Array, System.Array, int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Array.Copy(System.Array, int, System.Array, int, int)", "impure",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Array.Copy(System.Array, int, System.Array, int, int)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Array.CopyTo(System.Array, int)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Array.CopyTo(System.Array, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)",
            "impure", "impure_callee", "throw");
        AssertEffectVisibilityClassification(summary,
            "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.Fill(!!0[], !!0)", "impure", "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Array.Fill(!!0[], !!0)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.Fill(!!0[], !!0, int, int)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Array.Fill(!!0[], !!0, int, int)", "caller_visible");
        AssertPurityClassification(summary, "System.Array.Resize(ref !!0[], int)", "impure",
            "caller_visible_memory_write");
        AssertFreshnessClassification(summary, "System.Array.Resize(ref !!0[], int)",
            "fresh_array_candidate_requires_non_pure_resolution");
        AssertEffectVisibilityClassification(summary, "System.Array.Resize(ref !!0[], int)", "caller_visible");
        AssertPurityClassification(summary, "System.Span`1.Clear()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Span`1.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Span`1.CopyTo(System.Span`1<!0>)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Span`1.CopyTo(System.Span`1<!0>)", "caller_visible");
        AssertPurityClassification(summary, "System.Span`1.Fill(!0)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Span`1.Fill(!0)", "caller_visible");
        AssertPurityClassification(summary, "System.Span`1.TryCopyTo(System.Span`1<!0>)", "impure", "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Span`1.TryCopyTo(System.Span`1<!0>)", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Array.Clear(System.Array)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Clear(System.Array, int, int)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Copy(System.Array, System.Array, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Copy(System.Array, int, System.Array, int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.CopyTo(System.Array, int)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Fill(!!0[], !!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Fill(!!0[], !!0, int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Array.Resize(ref !!0[], int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Span`1.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Span`1.CopyTo(System.Span`1<!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Span`1.Fill(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Span`1.TryCopyTo(System.Span`1<!0>)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeGCKeepAliveSlice_ShowsStaleManualRowRemoval()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.GC.KeepAlive");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.GC.KeepAlive(object)", "pure");
        AssertEffectVisibilityClassification(summary, "System.GC.KeepAlive(object)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.GC.KeepAlive(object)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDataContractAttributeSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Runtime.Serialization.Primitives.dll",
            8,
            "System.Runtime.Serialization.DataContractAttribute..ctor()");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Runtime.Serialization.DataContractAttribute..ctor()", "pure");
        AssertFreshnessClassification(summary, "System.Runtime.Serialization.DataContractAttribute..ctor()",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Runtime.Serialization.DataContractAttribute..ctor()",
            "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Runtime.Serialization.DataContractAttribute..ctor()",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols,
            Is.EqualTo(new[] { "System.Runtime.Serialization.DataContractAttribute..ctor()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeParallelEnumerableAndLabelSlices_UsesGeneratedPurityCatalogEntries()
    {
        using var parallelSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Linq.Parallel.dll",
            20,
            "System.Linq.ParallelEnumerable.AsParallel");
        using var labelSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            8,
            "System.Reflection.Emit.Label.Equals(object)");

        var parallelReport = parallelSummary.RootElement.GetProperty("PurityReport");
        var parallelCatalogComparison = parallelReport.GetProperty("CatalogComparison");
        Assert.That(parallelCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(parallelCatalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(parallelCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(parallelSummary,
            "System.Linq.ParallelEnumerable.AsParallel(System.Collections.IEnumerable)", "impure");
        AssertEffectVisibilityClassification(parallelSummary,
            "System.Linq.ParallelEnumerable.AsParallel(System.Collections.IEnumerable)", "caller_visible");
        AssertPurityClassification(parallelSummary,
            "System.Linq.ParallelEnumerable.AsParallel(System.Collections.Generic.IEnumerable`1<!!0>)", "impure");
        AssertEffectVisibilityClassification(parallelSummary,
            "System.Linq.ParallelEnumerable.AsParallel(System.Collections.Generic.IEnumerable`1<!!0>)",
            "caller_visible");

        var parallelGeneratedSymbols = parallelSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol,
                "System.Linq.ParallelEnumerable.AsParallel(System.Collections.Generic.IEnumerable`1<!!0>)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            parallelGeneratedSymbols,
            Is.EqualTo(new[]
                { "System.Linq.ParallelEnumerable.AsParallel(System.Collections.Generic.IEnumerable`1<!!0>)" }));

        var labelReport = labelSummary.RootElement.GetProperty("PurityReport");
        var labelCatalogComparison = labelReport.GetProperty("CatalogComparison");
        Assert.That(labelCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(labelCatalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(labelCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(labelSummary, "System.Reflection.Emit.Label.Equals(object)", "pure");
        AssertEffectVisibilityClassification(labelSummary, "System.Reflection.Emit.Label.Equals(object)", "none");

        var labelGeneratedSymbols = labelSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Reflection.Emit.Label.Equals(object)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(labelGeneratedSymbols, Is.EqualTo(new[] { "System.Reflection.Emit.Label.Equals(object)" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeMutableCollectionReadSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            160,
            "System.Collections.Generic.Dictionary`2.get_Count",
            "System.Collections.Generic.Dictionary`2.ContainsKey",
            "System.Collections.Generic.HashSet`1.Contains",
            "System.Collections.Generic.Queue`1.Contains",
            "System.Collections.Generic.Queue`1.Peek",
            "System.Collections.Generic.Queue`1.TryPeek");
        using var stackSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            80,
            "System.Collections.Generic.Stack`1.Contains",
            "System.Collections.Generic.Stack`1.Peek");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));
        var stackReport = stackSummary.RootElement.GetProperty("PurityReport");
        var stackCatalogComparison = stackReport.GetProperty("CatalogComparison");
        Assert.That(stackCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(stackCatalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(stackCatalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.ContainsKey(!0)",
            "conservative_unknown");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.ContainsKey(!0)",
            "unknown");
        AssertPurityClassification(summary, "System.Collections.Generic.Dictionary`2.get_Count()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Dictionary`2.get_Count()", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.HashSet`1.Contains(!0)", "conservative_unknown",
            "unknown_callee");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.HashSet`1.Contains(!0)", "unknown");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.Contains(!0)", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.Contains(!0)", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.Peek()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.Peek()", "none");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.TryPeek(ref !0)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.TryPeek(ref !0)",
            "caller_visible");
        AssertPurityClassification(stackSummary, "System.Collections.Generic.Stack`1.Contains(!0)", "pure");
        AssertEffectVisibilityClassification(stackSummary, "System.Collections.Generic.Stack`1.Contains(!0)", "none");
        AssertPurityClassification(stackSummary, "System.Collections.Generic.Stack`1.Peek()", "pure");
        AssertEffectVisibilityClassification(stackSummary, "System.Collections.Generic.Stack`1.Peek()", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.ContainsKey(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Dictionary`2.get_Count()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.HashSet`1.Contains(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.Contains(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.Peek()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.TryPeek(ref !0)"));

        var stackGeneratedSymbols = stackSummary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(stackGeneratedSymbols, Does.Contain("System.Collections.Generic.Stack`1.Contains(!0)"));
        Assert.That(stackGeneratedSymbols, Does.Contain("System.Collections.Generic.Stack`1.Peek()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeQueueMutatorSlice_UsesGeneratedPurityAndFreshArrayEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Collections.Generic.Queue`1.Clear",
            "System.Collections.Generic.Queue`1.Enqueue",
            "System.Collections.Generic.Queue`1.Dequeue",
            "System.Collections.Generic.Queue`1.ToArray");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Queue<T>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "Queue<T>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Queue<T>.Enqueue(T)",
                StringComparison.Ordinal)),
            Is.False,
            "Queue<T>.Enqueue(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Queue<T>.Dequeue()",
                StringComparison.Ordinal)),
            Is.False,
            "Queue<T>.Dequeue() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Queue<T>.ToArray()",
                StringComparison.Ordinal)),
            Is.False,
            "Queue<T>.ToArray() should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.Clear()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.Enqueue(!0)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.Enqueue(!0)",
            "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.Dequeue()", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.Dequeue()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Queue`1.ToArray()", "pure");
        AssertFreshnessClassification(summary, "System.Collections.Generic.Queue`1.ToArray()",
            "fresh_array_candidate_via_local_helpers");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Queue`1.ToArray()", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.Enqueue(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.Dequeue()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Queue`1.ToArray()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeStackMutatorSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Collections.dll",
            80,
            "System.Collections.Generic.Stack`1.Clear",
            "System.Collections.Generic.Stack`1.Push",
            "System.Collections.Generic.Stack`1.Pop",
            "System.Collections.Generic.Stack`1.ToArray");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        var knownImpureRows = catalogComparison.GetProperty("KnownImpureMembers").EnumerateArray().ToArray();
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Stack<T>.Clear()",
                StringComparison.Ordinal)),
            Is.False,
            "Stack<T>.Clear() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Stack<T>.Push(T)",
                StringComparison.Ordinal)),
            Is.False,
            "Stack<T>.Push(T) should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Stack<T>.Pop()",
                StringComparison.Ordinal)),
            Is.False,
            "Stack<T>.Pop() should no longer overlap the manual impure catalog.");
        Assert.That(
            knownImpureRows.Any(row => string.Equals(
                row.GetProperty("DisplayName").GetString(),
                "System.Collections.Generic.Stack<T>.ToArray()",
                StringComparison.Ordinal)),
            Is.False,
            "Stack<T>.ToArray() should no longer overlap the manual impure catalog.");

        AssertPurityClassification(summary, "System.Collections.Generic.Stack`1.Clear()", "impure",
            "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Stack`1.Clear()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Stack`1.Push(!0)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Stack`1.Push(!0)", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Stack`1.Pop()", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Stack`1.Pop()", "caller_visible");
        AssertPurityClassification(summary, "System.Collections.Generic.Stack`1.ToArray()", "impure",
            "caller_visible_memory_write");
        AssertFreshnessClassification(summary, "System.Collections.Generic.Stack`1.ToArray()",
            "fresh_array_candidate_requires_non_pure_resolution");
        AssertEffectVisibilityClassification(summary, "System.Collections.Generic.Stack`1.ToArray()", "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Stack`1.Clear()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Stack`1.Push(!0)"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Stack`1.Pop()"));
        Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.Stack`1.ToArray()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeFileNotFoundExceptionSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync("System.IO.FileNotFoundException", 80);

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "none");

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var ctorEntry = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Single(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "System.IO.FileNotFoundException..ctor(string)",
                StringComparison.Ordinal));

        Assert.That(ctorEntry.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
        Assert.That(ctorEntry.GetProperty("PrimaryCategory").GetString(), Is.EqualTo("generated_purity_summary"));
        Assert.That(ctorEntry.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
        Assert.That(ctorEntry.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeAggregateExceptionSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            120,
            "System.AggregateException..ctor(System.Collections.Generic.IEnumerable",
            "System.AggregateException.Flatten");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(
            summary,
            "System.AggregateException..ctor(System.Collections.Generic.IEnumerable`1<System.Exception>)",
            "impure");
        AssertEffectVisibilityClassification(
            summary,
            "System.AggregateException..ctor(System.Collections.Generic.IEnumerable`1<System.Exception>)",
            "caller_visible");
        AssertPurityClassification(
            summary,
            "System.AggregateException.Flatten()",
            "impure");
        AssertEffectVisibilityClassification(
            summary,
            "System.AggregateException.Flatten()",
            "caller_visible");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols,
            Does.Contain(
                "System.AggregateException..ctor(System.Collections.Generic.IEnumerable`1<System.Exception>)"));
        Assert.That(generatedSymbols, Does.Contain("System.AggregateException.Flatten()"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeDeferredEnumerableSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Linq.dll",
            160,
            "System.Linq.Enumerable.Cast(",
            "System.Linq.Enumerable.Chunk(",
            "System.Linq.Enumerable.Distinct(",
            "System.Linq.Enumerable.Reverse(",
            "System.Linq.Enumerable.TakeWhile(",
            "System.Linq.Enumerable.Empty(",
            "System.Linq.Enumerable.OfType(",
            "System.Linq.Enumerable.Range(",
            "System.Linq.Enumerable.Repeat(");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Linq.Enumerable.Cast(System.Collections.IEnumerable)", "pure");
        AssertFreshnessClassification(summary, "System.Linq.Enumerable.Cast(System.Collections.IEnumerable)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Linq.Enumerable.Cast(System.Collections.IEnumerable)",
            "internal_only");
        AssertPurityClassification(summary,
            "System.Linq.Enumerable.Chunk(System.Collections.Generic.IEnumerable`1<!!0>, int)", "pure");
        AssertFreshnessClassification(summary,
            "System.Linq.Enumerable.Chunk(System.Collections.Generic.IEnumerable`1<!!0>, int)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary,
            "System.Linq.Enumerable.Chunk(System.Collections.Generic.IEnumerable`1<!!0>, int)", "internal_only");
        AssertPurityClassification(summary, "System.Linq.Enumerable.Empty()", "pure");
        AssertEffectVisibilityClassification(summary, "System.Linq.Enumerable.Empty()", "internal_only");
        AssertPurityClassification(summary,
            "System.Linq.Enumerable.Distinct(System.Collections.Generic.IEnumerable`1<!!0>)", "pure");
        AssertPurityClassification(summary, "System.Linq.Enumerable.OfType(System.Collections.IEnumerable)", "pure");
        AssertFreshnessClassification(summary, "System.Linq.Enumerable.OfType(System.Collections.IEnumerable)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Linq.Enumerable.OfType(System.Collections.IEnumerable)",
            "internal_only");
        AssertPurityClassification(summary, "System.Linq.Enumerable.Range(int, int)", "pure");
        AssertFreshnessClassification(summary, "System.Linq.Enumerable.Range(int, int)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Linq.Enumerable.Range(int, int)", "internal_only");
        AssertPurityClassification(summary, "System.Linq.Enumerable.Repeat(!!0, int)", "pure");
        AssertPurityClassification(summary,
            "System.Linq.Enumerable.Reverse(System.Collections.Generic.IEnumerable`1<!!0>)", "pure");
        AssertPurityClassification(
            summary,
            "System.Linq.Enumerable.TakeWhile(System.Collections.Generic.IEnumerable`1<!!0>, System.Func`2<!!0, bool>)",
            "pure");
        AssertFreshnessClassification(
            summary,
            "System.Linq.Enumerable.TakeWhile(System.Collections.Generic.IEnumerable`1<!!0>, System.Func`2<!!0, bool>)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(
            summary,
            "System.Linq.Enumerable.TakeWhile(System.Collections.Generic.IEnumerable`1<!!0>, System.Func`2<!!0, bool>)",
            "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Linq.Enumerable.Cast(System.Collections.IEnumerable)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Linq.Enumerable.Chunk(System.Collections.Generic.IEnumerable`1<!!0>, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Linq.Enumerable.Empty()"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Linq.Enumerable.Distinct(System.Collections.Generic.IEnumerable`1<!!0>)"));
        Assert.That(generatedSymbols, Does.Contain("System.Linq.Enumerable.OfType(System.Collections.IEnumerable)"));
        Assert.That(generatedSymbols, Does.Contain("System.Linq.Enumerable.Range(int, int)"));
        Assert.That(generatedSymbols, Does.Contain("System.Linq.Enumerable.Repeat(!!0, int)"));
        Assert.That(generatedSymbols,
            Does.Contain("System.Linq.Enumerable.Reverse(System.Collections.Generic.IEnumerable`1<!!0>)"));
        Assert.That(generatedSymbols,
            Does.Contain(
                "System.Linq.Enumerable.TakeWhile(System.Collections.Generic.IEnumerable`1<!!0>, System.Func`2<!!0, bool>)"));
    }

    [Test]
    public async Task EffectSummaryTool_CatalogComparison_NormalizesGenericMethodParameters()
    {
        using var enumerableSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Linq.dll",
            40,
            "System.Linq.Enumerable.Any(");
        using var taskSummary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            "System.Threading.Tasks.Task.FromResult(",
            "System.Threading.Tasks.ValueTask.AsTask");

        var enumerableEntry = enumerableSummary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Single(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "System.Linq.Enumerable.Any(System.Collections.Generic.IEnumerable`1<!!0>)",
                StringComparison.Ordinal));
        var enumerableExactKey = enumerableEntry.GetProperty("ExactSymbolKey").GetString();

        Assert.That(
            enumerableExactKey,
            Does.Contain("!!0"),
            "Enumerable.Any<TSource> should preserve method generic ordinals in the generated exact key.");

        var taskKnownPureRows = taskSummary.RootElement
            .GetProperty("PurityReport")
            .GetProperty("CatalogComparison")
            .GetProperty("KnownPureMembers")
            .EnumerateArray()
            .Where(row => row.GetProperty("DisplayName").GetString() is string symbol &&
                          (string.Equals(symbol, "System.Threading.Tasks.Task.FromResult<TResult>(TResult)",
                               StringComparison.Ordinal) ||
                           string.Equals(symbol, "System.Threading.Tasks.ValueTask.AsTask()",
                               StringComparison.Ordinal)))
            .ToArray();
        var generatedEntries = taskSummary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Where(entry => entry.GetProperty("DisplayName").GetString() is string symbol &&
                            (string.Equals(symbol, "System.Threading.Tasks.Task.FromResult(!!0)",
                                 StringComparison.Ordinal) ||
                             string.Equals(symbol, "System.Threading.Tasks.ValueTask.AsTask()",
                                 StringComparison.Ordinal)))
            .ToArray();

        Assert.That(taskKnownPureRows, Is.Empty);
        Assert.That(generatedEntries.Select(entry => entry.GetProperty("DisplayName").GetString()), Is.EquivalentTo(new[]
        {
            "System.Threading.Tasks.Task.FromResult(!!0)",
            "System.Threading.Tasks.ValueTask.AsTask()"
        }));
        Assert.That(generatedEntries.Select(entry => entry.GetProperty("ExactSymbolKey").GetString()), Is.EquivalentTo(
            new[]
            {
                "System.Threading.Tasks.Task.FromResult(!!0)->System.Threading.Tasks.Task`1<!!0>",
                "System.Threading.Tasks.ValueTask.AsTask()->System.Threading.Tasks.Task"
            }));
        AssertPurityClassification(taskSummary, "System.Threading.Tasks.Task.FromResult(!!0)", "impure",
            "caller_visible_memory_write", "global_state_read");
        AssertEffectVisibilityClassification(taskSummary, "System.Threading.Tasks.Task.FromResult(!!0)",
            "caller_visible");
        AssertPurityClassification(taskSummary, "System.Threading.Tasks.ValueTask.AsTask()", "impure", "impure_callee");
        AssertEffectVisibilityClassification(taskSummary, "System.Threading.Tasks.ValueTask.AsTask()",
            "caller_visible");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTaskSchedulingSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            80,
            "System.Threading.Tasks.Task.Delay(",
            "System.Threading.Tasks.Task.Run(System.Action)");

        var knownImpureRows = summary.RootElement
            .GetProperty("PurityReport")
            .GetProperty("CatalogComparison")
            .GetProperty("KnownImpureMembers")
            .EnumerateArray()
            .Select(row => row.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(knownImpureRows, Does.Not.Contain("System.Threading.Tasks.Task.Delay(int)"));
        Assert.That(knownImpureRows, Does.Not.Contain("System.Threading.Tasks.Task.Delay(System.TimeSpan)"));
        Assert.That(knownImpureRows, Does.Not.Contain("System.Threading.Tasks.Task.Run(System.Action)"));

        AssertPurityClassification(summary, "System.Threading.Tasks.Task.Delay(int)", "impure", "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.Task.Delay(int)", "caller_visible");
        AssertPrimaryCategory(summary, "System.Threading.Tasks.Task.Delay(int)", "global_state_write");
        AssertPurityClassification(summary, "System.Threading.Tasks.Task.Delay(System.TimeSpan)", "impure",
            "global_state_write");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.Task.Delay(System.TimeSpan)",
            "caller_visible");
        AssertPrimaryCategory(summary, "System.Threading.Tasks.Task.Delay(System.TimeSpan)", "global_state_write");
        AssertPurityClassification(summary, "System.Threading.Tasks.Task.Run(System.Action)", "impure",
            "caller_visible_memory_write");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.Task.Run(System.Action)",
            "caller_visible");
        AssertPrimaryCategory(summary, "System.Threading.Tasks.Task.Run(System.Action)", "caller_visible_memory_write");

        var generatedSymbols = summary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Threading.Tasks.Task.Delay(int)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Threading.Tasks.Task.Delay(System.TimeSpan)", StringComparison.Ordinal) ||
                string.Equals(symbol, "System.Threading.Tasks.Task.Run(System.Action)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            generatedSymbols,
            Is.EquivalentTo(new[]
            {
                "System.Threading.Tasks.Task.Delay(int)",
                "System.Threading.Tasks.Task.Delay(System.TimeSpan)",
                "System.Threading.Tasks.Task.Run(System.Action)"
            }));
    }

    [Test]
    public async Task EffectSummaryTool_RawThreadingStateReaders_UseGeneratedGlobalStateReadEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            2,
            false,
            "System.Threading.CancellationToken.get_IsCancellationRequested",
            "System.Threading.Tasks.Task.get_IsCompleted");

        AssertPurityClassification(summary, "System.Threading.CancellationToken.get_IsCancellationRequested()",
            "impure", "global_state_read");
        AssertPrimaryCategory(summary, "System.Threading.CancellationToken.get_IsCancellationRequested()",
            "global_state_read");
        AssertPurityClassification(summary, "System.Threading.Tasks.Task.get_IsCompleted()", "impure",
            "global_state_read");
        AssertPrimaryCategory(summary, "System.Threading.Tasks.Task.get_IsCompleted()", "global_state_read");
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeTaskResultSlice_UsesGeneratedImpureEvidence()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            40,
            2,
            false,
            "System.Threading.Tasks.Task`1.get_Result");

        var knownImpureRows = summary.RootElement
            .GetProperty("PurityReport")
            .GetProperty("CatalogComparison")
            .GetProperty("KnownImpureMembers")
            .EnumerateArray()
            .Select(row => row.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(knownImpureRows, Does.Not.Contain("System.Threading.Tasks.Task<TResult>.Result.get"));

        AssertPurityClassification(
            summary,
            "System.Threading.Tasks.Task`1.get_Result()",
            "impure",
            "global_state_read",
            "global_state_write",
            "impure_callee");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.Task`1.get_Result()", "caller_visible");
        AssertPrimaryCategory(summary, "System.Threading.Tasks.Task`1.get_Result()", "global_state_write");

        var generatedSymbols = summary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol =>
                string.Equals(symbol, "System.Threading.Tasks.Task`1.get_Result()", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.Threading.Tasks.Task`1.get_Result()" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePureConstructorsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.ArgumentNullException..ctor(string)",
            "System.ArgumentOutOfRangeException..ctor(string)",
            "System.AttributeUsageAttribute..ctor(System.AttributeTargets)",
            "System.BadImageFormatException..ctor(string)",
            "System.ObjectDisposedException..ctor(string)");

        AssertPurityClassification(summary, "System.Exception.set_HResult(int)", "impure", "object_state_write");
        AssertEffectVisibilityClassification(summary, "System.Exception.set_HResult(int)", "caller_visible");

        AssertPurityClassification(summary, "System.ArgumentNullException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.ArgumentNullException..ctor(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.ArgumentNullException..ctor(string)", "none");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.ArgumentOutOfRangeException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.ArgumentOutOfRangeException..ctor(string)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ArgumentOutOfRangeException..ctor(string)",
            "internal_only");

        AssertPurityClassification(summary, "System.BadImageFormatException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.BadImageFormatException..ctor(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.BadImageFormatException..ctor(string)", "none");

        AssertPurityClassification(summary, "System.ObjectDisposedException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.ObjectDisposedException..ctor(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.ObjectDisposedException..ctor(string)", "none");

        AssertPurityClassification(summary, "System.AttributeUsageAttribute..ctor(System.AttributeTargets)", "pure");
        AssertFreshnessClassification(summary, "System.AttributeUsageAttribute..ctor(System.AttributeTargets)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.AttributeUsageAttribute..ctor(System.AttributeTargets)",
            "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.ArgumentNullException..ctor(string)"));
        Assert.That(generatedSymbols, Does.Contain("System.ArgumentOutOfRangeException..ctor(string)"));
        Assert.That(generatedSymbols, Does.Contain("System.BadImageFormatException..ctor(string)"));
        Assert.That(generatedSymbols, Does.Contain("System.AttributeUsageAttribute..ctor(System.AttributeTargets)"));
        Assert.That(generatedSymbols, Does.Contain("System.ObjectDisposedException..ctor(string)"));
    }

    [Test]
    public async Task
        EffectSummaryTool_RuntimeTupleArraySegmentAndReferenceEqualsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            80,
            "System.Object.ReferenceEquals(object, object)",
            "System.Tuple.Create",
            "System.ValueTuple.Create",
            "System.ArraySegment`1..ctor(!0[])",
            "System.ArraySegment`1..ctor(!0[], int, int)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Object.ReferenceEquals(object, object)", "pure");
        AssertFreshnessClassification(summary, "System.Object.ReferenceEquals(object, object)", "none");
        AssertEffectVisibilityClassification(summary, "System.Object.ReferenceEquals(object, object)", "none");

        AssertPurityClassification(summary, "System.Tuple.Create(!!0, !!1)", "pure");
        AssertFreshnessClassification(summary, "System.Tuple.Create(!!0, !!1)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Tuple.Create(!!0, !!1)", "internal_only");

        AssertPurityClassification(summary, "System.ValueTuple.Create(!!0, !!1)", "pure");
        AssertFreshnessClassification(summary, "System.ValueTuple.Create(!!0, !!1)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ValueTuple.Create(!!0, !!1)", "internal_only");

        AssertPurityClassification(summary, "System.ArraySegment`1..ctor(!0[])", "pure");
        AssertFreshnessClassification(summary, "System.ArraySegment`1..ctor(!0[])", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ArraySegment`1..ctor(!0[])", "internal_only");

        AssertPurityClassification(summary, "System.ArraySegment`1..ctor(!0[], int, int)", "pure");
        AssertFreshnessClassification(summary, "System.ArraySegment`1..ctor(!0[], int, int)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ArraySegment`1..ctor(!0[], int, int)", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        Assert.That(generatedSymbols, Does.Contain("System.Object.ReferenceEquals(object, object)"));
        Assert.That(generatedSymbols, Does.Contain("System.Tuple.Create(!!0, !!1)"));
        Assert.That(generatedSymbols, Does.Contain("System.ValueTuple.Create(!!0, !!1)"));
        Assert.That(generatedSymbols, Does.Contain("System.ArraySegment`1..ctor(!0[])"));
        Assert.That(generatedSymbols, Does.Contain("System.ArraySegment`1..ctor(!0[], int, int)"));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimeObjectEqualsStaticSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsyncForAssembly(
            "System.Private.CoreLib.dll",
            20,
            "System.Object.Equals(object, object)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.Object.Equals(object, object)", "pure");
        AssertFreshnessClassification(summary, "System.Object.Equals(object, object)", "none");
        AssertEffectVisibilityClassification(summary, "System.Object.Equals(object, object)", "none");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => string.Equals(symbol, "System.Object.Equals(object, object)", StringComparison.Ordinal))
            .ToArray();

        Assert.That(generatedSymbols, Is.EqualTo(new[] { "System.Object.Equals(object, object)" }));
    }

    [Test]
    public async Task EffectSummaryTool_RuntimePureCoreConstructorsSlice_UsesGeneratedPurityCatalogEntries()
    {
        using var summary = await RunRuntimeEffectSummaryAsync(
            140,
            "System.ArgumentException..ctor(string, string)",
            "System.DivideByZeroException..ctor()",
            "System.FlagsAttribute..ctor()",
            "System.FormatException..ctor(string)",
            "System.Index..ctor(int, bool)",
            "System.IO.EndOfStreamException..ctor()",
            "System.InvalidOperationException..ctor(string)",
            "System.NotImplementedException..ctor()",
            "System.NotSupportedException..ctor(string)",
            "System.ObsoleteAttribute..ctor(string)",
            "System.OverflowException..ctor()",
            "System.PlatformNotSupportedException..ctor()",
            "System.Range..ctor(System.Index, System.Index)",
            "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute..ctor(string)",
            "System.Runtime.CompilerServices.MethodImplAttribute..ctor(System.Runtime.CompilerServices.MethodImplOptions)",
            "System.Security.AllowPartiallyTrustedCallersAttribute..ctor()",
            "System.SerializableAttribute..ctor()",
            "System.Threading.Tasks.ValueTask`1..ctor(!0)",
            "System.UIntPtr..ctor(uint)");

        var report = summary.RootElement.GetProperty("PurityReport");
        var catalogComparison = report.GetProperty("CatalogComparison");
        Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
        Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(),
            Is.EqualTo(0));

        AssertPurityClassification(summary, "System.DivideByZeroException..ctor()", "pure");
        AssertFreshnessClassification(summary, "System.DivideByZeroException..ctor()", "none");
        AssertEffectVisibilityClassification(summary, "System.DivideByZeroException..ctor()", "none");
        AssertPurityClassification(summary, "System.InvalidOperationException..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.InvalidOperationException..ctor(string)", "none");
        AssertEffectVisibilityClassification(summary, "System.InvalidOperationException..ctor(string)", "none");
        AssertPurityClassification(summary, "System.ObsoleteAttribute..ctor(string)", "pure");
        AssertFreshnessClassification(summary, "System.ObsoleteAttribute..ctor(string)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.ObsoleteAttribute..ctor(string)", "internal_only");
        AssertPurityClassification(summary,
            "System.Runtime.CompilerServices.MethodImplAttribute..ctor(System.Runtime.CompilerServices.MethodImplOptions)",
            "pure");
        AssertFreshnessClassification(summary,
            "System.Runtime.CompilerServices.MethodImplAttribute..ctor(System.Runtime.CompilerServices.MethodImplOptions)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary,
            "System.Runtime.CompilerServices.MethodImplAttribute..ctor(System.Runtime.CompilerServices.MethodImplOptions)",
            "internal_only");
        AssertPurityClassification(summary, "System.Security.AllowPartiallyTrustedCallersAttribute..ctor()", "pure");
        AssertFreshnessClassification(summary, "System.Security.AllowPartiallyTrustedCallersAttribute..ctor()",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Security.AllowPartiallyTrustedCallersAttribute..ctor()",
            "internal_only");
        AssertPurityClassification(summary, "System.Index..ctor(int, bool)", "pure");
        AssertFreshnessClassification(summary, "System.Index..ctor(int, bool)", "none");
        AssertEffectVisibilityClassification(summary, "System.Index..ctor(int, bool)", "none");
        AssertPurityClassification(summary, "System.Range..ctor(System.Index, System.Index)", "pure");
        AssertFreshnessClassification(summary, "System.Range..ctor(System.Index, System.Index)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Range..ctor(System.Index, System.Index)",
            "internal_only");
        AssertPurityClassification(summary, "System.Threading.Tasks.ValueTask`1..ctor(!0)", "pure");
        AssertFreshnessClassification(summary, "System.Threading.Tasks.ValueTask`1..ctor(!0)",
            "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.Threading.Tasks.ValueTask`1..ctor(!0)", "internal_only");
        AssertPurityClassification(summary, "System.UIntPtr..ctor(uint)", "pure");
        AssertFreshnessClassification(summary, "System.UIntPtr..ctor(uint)", "fresh_owned_object_write");
        AssertEffectVisibilityClassification(summary, "System.UIntPtr..ctor(uint)", "internal_only");

        var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("DisplayName").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();

        foreach (var symbol in new[]
                 {
                     "System.DivideByZeroException..ctor()",
                     "System.InvalidOperationException..ctor(string)",
                     "System.ObsoleteAttribute..ctor(string)",
                     "System.Runtime.CompilerServices.MethodImplAttribute..ctor(System.Runtime.CompilerServices.MethodImplOptions)",
                     "System.Security.AllowPartiallyTrustedCallersAttribute..ctor()",
                     "System.Index..ctor(int, bool)",
                     "System.Range..ctor(System.Index, System.Index)",
                     "System.Threading.Tasks.ValueTask`1..ctor(!0)",
                     "System.UIntPtr..ctor(uint)"
                 })
            Assert.That(generatedSymbols, Does.Contain(symbol));
    }

    [Test]
    public async Task EffectSummaryTool_GeneratedPurityCatalog_UsesDistinctExactKeys_ForDuplicateDisplaySymbols()
    {
        var source = """
                     public readonly struct ConversionFixture
                     {
                         private readonly int _value;

                         public ConversionFixture(int value)
                         {
                             _value = value;
                         }

                         public static explicit operator int(ConversionFixture value) => value._value;

                         public static explicit operator long(ConversionFixture value) => value._value;
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDuplicateDisplaySymbols", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            false);

        var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
        var operatorEntries = generatedCatalog.GetProperty("Entries")
            .EnumerateArray()
            .Where(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "ConversionFixture.op_Explicit(ConversionFixture)",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(operatorEntries.Length, Is.EqualTo(2));
        Assert.That(
            operatorEntries
                .Select(entry => entry.GetProperty("ExactSymbolKey").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(2));
    }

    [Test]
    public async Task EffectSummaryTool_BclFallbackInventory_EmitsReportOnlyGuesses()
    {
        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "bcl-fallback-inventory-" + Guid.NewGuid().ToString("N") + ".json");

        await RunEffectSummaryToolAsync(
            "--framework",
            "net8.0",
            "--symbol-prefix",
            "System.GC.CollectionCount",
            "--symbol-prefix",
            "System.GC.Collect",
            "--symbol-prefix",
            "System.Version..ctor",
            "--symbol-prefix",
            "System.TimeSpan..ctor",
            "--bcl-fallback-inventory",
            "--output",
            outputPath);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var inventory = summary.RootElement.GetProperty("BclFallbackInventory");
        Assert.That(inventory.GetProperty("CandidateCount").GetInt32(), Is.GreaterThanOrEqualTo(3));
        Assert.That(inventory.GetProperty("ProbablyPureCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(inventory.GetProperty("ProbablyImpureCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(inventory.GetProperty("UnknownCount").GetInt32(), Is.GreaterThanOrEqualTo(1));

        AssertInventoryEntry(
            inventory,
            "System.GC.CollectionCount(int)",
            "probably_pure",
            "value_return_no_ref_or_out");
        AssertInventoryEntry(
            inventory,
            "System.GC.Collect()",
            "probably_impure",
            "void_returning_metadata_method");
        AssertInventoryEntry(
            inventory,
            "System.Version..ctor(int, int)",
            "unknown",
            "metadata_constructor_without_body");
        AssertInventoryEntry(
            inventory,
            "System.TimeSpan..ctor(long)",
            "probably_pure",
            "value_type_constructor_value_like_parameters");
    }

    [Test]
    public async Task EffectSummaryTool_ClassifiesFreshObjectConstructionAsInternalOnly()
    {
        var source = """
                     public sealed class Box
                     {
                         private readonly int _value;

                         public Box(int value)
                         {
                             _value = value;
                         }
                     }

                     public sealed class MutableBox
                     {
                         public int Value;
                     }

                     public static class FreshObjectFixture
                     {
                         public static Box MakeConstructedBox()
                         {
                             return new Box(42);
                         }

                         public static MutableBox MakeAssignedBox()
                         {
                             var box = new MutableBox();
                             box.Value = 5;
                             return box;
                         }

                         public static void MutateExistingBox(MutableBox box)
                         {
                             box.Value = 7;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryFreshObjectWrites", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            false);

        AssertPurityClassification(summary, "Box..ctor(int)", "pure");
        AssertFreshnessClassification(summary, "Box..ctor(int)", "fresh_owned_object_write");
        AssertPurityClassification(summary, "FreshObjectFixture.MakeConstructedBox()", "pure");
        AssertPurityClassification(summary, "FreshObjectFixture.MakeAssignedBox()", "pure");
        AssertFreshnessClassification(summary, "FreshObjectFixture.MakeAssignedBox()", "fresh_owned_object_write");
        AssertPurityClassification(summary, "FreshObjectFixture.MutateExistingBox(MutableBox)", "impure",
            "object_state_write");
    }

    [Test]
    public async Task EffectSummaryTool_DoesNotTreatNonVirtualCallvirtAsDynamicDispatch()
    {
        var source = """
                     public sealed class Counter
                     {
                         private readonly int _value;

                         public Counter(int value)
                         {
                             _value = value;
                         }

                         public int GetValue()
                         {
                             return _value;
                         }
                     }

                     public static class CallvirtFixture
                     {
                         public static int Read(Counter counter)
                         {
                             return counter.GetValue();
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCallvirtFixture", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            false);

        AssertPurityClassification(summary, "Counter.GetValue()", "pure");
        AssertPurityClassification(summary, "CallvirtFixture.Read(Counter)", "pure");
        AssertEffectVisibilityClassification(summary, "CallvirtFixture.Read(Counter)", "none");
    }

    [Test]
    public async Task EffectSummaryTool_TreatsSameAssemblyDerivedReadonlyStaticFieldReadAsPure()
    {
        using var summary = await CreateSameAssemblyDerivedStaticFieldSummaryAsync();
        AssertPurityClassification(summary, "StableDerived.ReadStable()", "pure");
        AssertEffectVisibilityClassification(summary, "StableDerived.ReadStable()", "internal_only");
        AssertFreshnessClassification(summary, "StableDerived.ReadStable()", "none");
    }

    [Test]
    public async Task EffectSummaryTool_TreatsSameAssemblyDerivedMutableStaticFieldReadAsImpure()
    {
        using var summary = await CreateSameAssemblyDerivedStaticFieldSummaryAsync();

        AssertPurityClassification(summary, "MutableDerived.ReadMutable()", "impure", "global_state_read");
        AssertEffectVisibilityClassification(summary, "MutableDerived.ReadMutable()", "caller_visible");
        AssertFreshnessClassification(summary, "MutableDerived.ReadMutable()", "none");
    }

    [Test]
    public async Task EffectSummaryTool_TreatsSameAssemblyReadonlyStaticCacheReadAsPure()
    {
        using var summary = await CreateSameAssemblyDerivedStaticFieldSummaryAsync();

        AssertPurityClassification(summary, "StableCacheDerived.ReadStableToken()", "pure");
        AssertEffectVisibilityClassification(summary, "StableCacheDerived.ReadStableToken()", "internal_only");
        AssertFreshnessClassification(summary, "StableCacheDerived.ReadStableToken()", "none");

        var roots = FindMethod(summary, "StableCacheDerived.ReadStableToken()")
            .GetProperty("RootCandidates")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(roots, Does.Contain("safe_static_cache_read"));
    }

    [Test]
    public async Task EffectSummaryTool_TreatsGenericSameAssemblyReadonlyStaticCacheReadAsPure()
    {
        const string source = """
                              public sealed class Token
                              {
                              }

                              public static class GenericCache<T>
                              {
                                  public static readonly Token Value = new();
                              }

                              public static class GenericCacheConsumer
                              {
                                  public static Token Read()
                                  {
                                      return GenericCache<int>.Value;
                                  }
                              }
                              """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryGenericStaticCache", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            true);

        AssertPurityClassification(summary, "GenericCacheConsumer.Read()", "pure");
        AssertEffectVisibilityClassification(summary, "GenericCacheConsumer.Read()", "internal_only");
        AssertFreshnessClassification(summary, "GenericCacheConsumer.Read()", "none");

        var roots = FindMethod(summary, "GenericCacheConsumer.Read()")
            .GetProperty("RootCandidates")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(roots, Does.Contain("safe_static_cache_read"));
    }

    [Test]
    public async Task EffectSummaryTool_CapturesGenericSameAssemblyStringComparerReceiverEvidence()
    {
        const string source = """
                              using System;

                              public static class GenericComparerCache<T>
                              {
                                  public static readonly StringComparer Value = StringComparer.Ordinal;
                              }

                              public static class GenericComparerConsumer
                              {
                                  public static bool Compare(string left, string right)
                                  {
                                      return GenericComparerCache<int>.Value.Equals(left, right);
                                  }
                              }
                              """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryGenericStringComparerCache", source);
        using var summary = await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true,
            true);

        AssertPurityClassification(summary, "GenericComparerConsumer.Compare(string, string)", "pure");

        var callSite = FindMethod(summary, "GenericComparerConsumer.Compare(string, string)")
            .GetProperty("CallSites")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("ArgumentEvidence")
                .EnumerateArray()
                .Any(evidence =>
                    string.Equals(evidence.GetProperty("Target").GetString(), "receiver", StringComparison.Ordinal) &&
                    string.Equals(evidence.GetProperty("Type").GetString(), "System.StringComparer",
                        StringComparison.Ordinal)));

        var receiverEvidence = callSite.GetProperty("ArgumentEvidence")
            .EnumerateArray()
            .Single(evidence =>
                string.Equals(evidence.GetProperty("Target").GetString(), "receiver", StringComparison.Ordinal) &&
                string.Equals(evidence.GetProperty("Type").GetString(), "System.StringComparer",
                    StringComparison.Ordinal));

        Assert.That(receiverEvidence.GetProperty("Value").GetString(), Is.EqualTo("System.StringComparer.Ordinal"));
    }
}
