namespace AIVES.BLL.Services.Email;

public sealed class GmailSmtpOptions
{
    public const string SectionName = "GmailSmtp";
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
    public string SenderName { get; set; } = "AIVES";
}
