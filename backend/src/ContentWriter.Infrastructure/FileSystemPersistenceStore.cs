using System.Text;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure;

/// <summary>
/// Filesystem-backed persistence store. Data is organized as:
/// {dataDirectory}/{collection}/{id}.json
/// Writes are atomic: temp file + Move overwrite.
/// </summary>
public sealed class FileSystemPersistenceStore : IPersistenceStore
{
    private readonly string _dataDirectory;
    private readonly ILogger<FileSystemPersistenceStore> _logger;

    public FileSystemPersistenceStore(string dataDirectory, ILogger<FileSystemPersistenceStore> logger)
    {
        _dataDirectory = dataDirectory ?? "./data";
        _logger = logger;
        Directory.CreateDirectory(_dataDirectory);
    }

    public async Task SaveDocumentAsync(string collection, Guid id, string json, CancellationToken cancellationToken = default)
    {
        var collectionDir = Path.Combine(_dataDirectory, collection);
        Directory.CreateDirectory(collectionDir);

        var filePath = Path.Combine(collectionDir, $"{id:D}.json");
        var tempPath = Path.Combine(collectionDir, $"{id:D}.json.tmp");

        try
        {
            // Write to temp file first
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
            // Atomic rename
            File.Move(tempPath, filePath, overwrite: true);
            _logger.LogDebug("Persisted {Collection}/{Id} ({Bytes} bytes)", collection, id, json.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist {Collection}/{Id}", collection, id);
            // Clean up temp file if it exists
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public async Task<string?> LoadDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_dataDirectory, collection, $"{id:D}.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            _logger.LogDebug("Loaded {Collection}/{Id} ({Bytes} bytes)", collection, id, json.Length);
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Collection}/{Id}", collection, id);
            throw;
        }
    }

    public Task<IReadOnlyList<Guid>> ListDocumentsAsync(string collection, CancellationToken cancellationToken = default)
    {
        var collectionDir = Path.Combine(_dataDirectory, collection);

        if (!Directory.Exists(collectionDir))
        {
            return Task.FromResult((IReadOnlyList<Guid>)Array.Empty<Guid>());
        }

        try
        {
            var ids = Directory.GetFiles(collectionDir, "*.json")
                .Select(f =>
                {
                    var filename = Path.GetFileNameWithoutExtension(f);
                    return Guid.TryParse(filename, out var id) ? id : (Guid?)null;
                })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            _logger.LogDebug("Listed {Collection}: {Count} document(s)", collection, ids.Count);
            return Task.FromResult((IReadOnlyList<Guid>)ids);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list {Collection}", collection);
            throw;
        }
    }

    public Task DeleteDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_dataDirectory, collection, $"{id:D}.json");

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted {Collection}/{Id}", collection, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Collection}/{Id}", collection, id);
            throw;
        }

        return Task.CompletedTask;
    }
}
