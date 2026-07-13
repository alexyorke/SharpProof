using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class CommonBugCollectionAnalyzerTests
{
    [Test]
    public async Task MutatingListInsideItsForeach_Reports()
    {
        const string source = """
                              using System.Collections.Generic;
                              public static class Sample
                              {
                                  public static void RemoveEven(List<int> values)
                                  {
                                      foreach (var value in values)
                                          if (value % 2 == 0)
                                              values.Remove(value);
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.CollectionMutationDuringEnumerationId);
    }

    [Test]
    public async Task MutatingDifferentListInsideForeach_DoesNotReport()
    {
        const string source = """
                              using System.Collections.Generic;
                              public static class Sample
                              {
                                  public static void Copy(List<int> source, List<int> target)
                                  {
                                      foreach (var value in source)
                                          target.Add(value);
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.CollectionMutationDuringEnumerationId);
    }

    [Test]
    public async Task EscapingLambdaCapturesForVariable_Reports()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              public static class Sample
                              {
                                  public static List<Action> Build()
                                  {
                                      var actions = new List<Action>();
                                      for (var index = 0; index < 3; index++)
                                          actions.Add(() => Console.WriteLine(index));
                                      return actions;
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.CapturedLoopVariableId);
    }

    [Test]
    public async Task EscapingLambdaCapturesPerIterationCopy_DoesNotReport()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              public static class Sample
                              {
                                  public static List<Action> Build()
                                  {
                                      var actions = new List<Action>();
                                      for (var index = 0; index < 3; index++)
                                      {
                                          var copy = index;
                                          actions.Add(() => Console.WriteLine(copy));
                                      }
                                      return actions;
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.CapturedLoopVariableId);
    }

    [Test]
    public async Task MutableStruct_Reports()
    {
        const string source = """
                              public struct Counter
                              {
                                  public int Value { get; set; }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.MutableStructId);
    }

    [Test]
    public async Task ReadonlyStruct_DoesNotReport()
    {
        const string source = """
                              public readonly struct Counter
                              {
                                  public Counter(int value) => Value = value;
                                  public int Value { get; }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.MutableStructId);
    }

    [Test]
    public async Task DefinitelyOwnedDisposableFieldWithoutOwnerContract_Reports()
    {
        const string source = """
                              using System.IO;
                              public sealed class Owner
                              {
                                  private readonly MemoryStream _stream = new();
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.OwnedDisposableFieldId);
    }

    [Test]
    public async Task DisposableOwnerContract_DoesNotReportOwnershipRule()
    {
        const string source = """
                              using System;
                              using System.IO;
                              public sealed class Owner : IDisposable
                              {
                                  private readonly MemoryStream _stream = new();
                                  public void Dispose() => _stream.Dispose();
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.OwnedDisposableFieldId);
    }

    [Test]
    public async Task HttpClientConstructedInsideLoop_Reports()
    {
        const string source = """
                              using System.Net.Http;
                              public static class Sample
                              {
                                  public static void Run(int count)
                                  {
                                      for (var index = 0; index < count; index++)
                                      {
                                          using var client = new HttpClient();
                                      }
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.HttpClientInLoopId);
    }

    [Test]
    public async Task ReusedHttpClientOutsideLoop_DoesNotReport()
    {
        const string source = """
                              using System.Net.Http;
                              public static class Sample
                              {
                                  public static void Run(int count)
                                  {
                                      using var client = new HttpClient();
                                      for (var index = 0; index < count; index++)
                                          _ = client.DefaultRequestHeaders;
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.HttpClientInLoopId);
    }

    [Test]
    public async Task ParallelCallbackMutatesCapturedLocal_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static int Count()
                                  {
                                      var count = 0;
                                      Parallel.For(0, 100, _ => count++);
                                      return count;
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.UnsynchronizedSharedMutationId);
    }

    [Test]
    public async Task ParallelCallbackUsesInterlocked_DoesNotReport()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static int Count()
                                  {
                                      var count = 0;
                                      Parallel.For(0, 100, _ => Interlocked.Increment(ref count));
                                      return count;
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.UnsynchronizedSharedMutationId);
    }

    [Test]
    public async Task LinqEnumeratesConcurrentCollection_Reports()
    {
        const string source = """
                              using System.Collections.Concurrent;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static int Count(ConcurrentDictionary<int, int> values) =>
                                      values.Where(pair => pair.Value > 0).Count();
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.ConcurrentCollectionEnumerationId);
    }

    [Test]
    public async Task BoxingInsideLoop_Reports()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static object Run(int count)
                                  {
                                      object result = 0;
                                      for (var index = 0; index < count; index++)
                                          result = index;
                                      return result;
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.BoxingInLoopId);
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
