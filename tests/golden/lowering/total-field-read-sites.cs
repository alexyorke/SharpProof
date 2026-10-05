// golden-mode: Total
public class Cell
{
    public int Value;
    private int _count;
    public int Count { get { return _count; } }
    public bool Flag { get; set; }
}

public static class C
{
    public static int Target(Cell cell)
    {
        return cell.Flag ? cell.Count : cell.Value;
    }
}
