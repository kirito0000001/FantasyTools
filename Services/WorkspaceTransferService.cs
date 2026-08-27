using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class WorkspaceTransferService
{
    private const string PackageManifestFileName = "fantasy-package.json";
    private const int PackageSchemaVersion = 1;

    public string ExportCharacters(
        string projectRootPath,
        IEnumerable<string>? selectedCodes = null,
        IProgress<ProgressUpdate>? progress = null)
    {
        return ExportItems(projectRootPath, WorkspaceTransferKind.Characters, selectedCodes, progress);
    }

    public string ExportHandCards(
        string projectRootPath,
        IEnumerable<string>? selectedCodes = null,
        IProgress<ProgressUpdate>? progress = null)
    {
        return ExportItems(projectRootPath, WorkspaceTransferKind.HandCards, selectedCodes, progress);
    }

    public WorkspaceImportResult ImportPackages(
        string projectRootPath,
        WorkspaceTransferKind kind,
        IEnumerable<string> packagePaths,
        WorkspaceImportConflictPolicy conflictPolicy,
        IProgress<ProgressUpdate>? progress = null)
    {
        var packages = packagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (packages.Count == 0)
        {
            throw new InvalidOperationException("没有选择可导入的数据包。");
        }

        var stagedItems = new List<StagedWorkspaceItem>();
        var stagingRoot = BuildStagingRoot();
        Directory.CreateDirectory(stagingRoot);
        try
        {
            for (var index = 0; index < packages.Count; index++)
            {
                var packagePath = packages[index];
                if (!File.Exists(packagePath))
                {
                    throw new FileNotFoundException("导入包不存在。", packagePath);
                }

                progress?.Report(new ProgressUpdate(
                    "正在校验导入包...",
                    5 + (index * 35d / packages.Count),
                    Path.GetFileName(packagePath),
                    true));
                stagedItems.AddRange(StageZipPackage(packagePath, kind, stagingRoot));
            }

            return CommitStagedItems(projectRootPath, kind, stagedItems, conflictPolicy, progress);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public WorkspaceImportResult ImportFromProject(
        string projectRootPath,
        WorkspaceTransferKind kind,
        string sourceProjectRootPath,
        WorkspaceImportConflictPolicy conflictPolicy,
        IProgress<ProgressUpdate>? progress = null)
    {
        var sourceRoot = Path.GetFullPath(sourceProjectRootPath);
        var sourceItemsRoot = Path.Combine(sourceRoot, GetFolderName(kind));
        if (!Directory.Exists(sourceItemsRoot))
        {
            throw new DirectoryNotFoundException($"所选目录中没有找到 {GetFolderName(kind)}：{sourceRoot}");
        }

        var sourceItems = Directory.EnumerateDirectories(sourceItemsRoot).ToList();
        if (sourceItems.Count == 0)
        {
            throw new InvalidOperationException($"所选项目中没有可导入的{GetDisplayName(kind)}。");
        }

        var stagingRoot = BuildStagingRoot();
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var stagedItems = new List<StagedWorkspaceItem>();
            for (var index = 0; index < sourceItems.Count; index++)
            {
                var sourceItem = sourceItems[index];
                progress?.Report(new ProgressUpdate(
                    $"正在校验{GetDisplayName(kind)}...",
                    5 + (index * 35d / sourceItems.Count),
                    Path.GetFileName(sourceItem)));
                stagedItems.Add(StageDirectoryItem(sourceItem, kind, stagingRoot));
            }

            return CommitStagedItems(projectRootPath, kind, stagedItems, conflictPolicy, progress);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public string GetExportDirectory(string projectRootPath, WorkspaceTransferKind kind)
    {
        return Path.Combine(projectRootPath, "Exports", GetFolderName(kind));
    }

    private string ExportItems(
        string projectRootPath,
        WorkspaceTransferKind kind,
        IEnumerable<string>? selectedCodes,
        IProgress<ProgressUpdate>? progress)
    {
        progress?.Report(new ProgressUpdate($"正在扫描{GetDisplayName(kind)}...", 5, projectRootPath, true));
        var sourceRoot = Path.Combine(projectRootPath, GetFolderName(kind));
        Directory.CreateDirectory(sourceRoot);

        var selected = selectedCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => SanitizeCode(kind, code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itemDirectories = Directory.EnumerateDirectories(sourceRoot)
            .Where(path => selected is null || selected.Contains(Path.GetFileName(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (itemDirectories.Count == 0)
        {
            throw new InvalidOperationException($"没有可导出的{GetDisplayName(kind)}。");
        }

        for (var index = 0; index < itemDirectories.Count; index++)
        {
            var itemDirectory = itemDirectories[index];
            progress?.Report(new ProgressUpdate(
                $"正在校验{GetDisplayName(kind)}...",
                10 + ((index + 1) * 20d / itemDirectories.Count),
                Path.GetFileName(itemDirectory)));
            ValidateItemDirectory(itemDirectory, kind);
        }

        var exportRoot = GetExportDirectory(projectRootPath, kind);
        Directory.CreateDirectory(exportRoot);
        var label = itemDirectories.Count == 1
            ? Path.GetFileName(itemDirectories[0])
            : $"All-{itemDirectories.Count}";
        var exportPath = Path.Combine(
            exportRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{GetPackagePrefix(kind)}-{label}.zip");

        using var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create);
        var manifest = new WorkspacePackageManifest
        {
            SchemaVersion = PackageSchemaVersion,
            PackageKind = GetPackageKind(kind),
            CreatedAt = DateTimeOffset.Now,
            Items = itemDirectories.Select(Path.GetFileName).ToList()!
        };
        var manifestEntry = archive.CreateEntry(PackageManifestFileName, CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(JsonSerializer.Serialize(manifest, AppJsonSerializerContext.Default.WorkspacePackageManifest));
        }

        for (var index = 0; index < itemDirectories.Count; index++)
        {
            var itemDirectory = itemDirectories[index];
            var code = Path.GetFileName(itemDirectory);
            progress?.Report(new ProgressUpdate(
                $"正在写入{GetDisplayName(kind)}包...",
                35 + ((index + 1) * 60d / itemDirectories.Count),
                code));
            foreach (var filePath in Directory.EnumerateFiles(itemDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(itemDirectory, filePath).Replace('\\', '/');
                archive.CreateEntryFromFile(
                    filePath,
                    $"{GetFolderName(kind)}/{code}/{relativePath}",
                    CompressionLevel.Optimal);
            }
        }

        progress?.Report(new ProgressUpdate($"{GetDisplayName(kind)}包导出完成。", 100, exportPath));
        return exportPath;
    }

    private static List<StagedWorkspaceItem> StageZipPackage(
        string packagePath,
        WorkspaceTransferKind kind,
        string stagingRoot)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        ValidatePackageManifestIfPresent(archive, kind);

        var folderName = GetFolderName(kind);
        var metaFileName = GetMetaFileName(kind);
        var metaEntries = archive.Entries
            .Where(entry => string.Equals(Path.GetFileName(entry.FullName), metaFileName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new { Entry = entry, Prefix = TryGetItemPrefix(entry.FullName, folderName) })
            .Where(value => value.Prefix is not null)
            .ToList();
        if (metaEntries.Count == 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(packagePath)} 中没有找到可导入的{GetDisplayName(kind)}数据。");
        }

        var stagedItems = new List<StagedWorkspaceItem>();
        foreach (var metaEntry in metaEntries)
        {
            var prefix = metaEntry.Prefix!;
            var codeSegment = prefix.TrimEnd('/').Split('/').Last();
            var code = SanitizeCode(kind, codeSegment);
            if (string.IsNullOrWhiteSpace(code) || !string.Equals(code, codeSegment, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"导入包包含无效英文代号：{codeSegment}");
            }

            var stagedItemPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}-{code}");
            Directory.CreateDirectory(stagedItemPath);
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var relativePath = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = GetSafeDestinationPath(stagedItemPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            ValidateItemDirectory(stagedItemPath, kind, code);
            stagedItems.Add(new StagedWorkspaceItem(code, stagedItemPath));
        }

        return stagedItems;
    }

    private static StagedWorkspaceItem StageDirectoryItem(
        string sourceItemPath,
        WorkspaceTransferKind kind,
        string stagingRoot)
    {
        var codeSegment = Path.GetFileName(sourceItemPath);
        var code = SanitizeCode(kind, codeSegment);
        if (string.IsNullOrWhiteSpace(code) || !string.Equals(code, codeSegment, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"项目中包含无效英文代号：{codeSegment}");
        }

        ValidateItemDirectory(sourceItemPath, kind, code);
        var stagedItemPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}-{code}");
        CopyDirectory(sourceItemPath, stagedItemPath);
        return new StagedWorkspaceItem(code, stagedItemPath);
    }

    private static WorkspaceImportResult CommitStagedItems(
        string projectRootPath,
        WorkspaceTransferKind kind,
        IReadOnlyList<StagedWorkspaceItem> stagedItems,
        WorkspaceImportConflictPolicy conflictPolicy,
        IProgress<ProgressUpdate>? progress)
    {
        if (stagedItems.Count == 0)
        {
            throw new InvalidOperationException("导入包中没有有效数据。");
        }

        var destinationRoot = Path.Combine(projectRootPath, GetFolderName(kind));
        Directory.CreateDirectory(destinationRoot);
        var imported = 0;
        var replaced = 0;
        var skipped = 0;
        var importedCodes = new List<string>();

        for (var index = 0; index < stagedItems.Count; index++)
        {
            var item = stagedItems[index];
            var destinationPath = Path.Combine(destinationRoot, item.Code);
            var exists = Directory.Exists(destinationPath);
            progress?.Report(new ProgressUpdate(
                $"正在导入{GetDisplayName(kind)}...",
                45 + ((index + 1) * 50d / stagedItems.Count),
                item.Code));

            if (exists && conflictPolicy == WorkspaceImportConflictPolicy.Skip)
            {
                skipped++;
                continue;
            }

            if (exists)
            {
                BackupExistingItem(projectRootPath, kind, item.Code, destinationPath);
                Directory.Delete(destinationPath, recursive: true);
                replaced++;
            }
            else
            {
                imported++;
            }

            CopyDirectory(item.Path, destinationPath);
            importedCodes.Add(item.Code);
        }

        progress?.Report(new ProgressUpdate(
            $"{GetDisplayName(kind)}导入完成。",
            100,
            $"新增 {imported}，覆盖 {replaced}，跳过 {skipped}"));
        return new WorkspaceImportResult(imported, replaced, skipped, importedCodes);
    }

    private static void ValidatePackageManifestIfPresent(ZipArchive archive, WorkspaceTransferKind kind)
    {
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.Trim('/'), PackageManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            return;
        }

        using var reader = new StreamReader(manifestEntry.Open());
        var manifest = JsonSerializer.Deserialize(
            reader.ReadToEnd(),
            AppJsonSerializerContext.Default.WorkspacePackageManifest);
        if (manifest is null || manifest.SchemaVersion != PackageSchemaVersion)
        {
            throw new InvalidDataException("导入包版本不受支持。");
        }
        if (!string.Equals(manifest.PackageKind, GetPackageKind(kind), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"当前页面只能导入{GetDisplayName(kind)}包，所选包类型为：{manifest.PackageKind}");
        }
    }

    private static void ValidateItemDirectory(
        string itemPath,
        WorkspaceTransferKind kind,
        string? expectedCode = null)
    {
        var metaPath = Path.Combine(itemPath, GetMetaFileName(kind));
        if (!File.Exists(metaPath))
        {
            throw new InvalidDataException($"缺少元数据文件：{metaPath}");
        }
        var cardFacePath = Path.Combine(itemPath, GetCardFaceFileName(kind));
        if (!File.Exists(cardFacePath))
        {
            throw new InvalidDataException($"缺少卡面文件：{cardFacePath}");
        }

        var json = File.ReadAllText(metaPath);
        var metaCode = kind == WorkspaceTransferKind.Characters
            ? JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.CharacterMeta)?.Code
            : JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.HandCardMeta)?.Code;
        var sanitizedCode = SanitizeCode(kind, metaCode ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedCode))
        {
            throw new InvalidDataException($"元数据中的英文代号无效：{metaPath}");
        }
        if (!string.IsNullOrWhiteSpace(expectedCode) &&
            !string.Equals(sanitizedCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"文件夹代号 {expectedCode} 与元数据代号 {sanitizedCode} 不一致。");
        }
    }

    private static string? TryGetItemPrefix(string entryName, string folderName)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        var marker = $"{folderName}/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var afterMarker = normalized[(markerIndex + marker.Length)..];
        var slashIndex = afterMarker.IndexOf('/');
        if (slashIndex <= 0)
        {
            return null;
        }

        return normalized[..(markerIndex + marker.Length + slashIndex + 1)];
    }

    private static string GetSafeDestinationPath(string rootPath, string relativePath)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"导入包包含非法路径：{relativePath}");
        }
        return fullPath;
    }

    private static void BackupExistingItem(
        string projectRootPath,
        WorkspaceTransferKind kind,
        string code,
        string sourcePath)
    {
        var backupRoot = Path.Combine(projectRootPath, "Backups", GetFolderName(kind));
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{code}-PreImport");
        var suffix = 1;
        while (Directory.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{code}-PreImport-{suffix++}");
        }
        CopyDirectory(sourcePath, backupPath);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var targetPath = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static string BuildStagingRoot()
    {
        return Path.Combine(Path.GetTempPath(), "FantasyTools", "Imports", Guid.NewGuid().ToString("N"));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // The imported data is already committed; stale temp data can be cleaned later.
        }
    }

    private static string GetFolderName(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => CharacterWorkspaceService.CharactersFolderName,
        WorkspaceTransferKind.HandCards => HandCardWorkspaceService.HandCardsFolderName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetMetaFileName(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => CharacterWorkspaceService.CharacterMetaFileName,
        WorkspaceTransferKind.HandCards => HandCardWorkspaceService.HandCardMetaFileName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetCardFaceFileName(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => CharacterWorkspaceService.CardFaceFileName,
        WorkspaceTransferKind.HandCards => HandCardWorkspaceService.CardFaceFileName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetPackageKind(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => "characters",
        WorkspaceTransferKind.HandCards => "handcards",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetPackagePrefix(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => "CharacterPackage",
        WorkspaceTransferKind.HandCards => "HandCardPackage",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetDisplayName(WorkspaceTransferKind kind) => kind switch
    {
        WorkspaceTransferKind.Characters => "角色",
        WorkspaceTransferKind.HandCards => "手牌",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string SanitizeCode(WorkspaceTransferKind kind, string code) => kind switch
    {
        WorkspaceTransferKind.Characters => CharacterWorkspaceService.SanitizeCharacterCode(code),
        WorkspaceTransferKind.HandCards => HandCardWorkspaceService.SanitizeHandCardCode(code),
        _ => string.Empty
    };

    private sealed record StagedWorkspaceItem(string Code, string Path);
}
