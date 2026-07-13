using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class CommonBugAdditionalAnalyzerTests
{
    [Test]
    public async Task IdenticalIntegerOperands_Report()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static int Difference(int left, int right) => left - left;
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.IdenticalOperandsId);
    }

    [Test]
    public async Task DifferentIntegerOperands_DoNotReport()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static int Difference(int left, int right) => left - right;
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.IdenticalOperandsId);
    }

    [Test]
    public async Task IdenticalFloatingPointOperands_DoNotReportBecauseOfNaN()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static bool IsOrdinary(double value) => value == value;
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.IdenticalOperandsId);
    }

    [Test]
    public async Task UsingContainerResolvedService_Reports()
    {
        const string source = """
                              using System;
                              using Microsoft.Extensions.DependencyInjection;

                              namespace Microsoft.Extensions.DependencyInjection
                              {
                                  public static class ServiceProviderExtensions
                                  {
                                      public static T GetRequiredService<T>(this IServiceProvider provider) => default!;
                                  }
                              }

                              public sealed class Service : IDisposable
                              {
                                  public void Dispose() { }
                              }

                              public static class Sample
                              {
                                  public static void Run(IServiceProvider provider)
                                  {
                                      using var service = provider.GetRequiredService<Service>();
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.ContainerOwnedServiceDisposedId);
    }

    [Test]
    public async Task DirectlyDisposingContainerResolvedService_Reports()
    {
        const string source = """
                              using System;
                              using Microsoft.Extensions.DependencyInjection;

                              namespace Microsoft.Extensions.DependencyInjection
                              {
                                  public static class ServiceProviderExtensions
                                  {
                                      public static T GetRequiredService<T>(this IServiceProvider provider) => default!;
                                  }
                              }

                              public sealed class Service : IDisposable
                              {
                                  public void Dispose() { }
                              }

                              public static class Sample
                              {
                                  public static void Run(IServiceProvider provider) =>
                                      provider.GetRequiredService<Service>().Dispose();
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.ContainerOwnedServiceDisposedId);
    }

    [Test]
    public async Task UsingDirectlyCreatedService_DoesNotReportContainerOwnership()
    {
        const string source = """
                              using System;
                              public sealed class Service : IDisposable
                              {
                                  public void Dispose() { }
                              }

                              public static class Sample
                              {
                                  public static void Run()
                                  {
                                      using var service = new Service();
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.ContainerOwnedServiceDisposedId);
    }

    [Test]
    public async Task DiscardedDeferredQuery_Reports()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static void Run(IEnumerable<int> values)
                                  {
                                      values.Where(value => value > 0);
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.UnconsumedDeferredQueryId);
    }

    [Test]
    public async Task UnusedDeferredQueryLocal_Reports()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static void Run(IEnumerable<int> values)
                                  {
                                      var query = values.Where(value => value > 0);
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.UnconsumedDeferredQueryId);
    }

    [Test]
    public async Task EnumeratedDeferredQueryLocal_DoesNotReport()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static int Run(IEnumerable<int> values)
                                  {
                                      var query = values.Where(value => value > 0);
                                      return query.Count();
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.UnconsumedDeferredQueryId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            analyzerFeatures: AnalyzerFeatures.CommonBugs);
    }

    private static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain(diagnosticId));
    }

    private static void AssertMissing(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain(diagnosticId));
    }
}
