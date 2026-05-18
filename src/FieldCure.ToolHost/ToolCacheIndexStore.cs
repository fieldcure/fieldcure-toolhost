using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldCure.ToolHost;

/// <summary>
/// Atomic, fault-tolerant reader/writer for <see cref="ToolCacheIndex"/>.
/// </summary>
public sealed class ToolCacheIndexStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Creates a store backed by the default index file location
    /// (<see cref="ToolHostPaths.GetIndexFilePath"/>).
    /// </summary>
    public ToolCacheIndexStore() : this(ToolHostPaths.GetIndexFilePath())
    {
    }

    /// <summary>Creates a store backed by an explicit file path. Useful for tests.</summary>
    /// <param name="filePath">Absolute path to the JSON file used for persistence.</param>
    public ToolCacheIndexStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>
    /// Loads the index from disk, or returns an empty index if the file is missing,
    /// unreadable, or fails schema validation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ToolCacheIndex> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return ToolCacheIndex.Empty();
            }

            await using FileStream stream = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                ToolCacheIndex? index = await JsonSerializer
                    .DeserializeAsync<ToolCacheIndex>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);

                if (index is null || index.SchemaVersion != ToolCacheIndex.CurrentSchemaVersion)
                {
                    return ToolCacheIndex.Empty();
                }

                return index;
            }
            catch (JsonException)
            {
                // Corrupt file — treat as empty. Caller will overwrite on next save.
                return ToolCacheIndex.Empty();
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>
    /// Writes the index atomically: serializes to a sibling <c>.tmp</c> file, then renames.
    /// </summary>
    /// <param name="index">Index to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveAsync(ToolCacheIndex index, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            await using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, index, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>
    /// Reads the index, applies <paramref name="mutator"/>, and saves the result atomically.
    /// </summary>
    /// <param name="mutator">A pure function transforming the current index into the next state.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ToolCacheIndex> UpdateAsync(Func<ToolCacheIndex, ToolCacheIndex> mutator, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        ToolCacheIndex current = await LoadAsync(ct).ConfigureAwait(false);
        ToolCacheIndex next = mutator(current);
        await SaveAsync(next, ct).ConfigureAwait(false);
        return next;
    }

    /// <summary>Releases the synchronization primitive backing concurrent access.</summary>
    public void Dispose() => _gate.Dispose();
}
