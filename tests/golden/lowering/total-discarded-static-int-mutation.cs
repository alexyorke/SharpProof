// golden-mode: Total
public static class Subject
{
    public static int State;

    public static int Target(int value)
    {
        State++;
        _ = unchecked((byte)--State);
        return value;
    }
}
