// golden-mode: Total
public sealed class Cell
{
    public bool Value;
}

public static class C
{
    public static bool State;

    public static int Target(Cell cell, Cell other)
    {
        cell.Value = (State = ((cell = other) == null));
        return 0;
    }
}
