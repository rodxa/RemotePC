using System.Text.Json.Serialization;

namespace RemotePC.Models;

[JsonSerializable(typeof(RemoteHostHealth))]
[JsonSerializable(typeof(RemoteActionRequest))]
[JsonSerializable(typeof(ActionExecutionResult))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingResponse))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
