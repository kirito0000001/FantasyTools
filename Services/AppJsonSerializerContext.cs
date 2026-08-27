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
[JsonSerializable(typeof(GiteeRelease))]
[JsonSerializable(typeof(GiteeAttachFile))]
[JsonSerializable(typeof(List<GiteeRelease>))]
[JsonSerializable(typeof(List<GiteeAttachFile>))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateAssetManifest))]
[JsonSerializable(typeof(List<UpdateAssetManifest>))]
[JsonSerializable(typeof(UpdatePackageManifest))]
[JsonSerializable(typeof(CharacterMeta))]
[JsonSerializable(typeof(CharacterSkillMeta))]
[JsonSerializable(typeof(HandCardMeta))]
[JsonSerializable(typeof(BasicDeckSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<CharacterSkillMeta>))]
[JsonSerializable(typeof(WorkspacePackageManifest))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
