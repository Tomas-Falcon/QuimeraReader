using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Interfaces;

/// <summary>
/// Índices de búsqueda de texto (FTS5): libros por título + autores + saga, y autores por nombre.
/// Ignoran mayúsculas y tildes y devuelven los resultados ordenados por relevancia (el primero es el mejor).
/// </summary>
public interface ISearchIndex
{
    /// <param name="ftsQuery">Expresión generada por <see cref="Common.SearchQueryBuilder"/>.</param>
    Task<IReadOnlyList<int>> SearchBookIdsAsync(string ftsQuery, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> SearchAuthorIdsAsync(string ftsQuery, int limit, CancellationToken cancellationToken = default);
}
