#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class JsonIgnoreAttribute : Attribute
    {
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceProviderExtensions
    {
        public static T GetRequiredService<T>(this IServiceProvider provider) => default;
    }
}

public sealed class Worker
{
    public Task RunAsync() => Task.CompletedTask;
}

public static class AsyncBugs
{
    private static Task<int> ReadAsync() => Task.Delay(1).ContinueWith(_ => 1);

    public static async Task AwaitNullableAsync(Worker worker) => await worker?.RunAsync();

    public static string Render(Task<int> task) => $"value={task}";

    public static TaskCompletionSource<int> CreateCompletion() => new();

    public static async void FireAndForget()
    {
        await Task.Yield();
    }

    public static async Task<int> BlockAsync()
    {
        await Task.Yield();
        return ReadAsync().Result;
    }

    public static Task ReturnNull() => null;

    public static void UseTaskAsResource()
    {
        using var task = ReadAsync();
    }

    public static async Task<int> ValidateLateAsync(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        await Task.Yield();
        return value.Length;
    }
}

public struct MutableCounter
{
    public int Value { get; set; }
}

public sealed class DisposableOwner
{
    private readonly MemoryStream _stream = new();
}

public static class CollectionBugs
{
    public static void MutateDuringEnumeration(List<int> values)
    {
        foreach (var value in values)
            if (value > 0)
                values.Remove(value);
    }

    public static List<Action> CaptureLoopVariable()
    {
        var actions = new List<Action>();
        for (var index = 0; index < 3; index++)
            actions.Add(() => Console.WriteLine(index));
        return actions;
    }

    public static void ConstructClients(int count)
    {
        for (var index = 0; index < count; index++)
            using var client = new HttpClient();
    }

    public static int Race()
    {
        var count = 0;
        Parallel.For(0, 100, _ => count++);
        return count;
    }

    public static int EnumerateConcurrent(ConcurrentDictionary<int, int> values) =>
        values.Where(pair => pair.Value > 0).Count();

    public static object BoxInLoop(int count)
    {
        object boxed = null;
        for (var index = 0; index < count; index++)
            boxed = index;
        return boxed;
    }
}

public static class QueryBugs
{
    public static int FirstLength(IEnumerable<string> values) => values.FirstOrDefault().Length;

    public static IEnumerable<int> MaterializeEarly(IQueryable<int> values) =>
        values.ToList().Where(value => value > 0);

    public static IEnumerable<int> DeferredSideEffect(IEnumerable<int> values)
    {
        var total = 0;
        return values.Select(value => total += value);
    }

    public static IQueryable<int> TranslationRisk(IQueryable<int> values) =>
        values.Where(value => IsPositive(value));

    public static void DiscardQuery(IEnumerable<int> values)
    {
        values.Where(value => value > 0);
    }

    private static bool IsPositive(int value) => value > 0;
}

public sealed class Node
{
    public Node Next { get; set; }
}

public sealed class Payload
{
    [Newtonsoft.Json.JsonIgnore]
    public string Secret { get; set; }
}

public static class SerializationBugs
{
    public static string SerializeCycle(Node node) => JsonSerializer.Serialize(node);

    public static string SerializeWrongAttribute(Payload payload) => JsonSerializer.Serialize(payload);
}

public sealed class Request
{
    [Required]
    public int Count { get; set; }
}

public sealed class ContainerService : IDisposable
{
    public void Dispose()
    {
    }
}

public static class RemainingBugs
{
    public static byte[] Allocate(int count, int width) => new byte[count * width];

    public static int Difference(int left, int right) => left - left;

    public static void DisposeContainerService(IServiceProvider provider)
    {
        using var service = provider.GetRequiredService<ContainerService>();
    }
}

#pragma warning disable
#pragma warning restore
