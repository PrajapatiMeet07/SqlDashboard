using System.Data;

namespace SqlDashboard.Models
{
    public class QueryModel
    {
        public string? SelectedDatabase { get; set; }
        public List<string> AvailableDatabases { get; set; } = new List<string>();

        public string? Query { get; set; }

        public DataTable? ResultTable { get; set; }

        public string? ErrorMessage { get; set; }
        public List<string>? Tables { get; set; } = new List<string>();
    }
}
