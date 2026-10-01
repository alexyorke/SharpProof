using SharpProof.Attributes;
public static partial class Subject {
    public static partial long Target(long value);
}
public static partial class Subject {
    public static partial long Target(long value) {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
}
