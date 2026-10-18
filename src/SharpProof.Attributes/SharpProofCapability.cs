namespace SharpProof.Attributes;

/// <summary>Identifies ambient capabilities tracked by effect analysis.</summary>
[Flags]
public enum SharpProofCapability
{
    /// <summary>Uses no tracked ambient capability.</summary>
    None = 0,
    /// <summary>Uses general input or output.</summary>
    IO = 1 << 0,
    /// <summary>Reads from the file system.</summary>
    FileRead = 1 << 1,
    /// <summary>Writes to the file system.</summary>
    FileWrite = 1 << 2,
    /// <summary>Uses a network resource.</summary>
    Network = 1 << 3,
    /// <summary>Uses the process console.</summary>
    Console = 1 << 4,
    /// <summary>Uses process-management facilities.</summary>
    Process = 1 << 5,
    /// <summary>Reads or writes process environment state.</summary>
    Environment = 1 << 6,
    /// <summary>Uses the Windows registry.</summary>
    Registry = 1 << 7,
    /// <summary>Reads time or timer state.</summary>
    Clock = 1 << 8,
    /// <summary>Uses a source of randomness.</summary>
    Randomness = 1 << 9,
    /// <summary>Uses reflection facilities.</summary>
    Reflection = 1 << 10,
    /// <summary>Uses synchronization facilities.</summary>
    Synchronization = 1 << 11,
    /// <summary>Crosses a native-code boundary.</summary>
    NativeInterop = 1 << 12
}
