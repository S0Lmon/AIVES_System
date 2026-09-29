using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Options;

namespace AIVES.WebMVC.Services.Email;

public sealed class GmailSmtpEmailSender : IAppEmailSender
{
    private readonly GmailSmtpOptions _options;

    public GmailSmtpEmailSender(IOptions<GmailSmtpOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.AppPassword);

    public async Task SendVerificationCodeAsync(string recipientEmail, string displayName, string code, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Dịch vụ gửi email chưa được cấu hình.");

        using var message = new MailMessage
        {
            From = new MailAddress(_options.Username, _options.SenderName),
            Subject = "Mã xác minh tài khoản AIVES",
            Body = $"""
                Xin chào {WebUtility.HtmlEncode(displayName)},

                Mã xác minh AIVES của bạn là: {code}

                Mã có hiệu lực trong 10 phút. Nếu bạn không yêu cầu đăng ký, hãy bỏ qua email này.
                """,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        message.To.Add(new MailAddress(recipientEmail));
        message.Headers.Add("X-Auto-Response-Suppress", "All");

        using var smtp = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.AppPassword),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000
        };

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
