using MySocialApp.Application.DTOs.Call;
using MySocialApp.Application.DTOs.Chat;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Interfaces;

public interface ICallService
{
    Task<CallSessionDto> StartCallAsync(Guid callerId, StartCallRequest request);
    Task<CallSessionDto> AcceptCallAsync(Guid callSessionId, Guid calleeId);
    Task<(CallEndedDto endedDto, MessageDto? messageDto)> RejectCallAsync(Guid callSessionId, Guid userId);
    Task<(CallEndedDto endedDto, MessageDto? messageDto)> EndCallAsync(Guid callSessionId, Guid userId, string reason = "UserHangup");
    Task<(CallEndedDto endedDto, MessageDto? messageDto)?> HandleTimeoutAsync(Guid callSessionId);
    Task<CallSession?> GetActiveCallForUserAsync(Guid userId);
    Task<CallSession?> GetCallSessionAsync(Guid callSessionId);
}
