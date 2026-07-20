using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ContentWriter.Infrastructure.Data;

public static class DatabaseConnectionResolver
{
    public static string Resolve(IConfiguration configuration) =>
        TryResolve(configuration)
            ?? throw new InvalidOperationException("Connection string 'ContentWriterDb' is not configured.");

    /// <summary>Same as <see cref="Resolve"/> but returns null instead of throwing when no Postgres connection is configured — the caller falls back to an in-memory store.</summary>
    public static string? TryResolve(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("CONTENT_WRITER_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (!string.IsNullOrWhiteSpace(fromEnv))
            return NormalizePostgresUrl(fromEnv);

        return configuration.GetConnectionString("ContentWriterDb");
    }

    /// <summary>Converts Supabase/Railway <c>postgres://</c> URLs to Npgsql key/value format.</summary>
    public static string NormalizePostgresUrl(string rawValue)
    {
        var value = rawValue.ReplaceLineEndings("").Trim().Trim('"', '\'');
        if (!value.Contains("://", StringComparison.Ordinal))
            return value;

        try
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri))
                return value;

            if (databaseUri.Scheme is not ("postgres" or "postgresql"))
                return value;

            var userInfo = databaseUri.UserInfo.Split(':', 2);
            var username = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
            var database = databaseUri.AbsolutePath.Trim('/').Split('/', 2)[0];

            var connBuilder = new NpgsqlConnectionStringBuilder
            {
                Host = databaseUri.Host,
                Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
                Username = username,
                Password = password,
                Database = database,
            };

            foreach (var segment in databaseUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = segment.Split('=', 2);
                if (pair.Length != 2)
                    continue;

                var key = Uri.UnescapeDataString(pair[0]);
                var val = Uri.UnescapeDataString(pair[1]);
                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<SslMode>(val, true, out var parsedMode))
                {
                    connBuilder.SslMode = parsedMode;
                }
            }

            // Supabase pooler/session URLs often omit sslmode; require TLS.
            if (connBuilder.SslMode == SslMode.Disable && databaseUri.Host.Contains("supabase", StringComparison.OrdinalIgnoreCase))
                connBuilder.SslMode = SslMode.Require;

            return connBuilder.ConnectionString;
        }
        catch
        {
            return value;
        }
    }
}
