namespace SharpProof.Dataflow.Test;

internal static class GeneratedDomainLawAssertions
{
    private const int PropertyIterations = 512;

    public static void AssertLatticeAndBottomLaws<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> values,
        int seed)
    {
        Assert.That(values, Is.Not.Empty);
        var random = new Random(seed);
        var comparer = EqualityComparer<T>.Default;

        foreach (var value in values)
        {
            Assert.That(
                domain.LessThanOrEqual(value, value),
                Is.True,
                $"Reflexivity failed for {value}.");
            Assert.That(
                domain.LessThanOrEqual(domain.Bottom, value),
                Is.True,
                $"Bottom is not below {value}.");
            Assert.That(
                domain.AreEquivalent(domain.Join(domain.Bottom, value), value),
                Is.True,
                $"Left bottom join law failed for {value}.");
            Assert.That(
                domain.AreEquivalent(domain.Join(value, domain.Bottom), value),
                Is.True,
                $"Right bottom join law failed for {value}.");
        }

        for (var iteration = 0; iteration < PropertyIterations; iteration++)
        {
            var first = Pick(values, random);
            var second = Pick(values, random);
            var third = Pick(values, random);

            if (domain.LessThanOrEqual(first, second) &&
                domain.LessThanOrEqual(second, first))
            {
                Assert.That(
                    comparer.Equals(first, second),
                    Is.True,
                    $"Antisymmetry failed at seed {seed}, iteration {iteration}: " +
                    $"{first} and {second}.");
            }

            var middle = domain.Join(first, second);
            var upper = domain.Join(middle, third);
            Assert.That(
                domain.LessThanOrEqual(first, middle),
                Is.True,
                $"Generated transitivity premise failed at seed {seed}, " +
                $"iteration {iteration}.");
            Assert.That(
                domain.LessThanOrEqual(middle, upper),
                Is.True,
                $"Generated transitivity premise failed at seed {seed}, " +
                $"iteration {iteration}.");
            Assert.That(
                domain.LessThanOrEqual(first, upper),
                Is.True,
                $"Transitivity failed at seed {seed}, iteration {iteration}: " +
                $"{first}, {middle}, {upper}.");

            var join = domain.Join(first, second);
            Assert.That(
                domain.LessThanOrEqual(first, join),
                Is.True,
                $"Join is not above {first} at seed {seed}, iteration {iteration}.");
            Assert.That(
                domain.LessThanOrEqual(second, join),
                Is.True,
                $"Join is not above {second} at seed {seed}, iteration {iteration}.");

            var testedUpperBound = domain.Join(join, third);
            Assert.That(
                domain.LessThanOrEqual(join, testedUpperBound),
                Is.True,
                $"Join is not below a generated upper bound at seed {seed}, " +
                $"iteration {iteration}.");

            for (var upperBoundAttempt = 0; upperBoundAttempt < 8; upperBoundAttempt++)
            {
                var sampledUpperBound = Pick(values, random);
                if (!domain.LessThanOrEqual(first, sampledUpperBound) ||
                    !domain.LessThanOrEqual(second, sampledUpperBound))
                {
                    continue;
                }

                Assert.That(
                    domain.LessThanOrEqual(join, sampledUpperBound),
                    Is.True,
                    $"Join is not least below sampled upper bound {sampledUpperBound} " +
                    $"at seed {seed}, iteration {iteration}.");
            }
        }
    }

    public static void AssertTransfersAreMonotone<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> values,
        int seed,
        IReadOnlyList<(string Name, Func<T, T> Transfer)> unaryTransfers,
        IReadOnlyList<(string Name, Func<T, T, T> Transfer)> binaryTransfers)
    {
        Assert.That(values, Is.Not.Empty);
        var random = new Random(seed);

        for (var iteration = 0; iteration < PropertyIterations; iteration++)
        {
            var lower = Pick(values, random);
            var upper = domain.Join(lower, Pick(values, random));
            Assert.That(domain.LessThanOrEqual(lower, upper), Is.True);

            foreach (var (name, transfer) in unaryTransfers)
            {
                Assert.That(
                    domain.LessThanOrEqual(transfer(lower), transfer(upper)),
                    Is.True,
                    $"{name} is not monotone at seed {seed}, iteration {iteration}: " +
                    $"{lower} <= {upper}.");
            }

            var leftLower = Pick(values, random);
            var leftUpper = domain.Join(leftLower, Pick(values, random));
            var rightLower = Pick(values, random);
            var rightUpper = domain.Join(rightLower, Pick(values, random));

            foreach (var (name, transfer) in binaryTransfers)
            {
                Assert.That(
                    domain.LessThanOrEqual(
                        transfer(leftLower, rightLower),
                        transfer(leftUpper, rightUpper)),
                    Is.True,
                    $"{name} is not monotone at seed {seed}, iteration {iteration}: " +
                    $"({leftLower}, {rightLower}) <= ({leftUpper}, {rightUpper}).");
            }
        }
    }

    public static void AssertWideningTerminates<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> ascendingChain,
        int maximumChanges)
    {
        Assert.That(ascendingChain, Is.Not.Empty);
        var previousChainValue = domain.Bottom;
        var widened = domain.Bottom;
        var changes = 0;

        foreach (var next in ascendingChain)
        {
            Assert.That(
                domain.LessThanOrEqual(previousChainValue, next),
                Is.True,
                $"Input chain is not ascending: {previousChainValue}, {next}.");

            var previousWidened = widened;
            widened = domain.Widen(widened, next);
            Assert.That(
                domain.LessThanOrEqual(previousWidened, widened),
                Is.True,
                $"Widening moved down from {previousWidened} to {widened}.");
            Assert.That(
                domain.LessThanOrEqual(next, widened),
                Is.True,
                $"Widening does not cover the next state {next}: {widened}.");
            if (!domain.AreEquivalent(previousWidened, widened))
            {
                changes++;
            }

            previousChainValue = next;
        }

        Assert.That(
            changes,
            Is.LessThanOrEqualTo(maximumChanges),
            $"Widening changed {changes} times; expected at most {maximumChanges}.");

        var terminal = ascendingChain[^1];
        for (var repetition = 0; repetition < 32; repetition++)
        {
            Assert.That(
                domain.AreEquivalent(domain.Widen(widened, terminal), widened),
                Is.True,
                $"Widening did not remain stable at repetition {repetition}.");
        }
    }

    public static void AssertHavocIsConservative<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> values)
    {
        foreach (var value in values)
        {
            var havoced = domain.Havoc(value);
            Assert.That(
                domain.LessThanOrEqual(value, havoced),
                Is.True,
                $"Havoc is not conservative for {value}.");
            Assert.That(
                domain.AreEquivalent(domain.Havoc(havoced), havoced),
                Is.True,
                $"Havoc is not idempotent for {value}.");
            Assert.That(
                domain.AreEquivalent(
                    havoced,
                    domain.AreEquivalent(value, domain.Bottom)
                        ? domain.Bottom
                        : domain.Top),
                Is.True,
                $"Havoc did not forget all information for {value}.");
        }
    }

    private static T Pick<T>(IReadOnlyList<T> values, Random random)
    {
        return values[random.Next(values.Count)];
    }
}

internal static class GeneratedDomainSamples
{
    private static readonly int[] Moduli = [1, 2, 3, 4, 5, 8, 16];

    public static IReadOnlyList<IntervalValue> Intervals(int seed, int count)
    {
        var domain = IntervalDomain.Instance;
        var random = new Random(seed);
        var values = new List<IntervalValue> {
            domain.Bottom,
            domain.Top,
            domain.Constant(long.MinValue),
            domain.Constant(long.MaxValue),
            domain.Range(long.MinValue, long.MaxValue)
        };

        for (var index = 0; index < count; index++)
        {
            var first = NextSignedValue(random);
            var second = NextSignedValue(random);
            var lower = Math.Min(first, second);
            var upper = Math.Max(first, second);
            values.Add(random.Next(6) switch
            {
                0 => domain.Constant(first),
                1 => domain.Range(lower, upper),
                2 => domain.Range(random.Next(4) == 0 ? null : lower, upper),
                3 => domain.Range(lower, random.Next(4) == 0 ? null : upper),
                4 => domain.Create(
                    random.Next(4) == 0 ? null : lower,
                    random.Next(4) == 0 ? null : upper,
                    NextModulus(random),
                    random.NextInt64(-32, 33)),
                _ => domain.Join(
                    domain.Constant(first),
                    domain.Constant(second))
            });
        }

        return values;
    }

    public static IReadOnlyList<SequenceCardinalityValue> Sequences(int seed, int count)
    {
        var domain = SequenceCardinalityDomain.Instance;
        var random = new Random(seed);
        var values = new List<SequenceCardinalityValue> {
            domain.Bottom,
            domain.Empty,
            domain.NonEmpty,
            domain.Top,
            domain.KnownLength(long.MaxValue)
        };

        for (var index = 0; index < count; index++)
        {
            var first = NextLength(random);
            var second = NextLength(random);
            var lower = Math.Min(first, second);
            var upper = Math.Max(first, second);
            var length = random.Next(4) switch
            {
                0 => IntervalValue.Constant(first),
                1 => IntervalValue.Range(lower, upper),
                2 => IntervalValue.Range(lower, null),
                _ => IntervalValue.Congruent(
                    lower,
                    random.Next(4) == 0 ? null : upper,
                    NextModulus(random),
                    random.NextInt64(0, 17))
            };
            values.Add(random.Next(5) switch
            {
                0 => domain.KnownLength(first),
                1 => domain.Create(SequenceCardinalityKind.Empty, length),
                2 => domain.Create(SequenceCardinalityKind.NonEmpty, length),
                3 => domain.Create(SequenceCardinalityKind.Top, length),
                _ => domain.Join(
                    values[random.Next(values.Count)],
                    values[random.Next(values.Count)])
            });
        }

        return values;
    }

    private static long NextSignedValue(Random random)
    {
        return random.Next(24) switch
        {
            0 => long.MinValue,
            1 => long.MaxValue,
            _ => random.NextInt64(-512, 513)
        };
    }

    private static long NextLength(Random random)
    {
        return random.Next(24) == 0
            ? long.MaxValue
            : random.NextInt64(0, 513);
    }

    private static BigInteger NextModulus(Random random)
    {
        return new(Moduli[random.Next(Moduli.Length)]);
    }
}

[TestFixture]
public sealed class GeneratedIntervalDomainPropertyTests
{
    private const int Seed = 0x51A2;
    private readonly IntervalDomain _domain = IntervalDomain.Instance;
    private static IReadOnlyList<IntervalValue> Values =>
        GeneratedDomainSamples.Intervals(Seed, 256);

    [Test]
    public void GeneratedValuesSatisfyLatticeAndBottomLaws()
    {
        GeneratedDomainLawAssertions.AssertLatticeAndBottomLaws(
            _domain,
            Values,
            Seed);
    }

    [Test]
    public void GeneratedTransfersAreMonotone()
    {
        GeneratedDomainLawAssertions.AssertTransfersAreMonotone(
            _domain,
            Values,
            Seed + 1,
            [
                ("AddConstant", value => _domain.AddConstant(value, 7)),
                ("AssumeAtLeast", value => _domain.AssumeAtLeast(value, -17)),
                ("AssumeAtMost", value => _domain.AssumeAtMost(value, 23))
            ],
            [("Add", _domain.Add)]);
    }

    [Test]
    public void WideningTerminatesOnGeneratedAscendingChains()
    {
        var oneSided = new List<IntervalValue> { _domain.Bottom, _domain.Constant(11) };
        var congruent = new List<IntervalValue> { _domain.Bottom, _domain.Constant(0) };
        for (var step = 1; step <= 128; step++)
        {
            oneSided.Add(_domain.Range(11, 11 + step));
            congruent.Add(_domain.Create(0, step * 2L, 2, 0));
        }
        congruent.Add(_domain.Range(0, 256));

        GeneratedDomainLawAssertions.AssertWideningTerminates(
            _domain,
            oneSided,
            maximumChanges: 2);
        GeneratedDomainLawAssertions.AssertWideningTerminates(
            _domain,
            congruent,
            maximumChanges: 3);
    }

    [Test]
    public void GeneratedHavocIsConservative()
    {
        GeneratedDomainLawAssertions.AssertHavocIsConservative(_domain, Values);
    }
}

[TestFixture]
public sealed class GeneratedSequenceCardinalityDomainPropertyTests
{
    private const int Seed = 0x7E91;
    private readonly SequenceCardinalityDomain _domain =
        SequenceCardinalityDomain.Instance;
    private static IReadOnlyList<SequenceCardinalityValue> Values =>
        GeneratedDomainSamples.Sequences(Seed, 256);

    [Test]
    public void GeneratedValuesSatisfyLatticeAndBottomLaws()
    {
        GeneratedDomainLawAssertions.AssertLatticeAndBottomLaws(
            _domain,
            Values,
            Seed);
    }

    [Test]
    public void GeneratedTransfersAreMonotone()
    {
        GeneratedDomainLawAssertions.AssertTransfersAreMonotone(
            _domain,
            Values,
            Seed + 1,
            [
                ("Append", value => _domain.Append(value, 3)),
                ("AssumeEmpty", _domain.AssumeEmpty),
                ("AssumeNonEmpty", _domain.AssumeNonEmpty)
            ],
            [("Concat", _domain.Concat)]);
    }

    [Test]
    public void WideningTerminatesOnGeneratedAscendingChains()
    {
        var chain = new List<SequenceCardinalityValue> {
            _domain.Bottom,
            _domain.Empty
        };
        for (var step = 1; step <= 128; step++)
        {
            chain.Add(_domain.Create(
                SequenceCardinalityKind.Top,
                IntervalValue.Range(0, step)));
        }

        GeneratedDomainLawAssertions.AssertWideningTerminates(
            _domain,
            chain,
            maximumChanges: 2);
    }

    [Test]
    public void GeneratedHavocIsConservative()
    {
        GeneratedDomainLawAssertions.AssertHavocIsConservative(_domain, Values);
    }
}

[TestFixture]
public sealed class GeneratedNullnessDomainPropertyTests
{
    private const int Seed = 0x19F3;
    private readonly NullnessDomain _domain = NullnessDomain.Instance;
    private static IReadOnlyList<NullnessValue> Values
    {
        get
        {
            var random = new Random(Seed);
            return Enumerable.Range(0, 256)
                .Select(_ => (NullnessValue)random.Next(4))
                .Prepend(NullnessValue.Bottom)
                .Prepend(NullnessValue.MaybeNull)
                .ToArray();
        }
    }

    [Test]
    public void GeneratedValuesSatisfyLatticeAndBottomLaws()
    {
        GeneratedDomainLawAssertions.AssertLatticeAndBottomLaws(
            _domain,
            Values,
            Seed);
    }

    [Test]
    public void GeneratedTransfersAreMonotone()
    {
        GeneratedDomainLawAssertions.AssertTransfersAreMonotone(
            _domain,
            Values,
            Seed + 1,
            [
                ("AssumeNull", _domain.AssumeNull),
                ("AssumeNonNull", _domain.AssumeNonNull)
            ],
            []);
    }

    [Test]
    public void WideningTerminatesOnGeneratedAscendingChains()
    {
        GeneratedDomainLawAssertions.AssertWideningTerminates(
            _domain,
            [
                NullnessValue.Bottom,
                NullnessValue.Null,
                NullnessValue.MaybeNull
            ],
            maximumChanges: 2);
    }

    [Test]
    public void GeneratedHavocIsConservative()
    {
        GeneratedDomainLawAssertions.AssertHavocIsConservative(_domain, Values);
    }
}
