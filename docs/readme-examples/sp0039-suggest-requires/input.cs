using System;

public static class PreconditionCandidate
{
    public static int Positive(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}
