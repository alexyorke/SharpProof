// golden-scenario: vc-shadow-reference
using SharpProof.Attributes;
public static class Subject {
    public static int Target(int[] x, uint index) {
        Contract.Requires(x != null && x.Length == 0 && index == 0);
        Contract.Ensures(Contract.Result<int>() == 2 && index == 1 && Contract.Old(index) == 0);
        Contract.Ensures(Contract.Result<int>() == 3);
        int seen = 0;
        try { return Read(x, index++); }
        catch (System.NullReferenceException) when (++seen == 1) { return seen; }
        catch (System.IndexOutOfRangeException) when (++seen == 1) { return seen + 1; }
        finally { seen += 10; }
    }
    private static int Read(int[] value, uint index) { return value[index]; }
}
