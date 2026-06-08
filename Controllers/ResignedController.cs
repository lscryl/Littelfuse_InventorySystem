// ============================================================
// File: Controllers/ResignedController.cs
// Purpose: Handles all Resigned page actions.
//
//  PHP → ASP.NET MVC mapping:
//    $_POST['action'] === 'add_resigned'       → [HttpPost] Add()
//    $_POST['action'] === 'edit_resigned'      → [HttpPost] Edit()
//    $_POST['action'] === 'deploy_asset'       → [HttpPost] Deploy()
//    $_POST['action'] === 'return_to_resigned' → [HttpPost] ReturnToInventory()
//    $_POST['action'] === 'delete_resigned'    → [HttpPost] Delete()
//    $_GET['fetch_history']                    → [HttpGet]  FetchHistory()
//    $_GET['fetch_all_history']                → [HttpGet]  FetchAllHistory()
//    Main page load                            → [HttpGet]  Index()
// ============================================================

using Microsoft.AspNetCore.Mvc;
using ITInventorySystem.Models;
using ITInventorySystem.Data;
using MySqlConnector;

namespace ITInventorySystem.Controllers
{
    public class ResignedController : Controller
    {
        private readonly DbHelper _db;

        public ResignedController(DbHelper db)
        {
            _db = db;
        }

        // ── Auth guard ──────────────────────────────────────────
        private bool IsLoggedIn =>
            HttpContext.Session.GetString("logged_in") == "true";

        private string CurrentUser =>
            HttpContext.Session.GetString("username") ?? "System";

        private IActionResult? RequireLogin()
        {
            if (!IsLoggedIn)
                return RedirectToAction("Login", "Account");
            return null;
        }

        // ── Toast helper ────────────────────────────────────────
        private void SetToast(string message, string type)
        {
            HttpContext.Session.SetString("toast_message", message);
            HttpContext.Session.SetString("toast_type", type);
        }

        // ════════════════════════════════════════════════════════
        //  GET /Resigned?tab=resigned&search=
        //  Replaces: bottom GET data-fetch block in resigned.php
        // ════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult Index(string tab = "resigned", string search = "")
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            if (!new[] { "resigned", "deployed", "history" }.Contains(tab))
                tab = "resigned";

            var vm = new ResignedViewModel
            {
                ActiveTab = tab,
                Search    = search,
                ToastMsg  = HttpContext.Session.GetString("toast_message") ?? "",
                ToastType = HttpContext.Session.GetString("toast_type")    ?? "success",
                SaveError = HttpContext.Session.GetString("save_error")    ?? "",
            };

            HttpContext.Session.Remove("toast_message");
            HttpContext.Session.Remove("toast_type");
            HttpContext.Session.Remove("save_error");

            using var conn = _db.GetConnection();

            // Tab counts — replaces PHP: $count_resigned, $count_deployed, $count_history
            vm.CountResigned = GetCount(conn, "SELECT COUNT(*) FROM resigned WHERE asset_status = 'resigned'");
            vm.CountDeployed = GetCount(conn, "SELECT COUNT(*) FROM resigned WHERE asset_status = 'deployed'");
            vm.CountHistory  = GetCount(conn, "SELECT COUNT(*) FROM asset_history");

            if (tab != "history")
                vm.Records = FetchResigned(conn, tab, search);

            return View(vm);
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/Add
        //  Replaces: if ($_POST['action'] === 'add_resigned')
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(
            string employee_name, string? department, string? model,
            string? serial_tag,  string? accessories, string? date_of_clearance,
            string? notes,       string  asset_status = "resigned")
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(employee_name))
            {
                SetToast("Employee name is required.", "error");
                return RedirectToAction("Index", new { tab = asset_status });
            }

            if (!new[] { "resigned", "deployed" }.Contains(asset_status))
                asset_status = "resigned";

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(@"
                INSERT INTO resigned
                    (employee_name, department, model, serial_tag,
                     accessories, date_of_clearance, notes, asset_status)
                VALUES
                    (@name, @dept, @model, @serial,
                     @acc, @date, @notes, @status)", conn);

            cmd.Parameters.AddWithValue("@name",   employee_name);
            cmd.Parameters.AddWithValue("@dept",   department       ?? "");
            cmd.Parameters.AddWithValue("@model",  model            ?? "");
            cmd.Parameters.AddWithValue("@serial", serial_tag       ?? "");
            cmd.Parameters.AddWithValue("@acc",    accessories      ?? "");
            cmd.Parameters.AddWithValue("@date",   string.IsNullOrWhiteSpace(date_of_clearance)
                                                       ? DBNull.Value : date_of_clearance);
            cmd.Parameters.AddWithValue("@notes",  notes            ?? "");
            cmd.Parameters.AddWithValue("@status", asset_status);
            cmd.ExecuteNonQuery();

            int newId = (int)cmd.LastInsertedId;

            // Log history — replaces PHP logHistory()
            string details = $"Record created for {employee_name}" +
                             (department    != null ? $" ({department})"    : "") +
                             (model         != null ? $" | Model: {model}"  : "") +
                             (serial_tag    != null ? $" | Serial: {serial_tag}" : "") +
                             $" | Status: {asset_status}";
            LogHistory(conn, newId, "Record Created", details, MakeSnapshot(conn, newId));

            SetToast($"Record added for: {employee_name}", "success");
            return RedirectToAction("Index", new { tab = asset_status });
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/Edit
        //  Replaces: if ($_POST['action'] === 'edit_resigned')
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(
            int id, string employee_name, string? department,
            string? model, string? serial_tag, string? accessories,
            string? date_of_clearance, string? notes,
            string asset_status = "resigned",
            string? deployed_to = null, string? deployed_dept = null,
            string? deployed_notes = null)
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(employee_name))
            {
                SetToast("Employee name is required.", "error");
                return RedirectToAction("Index", new { tab = asset_status });
            }

            if (!new[] { "resigned", "deployed" }.Contains(asset_status))
                asset_status = "resigned";

            if (asset_status == "deployed" && string.IsNullOrWhiteSpace(deployed_to))
            {
                SetToast("Deployed To field is required when status is Deployed.", "error");
                return RedirectToAction("Index", new { tab = asset_status });
            }

            using var conn = _db.GetConnection();

            // Get old record to compare + handle deployed_at logic
            // Replaces PHP: $old = $conn->query("SELECT * FROM resigned WHERE id=$id")->fetch_assoc();
            string? oldStatus    = null;
            string? oldDeployedAt = null;
            using (var sel = new MySqlCommand(
                "SELECT asset_status, deployed_at FROM resigned WHERE id=@id", conn))
            {
                sel.Parameters.AddWithValue("@id", id);
                using var r = sel.ExecuteReader();
                if (r.Read())
                {
                    oldStatus     = r.IsDBNull(0) ? null : r.GetString(0);
                    oldDeployedAt = r.IsDBNull(1) ? null : r.GetDateTime(1).ToString("yyyy-MM-dd");
                }
            }

            // Determine deployed_at value
            string? deployedAtVal = oldDeployedAt;
            if (asset_status == "deployed" && string.IsNullOrEmpty(deployedAtVal))
                deployedAtVal = DateTime.Today.ToString("yyyy-MM-dd");
            if (asset_status == "resigned")
            {
                deployedAtVal  = null;
                deployed_to    = "";
                deployed_dept  = "";
                deployed_notes = "";
            }

            using var cmd = new MySqlCommand(@"
                UPDATE resigned SET
                    employee_name=@name, department=@dept, model=@model,
                    serial_tag=@serial, accessories=@acc,
                    date_of_clearance=@date, notes=@notes, asset_status=@status,
                    deployed_to=@dto, deployed_dept=@ddept,
                    deployed_notes=@dnotes, deployed_at=@dat
                WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@name",   employee_name);
            cmd.Parameters.AddWithValue("@dept",   department       ?? "");
            cmd.Parameters.AddWithValue("@model",  model            ?? "");
            cmd.Parameters.AddWithValue("@serial", serial_tag       ?? "");
            cmd.Parameters.AddWithValue("@acc",    accessories      ?? "");
            cmd.Parameters.AddWithValue("@date",   string.IsNullOrWhiteSpace(date_of_clearance)
                                                       ? DBNull.Value : date_of_clearance);
            cmd.Parameters.AddWithValue("@notes",  notes            ?? "");
            cmd.Parameters.AddWithValue("@status", asset_status);
            cmd.Parameters.AddWithValue("@dto",    deployed_to      ?? "");
            cmd.Parameters.AddWithValue("@ddept",  deployed_dept    ?? "");
            cmd.Parameters.AddWithValue("@dnotes", deployed_notes   ?? "");
            cmd.Parameters.AddWithValue("@dat",    deployedAtVal    ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id",     id);
            cmd.ExecuteNonQuery();

            string details = $"Edited record for {employee_name}";
            if (!string.IsNullOrEmpty(model))      details += $" | Model: {model}";
            if (!string.IsNullOrEmpty(serial_tag)) details += $" | Serial: {serial_tag}";
            if (oldStatus != asset_status)         details += $" | Status: {oldStatus} → {asset_status}";
            LogHistory(conn, id, "Record Edited", details, MakeSnapshot(conn, id));

            SetToast("Record updated successfully.", "success");
            return RedirectToAction("Index", new { tab = asset_status });
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/Deploy
        //  Replaces: if ($_POST['action'] === 'deploy_asset')
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Deploy(
            int id, string new_employee_name,
            string? new_department = null, string? deploy_notes = null)
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(new_employee_name))
            {
                SetToast("New employee name is required.", "error");
                return RedirectToAction("Index", new { tab = "resigned" });
            }

            string today = DateTime.Today.ToString("yyyy-MM-dd");
            using var conn = _db.GetConnection();

            // Get old record for history log
            string oldName = "";
            using (var sel = new MySqlCommand(
                "SELECT employee_name FROM resigned WHERE id=@id", conn))
            {
                sel.Parameters.AddWithValue("@id", id);
                oldName = sel.ExecuteScalar()?.ToString() ?? "";
            }

            using var cmd = new MySqlCommand(@"
                UPDATE resigned SET
                    asset_status='deployed',
                    deployed_to=@name, deployed_dept=@dept,
                    deployed_notes=@notes, deployed_at=@dat
                WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@name",  new_employee_name);
            cmd.Parameters.AddWithValue("@dept",  new_department ?? "");
            cmd.Parameters.AddWithValue("@notes", deploy_notes   ?? "");
            cmd.Parameters.AddWithValue("@dat",   today);
            cmd.Parameters.AddWithValue("@id",    id);
            cmd.ExecuteNonQuery();

            string details = $"Deployed to {new_employee_name}" +
                             (new_department != null ? $" ({new_department})" : "") +
                             $" | formerly: {oldName}" +
                             (!string.IsNullOrEmpty(deploy_notes) ? $" | Notes: {deploy_notes}" : "");
            LogHistory(conn, id, "Asset Deployed", details, MakeSnapshot(conn, id));

            SetToast($"Asset deployed to: {new_employee_name}", "success");
            return RedirectToAction("Index", new { tab = "deployed" });
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/ReturnToInventory
        //  Replaces: if ($_POST['action'] === 'return_to_resigned')
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnToInventory(int id)
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            using var conn = _db.GetConnection();

            // Get old record
            string oldName = "", oldDept = "", oldEmployeeName = "", deployedTo = "";
            using (var sel = new MySqlCommand(
                "SELECT employee_name, department, deployed_to, deployed_dept FROM resigned WHERE id=@id", conn))
            {
                sel.Parameters.AddWithValue("@id", id);
                using var r = sel.ExecuteReader();
                if (r.Read())
                {
                    oldEmployeeName = r.IsDBNull(0) ? "" : r.GetString(0);
                    oldDept         = r.IsDBNull(1) ? "" : r.GetString(1);
                    deployedTo      = r.IsDBNull(2) ? "" : r.GetString(2);
                    string dDept    = r.IsDBNull(3) ? "" : r.GetString(3);
                    // Use deployed_to as the new former user
                    oldName = !string.IsNullOrEmpty(deployedTo) ? deployedTo : oldEmployeeName;
                    oldDept = !string.IsNullOrEmpty(dDept)      ? dDept      : oldDept;
                }
            }

            using var cmd = new MySqlCommand(@"
                UPDATE resigned SET
                    asset_status='resigned', employee_name=@name, department=@dept,
                    deployed_to='', deployed_dept='', deployed_notes='', deployed_at=NULL
                WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@name", oldName);
            cmd.Parameters.AddWithValue("@dept", oldDept);
            cmd.Parameters.AddWithValue("@id",   id);
            cmd.ExecuteNonQuery();

            string details = $"Asset returned to inventory. Former user updated to: {oldName}" +
                             (!string.IsNullOrEmpty(oldDept) ? $" ({oldDept})" : "") +
                             $". Previously deployed from: {oldEmployeeName}.";
            LogHistory(conn, id, "Back to Inventory", details, MakeSnapshot(conn, id));

            SetToast($"Asset returned to inventory. Former user: {oldName}", "success");
            return RedirectToAction("Index", new { tab = "resigned" });
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/Delete
        //  Replaces: if ($_POST['action'] === 'delete_resigned')
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id, string current_tab = "resigned")
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            using var conn = _db.GetConnection();

            // Get record for history log before deleting
            string empName = "", empModel = "", empSerial = "";
            string snapshot = MakeSnapshot(conn, id);
            using (var sel = new MySqlCommand(
                "SELECT employee_name, model, serial_tag FROM resigned WHERE id=@id", conn))
            {
                sel.Parameters.AddWithValue("@id", id);
                using var r = sel.ExecuteReader();
                if (r.Read())
                {
                    empName   = r.IsDBNull(0) ? "" : r.GetString(0);
                    empModel  = r.IsDBNull(1) ? "" : r.GetString(1);
                    empSerial = r.IsDBNull(2) ? "" : r.GetString(2);
                }
            }

            string details = $"Record permanently deleted for {empName}" +
                             (!string.IsNullOrEmpty(empModel)  ? $" | Model: {empModel}"   : "") +
                             (!string.IsNullOrEmpty(empSerial) ? $" | Serial: {empSerial}" : "");

            LogHistory(conn, id, "Record Deleted", details, snapshot);

            using var cmd = new MySqlCommand("DELETE FROM resigned WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            SetToast("Record deleted.", "error");
            return RedirectToAction("Index", new { tab = current_tab });
        }

        // ════════════════════════════════════════════════════════
        //  POST /Resigned/ImportRecords
        // ════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ImportRecords(string rows_json)
        {
            var guard = RequireLogin();
            if (guard != null) return guard;

            int imported = 0;
            try
            {
                var rows = System.Text.Json.JsonSerializer.Deserialize<List<ResignedImportRow>>(rows_json ?? "[]")
                        ?? new List<ResignedImportRow>();

                using var conn = _db.GetConnection();
                foreach (var r in rows)
                {
                    var name = r.employee_name?.Trim() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    using var cmd = new MySqlCommand(@"
                        INSERT INTO resigned
                            (employee_name, department, model, serial_tag,
                            accessories, date_of_clearance, notes, asset_status,
                            deployed_to, deployed_dept, deployed_notes, deployed_at)
                        VALUES
                            (@name, @dept, @model, @serial,
                            @acc, @date, @notes, 'resigned',
                            '', '', '', NULL)", conn);

                    cmd.Parameters.AddWithValue("@name",   name);
                    cmd.Parameters.AddWithValue("@dept",   r.department?.Trim()  ?? "");
                    cmd.Parameters.AddWithValue("@model",  r.model?.Trim()       ?? "");
                    cmd.Parameters.AddWithValue("@serial", r.serial_tag?.Trim()  ?? "");
                    cmd.Parameters.AddWithValue("@acc",    r.accessories?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@notes",  r.notes?.Trim()       ?? "");
                    cmd.Parameters.AddWithValue("@date",
                        string.IsNullOrWhiteSpace(r.date_of_clearance)
                            ? (object)DBNull.Value
                            : r.date_of_clearance.Trim());
                    cmd.ExecuteNonQuery();

                    int newId = (int)cmd.LastInsertedId;
                    LogHistory(conn, newId, "Record Created",
                        $"Imported record for {name}", MakeSnapshot(conn, newId));
                    imported++;
                }
            }
            catch { }

            SetToast($"{imported} record(s) imported successfully.", "success");
            return RedirectToAction("Index", new { tab = "resigned" });
        }

        // ════════════════════════════════════════════════════════
        //  GET /Resigned/FetchHistory?id=N
        //  Returns JSON — called by JavaScript fetch() in the View
        //  Replaces: if (isset($_GET['fetch_history']))
        // ════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult FetchHistory(int id)
        {
            if (!IsLoggedIn) return Unauthorized();

            using var conn = _db.GetConnection();
            var items = new List<object>();

            using var cmd = new MySqlCommand(
                "SELECT * FROM asset_history WHERE resigned_id=@id ORDER BY created_at DESC", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    id         = reader.GetInt32("id"),
                    action     = reader.GetString("action"),
                    actor      = reader.IsDBNull(reader.GetOrdinal("actor"))    ? "" : reader.GetString("actor"),
                    details    = reader.IsDBNull(reader.GetOrdinal("details"))  ? "" : reader.GetString("details"),
                    snapshot   = reader.IsDBNull(reader.GetOrdinal("snapshot")) ? "" : reader.GetString("snapshot"),
                    created_at = reader.GetDateTime("created_at").ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            return Json(items);
        }

        // ════════════════════════════════════════════════════════
        //  GET /Resigned/FetchAllHistory?search_h=...
        //  Replaces: if (isset($_GET['fetch_all_history']))
        // ════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult FetchAllHistory(string search_h = "")
        {
            if (!IsLoggedIn) return Unauthorized();

            using var conn = _db.GetConnection();
            var items = new List<object>();

            string sql = @"
                SELECT h.*, r.employee_name, r.model, r.serial_tag, r.department
                FROM asset_history h
                LEFT JOIN resigned r ON r.id = h.resigned_id";

            if (!string.IsNullOrWhiteSpace(search_h))
                sql += @" WHERE (h.action LIKE @s OR h.actor LIKE @s OR h.details LIKE @s
                              OR r.employee_name LIKE @s OR r.model LIKE @s OR r.serial_tag LIKE @s)";

            sql += " ORDER BY h.created_at DESC LIMIT 500";

            using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search_h))
                cmd.Parameters.AddWithValue("@s", $"%{search_h}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    id            = reader.GetInt32("id"),
                    resigned_id   = reader.GetInt32("resigned_id"),
                    action        = reader.GetString("action"),
                    actor         = reader.IsDBNull(reader.GetOrdinal("actor"))         ? "" : reader.GetString("actor"),
                    details       = reader.IsDBNull(reader.GetOrdinal("details"))       ? "" : reader.GetString("details"),
                    snapshot      = reader.IsDBNull(reader.GetOrdinal("snapshot"))      ? "" : reader.GetString("snapshot"),
                    created_at    = reader.GetDateTime("created_at").ToString("yyyy-MM-dd HH:mm:ss"),
                    employee_name = reader.IsDBNull(reader.GetOrdinal("employee_name")) ? "" : reader.GetString("employee_name"),
                    model         = reader.IsDBNull(reader.GetOrdinal("model"))         ? "" : reader.GetString("model"),
                    serial_tag    = reader.IsDBNull(reader.GetOrdinal("serial_tag"))    ? "" : reader.GetString("serial_tag"),
                    department    = reader.IsDBNull(reader.GetOrdinal("department"))    ? "" : reader.GetString("department"),
                });
            }

            return Json(items);
        }

        // ════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ════════════════════════════════════════════════════════

        private int GetCount(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Fetches resigned/deployed records with optional search
        // Replaces PHP: $records = $conn->query("SELECT * FROM resigned $where ...")->fetch_all()
                private List<ResignedRecord> FetchResigned(
            MySqlConnection conn, string tab, string search)
        {
            var records = new List<ResignedRecord>();

            string sql = "SELECT * FROM resigned WHERE asset_status = @status";

            if (!string.IsNullOrWhiteSpace(search))
            {
                if (tab == "deployed")
                    sql += @" AND (deployed_to LIKE @s OR employee_name LIKE @s
                                OR deployed_dept LIKE @s OR department LIKE @s
                                OR model LIKE @s OR serial_tag LIKE @s)";
                else
                    sql += @" AND (employee_name LIKE @s OR department LIKE @s
                                OR model LIKE @s OR serial_tag LIKE @s)";
            }

            sql += " ORDER BY created_at DESC";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", tab);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@s", $"%{search}%");

            // ✅ THIS LINE WAS MISSING — must open reader before while loop
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new ResignedRecord
                {
                    Id              = reader.GetInt32("id"),
                    EmployeeName    = reader.GetString("employee_name"),
                    Department      = SafeString(reader, "department"),
                    Model           = SafeString(reader, "model"),
                    SerialTag       = SafeString(reader, "serial_tag"),
                    Accessories     = SafeString(reader, "accessories"),
                    DateOfClearance = SafeDate(reader, "date_of_clearance"),
                    Notes           = SafeString(reader, "notes"),
                    AssetStatus     = reader.GetString("asset_status"),
                    DeployedTo      = SafeString(reader, "deployed_to"),
                    DeployedDept    = SafeString(reader, "deployed_dept"),
                    DeployedNotes   = SafeString(reader, "deployed_notes"),
                    DeployedAt      = SafeDate(reader, "deployed_at"),
                    CreatedAt       = SafeDate(reader, "created_at") ?? "",
                });
            }
            return records;
        }

        // ── Safely reads a VARCHAR/TEXT column ──
        private string SafeString(MySqlDataReader reader, string col)
        {
            int ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? "" : reader.GetString(ord);
        }

        // ── Safely reads a DATE/TIMESTAMP column as string ──
        // MySqlConnector returns DATE as DateTime — cannot use GetString()
        private string? SafeDate(MySqlDataReader reader, string col)
        {
            int ord = reader.GetOrdinal(col);
            if (reader.IsDBNull(ord)) return null;
            return reader.GetDateTime(ord).ToString("yyyy-MM-dd");
        }

        // Replaces PHP logHistory() function
        private void LogHistory(MySqlConnection conn, int resignedId,
            string action, string details, string snapshot)
        {
            try
            {
                using var cmd = new MySqlCommand(@"
                    INSERT INTO asset_history (resigned_id, action, actor, details, snapshot)
                    VALUES (@rid, @action, @actor, @details, @snapshot)", conn);
                cmd.Parameters.AddWithValue("@rid",      resignedId);
                cmd.Parameters.AddWithValue("@action",   action);
                cmd.Parameters.AddWithValue("@actor",    CurrentUser);
                cmd.Parameters.AddWithValue("@details",  details);
                cmd.Parameters.AddWithValue("@snapshot", snapshot);
                cmd.ExecuteNonQuery();
            }
            catch { /* silently skip if table missing */ }
        }

        // Replaces PHP makeSnapshot() function
        private string MakeSnapshot(MySqlConnection conn, int id)
        {
            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT * FROM resigned WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var dict = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    return System.Text.Json.JsonSerializer.Serialize(dict);
                }
            }
            catch { }
            return "";
        }
    }
}
