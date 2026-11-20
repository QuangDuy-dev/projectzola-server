using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Call;

public record CallIceCandidateDto(
    Guid CallSessionId,
    Guid SenderId,
    string Candidate,
    string? SdpMid,
    int? SdpMLineIndex
);

public class SendRtcIceCandidateRequest
{
    [Required]
    public Guid CallSessionId { get; set; }

    [Required]
    public string Candidate { get; set; } = string.Empty;

    public string? SdpMid { get; set; }

    public int? SdpMLineIndex { get; set; }
}
