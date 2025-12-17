using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SqlDashboard.Models;
using System.Data;

namespace SqlDashboard.Controllers
{
    public class DashboardController : Controller
    {
        private static string? serverConnection;
        private static string? activeConnection;
        private static string? lastUsedDatabase;
        private static readonly string[] ImageRoots =
{
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
    Path.Combine(Directory.GetCurrentDirectory(), "AppImages")
};


        // ================= LOGIN =================

        public IActionResult SqlConnection()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SqlConnection(string server, string user, string password)
        {
            try
            {
                serverConnection =
                    $"Server={server};User Id={user};Password={password};Encrypt=False;";

                using var conn = new SqlConnection(serverConnection);
                conn.Open();

                return RedirectToAction("QueryEditor");
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View();
            }
        }
[HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TestConnection(SqlConnectionModel model)
        {
            if (string.IsNullOrWhiteSpace(model.ServerName))
            {
                ViewBag.Message = "Please enter Server Name.";
                return View("SqlConnection", model);
            }

            string coreConn;

            if (model.Authentication == "Windows")
            {
                coreConn = $"Server={model.ServerName};Trusted_Connection=True;Encrypt=False;";
            }
            else
            {
                coreConn = $"Server={model.ServerName};User Id={model.Login};Password={model.Password};Encrypt=False;";
            }

            try
            {
                using var conn = new SqlConnection(coreConn);
                conn.Open();

                serverConnection = coreConn;

                // Load database list
                DataTable dbList = conn.GetSchema("Databases");

                if (dbList.Rows.Count == 0)
                {
                    ViewBag.Message = "No databases found.";
                    return View("SqlConnection", model);
                }

                // RECENT DB FEATURE
                string defaultDb;

                if (!string.IsNullOrEmpty(lastUsedDatabase))
                {
                    defaultDb = lastUsedDatabase;
                }
                else
                {
                    defaultDb = dbList.Rows[0]["database_name"]?.ToString() ?? "master";
                }

                // Build active DB
                activeConnection = $"{serverConnection};Initial Catalog={defaultDb};";
                lastUsedDatabase = defaultDb;

                return RedirectToAction("QueryEditor");
            }
            catch (Exception ex)
            {
                ViewBag.Message = "Connection failed: " + ex.Message;
                return View("SqlConnection", model);
            }
        }
        // ================= QUERY EDITOR =================

        public IActionResult QueryEditor()
        {
            if (string.IsNullOrEmpty(serverConnection))
                return RedirectToAction("SqlConnection");

            QueryModel model = new();
            LoadDatabases(model);
            LoadTables(model);

            return View(model);
        }

        [HttpPost]
        public IActionResult ChangeDatabase(string databaseName)
        {
            activeConnection = $"{serverConnection};Initial Catalog={databaseName};";

            QueryModel model = new();
            LoadDatabases(model);
            model.SelectedDatabase = databaseName;
            LoadTables(model);

            return View("QueryEditor", model);
        }
[HttpPost]
public IActionResult RunQuery(string query)
{
    QueryModel model = new();
    LoadDatabases(model);
    LoadTables(model);
    model.Query = query;

    try
    {
        using var conn = new SqlConnection(activeConnection);
        conn.Open();

        using var da = new SqlDataAdapter(query, conn);
        DataTable dt = new();
        da.Fill(dt);

        model.ResultTable = dt;
    }
    catch (Exception ex)
    {
        model.ErrorMessage = ex.Message;
    }

    return View("QueryEditor", model);
}
        [HttpPost]
        public IActionResult Disconnect()
        {
            serverConnection = null;
            activeConnection = null;
            return RedirectToAction("SqlConnection");
        }

        // ================= IMAGE VIEWER (SECURE) =================

[HttpGet]
public IActionResult ViewTransactionImage(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return BadRequest("Empty path");

    // decode URL
    path = Uri.UnescapeDataString(path).Trim();

    // IMPORTANT: normalize slashes
    path = path.Replace("/", "\\");

    // DEBUG — TEMPORARY
    System.Diagnostics.Debug.WriteLine("IMAGE PATH = " + path);

    if (!System.IO.File.Exists(path))
        return NotFound("File not found on disk: " + path);

    return PhysicalFile(path, "image/jpeg");
}


private static string GetContentType(string path)
{
    string ext = Path.GetExtension(path).ToLowerInvariant();

    return ext switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}
        // ================= HELPERS =================

        private void LoadDatabases(QueryModel model)
        {
            using var conn = new SqlConnection(serverConnection);
            conn.Open();

            DataTable dt = conn.GetSchema("Databases");

            foreach (DataRow row in dt.Rows)
                model.AvailableDatabases.Add(row["database_name"].ToString()!);

            if (activeConnection == null && model.AvailableDatabases.Any())
            {
                model.SelectedDatabase = model.AvailableDatabases[0];
                activeConnection = $"{serverConnection};Initial Catalog={model.SelectedDatabase};";
            }
            else
            {
                model.SelectedDatabase =
                    new SqlConnectionStringBuilder(activeConnection!).InitialCatalog;
            }
        }

        private void LoadTables(QueryModel model)
        {
            model.Tables.Clear();

            using var conn = new SqlConnection(activeConnection);
            conn.Open();

            DataTable dt = conn.GetSchema("Tables");

            foreach (DataRow row in dt.Rows)
            {
                if (row["TABLE_TYPE"].ToString() == "BASE TABLE")
                    model.Tables.Add(row["TABLE_NAME"].ToString()!);
            }
        }
    }
}
