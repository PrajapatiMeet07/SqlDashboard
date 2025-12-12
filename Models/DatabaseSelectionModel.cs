namespace SqlDashboard.Models
{
    public class DatabaseSelectionModel
    {
        public string? ConnectionString { get; set; }
        public List<string>? Databases { get; set; }
        public string? SelectedDatabase { get; set; }
    }
}
