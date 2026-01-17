using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LeaveManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly DatabaseContext _context;

        public AccountController(DatabaseContext context)
        {
            _context = context;
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                TempData["Error"] = "Please enter both email and password.";
                return RedirectToAction("Index", "Home");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() && u.IsActive);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                TempData["Error"] = "Invalid email or password.";
                return RedirectToAction("Index", "Home");
            }

            // Generate JWT token
            var token = GenerateJwtToken(user);

            // ✅ FIXED COOKIE SETTINGS
            Response.Cookies.Append("jwt_token", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // ✅ CHANGE TO TRUE for HTTPS
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddMinutes(30),
                Path = "/",
                Domain = null,
                IsEssential = true
            });
            // Redirect based on role
            return user.Role switch
            {
                "Admin" => RedirectToAction("Admin", "Dashboard"),
                "Manager" => RedirectToAction("Index", "ManagerDashboard"),
                _ => RedirectToAction("Index", "EmployeeDashboard")
            };
        }
        // ✅ SIR KE PATTERN: JWT Token Generation
        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("OnlineLeaveManagementSystemJWTSecretKey12345"));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddHours(3),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public IActionResult Logout()
        {
            // ✅ SIR KE PATTERN: Delete JWT cookie
            Response.Cookies.Delete("jwt_token");
            return RedirectToAction("Index", "Home");
        }
    }
}