// Retires the legacy diamond-join regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(bool condition)
    {
        Contract.Ensures(Contract.Result<int>() == (Contract.Old(condition) ? 3 : 7));
        int result;
        if (condition)
        {
            result = 3;
        }
        else
        {
            result = 7;
        }
        return result;
    }
}
