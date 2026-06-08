// ============================================================
// File: Controllers/HomeController.cs
// Purpose: Redirect root URL to Login page
// ============================================================

using Microsoft.AspNetCore.Mvc;

namespace ITInventorySystem.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            // Redirect root URL to Login page
            return RedirectToAction("Login", "Account");
        }
    }
}