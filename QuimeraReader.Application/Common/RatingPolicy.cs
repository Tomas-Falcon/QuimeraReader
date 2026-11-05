using System.Collections.Generic;
using System.Linq;

namespace QuimeraReader.Application.Common;

/// <summary>
/// Qué puntuación se queda un libro: la que más gente votó, venga del proveedor que venga e independientemente de la
/// portada. Antes valía la del primer proveedor que respondía (un 5,0 de Google con un solo voto).
/// </summary>
public static class RatingPolicy
{
    /// <summary>Con menos votos la media no dice nada (Google da 5,0 con 1 voto en muchas ediciones en español).</summary>
    public const int MinVotes = 5;

    public readonly record struct Rating(double Value, int Votes, string? Source = null);

    /// <summary>Una puntuación usable: entre 0 y 5 y con votos suficientes.</summary>
    public static bool IsReliable(double? value, int? votes) => value is > 0 and <= 5 && votes >= MinVotes;

    /// <summary>La de más votos entre las fiables; null si no hay ninguna.</summary>
    public static Rating? Best(IEnumerable<Rating> candidates) =>
        candidates.Where(c => IsReliable(c.Value, c.Votes))
            .OrderByDescending(c => c.Votes)
            .Select(c => (Rating?)c)
            .FirstOrDefault();

    /// <summary>
    /// ¿La nueva reemplaza a la actual? Sí si es fiable y tiene más votos. Una actual sin número de votos (de antes de
    /// guardarlos) cuenta como cero, así que la primera fiable la sustituye.
    /// </summary>
    public static bool ShouldReplace(double? currentValue, int? currentVotes, Rating candidate) =>
        IsReliable(candidate.Value, candidate.Votes) && (!currentValue.HasValue || candidate.Votes > (currentVotes ?? 0));
}
