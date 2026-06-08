// ============================================================
//  Data/PrinterRepository.cs
//  All database queries converted from printers.php.
//
//  PLACE THIS FILE AT:
//    ITInventorySystem/Data/PrinterRepository.cs
// ============================================================

using ITInventorySystem.Models;
using MySqlConnector;

namespace ITInventorySystem.Data
{
    public class PrinterRepository
    {
        private readonly string _connString;

        public PrinterRepository(string connectionString)
        {
            _connString = connectionString;
        }

        private MySqlConnection GetConn()
        {
            var conn = new MySqlConnection(_connString);
            conn.Open();
            return conn;
        }

        // ════════════════════════════════════════════════════
        //  TABLE SETUP
        // ════════════════════════════════════════════════════

        public void EnsureTables()
        {
            using var conn = GetConn();

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS printers (
                    id                INT AUTO_INCREMENT PRIMARY KEY,
                    name              VARCHAR(150) NOT NULL,
                    building          VARCHAR(100) DEFAULT '',
                    department        VARCHAR(100) DEFAULT '',
                    ip_address        VARCHAR(45)  DEFAULT '',
                    mac_address       VARCHAR(20)  DEFAULT '',
                    model             VARCHAR(150) DEFAULT '',
                    vendor            VARCHAR(100) DEFAULT '',
                    serial            VARCHAR(100) DEFAULT '',
                    status            VARCHAR(20)  DEFAULT 'Active',
                    pullout_date      DATETIME     DEFAULT NULL,
                    replaced_date     DATETIME     DEFAULT NULL,
                    replaced_location VARCHAR(200) DEFAULT NULL,
                    remarks           TEXT         DEFAULT NULL,
                    created_at        TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
                )");

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS printer_history (
                    id           INT AUTO_INCREMENT PRIMARY KEY,
                    printer_id   INT NOT NULL,
                    event_type   VARCHAR(60)  NOT NULL,
                    old_name     VARCHAR(150) DEFAULT NULL,
                    old_building VARCHAR(100) DEFAULT NULL,
                    old_dept     VARCHAR(100) DEFAULT NULL,
                    old_ip       VARCHAR(45)  DEFAULT NULL,
                    old_mac      VARCHAR(20)  DEFAULT NULL,
                    old_model    VARCHAR(150) DEFAULT NULL,
                    old_vendor   VARCHAR(100) DEFAULT NULL,
                    old_serial   VARCHAR(100) DEFAULT NULL,
                    old_status   VARCHAR(20)  DEFAULT NULL,
                    new_name     VARCHAR(150) DEFAULT NULL,
                    new_building VARCHAR(100) DEFAULT NULL,
                    new_dept     VARCHAR(100) DEFAULT NULL,
                    new_ip       VARCHAR(45)  DEFAULT NULL,
                    new_mac      VARCHAR(20)  DEFAULT NULL,
                    new_model    VARCHAR(150) DEFAULT NULL,
                    new_vendor   VARCHAR(100) DEFAULT NULL,
                    new_serial   VARCHAR(100) DEFAULT NULL,
                    new_status   VARCHAR(20)  DEFAULT NULL,
                    remarks      TEXT         DEFAULT NULL,
                    created_at   TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
                )");

            // Migrate missing columns
            AddColIfMissing(conn, "printers",        "pullout_date",      "DATETIME DEFAULT NULL");
            AddColIfMissing(conn, "printers",        "replaced_date",     "DATETIME DEFAULT NULL");
            AddColIfMissing(conn, "printers",        "replaced_location", "VARCHAR(200) DEFAULT NULL");
            AddColIfMissing(conn, "printers",        "vendor",            "VARCHAR(100) DEFAULT ''");
            AddColIfMissing(conn, "printer_history", "old_vendor",        "VARCHAR(100) DEFAULT NULL");
            AddColIfMissing(conn, "printer_history", "new_vendor",        "VARCHAR(100) DEFAULT NULL");
        }

        // ════════════════════════════════════════════════════
        //  COUNTS
        // ════════════════════════════════════════════════════

        public PrinterCounts GetCounts()
        {
            using var conn = GetConn();
            int active   = ScalarInt(conn, "SELECT COUNT(*) FROM printers WHERE status='Active'");
            int pullout  = ScalarInt(conn, "SELECT COUNT(*) FROM printers WHERE status='Pullout'");
            int replaced = ScalarInt(conn, "SELECT COUNT(*) FROM printers WHERE status='Replaced'");
            return new PrinterCounts
            {
                Active   = active,
                Pullout  = pullout,
                Replaced = replaced,
                All      = active + pullout + replaced,
            };
        }

        // ════════════════════════════════════════════════════
        //  FETCH
        // ════════════════════════════════════════════════════

        public List<Printer> GetAll(string search = "", string statusFilter = "")
        {
            using var conn = GetConn();
            string sql = "SELECT * FROM printers";
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(search))
                conditions.Add("(name LIKE @s OR building LIKE @s OR department LIKE @s OR model LIKE @s OR vendor LIKE @s OR serial LIKE @s OR ip_address LIKE @s)");
            if (!string.IsNullOrWhiteSpace(statusFilter))
                conditions.Add("status = @status");

            if (conditions.Count > 0)
                sql += " WHERE " + string.Join(" AND ", conditions);

            sql += " ORDER BY created_at DESC";

            using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@s", $"%{search}%");
            if (!string.IsNullOrWhiteSpace(statusFilter))
                cmd.Parameters.AddWithValue("@status", statusFilter);

            return ReadPrinters(cmd);
        }

        public Printer? GetById(int id)
        {
            using var conn = GetConn();
            using var cmd  = new MySqlCommand("SELECT * FROM printers WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            return ReadPrinters(cmd).FirstOrDefault();
        }

        // ════════════════════════════════════════════════════
        //  ADD
        // ════════════════════════════════════════════════════

        public (bool ok, int newId, string error) Add(AddPrinterForm f)
        {
            if (string.IsNullOrWhiteSpace(f.Name))
                return (false, 0, "Printer name is required.");

            using var conn = GetConn();
            using var cmd  = new MySqlCommand(@"
                INSERT INTO printers
                    (name,building,department,ip_address,mac_address,model,vendor,serial,
                     status,pullout_date,replaced_date,replaced_location,remarks)
                VALUES
                    (@na,@bu,@de,@ip,@ma,@mo,@ve,@se,
                     @st,@pd,@rd,@rl,@re)",
                conn);

            BindPrinterParams(cmd, f);
            cmd.ExecuteNonQuery();
            int newId = (int)cmd.LastInsertedId;

            LogHistory(conn, newId, "Added",
                old: null,
                newName: f.Name, newBuilding: f.Building, newDept: f.Department,
                newIp: f.IpAddress, newMac: f.MacAddress, newModel: f.Model,
                newVendor: f.Vendor, newSerial: f.Serial, newStatus: f.Status,
                remarks: f.Remarks);

            return (true, newId, "");
        }

        // ════════════════════════════════════════════════════
        //  EDIT
        // ════════════════════════════════════════════════════

        public (bool ok, string error) Edit(EditPrinterForm f)
        {
            if (string.IsNullOrWhiteSpace(f.Name))
                return (false, "Printer name is required.");

            using var conn = GetConn();
            var old = GetById(f.Id);

            using var cmd = new MySqlCommand(@"
                UPDATE printers SET
                    name=@na, building=@bu, department=@de, ip_address=@ip, mac_address=@ma,
                    model=@mo, vendor=@ve, serial=@se, status=@st,
                    pullout_date=@pd, replaced_date=@rd, replaced_location=@rl, remarks=@re
                WHERE id=@id",
                conn);

            BindPrinterParams(cmd, f);
            cmd.Parameters.AddWithValue("@id", f.Id);
            cmd.ExecuteNonQuery();

            LogHistory(conn, f.Id, "Edited",
                old: old,
                newName: f.Name, newBuilding: f.Building, newDept: f.Department,
                newIp: f.IpAddress, newMac: f.MacAddress, newModel: f.Model,
                newVendor: f.Vendor, newSerial: f.Serial, newStatus: f.Status,
                remarks: f.Remarks);

            return (true, "");
        }

        // ════════════════════════════════════════════════════
        //  REPLACE
        // ════════════════════════════════════════════════════

        public (bool ok, string error) Replace(ReplacePrinterForm f)
        {
            if (string.IsNullOrWhiteSpace(f.NewName))
                return (false, "New printer name is required.");

            using var conn = GetConn();
            var old = GetById(f.Id);
            if (old == null) return (false, "Original printer not found.");

            string replacedDate = f.ReplacedDate ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Mark old as Replaced
            using (var cmd = new MySqlCommand(
                "UPDATE printers SET status='Replaced', replaced_date=@rd, replaced_location=@rl, remarks=@re WHERE id=@id",
                conn))
            {
                cmd.Parameters.AddWithValue("@rd", replacedDate);
                cmd.Parameters.AddWithValue("@rl", f.ReplacedLocation);
                cmd.Parameters.AddWithValue("@re", f.Remarks);
                cmd.Parameters.AddWithValue("@id", f.Id);
                cmd.ExecuteNonQuery();
            }

            // Insert new printer
            using var ins = new MySqlCommand(@"
                INSERT INTO printers (name,building,department,ip_address,mac_address,model,vendor,serial,status)
                VALUES (@na,@bu,@de,@ip,@ma,@mo,@ve,@se,'Active')",
                conn);
            ins.Parameters.AddWithValue("@na", f.NewName);
            ins.Parameters.AddWithValue("@bu", f.NewBuilding);
            ins.Parameters.AddWithValue("@de", f.NewDepartment);
            ins.Parameters.AddWithValue("@ip", f.NewIp);
            ins.Parameters.AddWithValue("@ma", f.NewMac);
            ins.Parameters.AddWithValue("@mo", f.NewModel);
            ins.Parameters.AddWithValue("@ve", f.NewVendor);
            ins.Parameters.AddWithValue("@se", f.NewSerial);
            ins.ExecuteNonQuery();
            int newId = (int)ins.LastInsertedId;

            // Log on old printer record
            LogHistory(conn, f.Id, "Replaced",
                old: old,
                newName: f.NewName, newBuilding: f.NewBuilding, newDept: f.NewDepartment,
                newIp: f.NewIp, newMac: f.NewMac, newModel: f.NewModel,
                newVendor: f.NewVendor, newSerial: f.NewSerial, newStatus: "Active",
                remarks: f.Remarks);

            // Log on new printer record
            LogHistory(conn, newId, $"Added (Replacement for: {old.Name})",
                old: null,
                newName: f.NewName, newBuilding: f.NewBuilding, newDept: f.NewDepartment,
                newIp: f.NewIp, newMac: f.NewMac, newModel: f.NewModel,
                newVendor: f.NewVendor, newSerial: f.NewSerial, newStatus: "Active",
                remarks: f.Remarks);

            return (true, "");
        }

        // ════════════════════════════════════════════════════
        //  DELETE
        // ════════════════════════════════════════════════════

        public void Delete(int id)
        {
            using var conn = GetConn();
            Execute(conn, $"DELETE FROM printer_history WHERE printer_id={id}");
            using var cmd = new MySqlCommand("DELETE FROM printers WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ════════════════════════════════════════════════════
        //  IMPORT (bulk CSV rows)
        // ════════════════════════════════════════════════════

        public int Import(List<ImportPrinterRow> rows)
        {
            int imported = 0;
            using var conn = GetConn();

            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Name)) continue;

                string status = new[] { "Active", "Pullout", "Replaced" }.Contains(r.Status)
                    ? r.Status : "Active";

                using var cmd = new MySqlCommand(@"
                    INSERT INTO printers (name,building,department,ip_address,mac_address,model,vendor,serial,status,remarks)
                    VALUES (@na,@bu,@de,@ip,@ma,@mo,@ve,@se,@st,@re)",
                    conn);

                cmd.Parameters.AddWithValue("@na", r.Name);
                cmd.Parameters.AddWithValue("@bu", r.Building);
                cmd.Parameters.AddWithValue("@de", r.Department);
                cmd.Parameters.AddWithValue("@ip", r.IpAddress);
                cmd.Parameters.AddWithValue("@ma", r.MacAddress);
                cmd.Parameters.AddWithValue("@mo", r.Model);
                cmd.Parameters.AddWithValue("@ve", r.Vendor);
                cmd.Parameters.AddWithValue("@se", r.Serial);
                cmd.Parameters.AddWithValue("@st", status);
                cmd.Parameters.AddWithValue("@re", r.Remarks);
                cmd.ExecuteNonQuery();

                int newId = (int)cmd.LastInsertedId;
                LogHistory(conn, newId, "Added",
                    old: null,
                    newName: r.Name, newBuilding: r.Building, newDept: r.Department,
                    newIp: r.IpAddress, newMac: r.MacAddress, newModel: r.Model,
                    newVendor: r.Vendor, newSerial: r.Serial, newStatus: status,
                    remarks: r.Remarks);

                imported++;
            }
            return imported;
        }

        // ════════════════════════════════════════════════════
        //  HISTORY
        // ════════════════════════════════════════════════════

        public List<PrinterHistory> GetHistory(int printerId)
        {
            using var conn = GetConn();
            using var cmd  = new MySqlCommand(
                "SELECT * FROM printer_history WHERE printer_id=@id ORDER BY created_at DESC",
                conn);
            cmd.Parameters.AddWithValue("@id", printerId);
            return ReadHistory(cmd);
        }

        // ════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ════════════════════════════════════════════════════

        private static void BindPrinterParams(MySqlCommand cmd, AddPrinterForm f)
        {
            cmd.Parameters.AddWithValue("@na", f.Name);
            cmd.Parameters.AddWithValue("@bu", f.Building);
            cmd.Parameters.AddWithValue("@de", f.Department);
            cmd.Parameters.AddWithValue("@ip", f.IpAddress);
            cmd.Parameters.AddWithValue("@ma", f.MacAddress);
            cmd.Parameters.AddWithValue("@mo", f.Model);
            cmd.Parameters.AddWithValue("@ve", f.Vendor);
            cmd.Parameters.AddWithValue("@se", f.Serial);
            cmd.Parameters.AddWithValue("@st", f.Status);
            cmd.Parameters.AddWithValue("@pd", (object?)f.PulloutDate      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rd", (object?)f.ReplacedDate     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rl", f.ReplacedLocation);
            cmd.Parameters.AddWithValue("@re", f.Remarks);
        }

        private static void LogHistory(MySqlConnection conn, int printerId, string eventType,
            Printer? old,
            string newName, string newBuilding, string newDept,
            string newIp, string newMac, string newModel,
            string newVendor, string newSerial, string newStatus,
            string remarks)
        {
            using var cmd = new MySqlCommand(@"
                INSERT INTO printer_history
                    (printer_id, event_type,
                     old_name, old_building, old_dept, old_ip, old_mac, old_model, old_vendor, old_serial, old_status,
                     new_name, new_building, new_dept, new_ip, new_mac, new_model, new_vendor, new_serial, new_status,
                     remarks)
                VALUES
                    (@pid, @ev,
                     @on,  @ob,  @od,  @oi,  @om2, @omo, @ov,  @os,  @ost,
                     @nn,  @nb,  @nd,  @ni,  @nm2, @nmo, @nv,  @ns,  @nst,
                     @re)",
                conn);

            cmd.Parameters.AddWithValue("@pid", printerId);
            cmd.Parameters.AddWithValue("@ev",  eventType);

            // Old values
            cmd.Parameters.AddWithValue("@on",  (object?)old?.Name     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ob",  (object?)old?.Building ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@od",  (object?)old?.Department ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oi",  (object?)old?.IpAddress  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@om2", (object?)old?.MacAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@omo", (object?)old?.Model      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ov",  (object?)old?.Vendor     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@os",  (object?)old?.Serial     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ost", (object?)old?.Status     ?? DBNull.Value);

            // New values
            cmd.Parameters.AddWithValue("@nn",  newName);
            cmd.Parameters.AddWithValue("@nb",  newBuilding);
            cmd.Parameters.AddWithValue("@nd",  newDept);
            cmd.Parameters.AddWithValue("@ni",  newIp);
            cmd.Parameters.AddWithValue("@nm2", newMac);
            cmd.Parameters.AddWithValue("@nmo", newModel);
            cmd.Parameters.AddWithValue("@nv",  newVendor);
            cmd.Parameters.AddWithValue("@ns",  newSerial);
            cmd.Parameters.AddWithValue("@nst", newStatus);
            cmd.Parameters.AddWithValue("@re",  remarks);

            cmd.ExecuteNonQuery();
        }

        private static List<Printer> ReadPrinters(MySqlCommand cmd)
        {
            var list = new List<Printer>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Printer
                {
                    Id               = r.GetInt32("id"),
                    Name             = r["name"]?.ToString()             ?? "",
                    Building         = r["building"]?.ToString()         ?? "",
                    Department       = r["department"]?.ToString()       ?? "",
                    IpAddress        = r["ip_address"]?.ToString()       ?? "",
                    MacAddress       = r["mac_address"]?.ToString()      ?? "",
                    Model            = r["model"]?.ToString()            ?? "",
                    Vendor           = r["vendor"]?.ToString()           ?? "",
                    Serial           = r["serial"]?.ToString()           ?? "",
                    Status           = r["status"]?.ToString()           ?? "Active",
                    PulloutDate      = r["pullout_date"]  == DBNull.Value ? null : r["pullout_date"].ToString(),
                    ReplacedDate     = r["replaced_date"] == DBNull.Value ? null : r["replaced_date"].ToString(),
                    ReplacedLocation = r["replaced_location"]?.ToString() ?? "",
                    Remarks          = r["remarks"]?.ToString()           ?? "",
                    CreatedAt        = r["created_at"]?.ToString()        ?? "",
                });
            }
            return list;
        }

        private static List<PrinterHistory> ReadHistory(MySqlCommand cmd)
        {
            var list = new List<PrinterHistory>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new PrinterHistory
                {
                    Id          = r.GetInt32("id"),
                    PrinterId   = r.GetInt32("printer_id"),
                    EventType   = r["event_type"]?.ToString()   ?? "",
                    OldName     = r["old_name"]?.ToString()     ?? "",
                    OldBuilding = r["old_building"]?.ToString() ?? "",
                    OldDept     = r["old_dept"]?.ToString()     ?? "",
                    OldIp       = r["old_ip"]?.ToString()       ?? "",
                    OldMac      = r["old_mac"]?.ToString()      ?? "",
                    OldModel    = r["old_model"]?.ToString()    ?? "",
                    OldVendor   = r["old_vendor"]?.ToString()   ?? "",
                    OldSerial   = r["old_serial"]?.ToString()   ?? "",
                    OldStatus   = r["old_status"]?.ToString()   ?? "",
                    NewName     = r["new_name"]?.ToString()     ?? "",
                    NewBuilding = r["new_building"]?.ToString() ?? "",
                    NewDept     = r["new_dept"]?.ToString()     ?? "",
                    NewIp       = r["new_ip"]?.ToString()       ?? "",
                    NewMac      = r["new_mac"]?.ToString()      ?? "",
                    NewModel    = r["new_model"]?.ToString()    ?? "",
                    NewVendor   = r["new_vendor"]?.ToString()   ?? "",
                    NewSerial   = r["new_serial"]?.ToString()   ?? "",
                    NewStatus   = r["new_status"]?.ToString()   ?? "",
                    Remarks     = r["remarks"]?.ToString()      ?? "",
                    CreatedAt   = r["created_at"]?.ToString()   ?? "",
                });
            }
            return list;
        }

        private static void Execute(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private static int ScalarInt(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static void AddColIfMissing(MySqlConnection conn, string table, string col, string definition)
        {
            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@t AND COLUMN_NAME=@c",
                conn);
            cmd.Parameters.AddWithValue("@t", table);
            cmd.Parameters.AddWithValue("@c", col);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                Execute(conn, $"ALTER TABLE {table} ADD COLUMN {col} {definition}");
        }
    }
}
