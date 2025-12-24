using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagementSystem.Controllers
{
    public class ManagerDashboardController : Controller
    {
        private readonly DatabaseContext _context;

        public ManagerDashboardController(DatabaseContext context)
        {
            _context = context;
        }

        // GET: Manager Dashboard
        public async Task<IActionResult> Index()
        {
            if (HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            int managerId = HttpContext.Session.GetInt32("UserId") ?? 0;

            // Dashboard metrics
            ViewBag.PendingRequests = await _context.LeaveApplications
                .CountAsync(l => l.User.ManagerId == managerId && l.Status == "Pending");
            ViewBag.TeamMembers = await _context.Users
                .CountAsync(u => u.ManagerId == managerId && u.IsActive);
            ViewBag.ApprovedThisMonth = await _context.LeaveApplications
                .CountAsync(l => l.User.ManagerId == managerId &&
                                l.Status == "Approved" &&
                                l.ActionDate.HasValue &&
                                l.ActionDate.Value.Month == DateTime.Now.Month);
            ViewBag.TotalTeamLeaves = await _context.LeaveApplications
                .CountAsync(l => l.User.ManagerId == managerId);

            // Manager's own leave stats
            ViewBag.MyPendingLeaves = await _context.LeaveApplications
                .CountAsync(l => l.UserId == managerId && l.Status == "Pending");
            ViewBag.MyApprovedLeaves = await _context.LeaveApplications
                .CountAsync(l => l.UserId == managerId && l.Status == "Approved");

            // Recent pending requests
            var recentRequests = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .Where(l => l.User.ManagerId == managerId && l.Status == "Pending")
                .OrderByDescending(l => l.AppliedOn)
                .Take(5)
                .ToListAsync();
            ViewBag.RecentRequests = recentRequests;

            // Manager's own recent leaves
            var myRecentLeaves = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.UserId == managerId)
                .OrderByDescending(l => l.AppliedOn)
                .Take(5)
                .ToListAsync();
            ViewBag.MyRecentLeaves = myRecentLeaves;

            // Leave balances
            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == managerId)
                .ToListAsync();
            ViewBag.LeaveBalances = balances;

            return View("Manager");
        }

        // GET: Apply for Leave (Manager's own leave)
        public async Task<IActionResult> ApplyLeave()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            // ✅ FIX: Agar balance nahi hai to automatically create karein
            var hasBalances = await _context.LeaveBalances
                .AnyAsync(b => b.UserId == managerId);

            if (!hasBalances)
            {
                // Automatically create leave balances for manager
                await CreateDefaultLeaveBalancesForUser(managerId.Value);
                TempData["InfoMessage"] = "Leave balances created automatically.";
            }

            var leaveTypes = await _context.LeaveTypes.ToListAsync();
            ViewBag.LeaveTypes = new SelectList(leaveTypes, "LeaveTypeId", "Name");

            // Get leave balances
            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == managerId)
                .ToListAsync();

            ViewBag.LeaveBalances = balances;
            return View();
        }

        // ✅ Helper method to create default leave balances
        private async Task CreateDefaultLeaveBalancesForUser(int userId)
        {
            try
            {
                var leaveTypes = await _context.LeaveTypes.ToListAsync();
                foreach (var leaveType in leaveTypes)
                {
                    // Check if balance already exists
                    var existingBalance = await _context.LeaveBalances
                        .FirstOrDefaultAsync(b => b.UserId == userId && b.LeaveTypeId == leaveType.LeaveTypeId);

                    if (existingBalance == null)
                    {
                        var leaveBalance = new LeaveBalance
                        {
                            UserId = userId,
                            LeaveTypeId = leaveType.LeaveTypeId,
                            TotalAssigned = leaveType.MaxPerYear,
                            Used = 0,
                            Remaining = leaveType.MaxPerYear
                        };
                        _context.LeaveBalances.Add(leaveBalance);
                    }
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating leave balances: {ex.Message}");
            }
        }

        // ✅ POST: Apply for Leave (Manager's own leave)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyLeave(LeaveApplication model)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var manager = await _context.Users
                .Include(u => u.Department)
                .FirstOrDefaultAsync(u => u.UserId == managerId);

            if (manager == null)
            {
                TempData["ErrorMessage"] = "Manager not found.";
                return RedirectToAction("Index");
            }

            // Manual validation
            if (model.LeaveTypeId <= 0)
            {
                TempData["ErrorMessage"] = "Please select a leave type";
                return await LoadManagerApplyLeavePage(managerId.Value);
            }

            if (model.StartDate == default || model.EndDate == default)
            {
                TempData["ErrorMessage"] = "Please select both start and end dates";
                return await LoadManagerApplyLeavePage(managerId.Value);
            }

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Length < 10)
            {
                TempData["ErrorMessage"] = "Reason must be at least 10 characters";
                return await LoadManagerApplyLeavePage(managerId.Value);
            }

            if (model.EndDate < model.StartDate)
            {
                TempData["ErrorMessage"] = "End date cannot be earlier than start date";
                return await LoadManagerApplyLeavePage(managerId.Value);
            }

            try
            {
                // Calculate total days
                model.TotalDays = (model.EndDate - model.StartDate).Days + 1;
                model.UserId = managerId.Value;
                model.Status = "Pending"; // Manager's leave will be approved by Admin
                model.AppliedOn = DateTime.Now;

                // Check leave balance - BUT DON'T DEDUCT YET
                var balance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.UserId == managerId && b.LeaveTypeId == model.LeaveTypeId);

                if (balance == null)
                {
                    var leaveType = await _context.LeaveTypes.FindAsync(model.LeaveTypeId);
                    if (leaveType != null)
                    {
                        // Create new balance
                        balance = new LeaveBalance
                        {
                            UserId = managerId.Value,
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
                        return await LoadManagerApplyLeavePage(managerId.Value);
                    }
                }

                // Check if enough balance AVAILABLE (but don't deduct)
                if (model.TotalDays > balance.Remaining)
                {
                    TempData["ErrorMessage"] = $"Not enough leave balance. Available: {balance.Remaining} days";
                    return await LoadManagerApplyLeavePage(managerId.Value);
                }

                // Check for overlapping leaves
                var overlappingLeaves = await _context.LeaveApplications
                    .Where(l => l.UserId == managerId &&
                               l.Status != "Rejected" &&
                               l.Status != "Cancelled" &&
                               ((model.StartDate >= l.StartDate && model.StartDate <= l.EndDate) ||
                                (model.EndDate >= l.StartDate && model.EndDate <= l.EndDate) ||
                                (l.StartDate >= model.StartDate && l.StartDate <= model.EndDate)))
                    .ToListAsync();

                if (overlappingLeaves.Any())
                {
                    TempData["ErrorMessage"] = "You have already applied for leave during this period.";
                    return await LoadManagerApplyLeavePage(managerId.Value);
                }

                // Create leave application WITHOUT deducting balance
                _context.LeaveApplications.Add(model);
                await _context.SaveChangesAsync();

                // ✅ Create notification for Admin (Manager's leaves approved by Admin)
                var admins = await _context.Users
                    .Where(u => u.Role == "Admin" && u.IsActive)
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    var leaveType = await _context.LeaveTypes.FindAsync(model.LeaveTypeId);
                    var notification = new Notification
                    {
                        UserId = admin.UserId,
                        Message = $"{manager.FullName} (Manager) has applied for {model.TotalDays} day(s) of {leaveType?.Name ?? "leave"} from {model.StartDate:dd-MMM-yyyy} to {model.EndDate:dd-MMM-yyyy}. Department: {manager.Department?.Name ?? "N/A"}",
                        CreatedOn = DateTime.Now
                    };
                    _context.Notifications.Add(notification);
                }
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Leave application submitted successfully! It will be reviewed by Admin.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return await LoadManagerApplyLeavePage(managerId.Value);
            }
        }

        // ✅ Helper method for manager apply leave page
        private async Task<IActionResult> LoadManagerApplyLeavePage(int managerId)
        {
            var leaveTypes = await _context.LeaveTypes.ToListAsync();
            ViewBag.LeaveTypes = new SelectList(leaveTypes, "LeaveTypeId", "Name");

            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == managerId)
                .ToListAsync();

            ViewBag.LeaveBalances = balances;
            return View("ApplyLeave");
        }

        // ✅ GET: My Leave Applications (Manager's own leaves)
        public async Task<IActionResult> MyLeaves()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var leaves = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.UserId == managerId)
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            return View(leaves);
        }

        // ✅ GET: Cancel Manager's Own Leave Application
        public async Task<IActionResult> CancelLeave(int id)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var leave = await _context.LeaveApplications
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.UserId == managerId);

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
                leave.ActionDate = DateTime.Now;
                leave.ManagerComments = "Cancelled by manager";

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Leave application cancelled successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error cancelling leave: {ex.Message}";
            }

            return RedirectToAction("MyLeaves");
        }

        // GET: Pending Requests (Team members' leaves)
        public async Task<IActionResult> PendingRequests()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var pendingRequests = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .Where(l => l.User.ManagerId == managerId && l.Status == "Pending")
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            return View(pendingRequests);
        }

        // GET: View Request Details
        public async Task<IActionResult> ViewRequest(int? id, int? userId)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            LeaveApplication leaveRequest = null;

            // If specific leave ID provided
            if (id.HasValue)
            {
                leaveRequest = await _context.LeaveApplications
                    .Include(l => l.User)
                      .ThenInclude(u => u.Department)
                    .Include(l => l.User)
                      .ThenInclude(u => u.Manager)
                    .Include(l => l.LeaveType)
                    .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.ManagerId == managerId);
            }
            // If user ID provided, show their most recent pending request
            else if (userId.HasValue)
            {
                // First check if this user reports to the manager
                var employee = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId && u.ManagerId == managerId);

                if (employee == null)
                {
                    TempData["ErrorMessage"] = "Employee not found or not under your management.";
                    return RedirectToAction("TeamMembers");
                }

                // Get the most recent pending request for this employee
                leaveRequest = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Include(l => l.LeaveType)
                    .Where(l => l.UserId == userId && l.Status == "Pending")
                    .OrderByDescending(l => l.AppliedOn)
                    .FirstOrDefaultAsync();

                if (leaveRequest == null)
                {
                    TempData["InfoMessage"] = "No pending leave requests found for this employee.";
                    return RedirectToAction("EmployeeLeaveHistory", new { employeeId = userId });
                }
            }

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found or you don't have permission.";
                return RedirectToAction("PendingRequests");
            }

            // Get employee's current leave balance
            var leaveBalance = await _context.LeaveBalances
                .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                         lb.LeaveTypeId == leaveRequest.LeaveTypeId);

            ViewBag.LeaveBalance = leaveBalance;
            ViewBag.AvailableBalance = leaveBalance?.Remaining ?? 0;

            return View(leaveRequest);
        }

        // POST: Approve Request - Only deduct balance when approving
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRequest(int id, string comments)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.ManagerId == managerId);

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found.";
                return RedirectToAction("PendingRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("ViewRequest", new { id });
            }

            try
            {
                // Check leave balance at approval time
                var leaveBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                             lb.LeaveTypeId == leaveRequest.LeaveTypeId);

                if (leaveBalance == null)
                {
                    TempData["ErrorMessage"] = "Leave balance not found for employee.";
                    return RedirectToAction("ViewRequest", new { id });
                }

                // Check if enough balance available NOW
                if (leaveRequest.TotalDays > leaveBalance.Remaining)
                {
                    TempData["ErrorMessage"] = $"Cannot approve. Employee has only {leaveBalance.Remaining} days remaining (requested: {leaveRequest.TotalDays} days).";
                    return RedirectToAction("ViewRequest", new { id });
                }

                // Update leave request status
                leaveRequest.Status = "Approved";
                leaveRequest.ManagerComments = comments;
                leaveRequest.ActionDate = DateTime.Now;

                // DEDUCT BALANCE ONLY WHEN APPROVING
                leaveBalance.Used += leaveRequest.TotalDays;
                leaveBalance.Remaining = leaveBalance.TotalAssigned - leaveBalance.Used;

                // Create notification for employee
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your leave request ({leaveRequest.LeaveType.Name}) from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been APPROVED by your manager." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave request approved successfully for {leaveRequest.User.FullName}. Balance deducted: {leaveRequest.TotalDays} days.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error approving leave request: {ex.Message}";
            }

            return RedirectToAction("PendingRequests");
        }

        // POST: Reject Request - No balance deduction on reject
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRequest(int id, string comments)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            // ✅ FIX: Include LeaveType in the query
            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType) // ✅ IMPORTANT: Add this line
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.ManagerId == managerId);

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found.";
                return RedirectToAction("PendingRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("PendingRequests");
            }

            try
            {
                // Update leave request status (NO BALANCE DEDUCTION)
                leaveRequest.Status = "Rejected";
                leaveRequest.ManagerComments = comments;
                leaveRequest.ActionDate = DateTime.Now;

                // ✅ FIX: Check if LeaveType is null before accessing Name property
                string leaveTypeName = leaveRequest.LeaveType?.Name ?? "leave";

                // Create notification for employee
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your {leaveTypeName} request from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been REJECTED by your manager." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave request rejected for {leaveRequest.User.FullName}. No balance deducted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error rejecting leave request: {ex.Message}";
            }

            return RedirectToAction("PendingRequests");
        }

        // ✅ GET: Team Members List
        public async Task<IActionResult> TeamMembers()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var teamMembers = await _context.Users
                .Include(u => u.Department)
                .Where(u => u.ManagerId == managerId && u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            // Get leave balances for each team member
            var teamMemberDetails = new List<TeamMemberDetail>();

            foreach (var member in teamMembers)
            {
                var leaveBalances = await _context.LeaveBalances
                    .Include(lb => lb.LeaveType)
                    .Where(lb => lb.UserId == member.UserId)
                    .ToListAsync();

                // Get pending leave requests count
                var pendingRequests = await _context.LeaveApplications
                    .CountAsync(l => l.UserId == member.UserId && l.Status == "Pending");

                teamMemberDetails.Add(new TeamMemberDetail
                {
                    User = member,
                    LeaveBalances = leaveBalances,
                    PendingRequests = pendingRequests
                });
            }

            ViewBag.TeamMemberDetails = teamMemberDetails;
            return View(teamMembers);
        }

        // Helper class for TeamMembers view
        public class TeamMemberDetail
        {
            public User User { get; set; } = null!;
            public List<LeaveBalance> LeaveBalances { get; set; } = new List<LeaveBalance>();
            public int PendingRequests { get; set; }
        }

        // GET: Team Leave Calendar
        public async Task<IActionResult> TeamCalendar()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            return View();
        }

        // GET: Get Team Leave Events (AJAX for calendar)
        public async Task<JsonResult> GetTeamLeaveEvents()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null)
                return Json(new { success = false, message = "Not authenticated" });

            try
            {
                // Get all leaves for team members (not just approved)
                var teamLeaves = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Include(l => l.LeaveType)
                    .Where(l => l.User.ManagerId == managerId)
                    .Select(l => new
                    {
                        id = l.LeaveId,
                        title = $"{l.User.FullName} - {l.LeaveType.Name}",
                        start = l.StartDate.ToString("yyyy-MM-dd"),
                        end = l.EndDate.AddDays(1).ToString("yyyy-MM-dd"), // Add 1 day for FullCalendar
                        color = l.Status == "Approved" ? "#22c55e" :
                               l.Status == "Pending" ? "#f59e0b" : "#ef4444",
                        employee = l.User.FullName,
                        leaveType = l.LeaveType.Name,
                        days = l.TotalDays,
                        status = l.Status,
                        reason = l.Reason,
                        appliedOn = l.AppliedOn.ToString("yyyy-MM-dd")
                    })
                    .ToListAsync();

                return Json(new { success = true, events = teamLeaves });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Team Leave Reports
        public async Task<IActionResult> TeamReports()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var currentYear = DateTime.Now.Year;

            // Monthly leave trends
            var monthlyLeaves = await _context.LeaveApplications
                .Include(l => l.User)
                .Where(l => l.User.ManagerId == managerId &&
                           l.StartDate.Year == currentYear)
                .GroupBy(l => l.StartDate.Month)
                .Select(g => new
                {
                    Month = g.Key,
                    Count = g.Count(),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected")
                })
                .OrderBy(g => g.Month)
                .ToListAsync();

            ViewBag.MonthlyLeaves = monthlyLeaves;

            // Leave type distribution
            var leaveTypeStats = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.User.ManagerId == managerId &&
                           l.StartDate.Year == currentYear)
                .GroupBy(l => l.LeaveType.Name)
                .Select(g => new
                {
                    LeaveType = g.Key,
                    Count = g.Count(),
                    TotalDays = g.Sum(x => x.TotalDays)
                })
                .ToListAsync();

            ViewBag.LeaveTypeStats = leaveTypeStats;

            // Employee-wise leave summary
            var employeeStats = await _context.LeaveApplications
                .Include(l => l.User)
                .Where(l => l.User.ManagerId == managerId &&
                           l.StartDate.Year == currentYear)
                .GroupBy(l => new { l.UserId, l.User.FullName })
                .Select(g => new
                {
                    EmployeeName = g.Key.FullName,
                    TotalLeaves = g.Count(),
                    TotalDays = g.Sum(x => x.TotalDays),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected")
                })
                .OrderByDescending(g => g.TotalDays)
                .ToListAsync();

            ViewBag.EmployeeStats = employeeStats;

            return View();
        }

        // ✅ NEW: View Manager's Leave Details
        public async Task<IActionResult> LeaveDetails(int id)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            var leave = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.UserId == managerId);

            if (leave == null)
            {
                TempData["ErrorMessage"] = "Leave application not found.";
                return RedirectToAction("MyLeaves");
            }

            // Get leave balance
            var balance = await _context.LeaveBalances
                .FirstOrDefaultAsync(b => b.UserId == managerId && b.LeaveTypeId == leave.LeaveTypeId);

            ViewBag.LeaveBalance = balance;

            return View(leave);
        }

        // GET: Mark Notification as Read
        public async Task<IActionResult> MarkNotificationAsRead(int id)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null) return Json(new { success = false });

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == managerId);

            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false });
        }

        // GET: Get Notifications Count (AJAX)
        public async Task<JsonResult> GetNotificationsCount()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null) return Json(new { count = 0 });

            var count = await _context.Notifications
                .CountAsync(n => n.UserId == managerId && !n.IsRead);

            return Json(new { count });
        }

        // GET: Leave Balance Summary for Manager
        public async Task<JsonResult> GetLeaveBalanceSummary()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null) return Json(new { success = false });

            var balances = await _context.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.UserId == managerId)
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

        // GET: Team Leave Balance Summary
        public async Task<JsonResult> GetTeamLeaveBalanceSummary()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null) return Json(new { success = false });

            var teamBalances = await _context.LeaveBalances
                .Include(lb => lb.User)
                .Include(lb => lb.LeaveType)
                .Where(lb => lb.User.ManagerId == managerId)
                .GroupBy(lb => lb.LeaveType.Name)
                .Select(g => new
                {
                    leaveType = g.Key,
                    totalAssigned = g.Sum(x => x.TotalAssigned),
                    totalUsed = g.Sum(x => x.Used),
                    totalRemaining = g.Sum(x => x.Remaining)
                })
                .ToListAsync();

            return Json(new { success = true, data = teamBalances });
        }

        // ✅ NEW: View Employee Leave History
        public async Task<IActionResult> EmployeeLeaveHistory(int employeeId)
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null || HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            // Check if employee reports to this manager
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == employeeId && u.ManagerId == managerId);

            if (employee == null)
            {
                TempData["ErrorMessage"] = "Employee not found or not under your management.";
                return RedirectToAction("TeamMembers");
            }

            var leaveHistory = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.UserId == employeeId)
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            var leaveBalances = await _context.LeaveBalances
                .Include(lb => lb.LeaveType)
                .Where(lb => lb.UserId == employeeId)
                .ToListAsync();

            ViewBag.Employee = employee;
            ViewBag.LeaveBalances = leaveBalances;

            return View(leaveHistory);
        }

        // ✅ NEW: Get Dashboard Summary for Manager
        public async Task<JsonResult> GetDashboardSummary()
        {
            var managerId = HttpContext.Session.GetInt32("UserId");
            if (managerId == null) return Json(new { success = false });

            var summary = new
            {
                pendingRequests = await _context.LeaveApplications
                    .CountAsync(l => l.User.ManagerId == managerId && l.Status == "Pending"),
                myPendingLeaves = await _context.LeaveApplications
                    .CountAsync(l => l.UserId == managerId && l.Status == "Pending"),
                teamMembers = await _context.Users
                    .CountAsync(u => u.ManagerId == managerId && u.IsActive),
                availableBalance = await _context.LeaveBalances
                    .Where(b => b.UserId == managerId)
                    .SumAsync(b => b.Remaining)
            };

            return Json(new { success = true, data = summary });
        }
    }
}