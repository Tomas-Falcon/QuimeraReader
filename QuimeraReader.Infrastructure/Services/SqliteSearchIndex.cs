using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Índices FTS5 de búsqueda. Son tablas propias (no "external content"): se rellenan desde Books/Authors/Series y
/// AppDbContext los mantiene al guardar cambios. remove_diacritics 2 hace que "zafon" y "Zafón" sean lo mismo.
/// </summary>
public class SqliteSearchIndex : ISearchIndex
{
    private readonly AppDbContext _dbContext;

    public SqliteSearchIndex(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public const string CreateSql = @"
CREATE VIRTUAL TABLE IF NOT EXISTS BooksSearch USING fts5(Title, Authors, Series, tokenize = 'unicode61 remove_diacritics 2');
CREATE VIRTUAL TABLE IF NOT EXISTS AuthorsSearch USING fts5(Name, tokenize = 'unicode61 remove_diacritics 2');";

    // Fila de búsqueda de un libro: título, todos sus autores y el nombre de su saga
    private const string BookRowsSelect = @"
SELECT b.Id, b.Title,
       (SELECT group_concat(a.Name, ' ; ') FROM BookAuthors ba JOIN Authors a ON a.Id = ba.AuthorId WHERE ba.BookId = b.Id),
       (SELECT s.Name FROM Series s WHERE s.Id = b.SeriesId)
FROM Books b";

    public const string RebuildSql = @"
DELETE FROM BooksSearch;
INSERT INTO BooksSearch(rowid, Title, Authors, Series)" + BookRowsSelect + @";
DELETE FROM AuthorsSearch;
INSERT INTO AuthorsSearch(rowid, Name) SELECT Id, Name FROM Authors;";

    public async Task<IReadOnlyList<int>> SearchBookIdsAsync(string ftsQuery, int limit, CancellationToken cancellationToken = default)
    {
        // bm25: cuanto menor, más relevante. Pesos: título 10, autores 6, saga 4
        return await QueryIdsAsync(
            "SELECT rowid FROM BooksSearch WHERE BooksSearch MATCH $q ORDER BY bm25(BooksSearch, 10.0, 6.0, 4.0) LIMIT $limit",
            ftsQuery, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> SearchAuthorIdsAsync(string ftsQuery, int limit, CancellationToken cancellationToken = default)
    {
        return await QueryIdsAsync(
            "SELECT rowid FROM AuthorsSearch WHERE AuthorsSearch MATCH $q ORDER BY rank LIMIT $limit",
            ftsQuery, limit, cancellationToken);
    }

    private async Task<IReadOnlyList<int>> QueryIdsAsync(string sql, string ftsQuery, int limit, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        bool opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "$q", ftsQuery);
            AddParameter(command, "$limit", limit);

            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt32(0));
            }
            return ids;
        }
        finally
        {
            if (opened) await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Crea las tablas si no existen y las reconstruye si están vacías o descuadradas con Books/Authors.</summary>
    public static async Task EnsureUpToDateAsync(AppDbContext dbContext, bool forceRebuild = false, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(CreateSql, cancellationToken);

        if (!forceRebuild)
        {
            var counts = await dbContext.Database.SqlQueryRaw<int>(@"
SELECT ((SELECT COUNT(*) FROM Books) <> (SELECT COUNT(*) FROM BooksSearch))
     + ((SELECT COUNT(*) FROM Authors) <> (SELECT COUNT(*) FROM AuthorsSearch)) AS Value").ToListAsync(cancellationToken);
            if (counts.FirstOrDefault() == 0) return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(RebuildSql, cancellationToken);
    }

    /// <summary>Recalcula las filas de los libros/autores indicados (los borrados desaparecen del índice).</summary>
    public static async Task RefreshAsync(AppDbContext dbContext, ICollection<int> bookIds, ICollection<int> authorIds, CancellationToken cancellationToken = default)
    {
        if (bookIds.Count > 0)
        {
            var ids = string.Join(",", bookIds.Distinct());
            // Los ids son enteros generados por la propia app: concatenarlos no admite inyección
            await dbContext.Database.ExecuteSqlRawAsync(
                $"DELETE FROM BooksSearch WHERE rowid IN ({ids}); INSERT INTO BooksSearch(rowid, Title, Authors, Series){BookRowsSelect} WHERE b.Id IN ({ids});",
                cancellationToken);
        }

        if (authorIds.Count > 0)
        {
            var ids = string.Join(",", authorIds.Distinct());
            await dbContext.Database.ExecuteSqlRawAsync(
                $"DELETE FROM AuthorsSearch WHERE rowid IN ({ids}); INSERT INTO AuthorsSearch(rowid, Name) SELECT Id, Name FROM Authors WHERE Id IN ({ids});",
                cancellationToken);
        }
    }
}
