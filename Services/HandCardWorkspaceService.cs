using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class HandCardWorkspaceService
{
    public const string HandCardsFolderName = "HandCards";
    public const string HandCardMetaFileName = "handcard.meta.json";
    public const string CardFaceFileName = "CardFace.png";
    private static readonly string[] LegacyCardFaceFileNames = ["CardFace.jpeg", "CardFace.jpg"];
    public const int HandCardFaceWidth = 357;
    public const int HandCardFaceHeight = 300;

    public string GetHandCardsFolderPath(string projectRootPath)
    {
        return Path.Combine(projectRootPath, HandCardsFolderName);
    }

    public string BuildHandCardFolderPath(string projectRootPath, string code)
    {
        return Path.Combine(GetHandCardsFolderPath(projectRootPath), SanitizeHandCardCode(code));
    }

    public string BuildHandCardFolderPreview(string projectRootPath, string code)
    {
        var sanitizedCode = SanitizeHandCardCode(code);
        return Path.Combine(GetHandCardsFolderPath(projectRootPath), string.IsNullOrWhiteSpace(sanitizedCode) ? "<手牌英文代号>" : sanitizedCode);
    }

    public HandCardInfo CreateHandCard(string projectRootPath, HandCardCreateInput input)
    {
        var code = SanitizeHandCardCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("手牌英文代号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.CardFaceSourcePath) || !File.Exists(input.CardFaceSourcePath))
        {
            throw new FileNotFoundException("手牌卡面图片不存在，请重新选择。", input.CardFaceSourcePath);
        }

        var handCardsFolderPath = GetHandCardsFolderPath(projectRootPath);
        Directory.CreateDirectory(handCardsFolderPath);

        var handCardPath = Path.Combine(handCardsFolderPath, code);
        if (Directory.Exists(handCardPath))
        {
            throw new IOException($"手牌文件夹已存在：{handCardPath}");
        }

        Directory.CreateDirectory(handCardPath);
        var cardFacePath = Path.Combine(handCardPath, CardFaceFileName);
        CharacterWorkspaceService.SaveCropToPng(
            input.CardFaceSourcePath,
            cardFacePath,
            input.CardFaceCrop ?? BuildCenterCrop(input.CardFaceSourcePath),
            HandCardFaceWidth,
            HandCardFaceHeight);

        var meta = new HandCardMeta
        {
            Code = code,
            Name = code,
            CardFaceFileName = CardFaceFileName,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
        SaveMeta(handCardPath, meta);
        return ReadHandCard(handCardPath);
    }

    public List<HandCardInfo> GetHandCards(string projectRootPath)
    {
        var handCardsFolderPath = GetHandCardsFolderPath(projectRootPath);
        Directory.CreateDirectory(handCardsFolderPath);

        return Directory.EnumerateDirectories(handCardsFolderPath)
            .Select(ReadHandCard)
            .OrderBy(card => card.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public HandCardInfo GetHandCard(string projectRootPath, string code)
    {
        var handCardPath = BuildHandCardFolderPath(projectRootPath, code);
        if (!Directory.Exists(handCardPath))
        {
            throw new DirectoryNotFoundException($"手牌文件夹不存在：{handCardPath}");
        }

        return ReadHandCard(handCardPath);
    }

    public HandCardInfo SaveHandCard(string projectRootPath, HandCardMeta meta)
    {
        var code = SanitizeHandCardCode(meta.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("手牌英文代号不能为空。");
        }

        var handCardPath = BuildHandCardFolderPath(projectRootPath, code);
        if (!Directory.Exists(handCardPath))
        {
            throw new DirectoryNotFoundException($"手牌文件夹不存在：{handCardPath}");
        }

        meta.Code = code;
        meta.Name = string.IsNullOrWhiteSpace(meta.Name) ? code : meta.Name.Trim();
        meta.CardFaceFileName = string.IsNullOrWhiteSpace(meta.CardFaceFileName) ? CardFaceFileName : meta.CardFaceFileName;
        meta.Description = meta.Description.Trim();
        meta.Suit = NormalizeOption(meta.Suit, "Hearts");
        meta.PokerNumber = Math.Clamp(meta.PokerNumber, 1, 13);
        meta.CardType = NormalizeOption(meta.CardType, "Base");
        meta.FunctionGroups = NormalizeEntries(meta.FunctionGroups);
        meta.EquipType = NormalizeOption(meta.EquipType, "Weapon");
        meta.Expression = meta.Expression.Trim();
        meta.UpdatedAt = DateTimeOffset.Now;
        SaveMeta(handCardPath, meta);
        return ReadHandCard(handCardPath);
    }

    public HandCardInfo ImportCardFaceImage(string projectRootPath, string code, string sourcePath, System.Drawing.Rectangle crop)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("手牌卡面图片不存在，请重新选择。", sourcePath);
        }

        var handCard = GetHandCard(projectRootPath, code);
        var cardFacePath = Path.Combine(handCard.Path, CardFaceFileName);
        CharacterWorkspaceService.SaveCropToPng(sourcePath, cardFacePath, crop, HandCardFaceWidth, HandCardFaceHeight);
        handCard.Meta.CardFaceFileName = CardFaceFileName;
        return SaveHandCard(projectRootPath, handCard.Meta);
    }

    public HandCardInfo RenameHandCardCode(string projectRootPath, string currentCode, string newCode)
    {
        var oldCode = SanitizeHandCardCode(currentCode);
        var sanitizedNewCode = SanitizeHandCardCode(newCode);
        if (string.IsNullOrWhiteSpace(sanitizedNewCode))
        {
            throw new InvalidOperationException("手牌英文代号不能为空。");
        }

        if (string.Equals(oldCode, sanitizedNewCode, StringComparison.Ordinal))
        {
            return GetHandCard(projectRootPath, oldCode);
        }

        var oldPath = BuildHandCardFolderPath(projectRootPath, oldCode);
        var newPath = BuildHandCardFolderPath(projectRootPath, sanitizedNewCode);
        if (!Directory.Exists(oldPath))
        {
            throw new DirectoryNotFoundException($"手牌文件夹不存在：{oldPath}");
        }

        if (Directory.Exists(newPath))
        {
            throw new IOException($"手牌文件夹已存在：{newPath}");
        }

        Directory.Move(oldPath, newPath);
        var handCard = ReadHandCard(newPath);
        handCard.Meta.Code = sanitizedNewCode;
        SaveMeta(newPath, handCard.Meta);
        return ReadHandCard(newPath);
    }

    public HandCardInfo DuplicateHandCard(string projectRootPath, string code)
    {
        var source = GetHandCard(projectRootPath, code);
        var newCode = BuildUniqueHandCardCode(projectRootPath, $"{source.Code}-Copy");
        var targetPath = BuildHandCardFolderPath(projectRootPath, newCode);
        CopyDirectory(source.Path, targetPath);

        var duplicated = ReadHandCard(targetPath);
        duplicated.Meta.Code = newCode;
        duplicated.Meta.Name = string.IsNullOrWhiteSpace(source.Meta.Name)
            ? newCode
            : $"{source.Meta.Name} 副本";
        duplicated.Meta.CreatedAt = DateTimeOffset.Now;
        duplicated.Meta.UpdatedAt = DateTimeOffset.Now;
        SaveMeta(targetPath, duplicated.Meta);
        return ReadHandCard(targetPath);
    }

    public string BackupHandCard(string projectRootPath, string code, string reason)
    {
        var handCard = GetHandCard(projectRootPath, code);
        var backupRoot = Path.Combine(projectRootPath, "Backups", "HandCards");
        Directory.CreateDirectory(backupRoot);

        var safeReason = SanitizeHandCardCode(reason);
        if (string.IsNullOrWhiteSpace(safeReason))
        {
            safeReason = "Manual";
        }

        var backupPath = Path.Combine(
            backupRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{handCard.Code}-{safeReason}");
        CopyDirectory(handCard.Path, backupPath);
        return backupPath;
    }

    public string ExportHandCard(string projectRootPath, string code)
    {
        var handCard = GetHandCard(projectRootPath, code);
        var exportRoot = Path.Combine(projectRootPath, "Exports", "HandCards");
        Directory.CreateDirectory(exportRoot);

        var exportPath = Path.Combine(
            exportRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{handCard.Code}");
        CopyDirectory(handCard.Path, exportPath);
        return exportPath;
    }

    public string DeleteHandCardWithBackup(string projectRootPath, string code)
    {
        var handCard = GetHandCard(projectRootPath, code);
        var backupPath = BackupHandCard(projectRootPath, handCard.Code, "PreDelete");
        Directory.Delete(handCard.Path, recursive: true);
        return backupPath;
    }

    private HandCardInfo ReadHandCard(string handCardPath)
    {
        var fallbackCode = Path.GetFileName(handCardPath);
        var metaPath = Path.Combine(handCardPath, HandCardMetaFileName);
        var meta = NormalizeMeta(ReadMeta(metaPath), fallbackCode);
        var code = string.IsNullOrWhiteSpace(meta.Code) ? fallbackCode : meta.Code;
        var cardFaceFileName = string.IsNullOrWhiteSpace(meta.CardFaceFileName) ? CardFaceFileName : meta.CardFaceFileName;
        var metaChanged = false;
        if (!string.Equals(cardFaceFileName, CardFaceFileName, StringComparison.OrdinalIgnoreCase))
        {
            cardFaceFileName = CardFaceFileName;
            meta.CardFaceFileName = CardFaceFileName;
            metaChanged = true;
        }

        if (DeleteLegacyCardFaceFiles(handCardPath))
        {
            metaChanged = true;
        }

        if (metaChanged)
        {
            SaveMeta(handCardPath, meta);
        }
        var cardFacePath = Path.Combine(handCardPath, cardFaceFileName);
        return new HandCardInfo(
            code,
            meta.Name,
            handCardPath,
            cardFacePath,
            meta,
            !File.Exists(cardFacePath));
    }

    private static HandCardMeta? ReadMeta(string metaPath)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.HandCardMeta);
        }
        catch
        {
            return null;
        }
    }

    private static HandCardMeta NormalizeMeta(HandCardMeta? meta, string fallbackCode)
    {
        meta ??= new HandCardMeta();
        meta.Code = string.IsNullOrWhiteSpace(meta.Code) ? fallbackCode : SanitizeHandCardCode(meta.Code);
        meta.Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Code : meta.Name.Trim();
        meta.CardFaceFileName = CardFaceFileName;
        meta.Description = meta.Description.Trim();
        meta.Suit = NormalizeOption(meta.Suit, "Hearts");
        meta.PokerNumber = Math.Clamp(meta.PokerNumber, 1, 13);
        meta.CardType = NormalizeOption(meta.CardType, "Base");
        meta.FunctionGroups = NormalizeEntries(meta.FunctionGroups);
        meta.EquipType = NormalizeOption(meta.EquipType, "Weapon");
        meta.Expression = meta.Expression.Trim();
        return meta;
    }

    private static void SaveMeta(string handCardPath, HandCardMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, AppJsonSerializerContext.Default.HandCardMeta);
        File.WriteAllText(Path.Combine(handCardPath, HandCardMetaFileName), json);
    }

    private static List<string> NormalizeEntries(IEnumerable<string>? entries)
    {
        return entries?
            .Select(entry => entry.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    private static string NormalizeOption(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : SanitizeHandCardCode(value);
    }

    public static string SanitizeHandCardCode(string code)
    {
        return CharacterWorkspaceService.SanitizeCharacterCode(code);
    }

    private string BuildUniqueHandCardCode(string projectRootPath, string baseCode)
    {
        var sanitizedBase = SanitizeHandCardCode(baseCode);
        if (string.IsNullOrWhiteSpace(sanitizedBase))
        {
            sanitizedBase = "HandCard";
        }

        var candidate = sanitizedBase;
        var index = 2;
        while (Directory.Exists(BuildHandCardFolderPath(projectRootPath, candidate)))
        {
            candidate = $"{sanitizedBase}{index}";
            index++;
        }

        return candidate;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: false);
        }
    }

    private static System.Drawing.Rectangle BuildCenterCrop(string sourcePath)
    {
        var (sourceWidth, sourceHeight) = CharacterWorkspaceService.GetImageSize(sourcePath);
        var targetRatio = HandCardFaceWidth / (double)HandCardFaceHeight;
        var sourceRatio = sourceWidth / (double)sourceHeight;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (int)Math.Round(sourceHeight * targetRatio);
            return new System.Drawing.Rectangle((sourceWidth - cropWidth) / 2, 0, cropWidth, sourceHeight);
        }

        var cropHeight = (int)Math.Round(sourceWidth / targetRatio);
        return new System.Drawing.Rectangle(0, (sourceHeight - cropHeight) / 2, sourceWidth, cropHeight);
    }

    private static bool DeleteLegacyCardFaceFiles(string ownerPath)
    {
        var deleted = false;
        foreach (var legacyFileName in LegacyCardFaceFileNames)
        {
            var legacyPath = Path.Combine(ownerPath, legacyFileName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
                deleted = true;
            }
        }

        return deleted;
    }
}
