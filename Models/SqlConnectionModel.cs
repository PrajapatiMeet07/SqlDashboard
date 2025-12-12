namespace SqlDashboard.Models
{
    public class SqlConnectionModel
    {
        public string? ServerName { get; set; }
        public string Authentication { get; set; } = "Sql";
        public string? Login { get; set; }
        public string? Password { get; set; }
    }
}
