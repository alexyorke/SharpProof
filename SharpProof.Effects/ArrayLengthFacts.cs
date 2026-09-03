namespace SharpProof.Effects;

internal static class ArrayLengthFacts
{
    internal static bool TryGetConstantLength(
        IOperation? operation,
        out int length)
    {
        if (operation is IArrayCreationOperation
            { DimensionSizes.Length: 1 } array &&
            array.DimensionSizes[0].ConstantValue is
            { HasValue: true, Value: int arrayLength })
        {
            length = arrayLength;
            return true;
        }

        length = 0;
        return false;
    }
}
