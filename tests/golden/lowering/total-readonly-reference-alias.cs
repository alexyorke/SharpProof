// golden-mode: Total
public sealed class Cell { public int Value; }
public static class Subject
{
    public static int Target(in int value, Cell cell)
    {
        cell.Value = 5;
        return value;
    }
}
