using System;
using System.Text.Json;

namespace SharpProof.Analyzer
{
    internal static class CompatibilityHelpers
    {
        public static string ToLowerHex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                chars[(i * 2)] = ToHexChar(value >> 4);
                chars[(i * 2) + 1] = ToHexChar(value & 0x0F);
            }

            return new string(chars);
        }

        public static string? GetTrimmedStringProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var valueElement) ||
                valueElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = valueElement.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value!.Trim();
        }

        private static char ToHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }
    }
}
