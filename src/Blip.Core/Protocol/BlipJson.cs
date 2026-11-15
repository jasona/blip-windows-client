using System.Text.Json;
using System.Text.Json.Serialization;
using Blip.Core.Cache;
using Blip.Core.Transport;

namespace Blip.Core.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(List<ChatInfo>))]
[JsonSerializable(typeof(List<GroupInfo>))]
[JsonSerializable(typeof(List<Message>))]
[JsonSerializable(typeof(ReadStateSnapshot))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(Snapshot))]
public partial class BlipJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ServeRequest))]
[JsonSerializable(typeof(ServeRequestWithStdin))]
[JsonSerializable(typeof(ServeFrame))]
[JsonSerializable(typeof(ReactEmoji))]
internal partial class ServeJson : JsonSerializerContext;

internal sealed record ReactEmoji(string emoji, string? name);
