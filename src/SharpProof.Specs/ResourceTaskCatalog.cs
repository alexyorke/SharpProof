using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Specs;

// Conditional source-work semantics, not a framework allocation summary or
// evidence that the deployment uses this runtime.
internal static class ResourceTaskCatalog
{
    internal const string Model = "task-join-source-at-most-once-v1";
    internal const string Witness = "bcl.task.run-action.when-all-array";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs";
    internal static string ContentSha256 { get; } = Hash(Model + "\n" + ResourceMetadataCatalog.Runtime + "\n" + EvidenceSource +
        "\nTask.Run(Action);default-scheduler;no-cancellation-token;at-most-once\n" +
        "Task.WhenAll(Task[]);fresh-closed-children;work-through-join-completion\n" +
        "isolated-source-callbacks;all-overlap-orders;per-root-invocation\n" +
        "excludes-framework-scheduler-ambient-callback-work;no-framework-allocation-summary\n");

    internal static bool IsReviewedReference(string assembly)
    {
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return assembly is
            "System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e" or
            "System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    }

    internal static string AssumptionId(string callableId, string assembly)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return "resource-metadata-" + Model + ":" + ContentSha256 + ":" + Witness + ":" + EvidenceSource +
            ":owner:" + Hash(callableId + "\n" + assembly);
    }

    private static string Hash(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }
}
