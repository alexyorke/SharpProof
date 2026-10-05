using System;
using SharpProof.Attributes;

public static class Subject
{
    private static int s_ambient;

    private static void TouchAmbient() => s_ambient++;

    public static int[] Target()
    {
        Contract.Ensures(Contract.Result<int[]>() != null);
        var result = Array.Empty<int>();
        TouchAmbient();
        return result;
    }
}
