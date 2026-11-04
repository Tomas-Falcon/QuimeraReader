using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace QuimeraReader.Clients.Common;

/// <summary>
/// Mantiene al día el esquema de la base de datos local de los clientes (Android y Desktop).
/// Esas bases usan las entidades del servidor y no tienen migraciones: cuando el servidor añade una propiedad,
/// una instalación existente se quedaba sin la columna y cualquier consulta fallaba ("no such column").
/// Aquí se crean las tablas que falten y se añade cada columna del modelo que no exista, comparando el modelo
/// de EF con <c>PRAGMA table_info</c>. Nunca borra ni cambia columnas, así que los datos locales se conservan.
/// Este archivo se comparte por enlace entre QuimeraReader.Mobile, QuimeraReader.Desktop y los tests.
/// </summary>
public static class LocalSchemaSync
{
    /// <summary>Crea las tablas e índices que falten y añade las columnas que falten. Devuelve "Tabla.Columna" de lo añadido.</summary>
    public static async Task<IReadOnlyList<string>> EnsureSchemaAsync(DbContext db, CancellationToken cancellationToken = default)
    {
        // EnsureCreated() no sirve: el logger crea el .db con su tabla antes que nadie y EnsureCreated lo da por creado.
        // Se usa el script de creación del modelo, en orden: tablas, columnas que falten y, al final, índices
        // (un índice sobre una columna nueva fallaría si se creara antes de añadirla).
        var statements = Regex.Split(db.Database.GenerateCreateScript(), @";\s*(?:\r?\n|$)")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        foreach (var statement in statements.Where(s => s.StartsWith("CREATE TABLE \"", StringComparison.Ordinal)))
        {
            await ExecuteAsync(db, "CREATE TABLE IF NOT EXISTS \"" + statement.Substring("CREATE TABLE \"".Length), cancellationToken);
        }

        var added = await AddMissingColumnsAsync(db, cancellationToken);

        foreach (var statement in statements.Where(s => s.StartsWith("CREATE ", StringComparison.Ordinal) && s.Contains("INDEX \"")))
        {
            await ExecuteAsync(db, Regex.Replace(statement, "^CREATE (UNIQUE )?INDEX \"", "CREATE $1INDEX IF NOT EXISTS \""), cancellationToken);
        }
        return added;
    }

    private static Task ExecuteAsync(DbContext db, string sql, CancellationToken cancellationToken) =>
        // ExecuteSqlRaw interpreta {n} como parámetros: se escapan las llaves por si el DDL las contuviera
        db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}") + ";", cancellationToken);

    public static async Task<IReadOnlyList<string>> AddMissingColumnsAsync(DbContext db, CancellationToken cancellationToken = default)
    {
        var added = new List<string>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            var existingByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entityType in db.Model.GetEntityTypes())
            {
                var table = entityType.GetTableName();
                if (string.IsNullOrEmpty(table)) continue;
                var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());

                if (!existingByTable.TryGetValue(table, out var existing))
                {
                    existing = await GetColumnsAsync(connection, table, cancellationToken);
                    existingByTable[table] = existing;
                }
                // Tabla inexistente: la crea CreateMissingTablesAsync con todas sus columnas
                if (existing.Count == 0) continue;

                foreach (var property in entityType.GetProperties())
                {
                    var column = property.GetColumnName(storeObject);
                    if (string.IsNullOrEmpty(column) || existing.Contains(column)) continue;

                    var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {property.GetColumnType()}";
                    // SQLite exige un valor por defecto para añadir una columna NOT NULL a una tabla con filas
                    if (!property.IsNullable) sql += $" NOT NULL DEFAULT {DefaultLiteral(property.ClrType)}";

                    await using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                    existing.Add(column);
                    added.Add($"{table}.{column}");
                }
            }
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
        return added;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static string DefaultLiteral(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string)) return "''";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "'0001-01-01 00:00:00'";
        if (type == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
        if (type == typeof(byte[])) return "X''";
        return "0"; // números, bool y enums
    }
}
