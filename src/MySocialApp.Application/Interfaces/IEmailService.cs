namespace MySocialApp.Application.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// Sends an email via SMTP if configured; otherwise logs the content for development and returns false.
    /// </summary>
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextForLog = null);
}
