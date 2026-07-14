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

        // ================= API ENDPOINTS FOR AJAX DYNAMIC UI =================

        public class ExecuteQueryRequest
        {
            public string? Query { get; set; }
            public string? Database { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> ExecuteQuery([FromBody] ExecuteQueryRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(serverConnection))
                return Json(new { success = false, errorMessage = "Not connected to any SQL Server." });

            if (string.IsNullOrWhiteSpace(request.Query))
                return Json(new { success = false, errorMessage = "Query is empty." });

            string targetDb = request.Database ?? lastUsedDatabase ?? "master";
            activeConnection = $"{serverConnection};Initial Catalog={targetDb};";
            lastUsedDatabase = targetDb;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var conn = new SqlConnection(activeConnection);
                await conn.OpenAsync(cancellationToken);

                using var cmd = new SqlCommand(request.Query, conn);
                
                // Execute reader asynchronously, checking CancellationToken
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                
                var dt = new DataTable();
                // Load column definitions
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                }

                // Read rows asynchronously, checking CancellationToken on each row
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.GetValue(i);
                    }
                    dt.Rows.Add(row);
                }

                stopwatch.Stop();

                var columns = new List<object>();
                foreach (DataColumn col in dt.Columns)
                {
                    columns.Add(new
                    {
                        name = col.ColumnName,
                        type = col.DataType.Name
                    });
                }

                var rows = new List<Dictionary<string, object?>>();
                foreach (DataRow row in dt.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in dt.Columns)
                    {
                        var val = row[col];
                        dict[col.ColumnName] = val == DBNull.Value ? null : val;
                    }
                    rows.Add(dict);
                }

                return Json(new
                {
                    success = true,
                    rowCount = rows.Count,
                    executionTimeMs = stopwatch.ElapsedMilliseconds,
                    columns = columns,
                    data = rows
                });
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return Json(new
                {
                    success = false,
                    isCancelled = true,
                    errorMessage = "Query execution was cancelled by the user.",
                    executionTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                // Also check if inner exceptions or SqlException indicates user abort/cancel
                if (ex is SqlException sqlEx && (sqlEx.Number == 0 || sqlEx.Message.Contains("cancelled") || sqlEx.Message.Contains("Operation cancelled") || sqlEx.Message.Contains("cancelled by user")))
                {
                    return Json(new
                    {
                        success = false,
                        isCancelled = true,
                        errorMessage = "Query execution was cancelled by the user.",
                        executionTimeMs = stopwatch.ElapsedMilliseconds
                    });
                }

                return Json(new
                {
                    success = false,
                    errorMessage = ex.Message,
                    executionTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
        }

        [HttpGet]
        public IActionResult GetSchema(string database)
        {
            if (string.IsNullOrEmpty(serverConnection))
                return Json(new { success = false, errorMessage = "Not connected to any server." });

            string targetDb = string.IsNullOrEmpty(database) ? (lastUsedDatabase ?? "master") : database;
            string dbConnection = $"{serverConnection};Initial Catalog={targetDb};";

            try
            {
                using var conn = new SqlConnection(dbConnection);
                conn.Open();

                // 1. Get Tables & Columns in a single optimized query
                string tableQuery = @"
                    SELECT 
                        t.TABLE_NAME,
                        c.COLUMN_NAME,
                        c.DATA_TYPE,
                        c.IS_NULLABLE,
                        CASE WHEN k.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
                    FROM INFORMATION_SCHEMA.TABLES t
                    INNER JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
                    LEFT JOIN (
                        SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                        INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc ON ku.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    ) k ON c.TABLE_NAME = k.TABLE_NAME AND c.COLUMN_NAME = k.COLUMN_NAME
                    WHERE t.TABLE_TYPE = 'BASE TABLE'
                    ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

                var tablesMap = new Dictionary<string, List<object>>();
                using (var cmd = new SqlCommand(tableQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        string columnName = reader.GetString(1);
                        string dataType = reader.GetString(2);
                        string isNullable = reader.GetString(3);
                        bool isPrimaryKey = reader.GetInt32(4) == 1;

                        if (!tablesMap.ContainsKey(tableName))
                        {
                            tablesMap[tableName] = new List<object>();
                        }

                        tablesMap[tableName].Add(new
                        {
                            name = columnName,
                            type = dataType,
                            nullable = isNullable == "YES",
                            isPrimaryKey = isPrimaryKey
                        });
                    }
                }

                var tablesList = tablesMap.Select(kv => new
                {
                    name = kv.Key,
                    columns = kv.Value
                }).OrderBy(t => t.name).ToList();

                // 2. Get Views
                var viewsList = new List<string>();
                string viewQuery = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS ORDER BY TABLE_NAME";
                using (var cmd = new SqlCommand(viewQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewsList.Add(reader.GetString(0));
                    }
                }

                // 3. Get Stored Procedures
                var proceduresList = new List<string>();
                string procQuery = "SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME";
                using (var cmd = new SqlCommand(procQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        proceduresList.Add(reader.GetString(0));
                    }
                }

                return Json(new
                {
                    success = true,
                    tables = tablesList,
                    views = viewsList,
                    procedures = proceduresList
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetTableScript(string tableName, string database)
        {
            if (string.IsNullOrEmpty(serverConnection))
                return Json(new { success = false, errorMessage = "Not connected to any server." });

            string targetDb = string.IsNullOrEmpty(database) ? (lastUsedDatabase ?? "master") : database;
            string dbConnection = $"{serverConnection};Initial Catalog={targetDb};";

            try
            {
                using var conn = new SqlConnection(dbConnection);
                conn.Open();

                string colQuery = @"
                    SELECT 
                        c.COLUMN_NAME,
                        c.DATA_TYPE,
                        c.IS_NULLABLE,
                        c.CHARACTER_MAXIMUM_LENGTH,
                        CASE WHEN k.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    LEFT JOIN (
                        SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                        INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc ON ku.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    ) k ON c.TABLE_NAME = k.TABLE_NAME AND c.COLUMN_NAME = k.COLUMN_NAME
                    WHERE c.TABLE_NAME = @TableName
                    ORDER BY c.ORDINAL_POSITION";

                var cols = new List<string>();
                var pks = new List<string>();

                using (var cmd = new SqlCommand(colQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string colName = reader.GetString(0);
                        string dataType = reader.GetString(1);
                        string isNullable = reader.GetString(2);
                        int maxLength = reader.IsDBNull(3) ? -1 : reader.GetInt32(3);
                        bool isPrimaryKey = reader.GetInt32(4) == 1;

                        string lengthStr = "";
                        if (dataType.Contains("char") || dataType.Contains("binary"))
                        {
                            lengthStr = maxLength == -1 ? "(MAX)" : $"({maxLength})";
                        }

                        string colDef = $"[{colName}] {dataType.ToUpper()}{lengthStr} {(isNullable == "YES" ? "NULL" : "NOT NULL")}";
                        cols.Add("    " + colDef);

                        if (isPrimaryKey)
                        {
                            pks.Add($"[{colName}]");
                        }
                    }
                }

                if (cols.Count == 0)
                {
                    return Json(new { success = false, errorMessage = $"Table '{tableName}' not found or could not be scripted." });
                }

                if (pks.Count > 0)
                {
                    cols.Add($"    PRIMARY KEY ({string.Join(", ", pks)})");
                }

                string script = $"CREATE TABLE [{tableName}] (\n{string.Join(",\n", cols)}\n);";

                return Json(new { success = true, script = script });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult QuerySuggestions(string term)
        {
            if (string.IsNullOrEmpty(activeConnection))
                return Json(new List<string>());

            try
            {
                using var conn = new SqlConnection(activeConnection);
                conn.Open();

                var tables = new List<string>();
                DataTable dt = conn.GetSchema("Tables");
                foreach (DataRow row in dt.Rows)
                {
                    if (row["TABLE_TYPE"].ToString() == "BASE TABLE")
                    {
                        tables.Add(row["TABLE_NAME"].ToString()!);
                    }
                }

                var matches = tables
                    .Where(t => string.IsNullOrEmpty(term) || t.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .ToList();

                return Json(matches);
            }
            catch
            {
                return Json(new List<string>());
            }
        }
    }
}
