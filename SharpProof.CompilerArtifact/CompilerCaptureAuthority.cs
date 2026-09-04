namespace SharpProof.CompilerArtifact;

// The compiler collector and the worker both consume the same capture image.
// Keep producer spellings and worker predicates together so a value cannot be
// valid merely because it is plausible JSON.
internal static class CompilerCaptureAuthority
{
    internal const string EmptyTextSha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A compiler capture path is required.",
                nameof(path));
        }

        return Path.GetFullPath(path);
    }

    internal static bool IsCanonicalPath(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) &&
                Path.IsPathRooted(path) &&
                string.Equals(
                    NormalizePath(path),
                    path,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsCanonicalVersion(string value)
    {
        return Version.TryParse(value, out var parsed) &&
            string.Equals(
                parsed.ToString(),
                value,
                StringComparison.Ordinal);
    }

    internal static string CaptureVersion(Type type)
    {
        var value = type.Assembly.GetName().Version ??
            throw new InvalidOperationException(
                "The compiler version is unavailable.");
        return value.ToString();
    }

    internal static string CaptureMvid(Type type)
    {
        var value = type.Module.ModuleVersionId;
        if (value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The compiler MVID is not canonical.");
        }

        return value.ToString("D");
    }

    internal static bool IsCanonicalLanguageVersion(string value)
    {
        return value is
            "Default" or
            "CSharp1" or
            "CSharp2" or
            "CSharp3" or
            "CSharp4" or
            "CSharp5" or
            "CSharp6" or
            "CSharp7" or
            "CSharp7_1" or
            "CSharp7_2" or
            "CSharp7_3" or
            "CSharp8" or
            "CSharp9" or
            "CSharp10" or
            "CSharp11" or
            "CSharp12" or
            "CSharp13" or
            "CSharp14" or
            "LatestMajor" or
            "Preview" or
            "Latest";
    }

    internal static bool IsCanonicalMvid(string value)
    {
        return Guid.TryParseExact(value, "D", out var parsed) &&
            parsed != Guid.Empty &&
            string.Equals(
                parsed.ToString("D"),
                value,
                StringComparison.Ordinal);
    }

    internal static bool IsCanonicalAssemblyIdentity(
        string value,
        out string identityName)
    {
        identityName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var parsed = new System.Reflection.AssemblyName(value);
            if (parsed.FullName is not { } fullName ||
                !string.Equals(fullName, value, StringComparison.Ordinal) ||
                parsed.Name is not { Length: > 0 } name ||
                parsed.Version == null ||
                value.IndexOf(", Culture=", StringComparison.Ordinal) < 0 ||
                value.IndexOf(", PublicKeyToken=", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            identityName = name;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FileLoadException)
        {
            return false;
        }
    }

    internal static bool HasValidEmptyTreeRepresentation(
        CompilerSyntaxTreeSnapshot value)
    {
        return value.TextLength != 0 ||
            string.Equals(
                value.Sha256,
                EmptyTextSha256,
                StringComparison.Ordinal) &&
            // Roslyn preserves duplicate parse-option symbols in the raw
            // capture, while the effective symbol set necessarily removes
            // duplicates. Compare against the producer's effective view so
            // an empty tree does not reject its own capture.
            value.EffectivePreprocessorSymbols.SequenceEqual(
                value.PreprocessorSymbols.Distinct(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }
}
