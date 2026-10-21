using System.Globalization;
using System.Numerics;
using System.Text;

namespace SharpProof.Ir;

internal sealed class ResourceRuntimeLayoutContext
{
    internal string Runtime { get; }
    internal string Version { get; }
    internal string Architecture { get; }
    internal int PointerBytes { get; }

    internal ResourceRuntimeLayoutContext(string runtime, string version, string architecture, int pointerBytes)
    {
        Runtime = Checked(runtime, nameof(runtime));
        Version = Checked(version, nameof(version));
        Architecture = Checked(architecture, nameof(architecture));
        if (pointerBytes is < 1 or > 16)
        { throw new ArgumentOutOfRangeException(nameof(pointerBytes)); }
        PointerBytes = pointerBytes;
    }

    private static string Checked(string value, string parameter)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        parameter = ArgumentNullGuard.NotNull(parameter, nameof(parameter));
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(static character => char.IsControl(character)))
        { throw new ArgumentException("A layout context identity is malformed.", parameter); }
        return value;
    }
}

internal sealed class ResourceAllocationFootprint
{
    internal ResourceExpression Lower { get; }
    internal ResourceExpression Upper { get; }
    internal int Objects { get; }
    internal bool IsExact => Lower.Equals(Upper);

    internal ResourceAllocationFootprint(ResourceExpression lower, ResourceExpression upper, int objects, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Lower = ArgumentNullGuard.NotNull(lower, nameof(lower));
        Upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        if (objects is < 1 or > 2)
        { throw new ArgumentOutOfRangeException(nameof(objects)); }
        if (!ResourceBoundOrder.Proves(lower, upper, cancellation))
        { throw new ArgumentException("Allocation byte bounds must be ordered.", nameof(upper)); }
        Objects = objects;
    }
}

internal sealed class ResourceLayoutCheck
{
    internal ResourceAllocationFootprint? Bounds { get; }
    internal string? UnknownReason { get; }
    internal bool Verified => Bounds is not null;

    internal ResourceLayoutCheck(ResourceAllocationFootprint? bounds, string? unknownReason)
    {
        if (bounds is null ? string.IsNullOrWhiteSpace(unknownReason) : unknownReason is not null)
        { throw new ArgumentException("A layout result requires either bounds or an Unknown reason.", nameof(unknownReason)); }
        Bounds = bounds;
        UnknownReason = unknownReason;
    }
}

// Conditional storage of fresh source-owned instances. The caller must bind
// this model to target evidence; a context declaration is not runtime detection.
// Referenced objects, runtime bookkeeping and constructor effects are separate.
internal static class ResourceRuntimeLayout
{
    internal const string ModelId = "coreclr-8.0.32-x64-source-storage-v1";
    internal const string RuntimeFamily = "CoreCLR";
    internal const string RuntimeVersion = "8.0.32";
    internal const string TargetArchitecture = "x64";
    internal const string SourceCommit = "b9a49e4a6d3a444a3685e318c74a7892d01873f7";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/" + SourceCommit + "/src/coreclr/vm/object.h";
    internal const int PointerBytes = 8;
    internal const int Alignment = 8;
    internal const int ObjectHeader = 16;
    internal const int MinimumObject = 24;
    internal const int ArrayHeader = 24;
    internal const int StringHeader = 22;
    internal const int DelegateBytes = 64;

    internal static string ContentSha256 { get; } = HashEncoding.ComputeSha256Hex(Encoding.UTF8.GetBytes(
        ModelId + "\n" + EvidenceSource + "\n" + RuntimeFamily + "\n" + RuntimeVersion + "\n" + TargetArchitecture + "\n" +
        FormattableString.Invariant($"pointer={PointerBytes};alignment={Alignment};object={ObjectHeader};minimum={MinimumObject};array={ArrayHeader};string={StringHeader};delegate={DelegateBytes}\n") +
        string.Join("\n", Enum.GetValues(typeof(ResourceStorageKind)).Cast<ResourceStorageKind>()
            .Where(static storage => storage != ResourceStorageKind.Unspecified)
            .Select(static storage => storage + ":" + Width(storage).ToString(CultureInfo.InvariantCulture))) +
        "\nobject:aligned-packed-lower,aligned-pointer-slot-upper;closure:frame+delegate;fresh-instances-only\n").ToImmutableArray());

    internal static ResourceLayoutCheck Bound(ResourceAllocationShape shape, ResourceRuntimeLayoutContext context, CancellationToken cancellation)
    {
        shape = ArgumentNullGuard.NotNull(shape, nameof(shape));
        context = ArgumentNullGuard.NotNull(context, nameof(context));
        cancellation.ThrowIfCancellationRequested();
        if (context.Runtime != RuntimeFamily || context.Version != RuntimeVersion || context.Architecture != TargetArchitecture || context.PointerBytes != PointerBytes)
        { return new(null, "IncompatibleResourceRuntimeLayout"); }
        try
        {
            switch (shape.Kind)
            {
                case ResourceAllocationKind.Array:
                    var array = ResourceExpression.Aligned(ResourceExpression.Add(ResourceExpression.Literal(ArrayHeader),
                        ResourceExpression.Multiply(shape.Extent, ResourceExpression.Literal(Width(shape.Storage)))), Alignment);
                    return new(new(array, array, 1, cancellation), null);
                case ResourceAllocationKind.String:
                    var text = ResourceExpression.Aligned(ResourceExpression.Add(ResourceExpression.Literal(StringHeader),
                        ResourceExpression.Multiply(shape.Extent, ResourceExpression.Literal(2))), Alignment);
                    return new(new(text, text, 1, cancellation), null);
                case ResourceAllocationKind.Box:
                    var boxed = ObjectBytes(Width(shape.Storage));
                    return new(new(boxed, boxed, 1, cancellation), null);
                case ResourceAllocationKind.Delegate:
                    var allocatedDelegate = ResourceExpression.Literal(DelegateBytes);
                    return new(new(allocatedDelegate, allocatedDelegate, 1, cancellation), null);
                case ResourceAllocationKind.Object:
                case ResourceAllocationKind.Closure:
                    BigInteger packed = 0, padded = 0;
                    foreach (var field in shape.Fields)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        packed += Width(field);
                        padded += PointerBytes;
                    }
                    var lower = ObjectBytes(packed);
                    var upper = ObjectBytes(padded);
                    if (shape.Kind == ResourceAllocationKind.Closure)
                    {
                        var payload = ResourceExpression.Literal(DelegateBytes);
                        return new(new(ResourceExpression.Add(lower, payload), ResourceExpression.Add(upper, payload), 2, cancellation), null);
                    }
                    return new(new(lower, upper, 1, cancellation), null);
                default:
                    return new(null, "UnsupportedManagedAllocationShape");
            }
        }
        catch (ResourceExpressionLimitException)
        { return new(null, "ResourceLayoutBudgetExceeded"); }
    }

    private static ResourceExpression ObjectBytes(BigInteger payload)
    {
        return ResourceExpression.Max(ResourceExpression.Literal(MinimumObject),
            ResourceExpression.Aligned(ResourceExpression.Literal(ObjectHeader + payload), Alignment));
    }

    private static int Width(ResourceStorageKind storage)
    {
        return storage switch
        {
            ResourceStorageKind.Byte => 1,
            ResourceStorageKind.Word16 => 2,
            ResourceStorageKind.Word32 => 4,
            ResourceStorageKind.Word64 or ResourceStorageKind.Reference or ResourceStorageKind.NativeInteger => PointerBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(storage))
        };
    }
}
