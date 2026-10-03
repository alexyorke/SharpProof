using SharpProof.Attributes;

public static class Guard
{
    public static int Need(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

public class ThrowFirst
{
    private int first = (new int[0])[0];
    private int later = Guard.Need(0);
}

public class CallFirst
{
    private int later = Guard.Need(0);
    private int first = (new int[0])[0];
}

public class NullIndexCall
{
    private int first = ((int[])null)[Guard.Need(0)];
    private int later = Guard.Need(0);
}

public class NullValueCall
{
    private int first = (((int[])null)[0] = Guard.Need(0));
    private int later = Guard.Need(0);
}

public class BoundsValueCall
{
    private int first = ((new int[0])[0] = Guard.Need(0));
    private int later = Guard.Need(0);
}
