using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

internal enum ExactReplayStatus
{
    Reproduced,
    Mismatch,
    Unsupported,
    InvalidModel,
    HarnessError
}

internal sealed record ExactReplayInput(int X, int Y, string? Text);
internal sealed record ExactReplayResult(ExactReplayStatus Status, string Detail)
{
    public string Detail { get; init; } = Detail ?? throw new ArgumentNullException(nameof(Detail));
}

// This decoder deliberately accepts only losslessly represented entry values
// for the generator's static Target(int, int, int[]?, string?) signature.
// Opaque heap displays and effect witnesses cannot supply executable inputs.
internal static class ExactCounterexampleReplay
{
    internal static ExactReplayResult Run(string source, string mode, WorkerModelValue[] model)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(model);
        if (mode != ContractClaimProgramGenerator.Ensures || model.Length == 0)
        {
            return new(ExactReplayStatus.Unsupported, "No executable postcondition entry model was reported.");
        }

        var displays = ImmutableArray.CreateBuilder<string>();
        foreach (var value in model)
        {
            displays.Add(value is null ? "<null binding>"
                : value.Variable + "=" + value.Value + " (" + value.Kind + ")");
        }
        var display = string.Join("; ", displays.ToImmutable());
        var bindingBuilder = ImmutableDictionary.CreateBuilder<string, WorkerModelValue>(StringComparer.Ordinal);
        foreach (var value in model)
        {
            if (value is null || value.Variable is null || !bindingBuilder.TryAdd(value.Variable, value))
            {
                return new(ExactReplayStatus.InvalidModel, "Null or duplicate entry binding; " + display);
            }
        }
        if (bindingBuilder.Count != 4)
        {
            return new(ExactReplayStatus.InvalidModel, "Expected exactly parameter:0..3; " + display);
        }
        var bindings = bindingBuilder.ToImmutable();
        for (var ordinal = 0; ordinal < 4; ordinal++)
        {
            if (!bindings.ContainsKey("parameter:" + ordinal.ToString(CultureInfo.InvariantCulture)))
            {
                return new(ExactReplayStatus.InvalidModel, "Expected exactly parameter:0..3; " + display);
            }
        }

        var x = bindings["parameter:0"];
        var y = bindings["parameter:1"];
        if (x.Kind != "Integer" || y.Kind != "Integer" ||
            !int.TryParse(x.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var xValue) ||
            !int.TryParse(y.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var yValue))
        {
            return new(ExactReplayStatus.InvalidModel, "Invalid Int32 entry binding; " + display);
        }
        var array = bindings["parameter:2"];
        var text = bindings["parameter:3"];
        if (array.Kind == "Reference" || array.Kind == "Sequence" || text.Kind == "Reference")
        {
            return new(ExactReplayStatus.Unsupported, "Opaque heap values cannot be reconstructed; " + display);
        }
        if (array.Kind != "Null" || array.Value != "null")
        {
            return new(ExactReplayStatus.InvalidModel, "Expected a null array entry binding; " + display);
        }
        string? textValue;
        if (text.Kind == "Null" && text.Value == "null")
        {
            textValue = null;
        }
        else if (text.Kind == "String" && text.Value is { } encoded && TryDecodeString(encoded, out textValue))
        {
            // Decode the protocol display directly: both unpaired surrogates
            // and literal Unicode line separators must retain their code units.
        }
        else
        {
            return new(ExactReplayStatus.InvalidModel, "Invalid string entry binding; " + display);
        }

        var observation = AnalyzerPipelineLongFuzzTests.RuntimeOracle.RunExact(source, mode,
            new ExactReplayInput(xValue, yValue, textValue));
        if (observation.Error is { } error)
        {
            return new(ExactReplayStatus.HarnessError, error + "; reported model: " + display);
        }
        if (observation.Inputs != 1)
        {
            return new(ExactReplayStatus.Mismatch, "Reported inputs fail the CLR precondition; " + display);
        }
        return observation.Violation is { } violation
            ? new(ExactReplayStatus.Reproduced, violation + "; reported model: " + display)
            : new(ExactReplayStatus.Mismatch, "Reported inputs do not falsify the CLR postcondition; " + display);
    }

    private static bool TryDecodeString(string encoded, out string? value)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        value = null;
        if (encoded.Length < 2 || encoded[0] != '"' || encoded[^1] != '"')
        {
            return false;
        }
        var decoded = new StringBuilder();
        for (var index = 1; index < encoded.Length - 1; index++)
        {
            var character = encoded[index];
            if (character == '\\')
            {
                if (++index >= encoded.Length - 1)
                {
                    return false;
                }
                character = encoded[index];
                if (character == 'u')
                {
                    if (index + 4 >= encoded.Length - 1 ||
                        !ushort.TryParse(encoded.AsSpan(index + 1, 4), NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture, out var codeUnit))
                    {
                        return false;
                    }
                    decoded.Append((char)codeUnit);
                    index += 4;
                    continue;
                }
                if (character != '\\' && character != '"')
                {
                    return false;
                }
            }
            else if (character == '"' || character < ' ')
            {
                return false;
            }
            decoded.Append(character);
        }
        value = decoded.ToString();
        return true;
    }
}
