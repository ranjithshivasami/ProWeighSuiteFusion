namespace ProWeighSuitePlatforms.Models
{
    public class CompanySettings
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string MaterialCodePrefix { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string GSTNo { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string ReportEmail { get; set; } = string.Empty;
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
        public System.DateTime UpdatedAt { get; set; } = System.DateTime.Now;
    }
}
