using System.Text.Json;

namespace MatchApi;

/// <summary>
/// Shared JsonSerializerOptions used across all opcode handlers and WebSocket sessions.
/// CamelCase policy covers the OpcodeRequest/Response envelope fields.
/// [JsonPropertyName] attributes on individual DTO properties take precedence (snake_case fields).
/// </summary>
internal static class ApiJsonOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
