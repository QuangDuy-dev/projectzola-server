using System.ComponentModel.DataAnnotations;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.DTOs.Call;

public class StartCallRequest
{
    [Required]
    public Guid ConversationId { get; set; }

    [Required]
    public Guid CalleeId { get; set; }

    public CallType Type { get; set; } = CallType.Voice;
}
