using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySocialApp.Application.DTOs.Auth;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Services;

public class OtpService : IOtpService
{
    public const string PurposeRegister = "Register";
    public const string PurposeForgotPassword = "ForgotPassword";
    private const int OtpExpiryMinutes = 10;

    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        IApplicationDbContext context,
        IEmailService emailService,
        ILogger<OtpService> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<SendOtpResponse> SendOtpAsync(string email, string purpose)
    {
        var normalizedEmail = NormalizeAndValidateEmail(email);
        var canonicalPurpose = NormalizeAndValidatePurpose(purpose);

        var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
        if (canonicalPurpose == PurposeRegister && emailExists)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        if (canonicalPurpose == PurposeForgotPassword && !emailExists)
        {
            throw new KeyNotFoundException("No account found with this email.");
        }

        // Invalidate previous unused OTPs for the same email + purpose
        var now = DateTime.UtcNow;
        var previousOtps = await _context.EmailOtps
            .Where(o => o.Email == normalizedEmail && o.Purpose == canonicalPurpose && !o.IsUsed)
            .ToListAsync();

        foreach (var previous in previousOtps)
        {
            previous.IsUsed = true;
            previous.UsedAt = now;
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var expiresAt = now.AddMinutes(OtpExpiryMinutes);

        var otp = new EmailOtp
        {
            Email = normalizedEmail,
            Code = code,
            Purpose = canonicalPurpose,
            ExpiresAt = expiresAt,
            IsUsed = false
        };

        _context.EmailOtps.Add(otp);
        await _context.SaveChangesAsync();

        var subject = canonicalPurpose == PurposeRegister
            ? "MySocialApp - Mã xác thực đăng ký tài khoản"
            : "MySocialApp - Mã xác thực đặt lại mật khẩu";

        var actionText = canonicalPurpose == PurposeRegister
            ? "hoàn tất đăng ký tài khoản"
            : "đặt lại mật khẩu";

        var htmlBody = $"""
            <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; padding: 24px; border: 1px solid #e5e7eb; border-radius: 12px;">
                <h2 style="color: #111827; margin-bottom: 12px;">MySocialApp</h2>
                <p style="color: #374151; font-size: 15px;">Mã OTP để {actionText} của bạn là:</p>
                <div style="font-size: 32px; font-weight: bold; letter-spacing: 6px; color: #2563eb; background: #eff6ff; padding: 14px 20px; text-align: center; border-radius: 8px; margin: 16px 0;">
                    {code}
                </div>
                <p style="color: #6b7280; font-size: 13px;">Mã có hiệu lực trong {OtpExpiryMinutes} phút. Không chia sẻ mã này cho bất kỳ ai.</p>
            </div>
            """;

        var sentViaEmail = await _emailService.SendEmailAsync(
            normalizedEmail,
            subject,
            htmlBody,
            $"OTP ({canonicalPurpose}) for {normalizedEmail}: {code} (expires in {OtpExpiryMinutes}m)");

        if (!sentViaEmail)
        {
            _logger.LogInformation("Generated OTP for {Email} ({Purpose}): {Code}", normalizedEmail, canonicalPurpose, code);
        }

        return new SendOtpResponse
        {
            Email = normalizedEmail,
            Purpose = canonicalPurpose,
            ExpiresAt = expiresAt,
            SentViaEmail = sentViaEmail,
            DevOtpHint = sentViaEmail ? null : code
        };
    }

    public async Task<bool> VerifyOtpAsync(string email, string code, string purpose)
    {
        var normalizedEmail = NormalizeAndValidateEmail(email);
        var canonicalPurpose = NormalizeAndValidatePurpose(purpose);
        var trimmedCode = code?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedCode))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        return await _context.EmailOtps.AnyAsync(o =>
            o.Email == normalizedEmail &&
            o.Purpose == canonicalPurpose &&
            o.Code == trimmedCode &&
            !o.IsUsed &&
            o.ExpiresAt > now);
    }

    public async Task VerifyAndConsumeOtpAsync(string email, string code, string purpose)
    {
        var normalizedEmail = NormalizeAndValidateEmail(email);
        var canonicalPurpose = NormalizeAndValidatePurpose(purpose);
        var trimmedCode = code?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedCode))
        {
            throw new InvalidOperationException("OTP code is required.");
        }

        var otp = await _context.EmailOtps
            .Where(o =>
                o.Email == normalizedEmail &&
                o.Purpose == canonicalPurpose &&
                o.Code == trimmedCode &&
                !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null)
        {
            throw new InvalidOperationException("Invalid OTP code.");
        }

        if (otp.ExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("OTP code has expired. Please request a new one.");
        }

        otp.IsUsed = true;
        otp.UsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private static string NormalizeAndValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.");
        }

        var trimmed = email.Trim();
        try
        {
            var addr = new MailAddress(trimmed);
            if (!string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid email format.");
            }
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid email format.");
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeAndValidatePurpose(string purpose)
    {
        if (string.Equals(purpose?.Trim(), PurposeRegister, StringComparison.OrdinalIgnoreCase))
        {
            return PurposeRegister;
        }

        if (string.Equals(purpose?.Trim(), PurposeForgotPassword, StringComparison.OrdinalIgnoreCase))
        {
            return PurposeForgotPassword;
        }

        throw new ArgumentException("Invalid OTP purpose. Allowed values: Register, ForgotPassword.");
    }
}
