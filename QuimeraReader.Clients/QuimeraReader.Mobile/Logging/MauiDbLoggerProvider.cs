using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using QuimeraReader.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Mobile.Logging
{
    public class MauiDbLoggerProvider : ILoggerProvider
    {
        private readonly string _connectionString;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private static readonly HttpClient _logHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        public static MauiDbLoggerProvider? Instance { get; private set; }

        public MauiDbLoggerProvider()
        {
            Instance = this;
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "quimerareader_local.db");
            _connectionString = "Data Source=" + dbPath;

            EnsureTableCreated();
            RegisterGlobalExceptionHandlers();
            _ = RunPeriodicFlushLoopAsync(_cts.Token);
        }

        private void EnsureTableCreated()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE IF NOT EXISTS ClientLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Level TEXT NOT NULL,
                        Message TEXT NOT NULL,
                        Exception TEXT NULL,
                        CreatedAt TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                WriteLogDirect("Critical", "[AppDomain.UnhandledException] Excepción no controlada en Android", e.ExceptionObject?.ToString());
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteLogDirect("Error", "[TaskScheduler.UnobservedTaskException] Tarea con excepción no observada", e.Exception?.ToString());
            };
        }

        public void WriteLogDirect(string level, string message, string? exception = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO ClientLogs (Level, Message, Exception, CreatedAt) VALUES (@l, @m, @e, @c)";
                cmd.Parameters.AddWithValue("@l", level);
                cmd.Parameters.AddWithValue("@m", message);
                cmd.Parameters.AddWithValue("@e", (object?)exception ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private async Task RunPeriodicFlushLoopAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    await FlushPendingLogsAsync();
                }
            }
            catch { }
        }

        public async Task<int> FlushPendingLogsAsync()
        {
            var serverUrl = Preferences.Default.Get("ServerUrl", "");
            if (string.IsNullOrWhiteSpace(serverUrl)) return 0;
            if (!serverUrl.EndsWith("/")) serverUrl += "/";

            if (!await _flushLock.WaitAsync(0)) return 0;

            try
            {
                var logsToSend = new List<ClientLogDto>();
                var idsToDelete = new List<int>();

                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var selectCmd = connection.CreateCommand();
                    selectCmd.CommandText = "SELECT Id, Level, Message, Exception, CreatedAt FROM ClientLogs ORDER BY Id ASC LIMIT 100";
                    using var reader = await selectCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        var level = reader.IsDBNull(1) ? "Information" : reader.GetString(1);
                        var message = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        var ex = reader.IsDBNull(3) ? null : reader.GetString(3);
                        var createdStr = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        DateTime.TryParse(createdStr, out var createdAt);

                        idsToDelete.Add(id);
                        logsToSend.Add(new ClientLogDto
                        {
                            Id = id,
                            Level = level,
                            Message = message,
                            Exception = ex,
                            CreatedAt = createdAt == default ? DateTime.UtcNow : createdAt
                        });
                    }
                }

                if (logsToSend.Count == 0) return 0;

                var endpoint = new Uri(new Uri(serverUrl), "api/Logs");
                using var response = await _logHttpClient.PostAsJsonAsync(endpoint, logsToSend);
                if (response.IsSuccessStatusCode)
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    using var deleteCmd = connection.CreateCommand();
                    deleteCmd.CommandText = $"DELETE FROM ClientLogs WHERE Id IN ({string.Join(",", idsToDelete)})";
                    await deleteCmd.ExecuteNonQueryAsync();
                    return logsToSend.Count;
                }
            }
            catch
            {
                // Si no hay red o el servidor no responde, quedan guardados en SQLite para el siguiente ciclo
            }
            finally
            {
                _flushLock.Release();
            }

            return 0;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new MauiDbLogger(this, categoryName);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    public class MauiDbLogger : ILogger
    {
        private readonly MauiDbLoggerProvider _provider;
        private readonly string _categoryName;

        public MauiDbLogger(MauiDbLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_categoryName.StartsWith("QuimeraReader", StringComparison.OrdinalIgnoreCase))
            {
                return logLevel >= LogLevel.Information;
            }
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var exStr = exception?.ToString();

            Task.Run(() =>
            {
                _provider.WriteLogDirect(logLevel.ToString(), "[" + _categoryName + "] " + message, exStr);
            });
        }
    }
}
