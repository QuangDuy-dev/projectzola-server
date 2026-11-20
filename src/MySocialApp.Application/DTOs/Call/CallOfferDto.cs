using System.ComponentModel.DataAnnotations;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.DTOs.Call;

public record CallOfferDto(
    Guid CallSessionId,
    Guid ConversationId,
    Guid CallerId,
    string CallerName,
    string? CallerAvatar,
    CallType CallType,
    string SdpOffer
);

public class SendRtcOfferRequest
{
    [Required]
    public Guid CallSessionId { get; set; }

    [Required]
    public string SdpOffer { get; set; } = string.Empty;
}
