using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace QuimeraReader.Mobile.Logging
{
    public class MauiDbLoggerProvider : ILoggerProvider
    {
        private readonly string _connectionString;

        public MauiDbLoggerProvider()
        {
            string dbPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "quimerareader_local.db");
            _connectionString = "Data Source=" + dbPath;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new MauiDbLogger(_connectionString, categoryName);
        }

        public void Dispose() { }
    }

    public class MauiDbLogger : ILogger
    {
        private readonly string _connectionString;
        private readonly string _categoryName;

        public MauiDbLogger(string connectionString, string categoryName)
        {
            _connectionString = connectionString;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning; // Only log warnings and above to save space

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var exStr = exception?.ToString();

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO ClientLogs (Level, Message, Exception, CreatedAt) VALUES (@l, @m, @e, @c)";
                    cmd.Parameters.AddWithValue("@l", logLevel.ToString());
                    cmd.Parameters.AddWithValue("@m", "[" + _categoryName + "] " + message);
                    cmd.Parameters.AddWithValue("@e", (object?)exStr ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
                catch { /* Ignore logging errors */ }
            });
        }
    }
}
