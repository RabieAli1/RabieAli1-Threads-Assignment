using System.Collections.Concurrent;
using System.Text.Json;
using OrderProcessing.Shared.Exceptions;

namespace OrderProcessing.Shared.Storage;

/// <summary>
/// Thread-safe file store responsible for reading and writing JSON collections.
/// Guarantees atomic writes (no half-written files) and concurrency protection.
/// </summary>
public class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim GetFileLock(string fileName)
    {
        string key = Path.GetFileName(fileName);
        return FileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<List<T>> ReadAllAsync<T>(string fileName, CancellationToken ct = default)
    {
        var semaphore = GetFileLock(fileName);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string filePath = DataFiles.GetFilePath(fileName);
            if (!File.Exists(filePath))
            {
                return new List<T>();
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0)
            {
                return new List<T>();
            }

            try
            {
                var result = await JsonSerializer.DeserializeAsync<List<T>>(stream, DataFiles.SerializerOptions, ct).ConfigureAwait(false);
                return result ?? new List<T>();
            }
            catch (JsonException ex)
            {
                throw new DataFileCorruptedException(fileName, $"Failed to parse corrupted JSON in file '{fileName}'.", ex);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task WriteAllAsync<T>(string fileName, IEnumerable<T> items, CancellationToken ct = default)
    {
        var semaphore = GetFileLock(fileName);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        string filePath = DataFiles.GetFilePath(fileName);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var list = items as IList<T> ?? items.ToList();
                await JsonSerializer.SerializeAsync(stream, list, DataFiles.SerializerOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task UpdateAsync<T>(string fileName, Action<List<T>> updateAction, CancellationToken ct = default)
    {
        var semaphore = GetFileLock(fileName);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        string filePath = DataFiles.GetFilePath(fileName);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");

        try
        {
            List<T> list;
            if (!File.Exists(filePath))
            {
                list = new List<T>();
            }
            else
            {
                using var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (readStream.Length == 0)
                {
                    list = new List<T>();
                }
                else
                {
                    try
                    {
                        list = await JsonSerializer.DeserializeAsync<List<T>>(readStream, DataFiles.SerializerOptions, ct).ConfigureAwait(false) ?? new List<T>();
                    }
                    catch (JsonException ex)
                    {
                        throw new DataFileCorruptedException(fileName, $"Failed to parse corrupted JSON in file '{fileName}'.", ex);
                    }
                }
            }

            updateAction(list);

            using (var writeStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(writeStream, list, DataFiles.SerializerOptions, ct).ConfigureAwait(false);
                await writeStream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task AppendAsync<T>(string fileName, T item, CancellationToken ct = default) =>
        UpdateAsync<T>(fileName, list => list.Add(item), ct);

    public List<T> ReadAll<T>(string fileName)
    {
        var semaphore = GetFileLock(fileName);
        semaphore.Wait();
        try
        {
            string filePath = DataFiles.GetFilePath(fileName);
            if (!File.Exists(filePath))
            {
                return new List<T>();
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0)
            {
                return new List<T>();
            }

            try
            {
                var result = JsonSerializer.Deserialize<List<T>>(stream, DataFiles.SerializerOptions);
                return result ?? new List<T>();
            }
            catch (JsonException ex)
            {
                throw new DataFileCorruptedException(fileName, $"Failed to parse corrupted JSON in file '{fileName}'.", ex);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void WriteAll<T>(string fileName, IEnumerable<T> items)
    {
        var semaphore = GetFileLock(fileName);
        semaphore.Wait();

        string filePath = DataFiles.GetFilePath(fileName);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var list = items as IList<T> ?? items.ToList();
                JsonSerializer.Serialize(stream, list, DataFiles.SerializerOptions);
                stream.Flush();
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Update<T>(string fileName, Action<List<T>> updateAction)
    {
        var semaphore = GetFileLock(fileName);
        semaphore.Wait();

        string filePath = DataFiles.GetFilePath(fileName);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");

        try
        {
            List<T> list;
            if (!File.Exists(filePath))
            {
                list = new List<T>();
            }
            else
            {
                using var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (readStream.Length == 0)
                {
                    list = new List<T>();
                }
                else
                {
                    try
                    {
                        list = JsonSerializer.Deserialize<List<T>>(readStream, DataFiles.SerializerOptions) ?? new List<T>();
                    }
                    catch (JsonException ex)
                    {
                        throw new DataFileCorruptedException(fileName, $"Failed to parse corrupted JSON in file '{fileName}'.", ex);
                    }
                }
            }

            updateAction(list);

            using (var writeStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(writeStream, list, DataFiles.SerializerOptions);
                writeStream.Flush();
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Append<T>(string fileName, T item) =>
        Update<T>(fileName, list => list.Add(item));
}
