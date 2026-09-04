using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpProof.Host;

public static class ContainerNativeLibrary
{
    private const string Z3ImportName = "libz3";
    private static readonly object Gate = new();
    private static Assembly? s_z3Assembly;
    private static IntPtr s_z3Handle;

    public static void InstallZ3ResolverRequired(Assembly z3Assembly)
    {
        ArgumentNullException.ThrowIfNull(z3Assembly);
        lock (Gate)
        {
            if (s_z3Handle != IntPtr.Zero)
            {
                if (!ReferenceEquals(s_z3Assembly, z3Assembly))
                {
                    throw new InvalidOperationException(
                        "The verified Z3 resolver is already bound to another assembly.");
                }
                return;
            }

            var handle = NativeLibrary.Load(
                ContainerContract.ResolveZ3LibraryRequired());
            var resolverInstalled = false;
            try
            {
                s_z3Assembly = z3Assembly;
                Volatile.Write(ref s_z3Handle, handle);
                NativeLibrary.SetDllImportResolver(
                    z3Assembly,
                    ResolveZ3Import);
                resolverInstalled = true;
            }
            finally
            {
                if (!resolverInstalled)
                {
                    Volatile.Write(ref s_z3Handle, IntPtr.Zero);
                    s_z3Assembly = null;
                    NativeLibrary.Free(handle);
                }
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
        return Volatile.Read(ref s_z3Handle);
    }
}
