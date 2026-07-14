using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class CommonBugAsyncAnalyzerTests : CommonBugAnalyzerTestBase
{
    [Test]
    public async Task AwaitNullConditional_Reports()
    {
        const string source = """
                              #nullable disable
                              using System.Threading.Tasks;
                              public sealed class Worker { public Task RunAsync() => Task.CompletedTask; }
                              public sealed class Sample
                              {
                                  public async Task RunAsync(Worker worker)
                                  {
                                      await worker?.RunAsync();
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.AwaitNullConditionalId);
    }

    [Test]
    public async Task AwaitNullConditionalCoalescedToCompletedTask_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System.Threading.Tasks;
                              public sealed class Worker { public Task RunAsync() => Task.CompletedTask; }
                              public sealed class Sample
                              {
                                  public async Task RunAsync(Worker? worker)
                                  {
                                      await (worker?.RunAsync() ?? Task.CompletedTask);
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.AwaitNullConditionalId);
    }

    [Test]
    public async Task TaskInterpolation_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static string Render(Task<int> value) => $"value={value}";
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.TaskConvertedToStringId);
    }

    [Test]
    public async Task AwaitedTaskInterpolation_DoesNotReport()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static async Task<string> Render(Task<int> value) => $"value={await value}";
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.TaskConvertedToStringId);
    }

    [Test]
    public async Task TaskCompletionSourceWithoutAsyncContinuations_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static TaskCompletionSource<int> Create() => new();
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.TaskCompletionSourceContinuationsId);
    }

    [Test]
    public async Task TaskCompletionSourceWithAsyncContinuations_DoesNotReport()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static TaskCompletionSource<int> Create() =>
                                      new(TaskCreationOptions.RunContinuationsAsynchronously);
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.TaskCompletionSourceContinuationsId);
    }

    [Test]
    public async Task AsyncVoidNonEventHandler_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public sealed class Sample
                              {
                                  public async void Run()
                                  {
                                      await Task.Yield();
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.AsyncVoidId);
    }

    [Test]
    public async Task AsyncVoidEventHandlerShape_DoesNotReport()
    {
        const string source = """
                              using System;
                              using System.Threading.Tasks;
                              public sealed class Sample
                              {
                                  private async void OnChanged(object? sender, EventArgs args)
                                  {
                                      await Task.Yield();
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.AsyncVoidId);
    }

    [Test]
    public async Task TaskResultInsideAsyncMethod_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  private static Task<int> ReadAsync() => Task.Delay(1).ContinueWith(_ => 1);
                                  public static async Task<int> RunAsync()
                                  {
                                      await Task.Yield();
                                      return ReadAsync().Result;
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.BlockingAsyncId);
    }

    [Test]
    public async Task CompletedTaskResultInsideAsyncMethod_DoesNotReport()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static async Task<int> RunAsync()
                                  {
                                      await Task.Yield();
                                      return Task.FromResult(1).Result;
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.BlockingAsyncId);
    }

    [Test]
    public async Task NonAsyncTaskMethodReturningNull_Reports()
    {
        const string source = """
                              #nullable disable
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static Task RunAsync() => null;
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.NullTaskReturnId);
    }

    [Test]
    public async Task AsyncTaskOfNullableReturningNullResult_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static async Task<string?> ReadAsync()
                                  {
                                      await Task.Yield();
                                      return null;
                                  }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.NullTaskReturnId);
    }

    [Test]
    public async Task TaskUsedDirectlyInUsing_Reports()
    {
        const string source = """
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  private static Task<int> ReadAsync() => Task.FromResult(1);
                                  public static void Run()
                                  {
                                      using var task = ReadAsync();
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.TaskUsedAsDisposableId);
    }

    [Test]
    public async Task PublicAsyncGuardValidation_ReportsInformationalDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static async Task<int> ReadAsync(string value)
                                  {
                                      ArgumentNullException.ThrowIfNull(value);
                                      await Task.Yield();
                                      return value.Length;
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.AsyncValidationDeferredId);
    }

}
