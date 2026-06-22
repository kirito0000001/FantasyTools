using System.Text.Json.Serialization;
using System.Collections.Generic;
using FantasyTools.Models;

namespace FantasyTools.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppSettingsBootstrap))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubReleaseAsset))]
[JsonSerializable(typeof(List<GitHubRelease>))]
[JsonSerializable(typeof(List<GitHubReleaseAsset>))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateAssetManifest))]
[JsonSerializable(typeof(List<UpdateAssetManifest>))]
[JsonSerializable(typeof(UpdatePackageManifest))]
[JsonSerializable(typeof(CharacterMeta))]
[JsonSerializable(typeof(CharacterSkillMeta))]
[JsonSerializable(typeof(HandCardMeta))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<CharacterSkillMeta>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
