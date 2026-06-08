// ============================================================
// File: Models/DeviceModels.cs
// Purpose: Models for the Devices / Loaner Laptop page.
// ============================================================

namespace ITInventorySystem.Models
{
    // ── devices table ────────────────────────────────────────────
    public class Device
    {
        public int    Id           { get; set; }
        public string DeviceName   { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Model        { get; set; } = "";
        public string Department   { get; set; } = "";
        public string OsName       { get; set; } = "";
        public string ReleaseId    { get; set; } = "";
        public string Pic          { get; set; } = "";
        public string? QrCode      { get; set; }
        public string CreatedAt    { get; set; } = "";

        // Populated from a joined device_loans row (active loan only)
        public string? BorrowerName  { get; set; }
        public string? LoanDept      { get; set; }
        public string? DateBorrowed  { get; set; }
        public string? ExpectedReturn { get; set; }
    }

    // ── device_loans table ────────────────────────────────────────
    public class DeviceLoan
    {
        public int     Id             { get; set; }
        public int     DeviceId       { get; set; }
        public string  DeviceName     { get; set; } = "";
        public string  SerialNumber   { get; set; } = "";
        public string  Model          { get; set; } = "";
        public string  BorrowerName   { get; set; } = "";
        public string  Department     { get; set; } = "";
        public string  DateBorrowed   { get; set; } = "";
        public string? ExpectedReturn { get; set; }
        public string  Status         { get; set; } = "Active";
        public string? DateReturned   { get; set; }

        // Computed
        public bool IsOverdue =>
            !string.IsNullOrEmpty(ExpectedReturn) &&
            string.Compare(ExpectedReturn, DateTime.Today.ToString("yyyy-MM-dd")) < 0;
    }

    // ── ViewModel for the whole page ─────────────────────────────
    public class DevicesViewModel
    {
        public string         ActiveTab      { get; set; } = "inventory";
        public List<Device>   Devices        { get; set; } = new();
        public List<DeviceLoan> Loans        { get; set; } = new();
        public int            CountAvailable { get; set; }
        public int            CountBorrowed  { get; set; }
        public int            CountOverdue   { get; set; }
        public string         ToastMessage   { get; set; } = "";
        public string         ToastType      { get; set; } = "success";

        // When page opened via ?borrow=ID (QR scan)
        public Device?        QrBorrowDevice { get; set; }
    }

    // ── Import row (from CSV JSON) ────────────────────────────────
    public class DeviceImportRow
    {
        public string device_name   { get; set; } = "";
        public string serial_number { get; set; } = "";
        public string model         { get; set; } = "";
    }

    public class LoanImportRow
{
    public string? device_name     { get; set; }
    public string? borrower_name   { get; set; }
    public string? department      { get; set; }
    public string? date_borrowed   { get; set; }
    public string? expected_return { get; set; }
}
}
