using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WallFlow.Search;

public record IndexedFileResult(
    string Name,
    string Path,
    string Extension,
    DateTime? DateModified,
    long? SizeBytes,
    bool IsDirectory
)
{
    public string FormattedSize
    {
        get
        {
            if (IsDirectory || !SizeBytes.HasValue) return "";
            double bytes = SizeBytes.Value;
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:0.#} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):0.#} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
        }
    }

    public string FormattedDate => DateModified.HasValue ? DateModified.Value.ToString("dd/MM/yyyy HH:mm") : "";

    public string FormattedSubtitle
    {
        get
        {
            string datePart = FormattedDate;
            string sizePart = FormattedSize;
            string details = "";

            if (!string.IsNullOrEmpty(datePart) && !string.IsNullOrEmpty(sizePart))
                details = $" • {datePart} • {sizePart}";
            else if (!string.IsNullOrEmpty(datePart))
                details = $" • {datePart}";
            else if (!string.IsNullOrEmpty(sizePart))
                details = $" • {sizePart}";

            return $"{Path}{details}";
        }
    }
}

/// <summary>
/// Service to query the native Windows Search Indexer (SystemIndex) with fallback to filesystem search.
/// </summary>
public static class WindowsSearchIndexer
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    /// <summary>
    /// Searches for portable executable files (.exe) across indexed directories.
    /// </summary>
    public static async Task<List<IndexedFileResult>> SearchExecutablesAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<IndexedFileResult>();

        try
        {
            var results = new List<IndexedFileResult>();
            string safeQuery = SanitizeQuery(query);
            if (string.IsNullOrEmpty(safeQuery)) return results;

            string sql = $@"
                SELECT TOP {maxResults} 
                    System.ItemName, 
                    System.ItemPathDisplay, 
                    System.DateModified, 
                    System.Size 
                FROM SystemIndex 
                WHERE Scope='file:' 
                  AND (System.ItemName LIKE '%{safeQuery}%.exe' OR System.ItemName LIKE '%{safeQuery}%')
                  AND System.ItemType = '.exe'
                ORDER BY System.DateModified DESC";

            using var connection = new OleDbConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new OleDbCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
                string path = reader.IsDBNull(1) ? "" : reader.GetString(1);
                DateTime? dateModified = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                long? size = reader.IsDBNull(3) ? null : Convert.ToInt64(reader.GetValue(3));

                if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                string cleanName = Path.GetFileNameWithoutExtension(name);
                if (cleanName.EndsWith("setup", StringComparison.OrdinalIgnoreCase) ||
                    cleanName.EndsWith("installer", StringComparison.OrdinalIgnoreCase) ||
                    cleanName.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new IndexedFileResult(cleanName, path, ".exe", dateModified, size, false));
                if (results.Count >= maxResults)
                    break;
            }

            return results;
        }
        catch
        {
            return new List<IndexedFileResult>();
        }
    }

    /// <summary>
    /// Searches indexed files asynchronously using the native Windows Search service.
    /// Falls back to filesystem enumeration if Windows Search is unavailable.
    /// </summary>
    public static async Task<List<IndexedFileResult>> SearchFilesAsync(string query, int maxResults = 25, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<IndexedFileResult>();

        try
        {
            return await QueryWindowsSearchAsync(query.Trim(), maxResults, cancellationToken);
        }
        catch
        {
            // Fallback to directory scan if Windows Search is disabled or encounters an error
            return await Task.Run(() => FallbackFilesystemSearch(query.Trim(), maxResults, cancellationToken), cancellationToken);
        }
    }

    private static string SanitizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        string safe = query.Replace("'", "''");
        safe = safe.Replace("[", "[[]").Replace("]", "[]]");
        return safe.Trim();
    }

    private static async Task<List<IndexedFileResult>> QueryWindowsSearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var results = new List<IndexedFileResult>();
        string safeQuery = SanitizeQuery(query);
        if (string.IsNullOrEmpty(safeQuery)) return results;

        // SQL query to search file names indexed by Windows Search
        string sql = $@"
            SELECT TOP {maxResults} 
                System.ItemName, 
                System.ItemPathDisplay, 
                System.ItemType, 
                System.DateModified, 
                System.Size 
            FROM SystemIndex 
            WHERE Scope='file:' 
              AND (System.ItemName LIKE '%{safeQuery}%' OR System.FileName LIKE '%{safeQuery}%')
            ORDER BY System.DateModified DESC";

        using var connection = new OleDbConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = new OleDbCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            string path = reader.IsDBNull(1) ? "" : reader.GetString(1);
            string itemType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            DateTime? dateModified = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            long? size = reader.IsDBNull(4) ? null : Convert.ToInt64(reader.GetValue(4));

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                continue;

            bool isDir = itemType.Equals(".folder", StringComparison.OrdinalIgnoreCase) || 
                         itemType.Equals("Directory", StringComparison.OrdinalIgnoreCase) ||
                         Directory.Exists(path);

            string ext = Path.GetExtension(path).ToLowerInvariant();

            // Filter out system binaries or shortcuts from file search mode
            if (!isDir && (ext == ".lnk" || ext == ".exe" || ext == ".msi" || ext == ".dll" || ext == ".sys"))
                continue;

            results.Add(new IndexedFileResult(name, path, ext, dateModified, size, isDir));
            if (results.Count >= maxResults)
                break;
        }

        return results;
    }

    private static List<IndexedFileResult> FallbackFilesystemSearch(string query, int maxResults, CancellationToken cancellationToken)
    {
        var results = new List<IndexedFileResult>();

        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = query.Length >= 3,
            MaxRecursionDepth = query.Length < 3 ? 1 : 3
        };

        foreach (var dir in searchDirs)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", options))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    string name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        bool isDir = Directory.Exists(path);
                        string ext = Path.GetExtension(path).ToLowerInvariant();

                        if (!isDir && (ext == ".lnk" || ext == ".exe" || ext == ".msi" || ext == ".dll" || ext == ".sys"))
                            continue;

                        DateTime? dateModified = null;
                        long? size = null;

                        try
                        {
                            if (isDir)
                            {
                                dateModified = Directory.GetLastWriteTime(path);
                            }
                            else
                            {
                                var fi = new FileInfo(path);
                                dateModified = fi.LastWriteTime;
                                size = fi.Length;
                            }
                        }
                        catch { /* ignored */ }

                        results.Add(new IndexedFileResult(name, path, ext, dateModified, size, isDir));
                        if (results.Count >= maxResults) return results;
                    }
                }
            }
            catch { /* ignored */ }
        }

        return results;
    }
}
