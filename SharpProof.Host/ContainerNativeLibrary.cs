using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpProof.Host;

internal sealed class Z3ResolverGate
{
    private readonly TaskCompletionSource<IntPtr> _publication =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Publish(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "The Z3 resolver cannot publish a null native handle.",
                nameof(handle));
        }
        _publication.TrySetResult(handle);
    }

    internal void Fail(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _publication.TrySetException(failure);
    }

    internal IntPtr Resolve()
    {
        var handle = _publication.Task.GetAwaiter().GetResult();
        return handle == IntPtr.Zero
            ? throw new DllNotFoundException(
                "The verified Z3 native handle was not published.")
            : handle;
    }
}

public static class ContainerNativeLibrary
{
    private const string Z3ImportName = "libz3";
    private static readonly object Synchronization = new();
    private static readonly Z3ResolverGate ResolverGate = new();
    private static Assembly? _z3Assembly;
    private static IntPtr _z3Handle;

    public static void InstallZ3ResolverRequired(Assembly z3Assembly)
    {
        ArgumentNullException.ThrowIfNull(z3Assembly);
        lock (Synchronization)
        {
            if (_z3Handle != IntPtr.Zero)
            {
                if (!ReferenceEquals(_z3Assembly, z3Assembly))
                {
                    throw new InvalidOperationException(
                        "The verified Z3 resolver is already bound to another assembly.");
                }
                return;
            }

            using var verified = ContainerContract.OpenZ3LibraryRequired();
            var handle = NativeLibrary.Load(verified.LoadPath);
            try
            {
                NativeLibrary.SetDllImportResolver(
                    z3Assembly,
                    ResolveZ3Import);
                _z3Assembly = z3Assembly;
                Volatile.Write(ref _z3Handle, handle);
                ResolverGate.Publish(handle);
            }
            catch (OperationCanceledException exception) when (
                FailInstallationAndFree(handle, exception))
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _ = FailInstallationAndFree(handle, exception);
                throw;
            }
        }
    }

    private static bool FailInstallationAndFree(IntPtr handle, Exception failure)
    {
        ResolverGate.Fail(failure);
        NativeLibrary.Free(handle);
        return true;
    }

    private static IntPtr ResolveZ3Import(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (!string.Equals(
                libraryName,
                Z3ImportName,
                StringComparison.Ordinal))
        {
            throw new DllNotFoundException(
                "The SharpProof verifier refuses ambient native libraries.");
        }
        return ResolverGate.Resolve();
    }
}
