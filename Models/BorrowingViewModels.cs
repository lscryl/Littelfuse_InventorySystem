// ============================================================
// File: Models/BorrowingViewModel.cs
// Purpose: Holds all data needed by the Borrowing page.
//          Replaces PHP's $borrowings, $deployments, $count_* variables
//          and the nested item arrays from GROUP_CONCAT queries.
// ============================================================

namespace ITInventorySystem.Models
{
    // ── Represents one row in the borrowings table (joined with borrow_items) ──
    public class BorrowingRecord
    {
        public int    Id            { get; set; }
        public string SerialNumber  { get; set; } = "";
        public string BorrowerName  { get; set; } = "";
        public string Department    { get; set; } = "";
        public string DateBorrowed  { get; set; } = "";
        public string? DateReturned { get; set; }
        public string? ReceivedBy   { get; set; }
        public string Status        { get; set; } = "Borrowed";

        // Computed: "Overdue" if Borrowed > 30 days (replaces PHP $display_status logic)
        public string DisplayStatus =>
            Status == "Borrowed" &&
            DateTime.TryParse(DateBorrowed, out var d) &&
            (DateTime.Today - d).TotalDays > 30
                ? "Overdue"
                : Status;

        // Items joined from borrow_items table
        public List<BorrowItem> Items { get; set; } = new();

        // True if more than one item exists (used for multi-return modal)
        public bool IsMulti => Items.Count(i => i.ItemName.Trim() != "") >= 1;

        // True if all items are returned (hides "Mark as Returned" button)
        public bool AllReturned => Items.All(i => i.Status == "Returned");

        // Total quantity across all items
        public int TotalQty => Items.Sum(i => i.Quantity);
    }

    // ── Represents one row in borrow_items table ──
    public class BorrowItem
    {
        public int    Id       { get; set; }
        public int    BorrowId { get; set; }
        public string ItemName { get; set; } = "";
        public int    Quantity { get; set; } = 1;
        public string Status   { get; set; } = "Borrowed";
    }

    // ── Represents one row in deployments table ──
    public class DeploymentRecord
    {
        public int    Id           { get; set; }
        public string SerialNumber { get; set; } = "";
        public string UserName     { get; set; } = "";
        public string Department   { get; set; } = "";
        public string DeviceName   { get; set; } = "";
        public string DateDeployed { get; set; } = "";
        public string DeployedBy   { get; set; } = "";
        public string Status       { get; set; } = "Deployed";
    }

    // ── The main ViewModel passed to the Razor View ──
    // Replaces all PHP variables at the top of borrowing.php
    public class BorrowingViewModel
    {
        // Currently active tab: "borrowed", "returned", or "deployed"
        public string ActiveTab { get; set; } = "borrowed";

        // Tab counts shown in tab headers
        public int CountBorrowed { get; set; }
        public int CountReturned { get; set; }
        public int CountDeployed { get; set; }

        // Current search query
        public string Search { get; set; } = "";

        // Data lists — only one will be populated depending on ActiveTab
        public List<BorrowingRecord>  Borrowings   { get; set; } = new();
        public List<DeploymentRecord> Deployments  { get; set; } = new();

        // Toast notification (replaces PHP $_SESSION['last_insert'])
        public string ToastMessage { get; set; } = "";
        public string ToastType    { get; set; } = ""; // "success" or "error"
    }
}
