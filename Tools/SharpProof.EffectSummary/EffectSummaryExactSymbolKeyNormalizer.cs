using System.Text;

internal static class EffectSummaryExactSymbolKeyNormalizer
{
    public static string NormalizeConstructedReceiverType(string exactSymbolKey)
    {
        var signatureStart = exactSymbolKey.IndexOf('(');
        if (signatureStart <= 0) return exactSymbolKey;

        var methodSeparator = -1;
        var genericDepth = 0;
        for (var i = 0; i < signatureStart; i++)
        {
            var current = exactSymbolKey[i];
            if (current == '<')
            {
                genericDepth++;
                continue;
            }

            if (current == '>')
            {
                if (genericDepth > 0) genericDepth--;

                continue;
            }

            if (current == '.' && genericDepth == 0) methodSeparator = i;
        }

        if (methodSeparator <= 0) return exactSymbolKey;

        var receiverType = exactSymbolKey[..methodSeparator];
        var normalizedReceiverType = StripGenericInstantiations(receiverType);
        if (string.Equals(receiverType, normalizedReceiverType, StringComparison.Ordinal)) return exactSymbolKey;

        return normalizedReceiverType + exactSymbolKey[methodSeparator..];
    }

    private static string StripGenericInstantiations(string text)
    {
        if (text.IndexOf('<') < 0) return text;

        var builder = new StringBuilder(text.Length);
        var genericDepth = 0;
        foreach (var current in text)
        {
            if (current == '<')
            {
                genericDepth++;
                continue;
            }

            if (current == '>')
            {
                if (genericDepth > 0) genericDepth--;

                continue;
            }

            if (genericDepth == 0) builder.Append(current);
        }

        return builder.ToString();
    }
}