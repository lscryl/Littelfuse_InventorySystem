// ============================================================
// File: Controllers/DevicesController.cs
// Purpose: Handles all actions from devices.php:
//   - Display Inventory (available) and Borrowed tabs
//   - Add / Edit / Delete devices
//   - Borrow / Edit Loan / Delete Loan
//   - Import devices from CSV JSON
//   - QR scan borrow flow (?borrow=ID)
// ============================================================

using Microsoft.AspNetCore.Mvc;
using ITInventorySystem.Data;
using ITInventorySystem.Models;
using MySqlConnector;
using System.Text.Json;

namespace ITInventorySystem.Controllers
{
    public class DevicesController : Controller
    {
        private readonly DbHelper _db;

        public DevicesController(DbHelper db)
        {
            _db = db;
        }

        // ── Auth guard ────────────────────────────────────────────
        private bool IsLoggedIn()
            => HttpContext.Session.GetString("logged_in") == "true";

        // ==========================================================
        // GET: /Devices/Index?tab=inventory&borrow=0
        // ==========================================================
        public IActionResult Index(string tab = "inventory", int borrow = 0)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (!new[] { "inventory", "borrowed" }.Contains(tab))
                tab = "inventory";

            using var conn = _db.GetConnection();
            EnsureTables(conn);

            var vm = new DevicesViewModel { ActiveTab = tab };

            // ── Counts ───────────────────────────────────────────
            vm.CountAvailable = GetCount(conn, @"
                SELECT COUNT(*) FROM devices d
                LEFT JOIN device_loans l ON l.device_id = d.id AND l.status='Active'
                WHERE l.id IS NULL");

            vm.CountBorrowed = GetCount(conn,
                "SELECT COUNT(*) FROM device_loans WHERE status='Active'");

            vm.CountOverdue = GetCount(conn, @"
                SELECT COUNT(*) FROM device_loans
                WHERE status='Active'
                  AND expected_return IS NOT NULL
                  AND expected_return < CURDATE()");

            // ── Inventory (all devices + their active loan info) ─
            vm.Devices = FetchDevices(conn);

            // ── Active loans ──────────────────────────────────────
            vm.Loans = FetchLoans(conn);

            // ── QR borrow trigger ─────────────────────────────────
            if (borrow > 0)
                vm.QrBorrowDevice = FetchDeviceById(conn, borrow);

            // ── Toast ─────────────────────────────────────────────
            vm.ToastMessage = HttpContext.Session.GetString("dev_msg")        ?? "";
            vm.ToastType    = HttpContext.Session.GetString("dev_msg_type")   ?? "success";
            HttpContext.Session.Remove("dev_msg");
            HttpContext.Session.Remove("dev_msg_type");

            return View(vm);
        }

        // ==========================================================
        // POST: /Devices/AddDevice
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddDevice(string device_name, string serial_number,
            string model, string department, string os_name,
            string release_id, string pic)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(device_name))
            {
                SetToast("Device name is required.", "error");
                return RedirectToAction("Index", new { tab = "inventory" });
            }

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(@"
                INSERT INTO devices
                    (device_name, serial_number, model, department, os_name, release_id, pic)
                VALUES
                    (@name, @serial, @model, @dept, @os, @rid, @pic)", conn);

            cmd.Parameters.AddWithValue("@name",   device_name.Trim());
            cmd.Parameters.AddWithValue("@serial", serial_number?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@model",  model?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@dept",   department?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@os",     os_name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@rid",    release_id?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@pic",    pic?.Trim() ?? "");
            cmd.ExecuteNonQuery();

            SetToast($"Device \"{device_name.Trim()}\" added.", "success");
            return RedirectToAction("Index", new { tab = "inventory" });
        }

        // ==========================================================
        // POST: /Devices/EditDevice
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditDevice(int id, string device_name, string serial_number,
            string model, string department, string os_name,
            string release_id, string pic)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(@"
                UPDATE devices
                SET device_name=@name, serial_number=@serial, model=@model,
                    department=@dept, os_name=@os, release_id=@rid, pic=@pic
                WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@name",   device_name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@serial", serial_number?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@model",  model?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@dept",   department?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@os",     os_name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@rid",    release_id?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@pic",    pic?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@id",     id);
            cmd.ExecuteNonQuery();

            SetToast("Device updated.", "success");
            return RedirectToAction("Index", new { tab = "inventory" });
        }

        // ==========================================================
        // POST: /Devices/DeleteDevice
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteDevice(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand("DELETE FROM devices WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            SetToast("Device deleted.", "success");
            return RedirectToAction("Index", new { tab = "inventory" });
        }

        // ==========================================================
        // POST: /Devices/BorrowDevice
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BorrowDevice(int device_id, string borrower_name,
            string department, string date_borrowed, string expected_return)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(borrower_name) || string.IsNullOrWhiteSpace(date_borrowed))
            {
                SetToast("Borrower name and date are required.", "error");
                return RedirectToAction("Index", new { tab = "inventory" });
            }

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(@"
                INSERT INTO device_loans
                    (device_id, borrower_name, department, date_borrowed, expected_return, status)
                VALUES
                    (@did, @name, @dept, @date, @exp, 'Active')", conn);

            cmd.Parameters.AddWithValue("@did",  device_id);
            cmd.Parameters.AddWithValue("@name", borrower_name.Trim());
            cmd.Parameters.AddWithValue("@dept", department?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@date", date_borrowed);
            cmd.Parameters.AddWithValue("@exp",
                string.IsNullOrWhiteSpace(expected_return) ? (object)DBNull.Value : expected_return);
            cmd.ExecuteNonQuery();

            SetToast($"Device borrowed by {borrower_name.Trim()}.", "success");
            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Devices/AddAndBorrow
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddAndBorrow(string device_name, string serial_number,
            string model, string os_name, string release_id, string pic,
            string borrower_name, string department,
            string date_borrowed, string expected_return)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(device_name) || string.IsNullOrWhiteSpace(borrower_name)
                || string.IsNullOrWhiteSpace(date_borrowed))
            {
                SetToast("Device name, borrower name and date borrowed are required.", "error");
                return RedirectToAction("Index", new { tab = "borrowed" });
            }

            using var conn = _db.GetConnection();

            // Step 1 — insert device
            using var addCmd = new MySqlCommand(@"
                INSERT INTO devices
                    (device_name, serial_number, model, os_name, release_id, pic)
                VALUES
                    (@name, @serial, @model, @os, @rid, @pic)", conn);

            addCmd.Parameters.AddWithValue("@name",   device_name.Trim());
            addCmd.Parameters.AddWithValue("@serial", serial_number?.Trim() ?? "");
            addCmd.Parameters.AddWithValue("@model",  model?.Trim() ?? "");
            addCmd.Parameters.AddWithValue("@os",     os_name?.Trim() ?? "");
            addCmd.Parameters.AddWithValue("@rid",    release_id?.Trim() ?? "");
            addCmd.Parameters.AddWithValue("@pic",    pic?.Trim() ?? "");
            addCmd.ExecuteNonQuery();

            // Step 2 — get the new device id
            long newDeviceId = addCmd.LastInsertedId;

            // Step 3 — insert loan
            using var loanCmd = new MySqlCommand(@"
                INSERT INTO device_loans
                    (device_id, borrower_name, department, date_borrowed, expected_return, status)
                VALUES
                    (@did, @bname, @dept, @date, @exp, 'Active')", conn);

            loanCmd.Parameters.AddWithValue("@did",   newDeviceId);
            loanCmd.Parameters.AddWithValue("@bname", borrower_name.Trim());
            loanCmd.Parameters.AddWithValue("@dept",  department?.Trim() ?? "");
            loanCmd.Parameters.AddWithValue("@date",  date_borrowed);
            loanCmd.Parameters.AddWithValue("@exp",
                string.IsNullOrWhiteSpace(expected_return) ? (object)DBNull.Value : expected_return);
            loanCmd.ExecuteNonQuery();

            SetToast($"\"{device_name.Trim()}\" added and borrowed by {borrower_name.Trim()}.", "success");
            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Devices/EditLoan
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditLoan(int id, string borrower_name, string department,
            string date_borrowed, string expected_return, string status, string date_returned)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(@"
                UPDATE device_loans
                SET borrower_name=@name, department=@dept,
                    date_borrowed=@date, expected_return=@exp,
                    status=@status, date_returned=@dr
                WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@name",   borrower_name?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@dept",   department?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@date",   date_borrowed);
            cmd.Parameters.AddWithValue("@exp",
                string.IsNullOrWhiteSpace(expected_return) ? (object)DBNull.Value : expected_return);
            cmd.Parameters.AddWithValue("@status", status ?? "Active");
            cmd.Parameters.AddWithValue("@dr",
                string.IsNullOrWhiteSpace(date_returned) ? (object)DBNull.Value : date_returned);
            cmd.Parameters.AddWithValue("@id",     id);
            cmd.ExecuteNonQuery();

            SetToast("Loan updated.", "success");
            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Devices/DeleteLoan
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteLoan(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand("DELETE FROM device_loans WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            SetToast("Loan record deleted.", "success");
            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Devices/ImportDevices
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ImportDevices(string rows_json)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            int imported = 0;
            try
            {
                var rows = JsonSerializer.Deserialize<List<DeviceImportRow>>(rows_json ?? "[]")
                           ?? new List<DeviceImportRow>();

                using var conn = _db.GetConnection();
                foreach (var r in rows)
                {
                    var name = r.device_name?.Trim() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    using var cmd = new MySqlCommand(@"
                        INSERT INTO devices (device_name, serial_number, model)
                        VALUES (@name, @serial, @model)", conn);
                    cmd.Parameters.AddWithValue("@name",   name);
                    cmd.Parameters.AddWithValue("@serial", r.serial_number?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@model",  r.model?.Trim() ?? "");
                    cmd.ExecuteNonQuery();
                    imported++;
                }
            }
            catch { /* malformed JSON — imported stays 0 */ }

            SetToast($"{imported} device(s) imported successfully.", "success");
            return RedirectToAction("Index", new { tab = "inventory" });
        }

        // ==========================================================
// POST: /Devices/ImportLoans
// ==========================================================
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult ImportLoans(string rows_json)
{
    if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

    int imported = 0;
    try
    {
        var rows = JsonSerializer.Deserialize<List<LoanImportRow>>(rows_json ?? "[]")
                   ?? new List<LoanImportRow>();

        using var conn = _db.GetConnection();
        foreach (var r in rows)
        {
            var deviceName = r.device_name?.Trim() ?? "";
            var borrower   = r.borrower_name?.Trim() ?? "";
            if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(borrower)) continue;

            // Find device by name
            using var findCmd = new MySqlCommand(
                "SELECT id FROM devices WHERE device_name = @name LIMIT 1", conn);
            findCmd.Parameters.AddWithValue("@name", deviceName);
            var deviceId = findCmd.ExecuteScalar();
            if (deviceId == null) continue;

            using var cmd = new MySqlCommand(@"
                INSERT INTO device_loans
                    (device_id, borrower_name, department, date_borrowed, expected_return, status)
                VALUES
                    (@did, @name, @dept, @date, @exp, 'Active')", conn);

            cmd.Parameters.AddWithValue("@did",  deviceId);
            cmd.Parameters.AddWithValue("@name", borrower);
            cmd.Parameters.AddWithValue("@dept", r.department?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@date",
                string.IsNullOrWhiteSpace(r.date_borrowed)
                    ? DateTime.Today.ToString("yyyy-MM-dd")
                    : r.date_borrowed.Trim());
            cmd.Parameters.AddWithValue("@exp",
                string.IsNullOrWhiteSpace(r.expected_return)
                    ? (object)DBNull.Value
                    : r.expected_return.Trim());
            cmd.ExecuteNonQuery();
            imported++;
        }
    }
    catch { /* malformed JSON */ }

    SetToast($"{imported} loan(s) imported successfully.", "success");
    return RedirectToAction("Index", new { tab = "borrowed" });
}

        // ==========================================================
        // PRIVATE HELPERS
        // ==========================================================

        private void EnsureTables(MySqlConnection conn)
        {
            // Create devices table
            new MySqlCommand(@"
                CREATE TABLE IF NOT EXISTS devices (
                    id            INT AUTO_INCREMENT PRIMARY KEY,
                    device_name   VARCHAR(150) NOT NULL,
                    serial_number VARCHAR(150) DEFAULT '',
                    model         VARCHAR(150) DEFAULT '',
                    qr_code       VARCHAR(500) DEFAULT NULL,
                    department    VARCHAR(150) DEFAULT '',
                    os_name       VARCHAR(150) DEFAULT '',
                    release_id    VARCHAR(100) DEFAULT '',
                    pic           VARCHAR(150) DEFAULT '',
                    created_at    TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
                )", conn).ExecuteNonQuery();

            // Create device_loans table
            new MySqlCommand(@"
                CREATE TABLE IF NOT EXISTS device_loans (
                    id              INT AUTO_INCREMENT PRIMARY KEY,
                    device_id       INT          NOT NULL,
                    borrower_name   VARCHAR(100) NOT NULL,
                    department      VARCHAR(100) DEFAULT '',
                    date_borrowed   DATE         NOT NULL,
                    expected_return DATE         DEFAULT NULL,
                    status          VARCHAR(20)  DEFAULT 'Active',
                    date_returned   DATE         DEFAULT NULL,
                    created_at      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
                )", conn).ExecuteNonQuery();
        }

        private int GetCount(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private List<Device> FetchDevices(MySqlConnection conn)
        {
            var list = new List<Device>();
            using var cmd = new MySqlCommand(@"
                SELECT d.id,
                       d.device_name, d.serial_number, d.model,
                       d.department, d.os_name, d.release_id, d.pic,
                       d.qr_code,
                       DATE_FORMAT(d.created_at, '%Y-%m-%d') AS created_at,
                       l.borrower_name,
                       l.department   AS loan_dept,
                       DATE_FORMAT(l.date_borrowed,   '%Y-%m-%d') AS date_borrowed,
                       DATE_FORMAT(l.expected_return, '%Y-%m-%d') AS expected_return
                FROM devices d
                LEFT JOIN device_loans l
                       ON l.device_id = d.id AND l.status = 'Active'
                WHERE l.id IS NULL
                ORDER BY d.id DESC", conn);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Device
                {
                    Id            = r.GetInt32("id"),
                    DeviceName    = r.GetString("device_name"),
                    SerialNumber  = r.IsDBNull(r.GetOrdinal("serial_number")) ? "" : r.GetString("serial_number"),
                    Model         = r.IsDBNull(r.GetOrdinal("model"))         ? "" : r.GetString("model"),
                    Department    = r.IsDBNull(r.GetOrdinal("department"))    ? "" : r.GetString("department"),
                    OsName        = r.IsDBNull(r.GetOrdinal("os_name"))       ? "" : r.GetString("os_name"),
                    ReleaseId     = r.IsDBNull(r.GetOrdinal("release_id"))    ? "" : r.GetString("release_id"),
                    Pic           = r.IsDBNull(r.GetOrdinal("pic"))           ? "" : r.GetString("pic"),
                    QrCode        = r.IsDBNull(r.GetOrdinal("qr_code"))       ? null : r.GetString("qr_code"),
                    CreatedAt     = r.IsDBNull(r.GetOrdinal("created_at"))    ? "" : r.GetString("created_at"),
                    BorrowerName  = r.IsDBNull(r.GetOrdinal("borrower_name")) ? null : r.GetString("borrower_name"),
                    LoanDept      = r.IsDBNull(r.GetOrdinal("loan_dept"))     ? null : r.GetString("loan_dept"),
                    DateBorrowed  = r.IsDBNull(r.GetOrdinal("date_borrowed")) ? null : r.GetString("date_borrowed"),
                    ExpectedReturn = r.IsDBNull(r.GetOrdinal("expected_return")) ? null : r.GetString("expected_return"),
                });
            }
            return list;
        }

        private List<DeviceLoan> FetchLoans(MySqlConnection conn)
        {
            var list = new List<DeviceLoan>();
            using var cmd = new MySqlCommand(@"
                SELECT l.id, l.device_id,
                       d.device_name, d.serial_number, COALESCE(d.model,'') AS model,
                       l.borrower_name, l.department,
                       DATE_FORMAT(l.date_borrowed,   '%Y-%m-%d') AS date_borrowed,
                       DATE_FORMAT(l.expected_return, '%Y-%m-%d') AS expected_return,
                       l.status,
                       DATE_FORMAT(l.date_returned,   '%Y-%m-%d') AS date_returned
                FROM device_loans l
                INNER JOIN devices d ON d.id = l.device_id
                WHERE l.status = 'Active'
                ORDER BY l.created_at DESC", conn);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new DeviceLoan
                {
                    Id             = r.GetInt32("id"),
                    DeviceId       = r.GetInt32("device_id"),
                    DeviceName     = r.GetString("device_name"),
                    SerialNumber   = r.IsDBNull(r.GetOrdinal("serial_number"))   ? "" : r.GetString("serial_number"),
                    Model          = r.GetString("model"),
                    BorrowerName   = r.GetString("borrower_name"),
                    Department     = r.IsDBNull(r.GetOrdinal("department"))      ? "" : r.GetString("department"),
                    DateBorrowed   = r.GetString("date_borrowed"),
                    ExpectedReturn = r.IsDBNull(r.GetOrdinal("expected_return")) ? null : r.GetString("expected_return"),
                    Status         = r.GetString("status"),
                    DateReturned   = r.IsDBNull(r.GetOrdinal("date_returned"))   ? null : r.GetString("date_returned"),
                });
            }
            return list;
        }

        private Device? FetchDeviceById(MySqlConnection conn, int id)
        {
            using var cmd = new MySqlCommand(@"
                SELECT d.*,
                       DATE_FORMAT(d.created_at, '%Y-%m-%d') AS created_fmt,
                       CASE WHEN l.id IS NOT NULL THEN 1 ELSE 0 END AS is_borrowed
                FROM devices d
                LEFT JOIN device_loans l ON l.device_id = d.id AND l.status='Active'
                WHERE d.id = @id LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new Device
            {
                Id           = r.GetInt32("id"),
                DeviceName   = r.GetString("device_name"),
                SerialNumber = r.IsDBNull(r.GetOrdinal("serial_number")) ? "" : r.GetString("serial_number"),
                Model        = r.IsDBNull(r.GetOrdinal("model"))         ? "" : r.GetString("model"),
            };
        }

        private void SetToast(string message, string type = "success")
        {
            HttpContext.Session.SetString("dev_msg",      message);
            HttpContext.Session.SetString("dev_msg_type", type);
        }
    }
}
