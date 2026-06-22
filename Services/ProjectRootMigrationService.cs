using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class ProjectRootMigrationService
{
    public MigrationResult Migrate(
        string oldProjectRootPath,
        string newProjectRootPath,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var oldRoot = NormalizePath(oldProjectRootPath);
        var newRoot = NormalizePath(newProjectRootPath);

        if (PathsEqual(oldRoot, newRoot))
        {
            progress?.Report(new ProgressUpdate("整体项目目录未变更。", 100, newRoot));
            return new MigrationResult(0, 0);
        }

        if (IsPathInsideDirectory(newRoot, oldRoot))
        {
            throw new InvalidOperationException("新整体项目目录不能位于旧整体项目目录内部。");
        }

        if (IsPathInsideDirectory(oldRoot, newRoot))
        {
            throw new InvalidOperationException("新整体项目目录不能包含旧整体项目目录。");
        }

        if (Directory.Exists(newRoot) && !IsDirectoryEmpty(newRoot))
        {
            throw new InvalidOperationException("新整体项目目录已存在且不是空目录，请选择空目录或新的父目录。");
        }

        if (!Directory.Exists(oldRoot))
        {
            progress?.Report(new ProgressUpdate("旧目录不存在，正在创建新目录。", 35, newRoot));
            Directory.CreateDirectory(newRoot);
            progress?.Report(new ProgressUpdate("新整体项目目录已就绪。", 100, newRoot));
            return new MigrationResult(0, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProgressUpdate("正在扫描整体项目目录...", 2, oldRoot, true));

        var directories = Directory.GetDirectories(oldRoot, "*", SearchOption.AllDirectories);
        var files = Directory.GetFiles(oldRoot, "*", SearchOption.AllDirectories);
        var totalWork = Math.Max(files.Length, 1);

        Directory.CreateDirectory(newRoot);
        for (var index = 0; index < directories.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetDirectory = MapPath(oldRoot, newRoot, directories[index]);
            Directory.CreateDirectory(targetDirectory);

            if (index % 20 == 0 || index == directories.Length - 1)
            {
                progress?.Report(new ProgressUpdate(
                    "正在创建目标目录...",
                    5 + (index + 1d) / Math.Max(directories.Length, 1) * 10,
                    targetDirectory));
            }
        }

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = files[index];
            var targetFile = MapPath(oldRoot, newRoot, sourceFile);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);

            if (index % 8 == 0 || index == files.Length - 1)
            {
                progress?.Report(new ProgressUpdate(
                    "正在复制整体项目文件...",
                    15 + (index + 1d) / totalWork * 55,
                    Path.GetFileName(sourceFile)));
            }
        }

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = files[index];
            var targetFile = MapPath(oldRoot, newRoot, sourceFile);
            VerifyCopiedFile(sourceFile, targetFile);

            if (index % 8 == 0 || index == files.Length - 1)
            {
                progress?.Report(new ProgressUpdate(
                    "正在校验整体项目文件...",
                    70 + (index + 1d) / totalWork * 24,
                    Path.GetFileName(sourceFile)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProgressUpdate(
            "整体项目文件复制和校验完成。",
            94,
            $"已校验 {files.Length} 个文件，等待写入新设置。"));
        return new MigrationResult(files.Length, directories.Length);
    }

    public bool TryDeleteOldProjectRoot(
        string oldProjectRootPath,
        IProgress<ProgressUpdate>? progress,
        out string? cleanupError)
    {
        var oldRoot = NormalizePath(oldProjectRootPath);
        cleanupError = null;

        if (!Directory.Exists(oldRoot))
        {
            progress?.Report(new ProgressUpdate("旧整体项目目录已不存在。", 100, oldRoot));
            return true;
        }

        try
        {
            progress?.Report(new ProgressUpdate("正在删除旧整体项目目录...", 96, oldRoot));
            Directory.Delete(oldRoot, recursive: true);
            progress?.Report(new ProgressUpdate("旧整体项目目录已删除。", 100, oldRoot));
            return true;
        }
        catch (Exception ex)
        {
            cleanupError = ex.Message;
            progress?.Report(new ProgressUpdate("旧整体项目目录清理失败。", 100, cleanupError));
            return false;
        }
    }

    public static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathInsideDirectory(string candidatePath, string directoryPath)
    {
        var candidate = NormalizePath(candidatePath);
        var directory = NormalizePath(directoryPath);
        if (PathsEqual(candidate, directory))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(directory, candidate);
        return !relativePath.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private static void VerifyCopiedFile(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
        {
            throw new IOException($"目标文件缺失：{targetFile}");
        }

        var sourceInfo = new FileInfo(sourceFile);
        var targetInfo = new FileInfo(targetFile);
        if (sourceInfo.Length != targetInfo.Length)
        {
            throw new IOException($"文件大小校验失败：{sourceFile}");
        }

        if (!HashesEqual(sourceFile, targetFile))
        {
            throw new IOException($"文件哈希校验失败：{sourceFile}");
        }
    }

    private static bool HashesEqual(string sourceFile, string targetFile)
    {
        using var sha256 = SHA256.Create();
        using var sourceStream = File.OpenRead(sourceFile);
        using var targetStream = File.OpenRead(targetFile);
        var sourceHash = sha256.ComputeHash(sourceStream);
        var targetHash = sha256.ComputeHash(targetStream);
        return sourceHash.AsSpan().SequenceEqual(targetHash);
    }

    private static string MapPath(string oldRoot, string newRoot, string sourcePath)
    {
        var relativePath = Path.GetRelativePath(oldRoot, sourcePath);
        return Path.Combine(newRoot, relativePath);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsDirectoryEmpty(string directoryPath)
    {
        using var entries = Directory.EnumerateFileSystemEntries(directoryPath).GetEnumerator();
        return !entries.MoveNext();
    }
}
