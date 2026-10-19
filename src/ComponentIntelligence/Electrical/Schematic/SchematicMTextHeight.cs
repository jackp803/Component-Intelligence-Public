using System.Globalization;
using System.Text;
using netDxf.Entities;

namespace ComponentIntelligence.Electrical.Schematic;

internal static class SchematicMTextHeight
{
    internal static void Normalize(MText text)
    {
        if (UniformHeight(text.Value, text.Height) is not { } normalized) return;
        text.Value = normalized.Value;
        text.Height = normalized.Height;
    }

    // Only flatten a height override if every visible glyph uses the same height.
    // Braces scope formatting; H<number>x multiplies the current height, H<number>
    // is absolute. Resolve before INSERT transforms so both forms scale once.
    private static (double Height, string Value)? UniformHeight(string value, double initial)
    {
        var height = initial;
        var scopes = new Stack<double>();
        var heightCodes = new List<(int Start, int Length)>();
        double? visibleHeight = null;
        var hasOverride = false;
        bool Glyph()
        {
            if (visibleHeight is { } seen && Math.Abs(seen - height) > 1e-9 * Math.Max(seen, height)) return false;
            visibleHeight = height; return true;
        }
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '{') { if (scopes.Count == 8) return null; scopes.Push(height); continue; }
            if (character == '}') { if (!scopes.TryPop(out height)) return null; continue; }
            if (character != '\\') { if (!char.IsWhiteSpace(character) && !Glyph()) return null; continue; }
            if (++i == value.Length) return null;
            var command = value[i];
            if (command is '\\' or '{' or '}') { if (!Glyph()) return null; continue; }
            if (command is 'P' or '~' or 'L' or 'l' or 'O' or 'o' or 'K' or 'k') continue;
            if (command == 'U' && i + 5 < value.Length && value[i + 1] == '+' &&
                ushort.TryParse(value.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            { i += 5; if (!Glyph()) return null; continue; }
            if (command is not ('H' or 'A' or 'C' or 'c' or 'F' or 'f' or 'Q' or 'T' or 'W' or 'p')) return null;
            var end = value.IndexOf(';', i + 1);
            if (end < 0) return null;
            if (command == 'H')
            {
                var number = value.AsSpan(i + 1, end - i - 1);
                var relative = number.EndsWith("x", StringComparison.OrdinalIgnoreCase);
                if (relative) number = number[..^1];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount) || amount <= 0)
                    return null;
                height = relative ? height * amount : amount;
                if (!double.IsFinite(height) || height <= 0) return null;
                hasOverride = true;
                heightCodes.Add((i - 1, end - i + 2));
            }
            else if (!ValidControl(command, value.AsSpan(i + 1, end - i - 1))) return null;
            i = end;
        }
        if (!hasOverride || scopes.Count != 0 || visibleHeight is not { } result) return null;
        // Leave escaped literals and other formatting for the final PlainText call.
        var content = new StringBuilder(value);
        foreach (var code in heightCodes.AsEnumerable().Reverse()) content.Remove(code.Start, code.Length);
        return (result, content.ToString());
    }

    private static bool ValidControl(char command, ReadOnlySpan<char> argument)
    {
        if (command is 'A' or 'C' or 'c')
            return int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                number >= 0 && number <= (command == 'A' ? 2 : command == 'C' ? 256 : 0xffffff);
        if (command is 'W' or 'Q' or 'T')
            return double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) &&
                (command == 'W' ? number > 0 : command == 'T' ? number is >= .75 and <= 4 : number is >= -85 and <= 85);
        if (command is not ('F' or 'f')) return false;
        var font = argument.ToString().Split('|');
        if (string.IsNullOrWhiteSpace(font[0]) || font[0].Contains('{') || font[0].Contains('}')) return false;
        return font.Skip(1).All(option => option.Length > 1 &&
            int.TryParse(option.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 &&
            (option[0] is 'b' or 'i' ? number <= 1 : option[0] is 'c' or 'p'));
    }
}
