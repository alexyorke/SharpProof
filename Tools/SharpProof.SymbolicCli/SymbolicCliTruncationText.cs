using System.Globalization;
using SharpProof.Symbolic;

internal static class SymbolicCliTruncationText
{
    public static string FormatInlineSuffix<T>(SymbolicBoundedProjection<T> projection)
    {
        return projection.IsTruncated
            ? " ... " + FormatOmittedCount(projection.OmittedCount)
            : string.Empty;
    }

    public static string FormatTruncatedLine<T>(
        string subject,
        SymbolicBoundedProjection<T> projection)
    {
        if (!projection.IsTruncated)
            throw new ArgumentException("The projection is not truncated.", nameof(projection));

        return subject + " truncated: " + FormatOmittedCount(projection.OmittedCount);
    }

    private static string FormatOmittedCount(int omittedCount)
    {
        return omittedCount.ToString(CultureInfo.InvariantCulture) + " omitted";
    }
}
