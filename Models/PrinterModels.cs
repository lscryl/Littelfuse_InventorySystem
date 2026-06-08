// ============================================================
//  Models/PrinterModels.cs
//
//  PLACE THIS FILE AT:
//    ITInventorySystem/Models/PrinterModels.cs
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ITInventorySystem.Models
{
    // ── One row from the `printers` table ───────────────────
    public class Printer
    {
        public int     Id               { get; set; }

        [Required(ErrorMessage = "Printer name is required.")]
        public string  Name             { get; set; } = "";
        public string  Building         { get; set; } = "";
        public string  Department       { get; set; } = "";
        public string  IpAddress        { get; set; } = "";
        public string  MacAddress       { get; set; } = "";
        public string  Model            { get; set; } = "";
        public string  Vendor           { get; set; } = "";
        public string  Serial           { get; set; } = "";

        // "Active" | "Pullout" | "Replaced"
        public string  Status           { get; set; } = "Active";

        public string? PulloutDate      { get; set; }
        public string? ReplacedDate     { get; set; }
        public string  ReplacedLocation { get; set; } = "";
        public string  Remarks          { get; set; } = "";
        public string  CreatedAt        { get; set; } = "";
    }

    // ── One row from the `printer_history` table ─────────────
    public class PrinterHistory
    {
        public int    Id          { get; set; }
        public int    PrinterId   { get; set; }
        public string EventType   { get; set; } = "";

        // "before" snapshot
        public string OldName     { get; set; } = "";
        public string OldBuilding { get; set; } = "";
        public string OldDept     { get; set; } = "";
        public string OldIp       { get; set; } = "";
        public string OldMac      { get; set; } = "";
        public string OldModel    { get; set; } = "";
        public string OldVendor   { get; set; } = "";
        public string OldSerial   { get; set; } = "";
        public string OldStatus   { get; set; } = "";

        // "after" snapshot
        public string NewName     { get; set; } = "";
        public string NewBuilding { get; set; } = "";
        public string NewDept     { get; set; } = "";
        public string NewIp       { get; set; } = "";
        public string NewMac      { get; set; } = "";
        public string NewModel    { get; set; } = "";
        public string NewVendor   { get; set; } = "";
        public string NewSerial   { get; set; } = "";
        public string NewStatus   { get; set; } = "";

        public string Remarks     { get; set; } = "";
        public string CreatedAt   { get; set; } = "";
    }

    // ── Badge counts shown on the tab bar ────────────────────
    public class PrinterCounts
    {
        public int All      { get; set; }
        public int Active   { get; set; }
        public int Pullout  { get; set; }
        public int Replaced { get; set; }
    }

    // ── Form: Add a new printer ──────────────────────────────
    public class AddPrinterForm
    {
        [Required(ErrorMessage = "Printer name is required.")]
        public string  Name             { get; set; } = "";
        public string  Building         { get; set; } = "";
        public string  Department       { get; set; } = "";
        public string  IpAddress        { get; set; } = "";
        public string  MacAddress       { get; set; } = "";
        public string  Model            { get; set; } = "";
        public string  Vendor           { get; set; } = "";
        public string  Serial           { get; set; } = "";
        public string  Status           { get; set; } = "Active";
        public string? PulloutDate      { get; set; }
        public string? ReplacedDate     { get; set; }
        public string  ReplacedLocation { get; set; } = "";
        public string  Remarks          { get; set; } = "";
    }

    // ── Form: Edit an existing printer ──────────────────────
    public class EditPrinterForm : AddPrinterForm
    {
        public int Id { get; set; }
    }

    // ── Form: Replace a printer (marks old as Replaced, adds new) ──
    public class ReplacePrinterForm
    {
        public int     Id               { get; set; }   // old printer id
        public string? ReplacedDate     { get; set; }
        public string  ReplacedLocation { get; set; } = "";
        public string  Remarks          { get; set; } = "";

        // New printer fields
        [Required(ErrorMessage = "New printer name is required.")]
        public string  NewName          { get; set; } = "";
        public string  NewBuilding      { get; set; } = "";
        public string  NewDepartment    { get; set; } = "";
        public string  NewIp            { get; set; } = "";
        public string  NewMac           { get; set; } = "";
        public string  NewModel         { get; set; } = "";
        public string  NewVendor        { get; set; } = "";
        public string  NewSerial        { get; set; } = "";
    }

    // ── Form: CSV bulk import ────────────────────────────────
    public class ImportPrinterRow
    {
        public string Name       { get; set; } = "";
        public string Building   { get; set; } = "";
        public string Department { get; set; } = "";
        public string IpAddress  { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string Model      { get; set; } = "";
        public string Vendor     { get; set; } = "";
        public string Serial     { get; set; } = "";
        public string Status     { get; set; } = "Active";
        public string Remarks    { get; set; } = "";
    }

    // ── ViewModel passed to Views/Printers/Index.cshtml ──────
    public class PrintersIndexViewModel
    {
        public List<Printer> Printers       { get; set; } = new();
        public PrinterCounts Counts         { get; set; } = new();

        // Active status tab filter: "" | "Active" | "Pullout" | "Replaced"
        public string  FilterStatus         { get; set; } = "";
        public string  Search               { get; set; } = "";

        public string  ToastMessage         { get; set; } = "";
        public string  ToastType            { get; set; } = "success";
        public string  SaveError            { get; set; } = "";
    }
}
