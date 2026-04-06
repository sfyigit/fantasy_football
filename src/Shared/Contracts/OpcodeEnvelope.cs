using System.Text.Json;

namespace Shared.Contracts;

// ── Generic envelope ──────────────────────────────────────────────────────────

/// <summary>
/// Incoming request envelope. Payload is kept as <see cref="JsonElement"/> so each opcode
/// handler can deserialize it to the correct strongly-typed request record.
/// </summary>
public record OpcodeRequest(int Opcode, string RequestId, JsonElement Payload);

public record OpcodeResponse(
    int Opcode,
    string RequestId,
    string Status,
    object? Data,
    OpcodeError? Error
)
{
    public static OpcodeResponse Ok(int opcode, string requestId, object data) =>
        new(opcode, requestId, "success", data, null);

    public static OpcodeResponse Fail(int opcode, string requestId, string code, string message) =>
        new(opcode, requestId, "error", null, new OpcodeError(code, message));
};

public record OpcodeError(string Code, string Message);
