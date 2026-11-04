using System.Globalization;
using System.Linq;
using System.Text;

namespace QuimeraReader.Application.Common;

public static class TextNormalizer
{
    /// <summary>Minúsculas y sin tildes: "Crónicas de Narnia" -> "cronicas de narnia". Para comparar nombres en memoria.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    /// <summary>true si todas las palabras de la búsqueda aparecen en el texto (sin tildes, en cualquier orden).</summary>
    public static bool MatchesAllWords(string? text, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var folded = Fold(text);
        return Fold(search).Split(' ', System.StringSplitOptions.RemoveEmptyEntries).All(folded.Contains);
    }
}
