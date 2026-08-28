namespace SharpProof.Testing;

/// <summary>
/// A reproducible non-cryptographic generator whose seed retains all 32 bits.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public DeterministicRandom(int seed)
        : this(unchecked((uint)seed))
    {
    }

    public DeterministicRandom(uint seed)
    {
        _state = seed;
    }

    public int Next(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);

        return (int)(NextUInt() % (uint)exclusiveMaximum);
    }

    private uint NextUInt()
    {
        _state += Increment;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return unchecked((uint)value);
    }
}
