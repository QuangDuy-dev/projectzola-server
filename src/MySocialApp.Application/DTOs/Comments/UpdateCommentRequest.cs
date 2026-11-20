using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Comments;

public class UpdateCommentRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}
