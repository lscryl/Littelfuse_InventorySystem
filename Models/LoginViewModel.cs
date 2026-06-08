// ============================================================
// File: Models/LoginViewModel.cs
// Purpose: Holds the data submitted from the login form.
//          Like PHP's $_POST['username'] and $_POST['password']
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ITInventorySystem.Models
{
    public class LoginViewModel
    {
        // [Required] means the field cannot be empty
        // This replaces PHP's manual empty checks

        [Required(ErrorMessage = "Username is required.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]  // Tells the form to render as password input
        public string Password { get; set; } = string.Empty;
    }
}