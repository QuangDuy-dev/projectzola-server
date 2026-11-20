using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.DTOs.Call;

public record CallEndedDto(
    Guid CallSessionId,
    Guid ConversationId,
    CallStatus Status,
    string Reason,
    int DurationSeconds,
    Guid EndedByUserId
);

public record CallEventDto(
    Guid CallSessionId,
    Guid ConversationId,
    Guid UserId,
    CallStatus Status,
    string? Message = null
);
