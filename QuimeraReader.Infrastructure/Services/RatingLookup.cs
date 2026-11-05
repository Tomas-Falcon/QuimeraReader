using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Busca la puntuación "real" de un libro: la que más gente votó, independientemente del proveedor que dio los demás
/// datos o la portada. Hardcover encuentra la obra original desde el título traducido ("Sistemas críticos" →
/// "All Systems Red", 4,07 con miles de votos), cosa que Google casi nunca tiene para ediciones en español.
/// </summary>
public static class RatingLookup
{
    public const string RatingProvider = "Hardcover";

    public static async Task<RatingPolicy.Rating?> FindBestAsync(IEnumerable<IMetadataProvider> providers, string title, string? author,
        Dictionary<string, string>? settings, BookMetadata? known = null)
    {
        var candidates = new List<RatingPolicy.Rating>();
        if (known?.AverageRating is double knownValue && known.RatingsCount is int knownVotes)
            candidates.Add(new RatingPolicy.Rating(knownValue, knownVotes, known.Source));

        // Si los datos ya vienen de Hardcover no se repite la consulta (su API limita las peticiones por minuto)
        var provider = known?.Source == RatingProvider ? null : providers.FirstOrDefault(p => p.ProviderName == RatingProvider);
        if (provider != null && !string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var results = await provider.SearchCoversAsync(CleanTitle(title), author, settings);
                candidates.AddRange(results
                    .Where(r => r.Rating.HasValue && r.RatingsCount.HasValue && CoverPolicy.AuthorsMatch(author, r.Authors))
                    .Select(r => new RatingPolicy.Rating(r.Rating!.Value, r.RatingsCount!.Value, r.Provider)));
            }
            catch (Exception) { /* sin puntuación externa: se queda la que haya */ }
        }
        return RatingPolicy.Best(candidates);
    }

    /// <summary>Aplica la puntuación si mejora la actual (más votos). Devuelve true si cambió.</summary>
    public static bool Apply(Book book, RatingPolicy.Rating? rating)
    {
        if (rating is not { } r || !RatingPolicy.ShouldReplace(book.AverageRating, book.RatingsCount, r)) return false;
        book.AverageRating = Math.Round(r.Value, 2);
        book.RatingsCount = r.Votes;
        return true;
    }

    // "Drive (no oficial)" → "Drive": los añadidos entre paréntesis o corchetes impiden encontrar la obra
    private static string CleanTitle(string title)
    {
        var cleaned = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();
        return cleaned.Length > 0 ? cleaned : title;
    }
}
