using System;
using System.IO;
using System.Text;
using System.Threading;

namespace FantasyTools.Services;

internal static class JsonFileWriteService
{
    private static readonly object FileWriteLock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAtomic(string path, string json)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"文件目录无效：{path}");
        Directory.CreateDirectory(directory);

        lock (FileWriteLock)
        {
            var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempPath, json, Utf8NoBom);
                RetryFileOperation(() =>
                {
                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, null);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                });
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    TryDeleteTempFile(tempPath);
                }
            }
        }
    }

    private static void RetryFileOperation(Action operation)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(35 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(35 * attempt);
            }
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // A failed cleanup should not hide the original save result.
        }
    }
}
