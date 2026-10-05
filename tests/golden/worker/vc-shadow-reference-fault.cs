// golden-scenario: vc-shadow-reference
using SharpProof.Attributes;
public static class Subject {
    public static int Target(string text) {
        Contract.Requires(text == null);
        Contract.Ensures(Contract.Result<int>() == 1);
        Contract.Ensures(Contract.Result<int>() == 0);
        int seen = 0;
        try { return Length(text); }
        catch (System.NullReferenceException) when (++seen == 1) { return seen; }
        finally { seen += 10; }
    }
    private static int Length(string value) { return value.Length; }
}
