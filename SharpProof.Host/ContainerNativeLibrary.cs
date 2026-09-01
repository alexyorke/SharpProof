using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpProof.Host;

public static class ContainerNativeLibrary
{
    private const string Z3ImportName = "libz3";
    private static readonly object Synchronization = new();
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

            var handle = NativeLibrary.Load(
                ContainerContract.ResolveZ3LibraryRequired());
            try
            {
                NativeLibrary.SetDllImportResolver(
                    z3Assembly,
                    ResolveZ3Import);
                _z3Assembly = z3Assembly;
                Volatile.Write(ref _z3Handle, handle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                NativeLibrary.Free(handle);
                throw;
            }
        }
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
        return Volatile.Read(ref _z3Handle);
    }
}
