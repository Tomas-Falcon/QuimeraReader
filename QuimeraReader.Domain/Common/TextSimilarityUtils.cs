using System;
using System.Collections.Generic;
using System.Linq;

namespace QuimeraReader.Domain.Common;

/// <summary>
/// Utilidades puras para cálculo de similitud textual y generación de n-gramas.
/// </summary>
public static class TextSimilarityUtils
{
    /// <summary>
    /// Calcula la similitud de dos cadenas (0 a 100) basándose en la distancia de Levenshtein.
    /// </summary>
    public static double CalculateSimilarity(string? source, string? target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;
        if (source == target) return 100.0;

        int n = source.Length;
        int m = target.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return (1.0 - ((double)d[n, m] / Math.Max(source.Length, target.Length))) * 100.0;
    }

    /// <summary>
    /// Genera un conjunto de trigramas de palabras a partir de un texto normalizado.
    /// </summary>
    public static HashSet<string> GenerateTrigrams(string? text)
    {
        var trigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return trigrams;

        var cleanText = new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        var words = cleanText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 3) return trigrams;

        for (int i = 0; i < words.Length - 2; i++)
        {
            trigrams.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
        }

        return trigrams;
    }
}
