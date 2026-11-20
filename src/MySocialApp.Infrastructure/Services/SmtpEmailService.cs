using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;

namespace MySocialApp.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<SmtpOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextForLog = null)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning(
                "[DEV EMAIL FALLBACK] SMTP not configured. To: {ToEmail} | Subject: {Subject} | Content: {Content}",
                toEmail,
                subject,
                plainTextForLog ?? htmlBody);
            return false;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.SenderEmail, _options.SenderName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(toEmail));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.Password)
        };

        await client.SendMailAsync(message);
        _logger.LogInformation("Email sent via SMTP to {ToEmail} with subject '{Subject}'.", toEmail, subject);
        return true;
    }
}
