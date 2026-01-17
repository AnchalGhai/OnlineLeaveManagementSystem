using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace LeaveManagementSystem.Controllers
{
    [Authorize(Roles = "Employee")] // ✅ ONLY JWT AUTHORIZATION
    public class EmployeeDashboardController : Controller
    {
        private readonly DatabaseContext _context;

        public EmployeeDashboardController(DatabaseContext context)
        {
            _context = context;
        }

        // ✅ HELPER: Get current user info from JWT token ONLY
        private (int UserId, string Role, string Name) GetCurrentUserFromJwt()
        {
            var jwtToken = HttpContext.Request.Cookies["jwt_token"];

            if (string.IsNullOrEmpty(jwtToken))
                return (0, "", "");

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(jwtToken);

                var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                var nameClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                if (int.TryParse(userIdClaim, out int userId))
                    return (userId, roleClaim ?? "", nameClaim ?? "");
            }
            catch (Exception)
            {
                // Token invalid - delete cookie
                Response.Cookies.Delete("jwt_token");
            }

            return (0, "", "");
        }

        // ✅ HELPER: Simple method to get just user ID
        private int GetUserIdFromJwt()
        {
            var (userId, _, _) = GetCurrentUserFromJwt();
            return userId;
        }

        // GET: Employee Dashboard
        public async Task<IActionResult> Index()
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            // Dashboard metrics
            ViewBag.MyLeaves = await _context.LeaveApplications
                .CountAsync(x => x.UserId == userId);
            ViewBag.PendingLeaves = await _context.LeaveApplications
                .CountAsync(x => x.UserId == userId && x.Status == "Pending");
            ViewBag.ApprovedLeaves = await _context.LeaveApplications
                .CountAsync(x => x.UserId == userId && x.Status == "Approved");

            // Recent leaves
            var recentLeaves = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.AppliedOn)
                .Take(5)
                .ToListAsync();
            ViewBag.RecentLeaves = recentLeaves;

            // Leave balances
            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == userId)
                .ToListAsync();
            ViewBag.LeaveBalances = balances;

            return View("Employee");
        }

        // GET: Apply for Leave
        public async Task<IActionResult> ApplyLeave()
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            // Check if user has leave balances
            var hasBalances = await _context.LeaveBalances
                .AnyAsync(b => b.UserId == userId);

            if (!hasBalances)
            {
                TempData["ErrorMessage"] = "No leave balance found. Please contact HR.";
                return RedirectToAction("Index");
            }

            var leaveTypes = await _context.LeaveTypes.ToListAsync();
            ViewBag.LeaveTypes = new SelectList(leaveTypes, "LeaveTypeId", "Name");

            // Get leave balances
            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == userId)
                .ToListAsync();

            ViewBag.LeaveBalances = balances;
            return View();
        }

        // POST: Apply for Leave
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyLeave(LeaveApplication model)
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            var user = await _context.Users
                .Include(u => u.Manager)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Manual validation
            if (model.LeaveTypeId <= 0)
            {
                TempData["ErrorMessage"] = "Please select a leave type";
                return await LoadApplyLeavePage(userId);
            }

            if (model.StartDate == default || model.EndDate == default)
            {
                TempData["ErrorMessage"] = "Please select both start and end dates";
                return await LoadApplyLeavePage(userId);
            }

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Length < 10)
            {
                TempData["ErrorMessage"] = "Reason must be at least 10 characters";
                return await LoadApplyLeavePage(userId);
            }

            if (model.EndDate < model.StartDate)
            {
                TempData["ErrorMessage"] = "End date cannot be earlier than start date";
                return await LoadApplyLeavePage(userId);
            }

            try
            {
                // Calculate total days
                model.TotalDays = (model.EndDate - model.StartDate).Days + 1;
                model.UserId = userId;
                model.Status = "Pending";
                model.AppliedOn = DateTime.Now;

                // Check leave balance
                var balance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.UserId == userId && b.LeaveTypeId == model.LeaveTypeId);

                if (balance == null)
                {
                    var leaveType = await _context.LeaveTypes.FindAsync(model.LeaveTypeId);
                    if (leaveType != null)
                    {
                        // Create new balance
                        balance = new LeaveBalance
                        {
                            UserId = userId,
                            LeaveTypeId = model.LeaveTypeId,
                            TotalAssigned = leaveType.MaxPerYear,
                            Used = 0,
                            Remaining = leaveType.MaxPerYear
                        };
                        _context.LeaveBalances.Add(balance);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Leave type not found";
                        return await LoadApplyLeavePage(userId);
                    }
                }

                // Check if enough balance available
                if (model.TotalDays > balance.Remaining)
                {
                    TempData["ErrorMessage"] = $"Not enough leave balance. Available: {balance.Remaining} days";
                    return await LoadApplyLeavePage(userId);
                }

                // Check for overlapping leaves
                var overlappingLeaves = await _context.LeaveApplications
                    .Where(l => l.UserId == userId &&
                               l.Status != "Rejected" &&
                               l.Status != "Cancelled" &&
                               ((model.StartDate >= l.StartDate && model.StartDate <= l.EndDate) ||
                                (model.EndDate >= l.StartDate && model.EndDate <= l.EndDate) ||
                                (l.StartDate >= model.StartDate && l.StartDate <= model.EndDate)))
                    .ToListAsync();

                if (overlappingLeaves.Any())
                {
                    TempData["ErrorMessage"] = "You have already applied for leave during this period.";
                    return await LoadApplyLeavePage(userId);
                }

                // Create leave application
                _context.LeaveApplications.Add(model);
                await _context.SaveChangesAsync();

                // Create notification for manager
                if (user.ManagerId != null)
                {
                    var leaveType = await _context.LeaveTypes.FindAsync(model.LeaveTypeId);
                    var notification = new Notification
                    {
                        UserId = user.ManagerId.Value,
                        Message = $"{user.FullName} has applied for {model.TotalDays} day(s) of {leaveType?.Name ?? "leave"} from {model.StartDate:dd-MMM-yyyy} to {model.EndDate:dd-MMM-yyyy}.",
                        CreatedOn = DateTime.Now
                    };
                    _context.Notifications.Add(notification);
                    await _context.SaveChangesAsync();
                    var employee = await _context.Users.FindAsync(userId);
                    if (employee != null && !string.IsNullOrEmpty(employee.Email))
                        Email.SendEmployeeApplied(employee.Email, employee.FullName, model);
                }

                TempData["SuccessMessage"] = "Leave application submitted successfully! It will be reviewed by your manager.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return await LoadApplyLeavePage(userId);
            }
        }

        // Helper method to reload page with data
        private async Task<IActionResult> LoadApplyLeavePage(int userId)
        {
            var leaveTypes = await _context.LeaveTypes.ToListAsync();
            ViewBag.LeaveTypes = new SelectList(leaveTypes, "LeaveTypeId", "Name");

            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == userId)
                .ToListAsync();

            ViewBag.LeaveBalances = balances;
            return View("ApplyLeave");
        }

        // GET: My Leave Applications
        public async Task<IActionResult> MyLeaves()
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            var leaves = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            return View(leaves);
        }

        // GET: Cancel Leave Application
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelLeave(int id)
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            var leave = await _context.LeaveApplications
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.UserId == userId);

            if (leave == null)
            {
                TempData["ErrorMessage"] = "Leave application not found.";
                return RedirectToAction("MyLeaves");
            }

            if (leave.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Cannot cancel leave with status: {leave.Status}";
                return RedirectToAction("MyLeaves");
            }

            try
            {
                leave.Status = "Cancelled";
                leave.ActionDate = null;
                leave.ManagerComments = null;

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Leave application cancelled successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error cancelling leave: {ex.Message}";
            }

            return RedirectToAction("MyLeaves");
        }

        // View Leave Details
        public async Task<IActionResult> LeaveDetails(int id)
        {
            // ✅ ONLY JWT CHECK
            var (userId, role, name) = GetCurrentUserFromJwt();

            if (userId == 0 || role != "Employee")
            {
                TempData["Error"] = "Please login to continue";
                return RedirectToAction("Index", "Home");
            }

            var leave = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.UserId == userId);

            if (leave == null)
            {
                TempData["ErrorMessage"] = "Leave application not found.";
                return RedirectToAction("MyLeaves");
            }

            // Get leave balance at the time of application
            var balance = await _context.LeaveBalances
                .FirstOrDefaultAsync(b => b.UserId == userId && b.LeaveTypeId == leave.LeaveTypeId);

            ViewBag.LeaveBalance = balance;

            return View(leave);
        }

        // GET: Leave Balance Summary (for dashboard widgets)
        public async Task<JsonResult> GetDashboardSummary()
        {
            var userId = GetUserIdFromJwt();
            if (userId == 0) return Json(new { success = false });

            var summary = new
            {
                pendingLeaves = await _context.LeaveApplications
                    .CountAsync(l => l.UserId == userId && l.Status == "Pending"),
                approvedLeaves = await _context.LeaveApplications
                    .CountAsync(l => l.UserId == userId && l.Status == "Approved"),
                totalLeaves = await _context.LeaveApplications
                    .CountAsync(l => l.UserId == userId),
                availableBalance = await _context.LeaveBalances
                    .Where(b => b.UserId == userId)
                    .SumAsync(b => b.Remaining)
            };

            return Json(new { success = true, data = summary });
        }

        // GET: Leave Balance Summary
        public async Task<JsonResult> GetLeaveBalanceSummary()
        {
            var userId = GetUserIdFromJwt();
            if (userId == 0) return Json(new { success = false });

            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == userId)
                .Select(b => new
                {
                    leaveType = b.LeaveType.Name,
                    total = b.TotalAssigned,
                    used = b.Used,
                    remaining = b.Remaining
                })
                .ToListAsync();

            return Json(new { success = true, data = balances });
        }

        // GET: Leave Statistics (for chart)
        public async Task<JsonResult> GetLeaveStatistics()
        {
            var userId = GetUserIdFromJwt();
            if (userId == 0) return Json(new { success = false });

            var stats = new
            {
                pending = await _context.LeaveApplications.CountAsync(l => l.UserId == userId && l.Status == "Pending"),
                approved = await _context.LeaveApplications.CountAsync(l => l.UserId == userId && l.Status == "Approved"),
                rejected = await _context.LeaveApplications.CountAsync(l => l.UserId == userId && l.Status == "Rejected"),
                cancelled = await _context.LeaveApplications.CountAsync(l => l.UserId == userId && l.Status == "Cancelled")
            };

            return Json(new { success = true, data = stats });
        }
    }
}