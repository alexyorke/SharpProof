// golden-scenario: passive-ownership
// Exercises exact owned IR enrollment, original Old reads, SSA construction,
// cancellation and limits without entering the production worker route.
public static class Subject
{
    public static int Target(int value) => value;
}
