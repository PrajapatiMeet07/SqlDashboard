using Microsoft.AspNetCore.Mvc;
using SqlDashboard.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace SqlDashboard.Controllers
{
    public class DashboardController : Controller
    {
        private static string? serverConnection;
        private static string? activeConnection;
        private static string? lastUsedDatabase;

        // ---------------------------------------
        // SQL CONNECTION PAGE
        // ---------------------------------------
        public IActionResult SqlConnection()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SqlConnection(SqlConnectionModel model)
        {
            try
            {
                string cs;

                if (model.Authentication == "Windows")
                {
                    cs = $"Server={model.ServerName};Trusted_Connection=True;Encrypt=False;";
                }
                else
                {
                    cs = $"Server={model.ServerName};User Id={model.Login};Password={model.Password};Encrypt=False;";
                }

                using SqlConnection conn = new SqlConnection(cs);
                conn.Open();

                serverConnection = cs;

                return RedirectToAction("TestConnection", model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        // ---------------------------------------
        // TEST CONNECTION → DIRECT TO QueryEditor
        // ---------------------------------------
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

        // ---------------------------------------
        // QUERY EDITOR
        // ---------------------------------------
        public IActionResult QueryEditor()
        {
            if (string.IsNullOrEmpty(serverConnection))
                return RedirectToAction("SqlConnection");

            QueryModel model = new QueryModel();
            LoadDatabases(model);

            return View(model);
        }

        // ---------------------------------------
        // CHANGE DATABASE
        // ---------------------------------------
        [HttpPost]
        public IActionResult ChangeDatabase(string databaseName)
        {
        if (string.IsNullOrEmpty(serverConnection))
        return RedirectToAction("SqlConnection");

        activeConnection = $"{serverConnection};Initial Catalog={databaseName};";
        lastUsedDatabase = databaseName;

        QueryModel model = new QueryModel();
        LoadDatabases(model);

        model.SelectedDatabase = databaseName;

        return View("QueryEditor", model);
        }


        // ---------------------------------------
        // RUN QUERY
        // ---------------------------------------
        [HttpPost]
        public IActionResult RunQuery(string query)
        {
            if (string.IsNullOrEmpty(activeConnection))
                return RedirectToAction("SqlConnection");

            QueryModel model = new QueryModel();
            LoadDatabases(model);
            model.Query = query;

            try
            {
                using SqlConnection conn = new SqlConnection(activeConnection);
                conn.Open();

                using SqlCommand cmd = new SqlCommand(query, conn);
                using SqlDataAdapter da = new SqlDataAdapter(cmd);

                DataTable dt = new DataTable();
                da.Fill(dt);

                model.ResultTable = dt;
            }
            catch (Exception ex)
            {
                model.ErrorMessage = ex.Message;
            }

            return View("QueryEditor", model);
        }

        // ---------------------------------------
        // DISCONNECT
        // ---------------------------------------
        [HttpPost]
        public IActionResult Disconnect()
        {
            serverConnection = null;
            activeConnection = null;
            lastUsedDatabase = null;

            return RedirectToAction("SqlConnection");
        }

        // ---------------------------------------
        // LOAD DATABASES & TABLES (LEFT PANEL)
        // ---------------------------------------
        private void LoadDatabases(QueryModel model)
        {
            if (string.IsNullOrEmpty(serverConnection))
                return;

            model.AvailableDatabases.Clear();

            using SqlConnection conn = new SqlConnection(serverConnection);
            conn.Open();

            DataTable dt = conn.GetSchema("Databases");

            foreach (DataRow row in dt.Rows)
            {
                string? db = row["database_name"]?.ToString();
                if (!string.IsNullOrEmpty(db))
                    model.AvailableDatabases.Add(db);
            }

            // Maintain selected DB
            if (string.IsNullOrEmpty(activeConnection))
            {
                string firstDb = model.AvailableDatabases.FirstOrDefault() ?? "master";
                activeConnection = $"{serverConnection};Initial Catalog={firstDb};";
                model.SelectedDatabase = firstDb;
            }
            else
            {
                model.SelectedDatabase =
                    new SqlConnectionStringBuilder(activeConnection).InitialCatalog;
            }

            // LOAD TABLES
            model.Tables ??= new List<string>();
            model.Tables.Clear();

            using SqlConnection tableConn = new SqlConnection(activeConnection);
            tableConn.Open();

            DataTable tables = tableConn.GetSchema("Tables");

            foreach (DataRow row in tables.Rows)
            {
                string? tableName = row["TABLE_NAME"]?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                    model.Tables.Add(tableName);
            }
        }
    }
}
