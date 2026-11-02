namespace ProWeighSuitePlatforms.Models
{
    public class EmailSettings
    {
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "ProWeighSuitePlatforms";
        public string AppName { get; set; } = "Pro Weigh Suite";
        public bool UseSsl { get; set; } = true;
    }
}
