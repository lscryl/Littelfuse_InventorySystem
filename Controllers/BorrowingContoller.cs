// ============================================================
// File: Controllers/BorrowingController.cs
// Purpose: Handles all actions from borrowing.php:
//   - Display Borrowed / Returned / Deployed tabs
//   - Add / Edit / Delete borrowings
//   - Add / Edit / Delete deployments
//   - Return single item or return all items
// All PHP $_POST action checks are now separate [HttpPost] methods.
// ============================================================

using Microsoft.AspNetCore.Mvc;
using ITInventorySystem.Data;
using ITInventorySystem.Models;
using MySqlConnector;

namespace ITInventorySystem.Controllers
{
    public class BorrowingController : Controller
    {
        private readonly DbHelper _db;

        public BorrowingController(DbHelper db)
        {
            _db = db;
        }

        // ── Auth guard helper ──────────────────────────────────────
        // Replaces PHP: if (empty($_SESSION['logged_in'])) header('Location: login.php');
        private bool IsLoggedIn()
            => HttpContext.Session.GetString("logged_in") == "true";

        // ==========================================================
        // GET: /Borrowing/Index?tab=borrowed&search=
        // Replaces PHP: the entire data-fetch block at bottom of borrowing.php
        // ==========================================================
        public IActionResult Index(string tab = "borrowed", string search = "")
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            // Validate tab value — same as PHP in_array check
            if (!new[] { "borrowed", "returned", "deployed" }.Contains(tab))
                tab = "borrowed";

            var vm = new BorrowingViewModel
            {
                ActiveTab = tab,
                Search    = search
            };

            using var conn = _db.GetConnection();

            // ── Tab counts ──────────────────────────────────────────
            // Replaces PHP: $count_borrowed = $conn->query("SELECT COUNT(*) ...")->fetch_row()[0];
            vm.CountBorrowed = GetCount(conn, "SELECT COUNT(*) FROM borrowings WHERE status = 'Borrowed'");
            vm.CountReturned = GetCount(conn, "SELECT COUNT(*) FROM borrowings WHERE status = 'Returned'");
            vm.CountDeployed = GetCount(conn, "SELECT COUNT(*) FROM deployments");

            // ── Fetch data based on active tab ──────────────────────
            if (tab == "deployed")
            {
                vm.Deployments = FetchDeployments(conn, search);
            }
            else
            {
                vm.Borrowings = FetchBorrowings(conn, tab, search);
            }

            // ── Toast from session ──────────────────────────────────
            // Replaces PHP: $_SESSION['last_insert'] toast logic
            vm.ToastMessage = HttpContext.Session.GetString("toast_message") ?? "";
            vm.ToastType    = HttpContext.Session.GetString("toast_type")    ?? "";
            HttpContext.Session.Remove("toast_message");
            HttpContext.Session.Remove("toast_type");

            return View(vm);
        }

        // ==========================================================
        // POST: /Borrowing/AddBorrow
        // Replaces PHP: action === 'add_borrow'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddBorrow(string borrower_name, string serial_number,
            string department, string date_borrowed, string status,
            List<string> items, List<int> items_qty)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(borrower_name) || string.IsNullOrWhiteSpace(date_borrowed))
            {
                SetToast("Missing required fields.", "error");
                return RedirectToAction("Index", new { tab = "borrowed" });
            }

            using var conn = _db.GetConnection();

            // Check for existing active borrow for same person + department
            // Replaces PHP: $check = $conn->prepare("SELECT id FROM borrowings WHERE ...")
            int? borrowId = null;
            using (var cmd = new MySqlCommand(
                "SELECT id FROM borrowings WHERE borrower_name=@name AND department=@dept AND status='Borrowed' ORDER BY created_at DESC LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@name", borrower_name);
                cmd.Parameters.AddWithValue("@dept", department ?? "");
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    borrowId = Convert.ToInt32(result);
            }

            // If no existing entry, create a new borrowings row
            if (borrowId == null)
            {
                using var cmd = new MySqlCommand(
                    "INSERT INTO borrowings (serial_number, borrower_name, department, date_borrowed, status) VALUES (@serial, @name, @dept, @date, @status)", conn);
                cmd.Parameters.AddWithValue("@serial", serial_number ?? "");
                cmd.Parameters.AddWithValue("@name",   borrower_name);
                cmd.Parameters.AddWithValue("@dept",   department ?? "");
                cmd.Parameters.AddWithValue("@date",   date_borrowed);
                cmd.Parameters.AddWithValue("@status", status ?? "Borrowed");
                cmd.ExecuteNonQuery();
                borrowId = (int)cmd.LastInsertedId;
            }

            // Insert each item into borrow_items
            // Replaces PHP: foreach ($items as $i => $item_name)
            for (int i = 0; i < items.Count; i++)
            {
                var itemName = items[i]?.Trim();
                if (string.IsNullOrEmpty(itemName)) continue;
                int qty = (items_qty != null && i < items_qty.Count) ? Math.Max(1, items_qty[i]) : 1;

                using var cmd2 = new MySqlCommand(
                    "INSERT INTO borrow_items (borrow_id, item_name, quantity, status) VALUES (@bid, @item, @qty, 'Borrowed')", conn);
                cmd2.Parameters.AddWithValue("@bid",  borrowId);
                cmd2.Parameters.AddWithValue("@item", itemName);
                cmd2.Parameters.AddWithValue("@qty",  qty);
                cmd2.ExecuteNonQuery();
            }

            SetToast($"New borrow entry created for: {borrower_name}", "success");
            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Borrowing/EditBorrow
        // Replaces PHP: action === 'edit_borrow'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditBorrow(int id, string borrower_name, string serial_number,
            string department, string date_borrowed, string status,
            List<string> items, List<int> items_qty)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(borrower_name) || string.IsNullOrWhiteSpace(date_borrowed))
            {
                SetToast("Missing required fields.", "error");
                return RedirectToAction("Index", new { tab = "borrowed" });
            }

            string redirectTab = (status == "Returned") ? "returned" : "borrowed";
            string? dateReturnedVal = (status == "Returned") ? DateTime.Today.ToString("yyyy-MM-dd") : null;

            using var conn = _db.GetConnection();

            // Update the borrowings row
            using (var cmd = new MySqlCommand(
                "UPDATE borrowings SET serial_number=@serial, borrower_name=@name, department=@dept, date_borrowed=@date, status=@status, date_returned=@dr WHERE id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@serial", serial_number ?? "");
                cmd.Parameters.AddWithValue("@name",   borrower_name);
                cmd.Parameters.AddWithValue("@dept",   department ?? "");
                cmd.Parameters.AddWithValue("@date",   date_borrowed);
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@dr",     (object?)dateReturnedVal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id",     id);
                cmd.ExecuteNonQuery();
            }

            // Delete old items and re-insert (replaces PHP DELETE + foreach)
            using (var del = new MySqlCommand("DELETE FROM borrow_items WHERE borrow_id=@id", conn))
            {
                del.Parameters.AddWithValue("@id", id);
                del.ExecuteNonQuery();
            }

            string itemStatus = (status == "Returned") ? "Returned" : "Borrowed";
            for (int i = 0; i < items.Count; i++)
            {
                var itemName = items[i]?.Trim();
                if (string.IsNullOrEmpty(itemName)) continue;
                int qty = (items_qty != null && i < items_qty.Count) ? Math.Max(1, items_qty[i]) : 1;

                using var cmd2 = new MySqlCommand(
                    "INSERT INTO borrow_items (borrow_id, item_name, quantity, status) VALUES (@bid, @item, @qty, @st)", conn);
                cmd2.Parameters.AddWithValue("@bid",  id);
                cmd2.Parameters.AddWithValue("@item", itemName);
                cmd2.Parameters.AddWithValue("@qty",  qty);
                cmd2.Parameters.AddWithValue("@st",   itemStatus);
                cmd2.ExecuteNonQuery();
            }

            SetToast($"Entry updated for: {borrower_name}", "success");
            return RedirectToAction("Index", new { tab = redirectTab });
        }

        // ==========================================================
        // POST: /Borrowing/DeleteBorrow
        // Replaces PHP: action === 'delete_borrow'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBorrow(int id, string current_tab = "borrowed")
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand("DELETE FROM borrowings WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            SetToast("Borrowing record deleted.", "success");
            return RedirectToAction("Index", new { tab = current_tab });
        }

        // ==========================================================
        // POST: /Borrowing/ReturnAll
        // Replaces PHP: action === 'return_all'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnAll(int borrow_id, string? received_by)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            string today = DateTime.Today.ToString("yyyy-MM-dd");
            using var conn = _db.GetConnection();

            // Mark all items as returned
            using (var cmd = new MySqlCommand(
                "UPDATE borrow_items SET status='Returned' WHERE borrow_id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", borrow_id);
                cmd.ExecuteNonQuery();
            }

            // Mark borrowing as returned
            using (var cmd = new MySqlCommand(
                "UPDATE borrowings SET status='Returned', date_returned=@dr, received_by=@rb WHERE id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@dr", today);
                cmd.Parameters.AddWithValue("@rb", received_by ?? "");
                cmd.Parameters.AddWithValue("@id", borrow_id);
                cmd.ExecuteNonQuery();
            }

            SetToast("Entry marked as returned.", "success");
            return RedirectToAction("Index", new { tab = "returned" });
        }

        // ==========================================================
        // POST: /Borrowing/ReturnItem
        // Replaces PHP: action === 'return_item'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnItem(int item_id, int borrow_id, string? received_by)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();

            // Mark the selected item as returned
            using (var cmd = new MySqlCommand(
                "UPDATE borrow_items SET status='Returned' WHERE id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", item_id);
                cmd.ExecuteNonQuery();
            }

            // Check how many items are still Borrowed
            // Replaces PHP: $check = $conn->prepare("SELECT COUNT(*) FROM borrow_items WHERE borrow_id=? AND status='Borrowed'")
            int remaining = 0;
            using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM borrow_items WHERE borrow_id=@bid AND status='Borrowed'", conn))
            {
                cmd.Parameters.AddWithValue("@bid", borrow_id);
                remaining = Convert.ToInt32(cmd.ExecuteScalar());
            }

            string today = DateTime.Today.ToString("yyyy-MM-dd");

            if (remaining == 0)
            {
                // All items returned — mark borrowing as Returned
                using var cmd = new MySqlCommand(
                    "UPDATE borrowings SET status='Returned', date_returned=@dr, received_by=@rb WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@dr", today);
                cmd.Parameters.AddWithValue("@rb", received_by ?? "");
                cmd.Parameters.AddWithValue("@id", borrow_id);
                cmd.ExecuteNonQuery();

                SetToast("All items returned — entry moved to Returned tab.", "success");
                return RedirectToAction("Index", new { tab = "returned" });
            }
            else
            {
                // Some items still out — just update received_by
                using var cmd = new MySqlCommand(
                    "UPDATE borrowings SET received_by=@rb WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@rb", received_by ?? "");
                cmd.Parameters.AddWithValue("@id", borrow_id);
                cmd.ExecuteNonQuery();
            }

            return RedirectToAction("Index", new { tab = "borrowed" });
        }

        // ==========================================================
        // POST: /Borrowing/AddDeployment
        // Replaces PHP: action === 'add_deployment'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddDeployment(string user_name, string serial_number,
            string department, string device_name, string date_deployed, string deployed_by)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(user_name) ||
                string.IsNullOrWhiteSpace(device_name) ||
                string.IsNullOrWhiteSpace(date_deployed))
            {
                SetToast("Missing required fields.", "error");
                return RedirectToAction("Index", new { tab = "deployed" });
            }

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(
                "INSERT INTO deployments (serial_number, user_name, department, device_name, date_deployed, deployed_by, status) VALUES (@serial, @name, @dept, @device, @date, @by, 'Deployed')", conn);
            cmd.Parameters.AddWithValue("@serial", serial_number ?? "");
            cmd.Parameters.AddWithValue("@name",   user_name);
            cmd.Parameters.AddWithValue("@dept",   department ?? "");
            cmd.Parameters.AddWithValue("@device", device_name);
            cmd.Parameters.AddWithValue("@date",   date_deployed);
            cmd.Parameters.AddWithValue("@by",     deployed_by ?? "");
            cmd.ExecuteNonQuery();

            SetToast($"New deployment created for: {user_name}", "success");
            return RedirectToAction("Index", new { tab = "deployed" });
        }

        // ==========================================================
        // POST: /Borrowing/EditDeployment
        // Replaces PHP: action === 'edit_deployment'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditDeployment(int id, string user_name, string serial_number,
            string department, string device_name, string date_deployed, string deployed_by)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(user_name) ||
                string.IsNullOrWhiteSpace(device_name) ||
                string.IsNullOrWhiteSpace(date_deployed))
            {
                SetToast("Missing required fields.", "error");
                return RedirectToAction("Index", new { tab = "deployed" });
            }

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand(
                "UPDATE deployments SET serial_number=@serial, user_name=@name, department=@dept, device_name=@device, date_deployed=@date, deployed_by=@by WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@serial", serial_number ?? "");
            cmd.Parameters.AddWithValue("@name",   user_name);
            cmd.Parameters.AddWithValue("@dept",   department ?? "");
            cmd.Parameters.AddWithValue("@device", device_name);
            cmd.Parameters.AddWithValue("@date",   date_deployed);
            cmd.Parameters.AddWithValue("@by",     deployed_by ?? "");
            cmd.Parameters.AddWithValue("@id",     id);
            cmd.ExecuteNonQuery();

            SetToast($"Deployment updated for: {user_name}", "success");
            return RedirectToAction("Index", new { tab = "deployed" });
        }

// ==========================================================
// POST: /Borrowing/ImportBorrowed
// ==========================================================
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult ImportBorrowed(string rowsJson, string tab)
{
    if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

    var rows = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rowsJson);
    int count = 0;

    using var conn = _db.GetConnection();

    foreach (var r in rows ?? [])
    {
        var borrowerName = r.GetValueOrDefault("borrower_name", "").Trim();
        var department   = r.GetValueOrDefault("department", "").Trim();
        var serial       = r.GetValueOrDefault("serial_number", "").Trim();
        var dateBorrowed = r.GetValueOrDefault("date_borrowed", "").Trim();
        var status       = r.GetValueOrDefault("status", "Borrowed").Trim();
        var itemsRaw     = r.GetValueOrDefault("items", "").Trim();

        if (string.IsNullOrWhiteSpace(borrowerName) || string.IsNullOrWhiteSpace(dateBorrowed))
            continue;

        if (!new[] { "Borrowed", "Returned", "Overdue" }.Contains(status))
            status = "Borrowed";

        if (!DateTime.TryParse(dateBorrowed, out var parsedDate))
            continue;

        string? dateReturnedVal = (status == "Returned")
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : null;

        int borrowId;
        using (var cmd = new MySqlCommand(
            @"INSERT INTO borrowings
                (serial_number, borrower_name, department, date_borrowed, status, date_returned)
              VALUES (@serial, @name, @dept, @date, @status, @dr)",
            conn))
        {
            cmd.Parameters.AddWithValue("@serial", serial);
            cmd.Parameters.AddWithValue("@name",   borrowerName);
            cmd.Parameters.AddWithValue("@dept",   department);
            cmd.Parameters.AddWithValue("@date",   parsedDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@dr",     (object?)dateReturnedVal ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            borrowId = (int)cmd.LastInsertedId;
        }

        string itemStatus = (status == "Returned") ? "Returned" : "Borrowed";
        foreach (var segment in itemsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = segment.Trim();
            if (string.IsNullOrEmpty(part)) continue;

            string itemName;
            int qty = 1;
            var xIdx = part.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (xIdx > 0 && int.TryParse(part[(xIdx + 2)..].Trim(), out var parsedQty))
            {
                itemName = part[..xIdx].Trim();
                qty      = Math.Max(1, parsedQty);
            }
            else
            {
                itemName = part;
            }

            if (string.IsNullOrWhiteSpace(itemName)) continue;

            using var cmd2 = new MySqlCommand(
                "INSERT INTO borrow_items (borrow_id, item_name, quantity, status) VALUES (@bid, @item, @qty, @st)",
                conn);
            cmd2.Parameters.AddWithValue("@bid",  borrowId);
            cmd2.Parameters.AddWithValue("@item", itemName);
            cmd2.Parameters.AddWithValue("@qty",  qty);
            cmd2.Parameters.AddWithValue("@st",   itemStatus);
            cmd2.ExecuteNonQuery();
        }

        count++;
    }

    SetToast($"{count} borrowed record(s) imported.", "success");
    return RedirectToAction("Index", new { tab = "borrowed" });
}

// ==========================================================
// POST: /Borrowing/ImportReturned
// ==========================================================
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult ImportReturned(string rowsJson, string tab)
{
    if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

    var rows = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rowsJson);
    int count = 0;

    using var conn = _db.GetConnection();

    foreach (var r in rows ?? [])
    {
        var borrowerName = r.GetValueOrDefault("borrower_name", "").Trim();
        var department   = r.GetValueOrDefault("department", "").Trim();
        var serial       = r.GetValueOrDefault("serial_number", "").Trim();
        var dateBorrowed = r.GetValueOrDefault("date_borrowed", "").Trim();
        var dateReturned = r.GetValueOrDefault("date_returned", "").Trim();
        var receivedBy   = r.GetValueOrDefault("received_by", "").Trim();
        var itemsRaw     = r.GetValueOrDefault("items", "").Trim();

        if (string.IsNullOrWhiteSpace(borrowerName) || string.IsNullOrWhiteSpace(dateBorrowed))
            continue;

        if (!DateTime.TryParse(dateBorrowed, out var parsedBorrow))
            continue;

        string resolvedDateReturned = DateTime.TryParse(dateReturned, out var parsedReturn)
            ? parsedReturn.ToString("yyyy-MM-dd")
            : DateTime.Today.ToString("yyyy-MM-dd");

        int borrowId;
        using (var cmd = new MySqlCommand(
            @"INSERT INTO borrowings
                (serial_number, borrower_name, department, date_borrowed, status, date_returned, received_by)
              VALUES (@serial, @name, @dept, @date, 'Returned', @dr, @rb)",
            conn))
        {
            cmd.Parameters.AddWithValue("@serial", serial);
            cmd.Parameters.AddWithValue("@name",   borrowerName);
            cmd.Parameters.AddWithValue("@dept",   department);
            cmd.Parameters.AddWithValue("@date",   parsedBorrow.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@dr",     resolvedDateReturned);
            cmd.Parameters.AddWithValue("@rb",     receivedBy);
            cmd.ExecuteNonQuery();
            borrowId = (int)cmd.LastInsertedId;
        }

        foreach (var segment in itemsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = segment.Trim();
            if (string.IsNullOrEmpty(part)) continue;

            string itemName;
            int qty = 1;
            var xIdx = part.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (xIdx > 0 && int.TryParse(part[(xIdx + 2)..].Trim(), out var parsedQty))
            {
                itemName = part[..xIdx].Trim();
                qty      = Math.Max(1, parsedQty);
            }
            else
            {
                itemName = part;
            }

            if (string.IsNullOrWhiteSpace(itemName)) continue;

            using var cmd2 = new MySqlCommand(
                "INSERT INTO borrow_items (borrow_id, item_name, quantity, status) VALUES (@bid, @item, @qty, 'Returned')",
                conn);
            cmd2.Parameters.AddWithValue("@bid",  borrowId);
            cmd2.Parameters.AddWithValue("@item", itemName);
            cmd2.Parameters.AddWithValue("@qty",  qty);
            cmd2.ExecuteNonQuery();
        }

        count++;
    }

    SetToast($"{count} returned record(s) imported.", "success");
    return RedirectToAction("Index", new { tab = "returned" });
}

// ==========================================================
// POST: /Borrowing/ImportDeployed
// ==========================================================
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult ImportDeployed(string rowsJson, string tab)
{
    if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

    var rows = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rowsJson);
    int count = 0;

    using var conn = _db.GetConnection();

    foreach (var r in rows ?? [])
    {
        var name         = r.GetValueOrDefault("name", "").Trim();
        var department   = r.GetValueOrDefault("department", "").Trim();
        var serial       = r.GetValueOrDefault("serial_number", "").Trim();
        var device       = r.GetValueOrDefault("device", "").Trim();
        var dateDeployed = r.GetValueOrDefault("date_deployed", "").Trim();
        var deployedBy   = r.GetValueOrDefault("deployed_by", "").Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(device) ||
            string.IsNullOrWhiteSpace(dateDeployed))
            continue;

        if (!DateTime.TryParse(dateDeployed, out var parsedDate))
            continue;

        using var cmd = new MySqlCommand(
            @"INSERT INTO deployments
                (serial_number, user_name, department, device_name, date_deployed, deployed_by, status)
              VALUES (@serial, @name, @dept, @device, @date, @by, 'Deployed')",
            conn);
        cmd.Parameters.AddWithValue("@serial", serial);
        cmd.Parameters.AddWithValue("@name",   name);
        cmd.Parameters.AddWithValue("@dept",   department);
        cmd.Parameters.AddWithValue("@device", device);
        cmd.Parameters.AddWithValue("@date",   parsedDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@by",     deployedBy);
        cmd.ExecuteNonQuery();

        count++;
    }

    SetToast($"{count} deployment record(s) imported.", "success");
    return RedirectToAction("Index", new { tab = "deployed" });
}

        // ==========================================================
        // POST: /Borrowing/DeleteDeployment
        // Replaces PHP: action === 'delete_deployment'
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteDeployment(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            using var conn = _db.GetConnection();
            using var cmd = new MySqlCommand("DELETE FROM deployments WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            SetToast("Deployment record deleted.", "success");
            return RedirectToAction("Index", new { tab = "deployed" });
        }

        // ==========================================================
        // PRIVATE HELPERS
        // ==========================================================

        // Gets a COUNT(*) integer from any query
        private int GetCount(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Fetches borrowings with their items (replaces GROUP_CONCAT query)
        // We use two queries instead of GROUP_CONCAT for clarity
        private List<BorrowingRecord> FetchBorrowings(MySqlConnection conn, string tab, string search)
        {
            string tabStatus = (tab == "returned") ? "Returned" : "Borrowed";
            var records = new List<BorrowingRecord>();

            // Build query — add search filter if provided
            string sql = @"
                SELECT id, serial_number, borrower_name, department,
                       date_borrowed, date_returned, received_by, status, created_at
                FROM borrowings
                WHERE status = @status";

            if (!string.IsNullOrWhiteSpace(search))
                sql += @" AND (borrower_name LIKE @search
                           OR department LIKE @search
                           OR id IN (SELECT borrow_id FROM borrow_items WHERE item_name LIKE @search))";

            sql += " ORDER BY created_at DESC";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", tabStatus);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
records.Add(new BorrowingRecord
{
    Id           = reader.GetInt32("id"),
    SerialNumber = reader.IsDBNull(reader.GetOrdinal("serial_number")) ? "" : reader.GetString("serial_number"),
    BorrowerName = reader.GetString("borrower_name"),
    Department   = reader.IsDBNull(reader.GetOrdinal("department"))    ? "" : reader.GetString("department"),
    DateBorrowed = reader.IsDBNull(reader.GetOrdinal("date_borrowed"))
                   ? ""
                   : reader.GetDateTime("date_borrowed").ToString("yyyy-MM-dd"),
    DateReturned = reader.IsDBNull(reader.GetOrdinal("date_returned"))
                   ? ""
                   : reader.GetDateTime("date_returned").ToString("yyyy-MM-dd"),
    ReceivedBy   = reader.IsDBNull(reader.GetOrdinal("received_by"))   ? null : reader.GetString("received_by"),
    Status       = reader.GetString("status")
});
            }
            reader.Close();

            // Fetch items for each borrowing record
            foreach (var record in records)
            {
                using var cmd2 = new MySqlCommand(
                    "SELECT id, borrow_id, item_name, quantity, status FROM borrow_items WHERE borrow_id=@bid ORDER BY id", conn);
                cmd2.Parameters.AddWithValue("@bid", record.Id);
                using var r2 = cmd2.ExecuteReader();
                while (r2.Read())
                {
                    record.Items.Add(new BorrowItem
                    {
                        Id       = r2.GetInt32("id"),
                        BorrowId = r2.GetInt32("borrow_id"),
                        ItemName = r2.GetString("item_name"),
                        Quantity = r2.GetInt32("quantity"),
                        Status   = r2.GetString("status")
                    });
                }
            }

            return records;
        }

        // Fetches deployments with optional search
        private List<DeploymentRecord> FetchDeployments(MySqlConnection conn, string search)
        {
            var records = new List<DeploymentRecord>();
            string sql = "SELECT * FROM deployments";
            if (!string.IsNullOrWhiteSpace(search))
                sql += " WHERE user_name LIKE @s OR department LIKE @s OR device_name LIKE @s OR serial_number LIKE @s";
            sql += " ORDER BY created_at DESC";

            using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@s", $"%{search}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new DeploymentRecord
                {
                    Id           = reader.GetInt32("id"),
                    SerialNumber = reader.IsDBNull(reader.GetOrdinal("serial_number")) ? "" : reader.GetString("serial_number"),
                    UserName     = reader.GetString("user_name"),
                    Department   = reader.IsDBNull(reader.GetOrdinal("department"))    ? "" : reader.GetString("department"),
                    DeviceName   = reader.GetString("device_name"),
                    DateDeployed = reader.GetDateTime("date_deployed")
                     .ToString("yyyy-MM-dd"),
                    DeployedBy   = reader.IsDBNull(reader.GetOrdinal("deployed_by"))   ? "" : reader.GetString("deployed_by"),
                    Status       = reader.GetString("status")
                });
            }
            return records;
        }

        // Stores toast message in session so it survives the redirect
        // Replaces PHP: $_SESSION['last_insert'] = '...'
        private void SetToast(string message, string type)
        {
            HttpContext.Session.SetString("toast_message", message);
            HttpContext.Session.SetString("toast_type", type);
        }
    }


}
