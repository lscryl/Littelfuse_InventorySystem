// ============================================================
// File: Controllers/AccountController.cs
// Purpose: Handles Login and Logout actions.
//          Replaces the PHP session_start(), $_POST checks,
//          header('Location: ...') redirects, and
//          $_SESSION['logged_in'] logic in login.php
// ============================================================

using Microsoft.AspNetCore.Mvc;
using ITInventorySystem.Models;

namespace ITInventorySystem.Controllers
{
    public class AccountController : Controller
    {
        // -------------------------------------------------------
        // Hardcoded credentials (same as PHP version)
        // In the future, these can be moved to appsettings.json
        // or a database table
        // -------------------------------------------------------
        private const string ValidUsername = "BTITOPS";
        private const string ValidPassword = "BTITOPS";

        // -------------------------------------------------------
        // GET: /Account/Login
        // Shows the login form
        // Replaces PHP: if ($_SERVER['REQUEST_METHOD'] === 'GET')
        // -------------------------------------------------------
        [HttpGet]
        public IActionResult Login()
        {
            // If user is already logged in, redirect to Borrowing page
            // Replaces PHP: if (!empty($_SESSION['logged_in'])) { header('Location: borrowing.php'); }
            if (HttpContext.Session.GetString("logged_in") == "true")
            {
                return RedirectToAction("Index", "Borrowing");
            }

            // Show empty login form
            return View(new LoginViewModel());
        }

        // -------------------------------------------------------
        // POST: /Account/Login
        // Processes the submitted login form
        // Replaces PHP: if ($_SERVER['REQUEST_METHOD'] === 'POST')
        // -------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]  // Security: prevents cross-site request forgery
        public IActionResult Login(LoginViewModel model)
        {
            // Check if form data is valid (Required fields filled)
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Check credentials (same logic as PHP version)
            // Replaces PHP: if ($username === 'BTITOPS' && $password === 'BTITOPS')
            if (model.Username == ValidUsername && model.Password == ValidPassword)
            {
                // Store session values
                // Replaces PHP: $_SESSION['logged_in'] = true; $_SESSION['username'] = $username;
                HttpContext.Session.SetString("logged_in", "true");
                HttpContext.Session.SetString("username", model.Username);

                // Redirect to Borrowing page
                // Replaces PHP: header('Location: borrowing.php');
                return RedirectToAction("Index", "Borrowing");
            }

            // Invalid credentials — show error on form
            // Replaces PHP: $error = 'Invalid username or password.';
            ModelState.AddModelError("", "Invalid username or password.");
            return View(model);
        }

        // -------------------------------------------------------
        // GET: /Account/Logout
        // Clears session and redirects to login
        // Replaces PHP logout.php
        // -------------------------------------------------------
        [HttpGet]
        public IActionResult Logout()
        {
            // Clear all session data
            // Replaces PHP: session_destroy();
            HttpContext.Session.Clear();

            // Redirect to login page
            return RedirectToAction("Login", "Account");
        }
    }
}