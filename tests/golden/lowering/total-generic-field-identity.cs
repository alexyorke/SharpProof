// golden-mode: Total
// golden-inline-source: true
// golden-opaque-calls: true
// golden-contracts: true
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(Outer<int>.Cell<string> cell)
    {
        Contract.Requires(cell != null && cell.Value == 3);
        Contract.Ensures(cell.Value == 7 && Contract.Old(cell).Value == 3);
        cell.Set();
        cell.Property = 7;
        return cell.Value;
    }
}
public class Outer<T>
{
    public class Cell<U>
    {
        public int Value;
        public void Set()
        {
            Contract.Requires(Value == 3);
            Value = 7;
        }
        public int Property
        {
            get { return Value; }
            set { Value = value; }
        }
    }
}
