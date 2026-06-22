using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class CharacterWorkspaceService
{
    public const string CharactersFolderName = "Characters";
    public const string CharacterMetaFileName = "character.meta.json";
    public const string CardFaceFileName = "CardFace.png";
    public const string BackgroundImageFileName = "BackgroundImage.png";
    private static readonly string[] LegacyCardFaceFileNames = ["CardFace.jpeg", "CardFace.jpg"];
    public const int CharacterCardFaceWidth = 732;
    public const int CharacterCardFaceHeight = 1028;
    public const int HandCardFaceWidth = 357;
    public const int HandCardFaceHeight = 300;
    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public string GetCharactersFolderPath(string projectRootPath)
    {
        return Path.Combine(projectRootPath, CharactersFolderName);
    }

    public string BuildCharacterFolderPath(string projectRootPath, string code)
    {
        return Path.Combine(GetCharactersFolderPath(projectRootPath), SanitizeCharacterCode(code));
    }

    public string BuildCharacterFolderPreview(string projectRootPath, string code)
    {
        var sanitizedCode = SanitizeCharacterCode(code);
        return Path.Combine(GetCharactersFolderPath(projectRootPath), string.IsNullOrWhiteSpace(sanitizedCode) ? "<角色英文代号>" : sanitizedCode);
    }

    public CharacterInfo CreateCharacter(string projectRootPath, CharacterCreateInput input)
    {
        var code = SanitizeCharacterCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("角色英文代号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.CardFaceSourcePath) || !File.Exists(input.CardFaceSourcePath))
        {
            throw new FileNotFoundException("卡面图片不存在，请重新选择。", input.CardFaceSourcePath);
        }

        var charactersFolderPath = GetCharactersFolderPath(projectRootPath);
        Directory.CreateDirectory(charactersFolderPath);

        var characterPath = Path.Combine(charactersFolderPath, code);
        if (Directory.Exists(characterPath))
        {
            throw new IOException($"角色文件夹已存在：{characterPath}");
        }

        Directory.CreateDirectory(characterPath);
        var cardFacePath = Path.Combine(characterPath, CardFaceFileName);
        SaveCroppedImage(input.CardFaceSourcePath, cardFacePath, CharacterCardFaceWidth, CharacterCardFaceHeight, input.CardFaceCrop);

        var meta = new CharacterMeta
        {
            Code = code,
            Name = code,
            CardFaceFileName = CardFaceFileName,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
        SaveMeta(characterPath, meta);

        return ReadCharacter(characterPath);
    }

    public List<CharacterInfo> GetCharacters(string projectRootPath)
    {
        var charactersFolderPath = GetCharactersFolderPath(projectRootPath);
        Directory.CreateDirectory(charactersFolderPath);

        return Directory.EnumerateDirectories(charactersFolderPath)
            .Select(ReadCharacter)
            .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private CharacterInfo ReadCharacter(string characterPath)
    {
        var fallbackCode = Path.GetFileName(characterPath);
        var metaPath = Path.Combine(characterPath, CharacterMetaFileName);
        var meta = NormalizeMeta(ReadMeta(metaPath), fallbackCode);
        var code = string.IsNullOrWhiteSpace(meta.Code)
            ? fallbackCode
            : meta.Code;
        var cardFaceFileName = string.IsNullOrWhiteSpace(meta.CardFaceFileName)
            ? CardFaceFileName
            : meta.CardFaceFileName;
        var metaChanged = false;
        if (!string.Equals(cardFaceFileName, CardFaceFileName, StringComparison.OrdinalIgnoreCase))
        {
            cardFaceFileName = CardFaceFileName;
            meta.CardFaceFileName = CardFaceFileName;
            metaChanged = true;
        }

        if (DeleteLegacyCardFaceFiles(characterPath))
        {
            metaChanged = true;
        }

        if (metaChanged)
        {
            SaveMeta(characterPath, meta);
        }
        var cardFacePath = Path.Combine(characterPath, cardFaceFileName);
        var backgroundImagePath = string.IsNullOrWhiteSpace(meta.BackgroundImageFileName)
            ? string.Empty
            : Path.Combine(characterPath, meta.BackgroundImageFileName);
        return new CharacterInfo(
            code,
            meta.Name,
            characterPath,
            cardFacePath,
            backgroundImagePath,
            meta,
            !File.Exists(cardFacePath));
    }

    public CharacterInfo GetCharacter(string projectRootPath, string code)
    {
        var characterPath = BuildCharacterFolderPath(projectRootPath, code);
        if (!Directory.Exists(characterPath))
        {
            throw new DirectoryNotFoundException($"角色文件夹不存在：{characterPath}");
        }

        return ReadCharacter(characterPath);
    }

    public CharacterInfo SaveCharacter(string projectRootPath, CharacterMeta meta)
    {
        var code = SanitizeCharacterCode(meta.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("角色英文代号不能为空。");
        }

        var characterPath = BuildCharacterFolderPath(projectRootPath, code);
        if (!Directory.Exists(characterPath))
        {
            throw new DirectoryNotFoundException($"角色文件夹不存在：{characterPath}");
        }

        meta.Code = code;
        meta.Name = string.IsNullOrWhiteSpace(meta.Name) ? code : meta.Name.Trim();
        meta.Health = Math.Max(1, meta.Health);
        meta.Tags = NormalizeEntries(meta.Tags);
        meta.SkillGroups = NormalizeEntries(meta.SkillGroups);
        meta.Skills = NormalizeSkills(meta.Skills, code);
        meta.CarryCards = NormalizeEntries(meta.CarryCards);
        meta.UpdatedAt = DateTimeOffset.Now;
        SaveMeta(characterPath, meta);
        return ReadCharacter(characterPath);
    }

    public CharacterInfo ImportBackgroundImage(string projectRootPath, string code, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("背景图不存在，请重新选择。", sourcePath);
        }

        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("背景图只支持 png、jpg、jpeg、webp 来源，导入后会统一保存为 png。");
        }

        var character = GetCharacter(projectRootPath, code);
        var backgroundPath = Path.Combine(character.Path, BackgroundImageFileName);
        SaveImageToPng(sourcePath, backgroundPath);
        character.Meta.BackgroundImageFileName = BackgroundImageFileName;
        return SaveCharacter(projectRootPath, character.Meta);
    }

    public CharacterInfo ImportCardFaceImage(string projectRootPath, string code, string sourcePath, Rectangle crop)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("卡面图片不存在，请重新选择。", sourcePath);
        }

        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("卡面图片只支持 png、jpg、jpeg、webp。");
        }

        var character = GetCharacter(projectRootPath, code);
        var cardFacePath = Path.Combine(character.Path, CardFaceFileName);
        SaveCropToPng(sourcePath, cardFacePath, crop, CharacterCardFaceWidth, CharacterCardFaceHeight);
        character.Meta.CardFaceFileName = CardFaceFileName;
        return SaveCharacter(projectRootPath, character.Meta);
    }

    public CharacterInfo RenameCharacterCode(string projectRootPath, string currentCode, string newCode)
    {
        var oldCode = SanitizeCharacterCode(currentCode);
        var sanitizedNewCode = SanitizeCharacterCode(newCode);
        if (string.IsNullOrWhiteSpace(sanitizedNewCode))
        {
            throw new InvalidOperationException("角色英文代号不能为空。");
        }

        if (string.Equals(oldCode, sanitizedNewCode, StringComparison.Ordinal))
        {
            return GetCharacter(projectRootPath, oldCode);
        }

        var oldPath = BuildCharacterFolderPath(projectRootPath, oldCode);
        var newPath = BuildCharacterFolderPath(projectRootPath, sanitizedNewCode);
        if (!Directory.Exists(oldPath))
        {
            throw new DirectoryNotFoundException($"角色文件夹不存在：{oldPath}");
        }

        if (Directory.Exists(newPath))
        {
            throw new IOException($"角色文件夹已存在：{newPath}");
        }

        Directory.Move(oldPath, newPath);
        var character = ReadCharacter(newPath);
        character.Meta.Code = sanitizedNewCode;
        character.Meta.Skills = NormalizeSkills(character.Meta.Skills, sanitizedNewCode);
        SaveMeta(newPath, character.Meta);
        return ReadCharacter(newPath);
    }

    public CharacterInfo DuplicateCharacter(string projectRootPath, string code)
    {
        var source = GetCharacter(projectRootPath, code);
        var newCode = BuildUniqueCharacterCode(projectRootPath, $"{source.Code}-Copy");
        var targetPath = BuildCharacterFolderPath(projectRootPath, newCode);
        CopyDirectory(source.Path, targetPath);

        var duplicated = ReadCharacter(targetPath);
        duplicated.Meta.Code = newCode;
        duplicated.Meta.Name = string.IsNullOrWhiteSpace(source.Meta.Name)
            ? newCode
            : $"{source.Meta.Name} 副本";
        duplicated.Meta.CreatedAt = DateTimeOffset.Now;
        duplicated.Meta.UpdatedAt = DateTimeOffset.Now;
        duplicated.Meta.Skills = NormalizeSkills(duplicated.Meta.Skills, newCode);
        SaveMeta(targetPath, duplicated.Meta);
        return ReadCharacter(targetPath);
    }

    public string BackupCharacter(string projectRootPath, string code, string reason)
    {
        var character = GetCharacter(projectRootPath, code);
        var backupRoot = Path.Combine(projectRootPath, "Backups", "Characters");
        Directory.CreateDirectory(backupRoot);

        var safeReason = SanitizeCharacterCode(reason);
        if (string.IsNullOrWhiteSpace(safeReason))
        {
            safeReason = "Manual";
        }

        var backupPath = Path.Combine(
            backupRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{character.Code}-{safeReason}");
        CopyDirectory(character.Path, backupPath);
        return backupPath;
    }

    public string ExportCharacter(string projectRootPath, string code)
    {
        var character = GetCharacter(projectRootPath, code);
        var exportRoot = Path.Combine(projectRootPath, "Exports", "Characters");
        Directory.CreateDirectory(exportRoot);

        var exportPath = Path.Combine(
            exportRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{character.Code}");
        CopyDirectory(character.Path, exportPath);
        return exportPath;
    }

    public string DeleteCharacterWithBackup(string projectRootPath, string code)
    {
        var character = GetCharacter(projectRootPath, code);
        var backupPath = BackupCharacter(projectRootPath, character.Code, "PreDelete");
        Directory.Delete(character.Path, recursive: true);
        return backupPath;
    }

    private static CharacterMeta? ReadMeta(string metaPath)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.CharacterMeta);
        }
        catch
        {
            return null;
        }
    }

    private static CharacterMeta NormalizeMeta(CharacterMeta? meta, string fallbackCode)
    {
        meta ??= new CharacterMeta();
        meta.Code = string.IsNullOrWhiteSpace(meta.Code) ? fallbackCode : SanitizeCharacterCode(meta.Code);
        meta.Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Code : meta.Name.Trim();
        meta.CardFaceFileName = CardFaceFileName;
        meta.Health = Math.Max(1, meta.Health);
        meta.Tags = NormalizeEntries(meta.Tags);
        meta.SkillGroups = NormalizeEntries(meta.SkillGroups);
        meta.Skills = NormalizeSkills(meta.Skills, meta.Code);
        meta.CarryCards = NormalizeEntries(meta.CarryCards);
        return meta;
    }

    private static List<string> NormalizeEntries(IEnumerable<string>? entries)
    {
        return entries?
            .Select(entry => entry.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    private static List<CharacterSkillMeta> NormalizeSkills(IEnumerable<CharacterSkillMeta>? skills, string characterCode)
    {
        var normalizedCode = SanitizeCharacterCode(characterCode);
        return skills?
            .Where(skill =>
                !string.IsNullOrWhiteSpace(skill.Name) ||
                !string.IsNullOrWhiteSpace(skill.Description) ||
                !string.IsNullOrWhiteSpace(skill.Function) ||
                !string.IsNullOrWhiteSpace(skill.Type))
            .Select((skill, index) => new CharacterSkillMeta
            {
                Name = skill.Name.Trim(),
                Code = BuildSkillCode(normalizedCode, index),
                Description = skill.Description.Trim(),
                Function = skill.Function.Trim(),
                Type = skill.Type.Trim()
            })
            .ToList() ?? [];
    }

    public static string BuildSkillCode(string characterCode, int zeroBasedIndex)
    {
        var normalizedCode = SanitizeCharacterCode(characterCode);
        return $"{normalizedCode}-Skill{zeroBasedIndex + 1}";
    }

    private static void SaveMeta(string characterPath, CharacterMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, AppJsonSerializerContext.Default.CharacterMeta);
        File.WriteAllText(Path.Combine(characterPath, CharacterMetaFileName), json);
    }

    private string BuildUniqueCharacterCode(string projectRootPath, string baseCode)
    {
        var sanitizedBase = SanitizeCharacterCode(baseCode);
        if (string.IsNullOrWhiteSpace(sanitizedBase))
        {
            sanitizedBase = "Character";
        }

        var candidate = sanitizedBase;
        var index = 2;
        while (Directory.Exists(BuildCharacterFolderPath(projectRootPath, candidate)))
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

    public static string SanitizeCharacterCode(string code)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(code.Trim().Where(ch => !invalidChars.Contains(ch) && !char.IsWhiteSpace(ch)).ToArray());
    }

    public static (int Width, int Height) GetImageSize(string sourcePath)
    {
        using var image = Image.FromFile(sourcePath);
        return (image.Width, image.Height);
    }

    public static bool IsSupportedImage(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    public static void SaveCropToPng(string sourcePath, string targetPath, Rectangle crop, int targetWidth, int targetHeight)
    {
        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("图片只支持 png、jpg、jpeg、webp。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.tmp.png");

        try
        {
            using (var source = Image.FromFile(sourcePath))
            {
                crop = Rectangle.Intersect(crop, new Rectangle(0, 0, source.Width, source.Height));
                if (crop.Width <= 0 || crop.Height <= 0)
                {
                    throw new InvalidOperationException("裁剪范围无效。");
                }

                using var output = new Bitmap(targetWidth, targetHeight);
                output.SetResolution(96, 96);
                using var graphics = Graphics.FromImage(output);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight), crop, GraphicsUnit.Pixel);
                output.Save(tempPath, ImageFormat.Png);
            }

            File.Copy(tempPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void SaveCroppedImage(string sourcePath, string targetPath, int targetWidth, int targetHeight, Rectangle? cropOverride)
    {
        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("图片只支持 png、jpg、jpeg、webp。");
        }

        int sourceWidth;
        int sourceHeight;
        using (var source = Image.FromFile(sourcePath))
        {
            sourceWidth = source.Width;
            sourceHeight = source.Height;
        }

        var crop = cropOverride.HasValue
            ? Rectangle.Intersect(cropOverride.Value, new Rectangle(0, 0, sourceWidth, sourceHeight))
            : BuildCenterCrop(sourceWidth, sourceHeight, targetWidth, targetHeight);
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            throw new InvalidOperationException("裁剪范围无效。");
        }

        SaveCropToPng(sourcePath, targetPath, crop, targetWidth, targetHeight);
    }

    private static void SaveImageToPng(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.tmp.png");

        try
        {
            using (var source = Image.FromFile(sourcePath))
            {
                source.Save(tempPath, ImageFormat.Png);
            }

            File.Copy(tempPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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

    private static Rectangle BuildCenterCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var targetRatio = targetWidth / (double)targetHeight;
        var sourceRatio = sourceWidth / (double)sourceHeight;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (int)Math.Round(sourceHeight * targetRatio);
            return new Rectangle((sourceWidth - cropWidth) / 2, 0, cropWidth, sourceHeight);
        }

        var cropHeight = (int)Math.Round(sourceWidth / targetRatio);
        return new Rectangle(0, (sourceHeight - cropHeight) / 2, sourceWidth, cropHeight);
    }
}
