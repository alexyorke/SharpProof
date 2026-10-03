// golden-scenario: model-u64
// The directive verifies an exact native bitvector model and concrete typed identity replay.
public static class Subject
{
    public static ulong Target(ulong value) => value;
}
