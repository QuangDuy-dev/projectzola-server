using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.DTOs.Call;

public record CallSessionDto(
    Guid Id,
    Guid ConversationId,
    Guid CallerId,
    string CallerName,
    string? CallerAvatar,
    Guid CalleeId,
    string CalleeName,
    string? CalleeAvatar,
    CallType Type,
    CallStatus Status,
    DateTime CreatedAt,
    DateTime? ConnectedAt,
    DateTime? EndedAt,
    int DurationSeconds
);
