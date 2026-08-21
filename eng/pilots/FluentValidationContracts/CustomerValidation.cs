using FluentValidation;
using SharpProof.Attributes;

namespace SharpProof.Pilots.FluentValidationContracts;

public sealed record Customer(string Name, int Age);

public sealed class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(static customer => customer.Name).NotEmpty();
        RuleFor(static customer => customer.Age).GreaterThanOrEqualTo(0);
    }
}

public static class CustomerContracts
{
    public static int PositiveAge(int value)
    {
        Contract.Requires(value >= 0);
        Contract.Ensures(Contract.Result<int>() >= 0);
        return value;
    }

    public static int KnownGoodAge() => PositiveAge(21);

#if SHARPPROOF_NEGATIVE_PROBE
    public static int RejectedAgeProbe() => PositiveAge(-1);
#endif
}
