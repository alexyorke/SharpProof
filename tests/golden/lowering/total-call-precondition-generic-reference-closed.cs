// golden-mode: Total
// golden-inline-source: true
// golden-contracts: true
using System.Collections.Generic;
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(List<int> values) => Box<int>.Read(values);
}
public static class Box<T>
{
    public static int Read(List<T> values)
    {
        Contract.Requires(values != null);
        return 1;
    }
}
