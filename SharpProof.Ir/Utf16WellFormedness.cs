namespace SharpProof.Ir;

internal static class Utf16WellFormedness
{
    internal static bool IsWellFormed(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                return false;
            }
        }
        return true;
    }
}
