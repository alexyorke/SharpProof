namespace SharpProof.Dataflow.Test;

internal static class DomainLawAssertions
{
    public static void AssertCanonicalTransfers<T>(
        CanonicalAbstractDomain<T> domain,
        IReadOnlyList<T> samples)
    {
        foreach (var sample in samples)
        {
            Assert.That(
                domain.IsCanonicalTransfer(sample),
                Is.True,
                $"Factory value is not canonical: {sample}.");
        }
    }

    public static void AssertOrderAndJoinLaws<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> samples)
    {
        foreach (var value in samples)
        {
            Assert.That(
                domain.LessThanOrEqual(value, value),
                Is.True,
                $"Order is not reflexive for {value}.");
            Assert.That(
                domain.LessThanOrEqual(domain.Bottom, value),
                Is.True,
                $"Bottom is not below {value}.");
            Assert.That(
                domain.LessThanOrEqual(value, domain.Top),
                Is.True,
                $"{value} is not below top.");
            Assert.That(
                domain.AreEquivalent(domain.Join(value, value), value),
                Is.True,
                $"Join is not idempotent for {value}.");
        }

        foreach (var left in samples)
        {
            foreach (var right in samples)
            {
                if (domain.LessThanOrEqual(left, right) &&
                    domain.LessThanOrEqual(right, left))
                {
                    Assert.That(
                        domain.AreEquivalent(left, right),
                        Is.True,
                        $"Order antisymmetry failed for {left} and {right}.");
                }

                var join = domain.Join(left, right);
                Assert.That(
                    domain.LessThanOrEqual(left, join),
                    Is.True,
                    $"Join is not above its left operand: {left}, {right}.");
                Assert.That(
                    domain.LessThanOrEqual(right, join),
                    Is.True,
                    $"Join is not above its right operand: {left}, {right}.");
                if (domain.LessThanOrEqual(left, right))
                {
                    Assert.That(
                        domain.AreEquivalent(join, right),
                        Is.True,
                        $"Ordered join did not absorb its right operand: {left}, {right}.");
                }

                if (domain.LessThanOrEqual(right, left))
                {
                    Assert.That(
                        domain.AreEquivalent(join, left),
                        Is.True,
                        $"Ordered join did not absorb its left operand: {left}, {right}.");
                }
                Assert.That(
                    domain.AreEquivalent(join, domain.Join(right, left)),
                    Is.True,
                    $"Join is not commutative for {left} and {right}.");

                foreach (var upperBound in samples)
                {
                    if (domain.LessThanOrEqual(left, upperBound) &&
                        domain.LessThanOrEqual(right, upperBound))
                    {
                        Assert.That(
                            domain.LessThanOrEqual(join, upperBound),
                            Is.True,
                            $"Join is not least below sampled upper bound {upperBound}.");
                    }
                }
            }
        }

        foreach (var first in samples)
        {
            foreach (var second in samples)
            {
                foreach (var third in samples)
                {
                    if (domain.LessThanOrEqual(first, second) &&
                        domain.LessThanOrEqual(second, third))
                    {
                        Assert.That(
                            domain.LessThanOrEqual(first, third),
                            Is.True,
                            $"Order is not transitive for {first}, {second}, {third}.");
                    }

                    Assert.That(
                        domain.AreEquivalent(
                            domain.Join(domain.Join(first, second), third),
                            domain.Join(first, domain.Join(second, third))),
                        Is.True,
                        $"Join is not associative for {first}, {second}, {third}.");
                }
            }
        }
    }

    public static void AssertMonotone<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> samples,
        Func<T, T> transfer)
    {
        foreach (var left in samples)
        {
            foreach (var right in samples)
            {
                if (domain.LessThanOrEqual(left, right))
                {
                    Assert.That(
                        domain.LessThanOrEqual(transfer(left), transfer(right)),
                        Is.True,
                        $"Transfer is not monotone for {left} <= {right}.");
                }
            }
        }
    }

    public static void AssertConservativeHavoc<T>(
        IAbstractDomain<T> domain,
        IReadOnlyList<T> samples)
    {
        foreach (var value in samples)
        {
            var havoced = domain.Havoc(value);
            Assert.That(
                domain.LessThanOrEqual(value, havoced),
                Is.True,
                $"Havoc is not conservative for {value}.");
            Assert.That(
                domain.AreEquivalent(
                    havoced,
                    domain.AreEquivalent(value, domain.Bottom) ? domain.Bottom : domain.Top),
                Is.True,
                $"Havoc did not collapse {value} to the expected extremum.");
        }
    }
}
