namespace MySocialApp.Application.DTOs.Auth;

public class LogoutAllRequest
{
    public bool KeepCurrentSession { get; set; } = false;
}
