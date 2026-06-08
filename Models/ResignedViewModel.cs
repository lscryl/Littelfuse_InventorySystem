// ============================================================
// File: Models/ResignedViewModel.cs
// Purpose: All models needed by ResignedController and its View.
//          Replaces PHP's $records, $count_resigned etc. variables.
// ============================================================

namespace ITInventorySystem.Models
{
    // ── One row from the resigned table ──────────────────────────
    public class ResignedRecord
    {
        public int     Id               { get; set; }
        public string  EmployeeName     { get; set; } = "";
        public string  Department       { get; set; } = "";
        public string  Model            { get; set; } = "";
        public string  SerialTag        { get; set; } = "";
        public string  Accessories      { get; set; } = "";
        public string? DateOfClearance  { get; set; }
        public string  Notes            { get; set; } = "";
        public string  AssetStatus      { get; set; } = "resigned";
        public string  DeployedTo       { get; set; } = "";
        public string  DeployedDept     { get; set; } = "";
        public string  DeployedNotes    { get; set; } = "";
        public string? DeployedAt       { get; set; }
        public string  CreatedAt        { get; set; } = "";

        public bool IsDeployed   => AssetStatus == "deployed";
        public bool HasClearance => !string.IsNullOrEmpty(DateOfClearance);

        public string DisplayName => IsDeployed && !string.IsNullOrEmpty(DeployedTo)
            ? DeployedTo : EmployeeName;

        public string DisplayDept => IsDeployed && !string.IsNullOrEmpty(DeployedDept)
            ? DeployedDept : Department;

        public string DisplayDate => IsDeployed && !string.IsNullOrEmpty(DeployedAt)
            ? DeployedAt!
            : HasClearance ? DateOfClearance! : "";
    }

    // ── One row from the asset_history table ─────────────────────
    public class AssetHistoryRecord
    {
        public int    Id           { get; set; }
        public int    ResignedId   { get; set; }
        public string Action       { get; set; } = "";
        public string Actor        { get; set; } = "";
        public string Details      { get; set; } = "";
        public string Snapshot     { get; set; } = "";
        public string CreatedAt    { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string ModelName    { get; set; } = "";
        public string SerialTag    { get; set; } = "";
        public string Department   { get; set; } = "";

        public string DotClass => Action.ToLower() switch
        {
            "record created"    => "created",
            "record edited"     => "edited",
            "asset deployed"    => "deployed",
            "back to inventory" => "backtoinventory",
            "record deleted"    => "deleted",
            _                   => "default"
        };
    }

    // ── Main ViewModel passed to the Razor View ──────────────────
    public class ResignedViewModel
    {
        public string ActiveTab     { get; set; } = "resigned";
        public string Search        { get; set; } = "";
        public int    CountResigned { get; set; }
        public int    CountDeployed { get; set; }
        public int    CountHistory  { get; set; }
        public string ToastMsg      { get; set; } = "";
        public string ToastType     { get; set; } = "success";
        public string SaveError     { get; set; } = "";
        public List<ResignedRecord> Records { get; set; } = new();
    }

    // ── Import row model for CSV import ──────────────────────────
    public class ResignedImportRow
    {
        public string? employee_name     { get; set; }
        public string? department        { get; set; }
        public string? model             { get; set; }
        public string? serial_tag        { get; set; }
        public string? accessories       { get; set; }
        public string? date_of_clearance { get; set; }
        public string? notes             { get; set; }
    }
}
