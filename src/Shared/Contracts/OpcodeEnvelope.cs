namespace Shared.Contracts;

// ── Generic envelope ──────────────────────────────────────────────────────────

public record OpcodeRequest(int Opcode, string RequestId, object? Payload);

public record OpcodeResponse(
    int Opcode,
    string RequestId,
    string Status,
    object? Data,
    OpcodeError? Error
);

public record OpcodeError(string Code, string Message);
