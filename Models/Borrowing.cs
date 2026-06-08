using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BorrowingApp.Models
{
    // ── Borrowings ─────────────────────────────────────────────────────────────
    [Table("borrowings")]
    public class Borrowing
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("serial_number")]
        [MaxLength(100)]
        public string SerialNumber { get; set; } = "";

        [Required]
        [Column("borrower_name")]
        [MaxLength(100)]
        public string BorrowerName { get; set; } = "";

        [Column("department")]
        [MaxLength(100)]
        public string Department { get; set; } = "";

        [Required]
        [Column("date_borrowed")]
        public DateTime DateBorrowed { get; set; }

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "Borrowed";

        [Column("date_returned")]
        public DateTime? DateReturned { get; set; }

        [Column("received_by")]
        [MaxLength(100)]
        public string ReceivedBy { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<BorrowItem> Items { get; set; } = new List<BorrowItem>();

        // Computed (not mapped)
        [NotMapped]
        public string DisplayStatus
        {
            get
            {
                if (Status == "Borrowed")
                {
                    int days = (int)Math.Floor((DateTime.UtcNow - DateBorrowed).TotalDays);
                    return days > 30 ? "Overdue" : "Borrowed";
                }
                return Status;
            }
        }
    }

    // ── Borrow Items ────────────────────────────────────────────────────────────
    [Table("borrow_items")]
    public class BorrowItem
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("borrow_id")]
        public int BorrowId { get; set; }

        [Required]
        [Column("item_name")]
        [MaxLength(150)]
        public string ItemName { get; set; } = "";

        [Column("quantity")]
        public int Quantity { get; set; } = 1;

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "Borrowed";

        // Navigation
        [ForeignKey("BorrowId")]
        public Borrowing? Borrowing { get; set; }
    }

    // ── Deployments ─────────────────────────────────────────────────────────────
    [Table("deployments")]
    public class Deployment
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("serial_number")]
        [MaxLength(100)]
        public string SerialNumber { get; set; } = "";

        [Required]
        [Column("user_name")]
        [MaxLength(100)]
        public string UserName { get; set; } = "";

        [Column("department")]
        [MaxLength(100)]
        public string Department { get; set; } = "";

        [Required]
        [Column("device_name")]
        [MaxLength(150)]
        public string DeviceName { get; set; } = "";

        [Required]
        [Column("date_deployed")]
        public DateTime DateDeployed { get; set; }

        [Column("deployed_by")]
        [MaxLength(100)]
        public string DeployedBy { get; set; } = "";

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "Deployed";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
