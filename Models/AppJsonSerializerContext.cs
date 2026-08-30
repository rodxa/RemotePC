using System.Text.Json.Serialization;

namespace RemotePC.Models;

[JsonSerializable(typeof(RemoteHostHealth))]
[JsonSerializable(typeof(RemoteActionRequest))]
[JsonSerializable(typeof(ActionExecutionResult))]
[JsonSerializable(typeof(RemotePasswordRequest))]
[JsonSerializable(typeof(RemotePasswordResponse))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
