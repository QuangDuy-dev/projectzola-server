using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Posts;

public class CreatePostRequest
{
    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}
