using System.Text.Json.Serialization;
using System.Collections.Generic;
using FantasyTools.Models;

namespace FantasyTools.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppSettingsBootstrap))]
[JsonSerializable(typeof(CharacterMeta))]
[JsonSerializable(typeof(CharacterSkillMeta))]
[JsonSerializable(typeof(HandCardMeta))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<CharacterSkillMeta>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
