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
    [TestCase(17, 344)]
    [TestCase(32, 464)]
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

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TwoFaultRecordingSitesRetainBothDispatchesWithinTheFixedCarrierEnvelope(bool schedulerFirst, bool observeBetween)
    {
        AssertPinnedRuntime();
        var record = CreateFaultRecorder();
        Exception scheduling = new TaskSchedulerException("queue failure");
        Exception callback = new InvalidOperationException("callback failure");
        var first = schedulerFirst ? scheduling : callback;
        var second = schedulerFirst ? callback : scheduling;
        for (var index = 0; index < 64; index++)
        {
            var warm = new Task(static () => { });
            record(warm, first);
            if (observeBetween)
            { _ = ReadFaultsAndMarkHandled(warm); }
            record(warm, second);
            _ = ReadFaultsAndMarkHandled(warm);
        }
        var task = new Task(static () => { });
        var before = GC.GetAllocatedBytesForCurrentThread();
        record(task, first);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(ReadHolderHandled(task), Is.False);
        if (observeBetween)
        {
            _ = ReadFaultsAndMarkHandled(task);
            Assert.That(ReadHolderHandled(task), Is.True);
        }
        // Reflection/observation work stays outside both measured intervals.
        before = GC.GetAllocatedBytesForCurrentThread();
        record(task, second);
        bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(ReadHolderHandled(task), Is.False);
        var faults = ReadFaultsAndMarkHandled(task);
        Assert.That(ReadHolderHandled(task), Is.True);
        Assert.That(faults, Has.Count.EqualTo(2));
        Assert.That(faults.Capacity, Is.EqualTo(2));
        Assert.That(faults[0].SourceException, Is.SameAs(first));
        Assert.That(faults[1].SourceException, Is.SameAs(second));
        Assert.That(faults[0], Is.Not.SameAs(faults[1]));
        // Two independent 368-byte envelopes include losing publication
        // candidates and list growth. Original payloads/traces are excluded.
        Assert.That(bytes, Is.Positive.And.LessThanOrEqualTo(736));
        Assert.That(ResourceTaskFaultCarrierCatalog.MaximumChildFaultRecords, Is.EqualTo(2));
    }

    private static Action<Task, Exception> CreateFaultRecorder()
    {
        var target = typeof(Task).GetMethod("AddException", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object)], null)!;
        var method = new DynamicMethod("RecordTaskFault", typeof(void), [typeof(Task), typeof(Exception)],
            typeof(TaskFaultNotificationRuntimeTests).Module, true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Call, target);
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<Task, Exception>>();
    }

    private static object FaultHolder(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var contingentField = typeof(Task).GetField("m_contingentProperties", instance)!;
        var contingent = contingentField.GetValue(task)!;
        var holderField = contingent.GetType().GetField("m_exceptionsHolder", instance)!;
        return holderField.GetValue(contingent)!;
    }

    private static bool ReadHolderHandled(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var holder = FaultHolder(task);
        return (bool)holder.GetType().GetField("m_isHandled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(holder)!;
    }

    private static List<ExceptionDispatchInfo> ReadFaultsAndMarkHandled(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var holder = FaultHolder(task);
        _ = holder.GetType().GetMethod("MarkAsHandled", instance)!.Invoke(holder, [false]);
        return (List<ExceptionDispatchInfo>)holder.GetType().GetField("m_faultExceptions", instance)!.GetValue(holder)!;
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
