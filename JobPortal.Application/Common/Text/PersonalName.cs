namespace JobPortal.Application.Common.Text;

using System.Globalization;
using System.Text;

public static class PersonalName
{
    public static bool TrySplit(
        string? value,
        out string firstName,
        out string lastName)
    {
        firstName = string.Empty;
        lastName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.Length > 201 || !IsValid(normalized))
            return false;

        var words = normalized.Split(' ', StringSplitOptions.None);
        firstName = words[0];
        lastName = string.Join(' ', words.Skip(1));
        return firstName.Length is > 0 and <= 100 && lastName.Length <= 100;
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (normalized.Contains("  ", StringComparison.Ordinal) ||
            normalized.Any(char.IsControl) || normalized.Contains('<') || normalized.Contains('>'))
            return false;
        return normalized.Split(' ').All(word =>
        {
            var runes = word.EnumerateRunes().ToArray();
            if (runes.Length == 0 || !IsUnicodeNameCharacter(runes[0]) ||
                !IsUnicodeNameCharacter(runes[^1])) return false;
            for (var index = 1; index < runes.Length - 1; index++)
            {
                if (IsUnicodeNameCharacter(runes[index])) continue;
                if (runes[index].Value is not ('\'' or '-') ||
                    !IsUnicodeNameCharacter(runes[index - 1]) ||
                    !IsUnicodeNameCharacter(runes[index + 1])) return false;
            }
            return true;
        });
    }

    private static bool IsUnicodeNameCharacter(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark;
}
