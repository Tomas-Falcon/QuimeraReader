using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuimeraReader.Application.Common;

/// <summary>
/// Convierte lo que escribe el usuario en una consulta FTS5 segura para los índices de búsqueda.
/// - Cada palabra es un prefijo ("potter" encuentra "Potter", "rowl" encuentra "Rowling").
/// - Todas las palabras deben aparecer, en cualquier orden y en cualquier campo (título, autores, saga):
///   "harry potter rowling" o "wells martha" funcionan.
/// - Las tildes las ignora el tokenizador (remove_diacritics), así que "zafon" encuentra "Zafón".
/// - Iniciales juntas en mayúsculas ("JK Rowling") también encuentran "J. K. Rowling".
/// </summary>
public static class SearchQueryBuilder
{
    private static readonly Regex TokenSplitter = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    /// <returns>La expresión MATCH, o null si el texto no contiene ninguna palabra buscable.</returns>
    public static string? Build(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput)) return null;

        var tokens = TokenSplitter.Split(userInput.Trim())
            .Where(t => t.Length > 0)
            .Take(12) // una búsqueda razonable nunca necesita más; evita consultas FTS gigantes
            .ToList();
        if (tokens.Count == 0) return null;

        var parts = new List<string>();
        foreach (var token in tokens)
        {
            var term = $"\"{token.ToLowerInvariant()}\"*";
            if (IsInitials(token))
            {
                // "JK" -> ("jk"* OR ("j" AND "k")): las iniciales del nombre se indexan como palabras sueltas
                var letters = string.Join(" AND ", token.ToLowerInvariant().Select(c => $"\"{c}\""));
                parts.Add($"({term} OR ({letters}))");
            }
            else
            {
                parts.Add(term);
            }
        }
        // AND explícito: FTS5 no admite el AND implícito junto a grupos entre paréntesis
        return string.Join(" AND ", parts);
    }

    private static bool IsInitials(string token) =>
        token.Length is >= 2 and <= 3 && token.All(char.IsUpper);
}
