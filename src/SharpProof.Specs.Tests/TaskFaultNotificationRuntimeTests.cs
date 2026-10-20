using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class TaskFaultNotificationRuntimeTests
{
    [TestCase(1, 216)]
    [TestCase(2, 224)]
    [TestCase(8, 272)]
    [TestCase(16, 336)]
    public void PinnedConstructorAllocationsFitTheIndependentConstructionUpper(int count, int upper)
    {
        AssertPinnedRuntime();
        var construct = CreateFactory();
        var dispatches = new List<ExceptionDispatchInfo>(count);
        for (var index = 0; index < count; index++)
        { dispatches.Add(ExceptionDispatchInfo.Capture(new InvalidOperationException("source"))); }
        for (var index = 0; index < 64; index++)
        { GC.KeepAlive(construct(dispatches)); }
        const int repetitions = 128;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < repetitions; index++)
        { GC.KeepAlive(construct(dispatches)); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Positive.And.LessThanOrEqualTo((long)upper * repetitions));
    }

    [Test]
    public void PinnedRuntimeFieldsMatchTheCompleteInheritedStorageTables()
    {
        AssertPinnedRuntime();
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var exception = typeof(Exception).GetFields(instance);
        string[] expected = ["_exceptionMethod", "_message", "_data", "_innerException", "_helpURL", "_stackTrace", "_watsonBuckets",
            "_stackTraceString", "_remoteStackTraceString", "_dynamicMethods", "_source", "_ipForWatsonBuckets", "_xptrs", "_xcode", "_HResult"];
        Assert.That(exception.Select(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.Name).Order(StringComparer.Ordinal),
            Is.EqualTo(expected.Order(StringComparer.Ordinal)));
        Assert.That(exception.Select(Storage).Order(), Is.EqualTo(ResourceTaskFaultNotificationCatalog.ExceptionFields.Order()));
        var aggregate = typeof(AggregateException).GetFields(instance);
        string[] aggregateNames = ["_innerExceptions", "_rocView"];
        Assert.That(aggregate.Select(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.Name).Order(StringComparer.Ordinal),
            Is.EqualTo(aggregateNames.Order(StringComparer.Ordinal)));
        Assert.That(exception.Concat(aggregate).Select(Storage).Order(), Is.EqualTo(ResourceTaskFaultNotificationCatalog.AggregateFields.Order()));
        Assert.That(typeof(AggregateException).BaseType, Is.EqualTo(typeof(Exception)));
        Assert.That(typeof(UnobservedTaskExceptionEventArgs).BaseType, Is.EqualTo(typeof(EventArgs)));
        Assert.That(typeof(EventArgs).GetFields(instance), Is.Empty);
        Assert.That(typeof(UnobservedTaskExceptionEventArgs).GetFields(instance).Select(Storage).Order(),
            Is.EqualTo(ResourceTaskFaultNotificationCatalog.EventFields.Order()));
    }

    private static void AssertPinnedRuntime()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
    }

    private static ResourceStorageKind Storage(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.FieldType == typeof(bool) ? ResourceStorageKind.Byte :
            field.FieldType == typeof(int) ? ResourceStorageKind.Word32 :
            field.FieldType == typeof(IntPtr) || field.FieldType == typeof(UIntPtr) ? ResourceStorageKind.NativeInteger :
            !field.FieldType.IsValueType ? ResourceStorageKind.Reference : ResourceStorageKind.Unspecified;
    }

    private static Func<List<ExceptionDispatchInfo>, UnobservedTaskExceptionEventArgs> CreateFactory()
    {
        Type[] aggregateParameters = [typeof(string), typeof(List<ExceptionDispatchInfo>)];
        var aggregate = typeof(AggregateException).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, aggregateParameters, null)!;
        Type[] eventParameters = [typeof(AggregateException)];
        var arguments = typeof(UnobservedTaskExceptionEventArgs).GetConstructor(eventParameters)!;
        Type[] factoryParameters = [typeof(List<ExceptionDispatchInfo>)];
        var method = new DynamicMethod("FaultNotification", typeof(UnobservedTaskExceptionEventArgs), factoryParameters,
            typeof(TaskFaultNotificationRuntimeTests).Module, true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldstr, "faults");
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Newobj, aggregate);
        generator.Emit(OpCodes.Newobj, arguments);
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<List<ExceptionDispatchInfo>, UnobservedTaskExceptionEventArgs>>();
    }
}
