// golden-mode: Total
public sealed class Cell
{
    public int Value;
}

public static class Subject
{
    public static int Target(Cell cell, int value)
    {
        _ = ++cell.Value;
        return value;
    }
}
