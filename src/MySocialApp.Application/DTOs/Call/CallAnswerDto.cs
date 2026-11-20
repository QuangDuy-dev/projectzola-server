using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Call;

public record CallAnswerDto(
    Guid CallSessionId,
    Guid CalleeId,
    string SdpAnswer
);

public class SendRtcAnswerRequest
{
    [Required]
    public Guid CallSessionId { get; set; }

    [Required]
    public string SdpAnswer { get; set; } = string.Empty;
}
