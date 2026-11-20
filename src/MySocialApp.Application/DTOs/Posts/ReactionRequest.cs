using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Posts;

public class ReactionRequest
{
    [Required]
    public string Type { get; set; } = string.Empty; // "Like" or "Dislike"
}
