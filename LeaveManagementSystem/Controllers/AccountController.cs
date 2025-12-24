using LeaveManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly DatabaseContext _context;

        public AccountController(DatabaseContext context)
        {
            _context = context;
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            // ✅ Check if user is active
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email && x.IsActive);

            // Check password using BCrypt
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                TempData["Error"] = "Invalid email or password!";
                return RedirectToAction("Index", "Home");
            }

            // Store session values
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("UserName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role);
            HttpContext.Session.SetString("DepartmentId", user.DepartmentId?.ToString() ?? "");

            // ✅ FIX: Redirect to correct controllers
            return user.Role switch
            {
                "Admin" => RedirectToAction("Admin", "Dashboard"),
                "Manager" => RedirectToAction("Index", "ManagerDashboard"),  // ✅ CHANGED
                _ => RedirectToAction("Index", "EmployeeDashboard"),         // ✅ CHANGED
            };
        }

        // GET: /Account/Logout
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }
    }
}