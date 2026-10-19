using System.Collections.Immutable;

namespace SharpProof.Ir;

internal enum ResourceAllocationKind { Unspecified, Array, Object, String, Box, Delegate, Closure }
internal enum ResourceStorageKind { Unspecified, Byte, Word16, Word32, Word64, Reference, NativeInteger }

// A fresh allocation's storage shape, not a producer-supplied byte verdict.
// Object fields are distinct, non-overlapping instance slots without custom
// layout or inherited payload. Closure bundles own one frame and one delegate.
internal sealed class ResourceAllocationShape
{
    internal const int ModelVersion = 1;
    internal const int MaximumFields = 1024;
    internal ResourceAllocationKind Kind { get; }
    internal ResourceExpression Extent { get; }
    internal ResourceStorageKind Storage { get; }
    internal ImmutableArray<ResourceStorageKind> Fields { get; }
    internal static ResourceAllocationShape Unsupported { get; } = new(ResourceAllocationKind.Unspecified, ResourceExpression.Zero, default, []);

    private ResourceAllocationShape(ResourceAllocationKind kind, ResourceExpression extent,
        ResourceStorageKind storage, ImmutableArray<ResourceStorageKind> fields)
    {
        Extent = ArgumentNullGuard.NotNull(extent, nameof(extent));
        if (fields.IsDefault)
        { throw new ArgumentException("Fields must be initialized.", nameof(fields)); }
        if (fields.Length > MaximumFields)
        { throw new ResourceExpressionLimitException(); }
        foreach (var field in fields)
        { RequireStorage(field, nameof(fields)); }
        Kind = kind;
        Storage = storage;
        Fields = fields;
    }

    internal static ResourceAllocationShape Array(ResourceStorageKind element, ResourceExpression length)
    {
        length = ArgumentNullGuard.NotNull(length, nameof(length));
        RequireStorage(element, nameof(element));
        return new(ResourceAllocationKind.Array, length, element, []);
    }

    internal static ResourceAllocationShape Object(ImmutableArray<ResourceStorageKind> fields)
    {
        if (fields.IsDefault)
        { throw new ArgumentException("Fields must be initialized.", nameof(fields)); }
        return new(ResourceAllocationKind.Object, ResourceExpression.One, default, fields);
    }

    internal static ResourceAllocationShape String(ResourceExpression length)
    {
        length = ArgumentNullGuard.NotNull(length, nameof(length));
        return new(ResourceAllocationKind.String, length, ResourceStorageKind.Word16, []);
    }

    internal static ResourceAllocationShape Box(ResourceStorageKind value)
    {
        RequireStorage(value, nameof(value));
        if (value == ResourceStorageKind.Reference)
        { throw new ArgumentException("A reference slot is not a boxed value payload.", nameof(value)); }
        return new(ResourceAllocationKind.Box, ResourceExpression.One, value, []);
    }

    internal static ResourceAllocationShape Delegate()
    { return new(ResourceAllocationKind.Delegate, ResourceExpression.One, default, []); }

    internal static ResourceAllocationShape Closure(ImmutableArray<ResourceStorageKind> captures)
    {
        if (captures.IsDefault)
        { throw new ArgumentException("Captures must be initialized.", nameof(captures)); }
        return new(ResourceAllocationKind.Closure, ResourceExpression.One, default, captures);
    }

    private static void RequireStorage(ResourceStorageKind storage, string parameter)
    {
        parameter = ArgumentNullGuard.NotNull(parameter, nameof(parameter));
        if (storage == ResourceStorageKind.Unspecified || !Enum.IsDefined(typeof(ResourceStorageKind), storage))
        { throw new ArgumentOutOfRangeException(parameter); }
    }
}
