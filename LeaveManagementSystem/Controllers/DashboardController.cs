using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LeaveManagementSystem.Controllers
{
    public class DashboardController : Controller
    {
        private readonly DatabaseContext _context;

        public DashboardController(DatabaseContext context)
        {
            _context = context;
        }

        // ---------------------------
        // Admin Dashboard
        // ---------------------------
        public async Task<IActionResult> Admin()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            // Metrics
            ViewBag.TotalUsers = await _context.Users.CountAsync();
            ViewBag.TotalEmployees = await _context.Users.CountAsync(u => u.Role == "Employee");
            ViewBag.TotalManagers = await _context.Users.CountAsync(u => u.Role == "Manager");
            ViewBag.TotalLeaveRequests = await _context.LeaveApplications.CountAsync();
            ViewBag.TotalLeaveTypes = await _context.LeaveTypes.CountAsync();
            ViewBag.TotalDepartments = await _context.Departments.CountAsync();
            ViewBag.PendingApprovals = await _context.LeaveApplications.CountAsync(l => l.Status == "Pending");

            // ✅ Manager leaves pending for Admin approval
            ViewBag.PendingManagerLeaves = await _context.LeaveApplications
                .CountAsync(l => l.Status == "Pending" && l.User.Role == "Manager");

            ViewBag.ApprovedThisMonth = await _context.LeaveApplications
                .CountAsync(l => l.Status == "Approved" &&
                               l.StartDate.Month == DateTime.Now.Month &&
                               l.StartDate.Year == DateTime.Now.Year);

            ViewBag.ThisMonthLeaves = await _context.LeaveApplications
                .CountAsync(l => l.StartDate.Month == DateTime.Now.Month &&
                               l.StartDate.Year == DateTime.Now.Year);

            // Data Lists
            var usersList = await _context.Users
                .AsNoTracking()
                .Include(u => u.Manager)
                .Include(u => u.Department)
                .OrderBy(u => u.UserId)
                .ToListAsync();

            ViewBag.Users = usersList;
            ViewBag.Departments = await _context.Departments.AsNoTracking().ToListAsync();

            // IMPORTANT: Load ONLY REAL database leaves
            var allLeaves = await _context.LeaveApplications
                .AsNoTracking()
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            ViewBag.Leaves = allLeaves;

            // ✅ Recent Manager leaves for Admin approval
            var managerLeaves = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .Where(l => l.Status == "Pending" && l.User.Role == "Manager")
                .OrderByDescending(l => l.AppliedOn)
                .Take(5)
                .ToListAsync();

            ViewBag.ManagerLeaves = managerLeaves;

            return View();
        }

        // ---------------------------
        // ✅ GET: Manager Leave Requests (for Admin approval) - UPDATED
        // ---------------------------
        public async Task<IActionResult> ManagerLeaveRequests()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var managerLeaves = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .Where(l => l.Status == "Pending" && l.User.Role == "Manager")
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            return View(managerLeaves);
        }

        // ---------------------------
        // ✅ GET: View Manager Leave Request Details
        // ---------------------------
        public async Task<IActionResult> ViewManagerLeaveRequest(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.Role == "Manager");

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Manager leave request not found.";
                return RedirectToAction("ManagerLeaveRequests");
            }

            var leaveBalance = await _context.LeaveBalances
                .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                         lb.LeaveTypeId == leaveRequest.LeaveTypeId);

            ViewBag.LeaveBalance = leaveBalance;
            ViewBag.AvailableBalance = leaveBalance?.Remaining ?? 0;

            return View(leaveRequest);
        }

        // ---------------------------
        // ✅ POST: Approve Manager Leave Request
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveManagerLeave(int id, string comments)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.Role == "Manager");

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Manager leave request not found.";
                return RedirectToAction("ManagerLeaveRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("ViewManagerLeaveRequest", new { id });
            }

            try
            {
                var leaveBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                             lb.LeaveTypeId == leaveRequest.LeaveTypeId);

                if (leaveBalance == null)
                {
                    TempData["ErrorMessage"] = "Leave balance not found for manager.";
                    return RedirectToAction("ViewManagerLeaveRequest", new { id });
                }

                if (leaveRequest.TotalDays > leaveBalance.Remaining)
                {
                    TempData["ErrorMessage"] = $"Cannot approve. Manager has only {leaveBalance.Remaining} days remaining (requested: {leaveRequest.TotalDays} days).";
                    return RedirectToAction("ViewManagerLeaveRequest", new { id });
                }

                // Update leave request
                leaveRequest.Status = "Approved";
                leaveRequest.ManagerComments = $"Approved by Admin: {comments}";
                leaveRequest.ActionDate = DateTime.Now;

                // Deduct balance
                leaveBalance.Used += leaveRequest.TotalDays;
                leaveBalance.Remaining = leaveBalance.TotalAssigned - leaveBalance.Used;

                // Create notification
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your {leaveRequest.LeaveType?.Name ?? "leave"} request from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been APPROVED by Admin." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Manager's leave request approved successfully for {leaveRequest.User.FullName}. Balance deducted: {leaveRequest.TotalDays} days.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error approving manager leave request: {ex.Message}";
            }

            return RedirectToAction("ManagerLeaveRequests");
        }

        // ---------------------------
        // ✅ POST: Reject Manager Leave Request
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectManagerLeave(int id, string comments)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id && l.User.Role == "Manager");

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Manager leave request not found.";
                return RedirectToAction("ManagerLeaveRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("ManagerLeaveRequests");
            }

            try
            {
                // Update leave request status (NO BALANCE DEDUCTION)
                leaveRequest.Status = "Rejected";
                leaveRequest.ManagerComments = $"Rejected by Admin: {comments}";
                leaveRequest.ActionDate = DateTime.Now;

                // Create notification
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your {leaveRequest.LeaveType?.Name ?? "leave"} request from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been REJECTED by Admin." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Manager's leave request rejected for {leaveRequest.User.FullName}. No balance deducted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error rejecting manager leave request: {ex.Message}";
            }

            return RedirectToAction("ManagerLeaveRequests");
        }

        // ---------------------------
        // ✅ GET: All Leave Requests
        // ---------------------------
        public async Task<IActionResult> AllLeaveRequests()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var allLeaves = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.User)
                    .ThenInclude(u => u.Manager) // ✅ YEH ADD KAREN
                .Include(l => l.LeaveType)
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            return View(allLeaves);
        }

        // ---------------------------
        // ✅ GET: View Any Leave Request
        // ---------------------------
        public async Task<IActionResult> ViewLeaveRequest(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.User)
                    .ThenInclude(u => u.Manager) // ✅ YEH LINE ADD KAREN
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id);

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found.";
                return RedirectToAction("AllLeaveRequests");
            }

            var leaveBalance = await _context.LeaveBalances
                .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                         lb.LeaveTypeId == leaveRequest.LeaveTypeId);

            ViewBag.LeaveBalance = leaveBalance;
            ViewBag.AvailableBalance = leaveBalance?.Remaining ?? 0;

            return View(leaveRequest);
        }

        // ---------------------------
        // ✅ POST: Approve Any Leave Request
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveLeave(int id, string comments)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id);

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found.";
                return RedirectToAction("AllLeaveRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("ViewLeaveRequest", new { id });
            }

            try
            {
                var leaveBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(lb => lb.UserId == leaveRequest.UserId &&
                                             lb.LeaveTypeId == leaveRequest.LeaveTypeId);

                if (leaveBalance == null)
                {
                    TempData["ErrorMessage"] = "Leave balance not found.";
                    return RedirectToAction("ViewLeaveRequest", new { id });
                }

                if (leaveRequest.TotalDays > leaveBalance.Remaining)
                {
                    TempData["ErrorMessage"] = $"Cannot approve. User has only {leaveBalance.Remaining} days remaining (requested: {leaveRequest.TotalDays} days).";
                    return RedirectToAction("ViewLeaveRequest", new { id });
                }

                // Update leave request
                leaveRequest.Status = "Approved";
                leaveRequest.ManagerComments = $"Approved by Admin: {comments}";
                leaveRequest.ActionDate = DateTime.Now;

                // Deduct balance
                leaveBalance.Used += leaveRequest.TotalDays;
                leaveBalance.Remaining = leaveBalance.TotalAssigned - leaveBalance.Used;

                // Create notification
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your {leaveRequest.LeaveType?.Name ?? "leave"} request from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been APPROVED by Admin." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave request approved successfully for {leaveRequest.User.FullName}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error approving leave request: {ex.Message}";
            }

            return RedirectToAction("ViewLeaveRequest", new { id });
        }

        // ---------------------------
        // ✅ POST: Reject Any Leave Request
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectLeave(int id, string comments)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveRequest = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.LeaveId == id);

            if (leaveRequest == null)
            {
                TempData["ErrorMessage"] = "Leave request not found.";
                return RedirectToAction("AllLeaveRequests");
            }

            if (leaveRequest.Status != "Pending")
            {
                TempData["ErrorMessage"] = $"Leave request is already {leaveRequest.Status.ToLower()}.";
                return RedirectToAction("ViewLeaveRequest", new { id });
            }

            try
            {
                // Update leave request status (NO BALANCE DEDUCTION)
                leaveRequest.Status = "Rejected";
                leaveRequest.ManagerComments = $"Rejected by Admin: {comments}";
                leaveRequest.ActionDate = DateTime.Now;

                // Create notification
                var notification = new Notification
                {
                    UserId = leaveRequest.UserId,
                    Message = $"Your {leaveRequest.LeaveType?.Name ?? "leave"} request from {leaveRequest.StartDate:dd-MMM-yyyy} to {leaveRequest.EndDate:dd-MMM-yyyy} has been REJECTED by Admin." +
                              (string.IsNullOrEmpty(comments) ? "" : $" Comments: {comments}"),
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave request rejected for {leaveRequest.User.FullName}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error rejecting leave request: {ex.Message}";
            }

            return RedirectToAction("ViewLeaveRequest", new { id });
        }

        // ---------------------------
        // Users CRUD
        // ---------------------------
        public async Task<IActionResult> ManageUser(int? id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            User model;
            if (id == null)
            {
                model = new User();
            }
            else
            {
                model = await _context.Users
                    .Include(u => u.Department)
                    .Include(u => u.Manager)
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (model == null) return NotFound();
            }

            await PopulateDropdownsAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageUser(User model)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            ModelState.Remove("PasswordHash");
            ModelState.Remove("DateOfJoining");
            ModelState.Remove("Manager");
            ModelState.Remove("Department");

            // Password validation
            if (model.UserId == 0)
            {
                if (string.IsNullOrWhiteSpace(model.Password))
                    ModelState.AddModelError("Password", "Password is required for new user.");
                if (model.Password != model.ConfirmPassword)
                    ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");
            }
            else if (!string.IsNullOrWhiteSpace(model.Password) && model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");
            }

            // Role-based validation
            if (model.Role == "Employee")
            {
                if (!model.DepartmentId.HasValue)
                {
                    ModelState.AddModelError("DepartmentId", "Department is required for Employees.");
                }
                else
                {
                    if (!model.ManagerId.HasValue)
                    {
                        ModelState.AddModelError("ManagerId", "Manager is required for Employees.");
                    }
                    else
                    {
                        var manager = await _context.Users
                            .Include(u => u.Department)
                            .FirstOrDefaultAsync(u => u.UserId == model.ManagerId &&
                                                     u.Role == "Manager" &&
                                                     u.IsActive);

                        if (manager == null)
                        {
                            ModelState.AddModelError("ManagerId", "Selected manager does not exist or is not active.");
                        }
                        else if (manager.DepartmentId != model.DepartmentId)
                        {
                            ModelState.AddModelError("ManagerId", "Selected manager does not belong to the chosen department.");
                        }
                    }
                }
            }

            if (model.Role == "Admin")
            {
                ModelState.Remove("DepartmentId");
                ModelState.Remove("ManagerId");
                model.DepartmentId = null;
                model.ManagerId = null;
            }
            else if (model.Role == "Manager")
            {
                ModelState.Remove("ManagerId");
                model.ManagerId = null;
            }

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(model);
                return View(model);
            }

            try
            {
                if (model.UserId == 0)
                {
                    // Create new user
                    var newUser = new User
                    {
                        FullName = model.FullName,
                        Email = model.Email,
                        Role = model.Role,
                        DepartmentId = model.DepartmentId,
                        ManagerId = model.ManagerId,
                        DateOfJoining = DateTime.Now,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password!),
                        IsActive = true
                    };

                    _context.Users.Add(newUser);
                    await _context.SaveChangesAsync();

                    // Create leave balances
                    var leaveTypes = await _context.LeaveTypes.ToListAsync();
                    foreach (var lt in leaveTypes)
                    {
                        _context.LeaveBalances.Add(new LeaveBalance
                        {
                            UserId = newUser.UserId,
                            LeaveTypeId = lt.LeaveTypeId,
                            TotalAssigned = lt.MaxPerYear,
                            Used = 0,
                            Remaining = lt.MaxPerYear
                        });
                    }
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"User '{newUser.FullName}' added successfully!";
                    return RedirectToAction("Admin");
                }
                else
                {
                    // Update existing user
                    var existingUser = await _context.Users.FindAsync(model.UserId);
                    if (existingUser == null) return NotFound();

                    // Check for duplicate email
                    if (existingUser.Email != model.Email)
                    {
                        var emailExists = await _context.Users
                            .AnyAsync(u => u.Email == model.Email && u.UserId != model.UserId);
                        if (emailExists)
                        {
                            ModelState.AddModelError("Email", "Email already exists.");
                            await PopulateDropdownsAsync(model);
                            return View(model);
                        }
                    }

                    existingUser.FullName = model.FullName;
                    existingUser.Email = model.Email;
                    existingUser.Role = model.Role;
                    existingUser.IsActive = model.IsActive;

                    // Update department and manager based on role
                    if (model.Role == "Admin")
                    {
                        existingUser.DepartmentId = null;
                        existingUser.ManagerId = null;
                    }
                    else if (model.Role == "Manager")
                    {
                        existingUser.DepartmentId = model.DepartmentId;
                        existingUser.ManagerId = null;
                    }
                    else // Employee
                    {
                        existingUser.DepartmentId = model.DepartmentId;
                        existingUser.ManagerId = model.ManagerId;
                    }

                    // Update password if provided
                    if (!string.IsNullOrWhiteSpace(model.Password))
                    {
                        existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                    }

                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"User '{existingUser.FullName}' updated successfully!";
                    return RedirectToAction("Admin");
                }
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
                await PopulateDropdownsAsync(model);
                return View(model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Unexpected error: {ex.Message}");
                await PopulateDropdownsAsync(model);
                return View(model);
            }
        }

        // ---------------------------
        // Get Managers by Department API
        // ---------------------------
        [HttpGet]
        public async Task<JsonResult> GetManagersByDepartment(int departmentId)
        {
            try
            {
                var managers = await _context.Users
                    .Where(u => u.Role == "Manager" && u.IsActive && u.DepartmentId == departmentId)
                    .OrderBy(u => u.FullName)
                    .Select(u => new
                    {
                        userId = u.UserId,
                        fullName = u.FullName,
                        email = u.Email,
                        departmentName = u.Department != null ? u.Department.Name : ""
                    })
                    .ToListAsync();

                return Json(new { success = true, managers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ---------------------------
        // ✅ FIXED: Get Leaves for Calendar - WORKING VERSION
        // ---------------------------
        [HttpGet]
        public async Task<JsonResult> GetLeavesForCalendar(int? departmentId = null, string status = "all")
        {
            try
            {
                Console.WriteLine($"GetLeavesForCalendar called - Dept: {departmentId}, Status: {status}");

                var query = _context.LeaveApplications
                    .Include(l => l.User)
                        .ThenInclude(u => u.Department)
                    .Include(l => l.LeaveType)
                    .AsQueryable();

                // Apply status filter
                if (!string.IsNullOrEmpty(status) && status != "all")
                {
                    query = query.Where(l => l.Status == status);
                }

                // Apply department filter
                if (departmentId.HasValue && departmentId > 0)
                {
                    query = query.Where(l => l.User.DepartmentId == departmentId.Value);
                }

                var leaves = await query
                    .OrderByDescending(l => l.AppliedOn)
                    .ToListAsync();

                Console.WriteLine($"Found {leaves.Count} leaves in database");

                // Format for FullCalendar
                var events = leaves.Select(l => new
                {
                    id = l.LeaveId,
                    title = $"{l.User?.FullName ?? "Unknown"} - {l.LeaveType?.Name ?? "Leave"} ({l.Status})",
                    start = l.StartDate.ToString("yyyy-MM-dd"),
                    end = l.EndDate.AddDays(1).ToString("yyyy-MM-dd"), // FullCalendar needs exclusive end date
                    backgroundColor = GetColorByStatus(l.Status),
                    borderColor = GetColorByStatus(l.Status),
                    textColor = "#ffffff",
                    allDay = true,
                    extendedProps = new
                    {
                        status = l.Status,
                        employee = l.User?.FullName ?? "Unknown",
                        leaveType = l.LeaveType?.Name ?? "Leave",
                        department = l.User?.Department?.Name ?? "No Department",
                        days = (l.EndDate - l.StartDate).Days + 1,
                        reason = l.Reason ?? "No reason provided",
                        appliedOn = l.AppliedOn.ToString("yyyy-MM-dd"),
                        comments = l.ManagerComments ?? "No comments"
                    }
                }).ToList();

                Console.WriteLine($"Converted to {events.Count} events");

                return Json(events);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeavesForCalendar: {ex.Message}\n{ex.StackTrace}");
                return Json(new { error = ex.Message });
            }
        }

        private string GetColorByStatus(string status)
        {
            return status.ToLower() switch
            {
                "approved" => "#22c55e",   // Green
                "pending" => "#f59e0b",    // Yellow/Orange
                "rejected" => "#ef4444",   // Red
                _ => "#6b7280"             // Gray
            };
        }

        public async Task<IActionResult> ToggleAdminStatus(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var currentAdminId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.UserId == currentAdminId)
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own account!";
                return RedirectToAction("Admin");
            }

            if (user.Role == "Admin" && user.IsActive)
            {
                var activeAdmins = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive && u.UserId != user.UserId);
                if (activeAdmins == 0)
                {
                    TempData["ErrorMessage"] = "Cannot deactivate the last active admin!";
                    return RedirectToAction("Admin");
                }
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Admin {(user.IsActive ? "activated" : "deactivated")} successfully!";
            return RedirectToAction("Admin");
        }

        public async Task<IActionResult> DeleteUser(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            if (user.Role == "Admin")
            {
                TempData["ErrorMessage"] = "Admins cannot be deleted. Use deactivate instead.";
                return RedirectToAction("Admin");
            }

            try
            {
                var balances = await _context.LeaveBalances.Where(lb => lb.UserId == id).ToListAsync();
                _context.LeaveBalances.RemoveRange(balances);

                var leaves = await _context.LeaveApplications.Where(la => la.UserId == id).ToListAsync();
                _context.LeaveApplications.RemoveRange(leaves);

                var notis = await _context.Notifications.Where(n => n.UserId == id).ToListAsync();
                _context.Notifications.RemoveRange(notis);

                var reports = await _context.Users.Where(u => u.ManagerId == id).ToListAsync();
                foreach (var r in reports) r.ManagerId = null;

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "User deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting user: {ex.Message}";
            }

            return RedirectToAction("Admin");
        }

        // ---------------------------
        // Departments CRUD
        // ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDepartment(string Name)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            if (string.IsNullOrWhiteSpace(Name))
            {
                TempData["ErrorMessage"] = "Department name cannot be empty.";
                return RedirectToAction("Admin");
            }

            try
            {
                var exists = await _context.Departments.AnyAsync(d => d.Name.ToLower() == Name.Trim().ToLower());
                if (exists)
                {
                    TempData["ErrorMessage"] = "Department with the same name already exists.";
                    return RedirectToAction("Admin");
                }

                var dept = new Department { Name = Name.Trim() };
                _context.Departments.Add(dept);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Department added successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error adding department: {ex.Message}";
            }

            return RedirectToAction("Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var dept = await _context.Departments.FindAsync(id);
            if (dept == null)
            {
                TempData["ErrorMessage"] = "Department not found.";
                return RedirectToAction("Admin");
            }

            try
            {
                var usersInDept = await _context.Users.Where(u => u.DepartmentId == id).ToListAsync();
                foreach (var u in usersInDept) u.DepartmentId = null;

                _context.Departments.Remove(dept);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Department deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting department: {ex.Message}";
            }

            return RedirectToAction("Admin");
        }

        [HttpGet]
        public async Task<JsonResult> GetManagers()
        {
            try
            {
                var managers = await _context.Users
                    .Where(u => u.Role == "Manager" && u.IsActive)
                    .Select(u => new { u.UserId, u.FullName })
                    .ToListAsync();

                return Json(managers);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> Manager()
        {
            if (HttpContext.Session.GetString("Role") != "Manager")
                return RedirectToAction("Index", "Home");

            int managerId = HttpContext.Session.GetInt32("UserId") ?? 0;
            ViewBag.PendingRequests = await _context.LeaveApplications
                .Include(l => l.User)
                .Where(x => x.User.ManagerId == managerId && x.Status == "Pending")
                .CountAsync();

            return View();
        }

        public async Task<IActionResult> Employee()
        {
            if (HttpContext.Session.GetString("Role") != "Employee")
                return RedirectToAction("Index", "Home");

            int userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            ViewBag.MyLeaves = await _context.LeaveApplications
                .Where(x => x.UserId == userId)
                .CountAsync();

            return View();
        }

        // ---------------------------
        // Helpers
        // ---------------------------
        private async Task PopulateDropdownsAsync(User model)
        {
            ViewBag.RoleList = new List<SelectListItem>
            {
                new SelectListItem { Text = "Admin", Value = "Admin", Selected = model.Role == "Admin" },
                new SelectListItem { Text = "Manager", Value = "Manager", Selected = model.Role == "Manager" },
                new SelectListItem { Text = "Employee", Value = "Employee", Selected = model.Role == "Employee" }
            };

            // Get all departments
            var departments = await _context.Departments.ToListAsync();
            ViewBag.DepartmentList = new SelectList(departments, "DepartmentId", "Name", model.DepartmentId);

            // Manager list initially empty - will be loaded via JavaScript based on selected department
            ViewBag.ManagerList = new SelectList(new List<object>(), "UserId", "FullName");
        }

        // ✅ Create Leave Balances for Existing Users (EXCLUDING Admin)
        [HttpGet]
        public async Task<IActionResult> CreateBalancesForExistingUsers()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            try
            {
                // ✅ Only non-admin users
                var users = await _context.Users
                    .Where(u => u.Role != "Admin" && u.IsActive)
                    .ToListAsync();

                var leaveTypes = await _context.LeaveTypes.ToListAsync();
                int createdCount = 0;

                foreach (var user in users)
                {
                    // Check if user already has leave balances
                    var hasBalances = await _context.LeaveBalances
                        .AnyAsync(b => b.UserId == user.UserId);

                    if (!hasBalances)
                    {
                        foreach (var leaveType in leaveTypes)
                        {
                            var balance = new LeaveBalance
                            {
                                UserId = user.UserId,
                                LeaveTypeId = leaveType.LeaveTypeId,
                                TotalAssigned = leaveType.MaxPerYear,
                                Used = 0,
                                Remaining = leaveType.MaxPerYear
                            };
                            _context.LeaveBalances.Add(balance);
                        }
                        createdCount++;
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave balances created for {createdCount} non-admin users successfully!";
                return RedirectToAction("Admin");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Admin");
            }
        }

        // ✅ Test Calendar Data API
        [HttpGet]
        public async Task<IActionResult> TestCalendarData()
        {
            try
            {
                var leaves = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Include(l => l.LeaveType)
                    .Take(5)
                    .ToListAsync();

                return Json(new
                {
                    count = leaves.Count,
                    leaves = leaves.Select(l => new
                    {
                        id = l.LeaveId,
                        employee = l.User?.FullName,
                        type = l.LeaveType?.Name,
                        status = l.Status,
                        start = l.StartDate.ToString("yyyy-MM-dd"),
                        end = l.EndDate.ToString("yyyy-MM-dd")
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ✅ Get Calendar Stats
        [HttpGet]
        public async Task<JsonResult> GetCalendarStats()
        {
            try
            {
                var total = await _context.LeaveApplications.CountAsync();
                var approved = await _context.LeaveApplications.CountAsync(l => l.Status == "Approved");
                var pending = await _context.LeaveApplications.CountAsync(l => l.Status == "Pending");
                var rejected = await _context.LeaveApplications.CountAsync(l => l.Status == "Rejected");

                return Json(new
                {
                    total,
                    approved,
                    pending,
                    rejected
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ✅ Update Leave Status from Calendar
        [HttpPost]
        public async Task<JsonResult> UpdateLeaveStatus(int id, string status, string comments = "")
        {
            try
            {
                if (HttpContext.Session.GetString("Role") != "Admin")
                    return Json(new { success = false, message = "Unauthorized" });

                var leave = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Include(l => l.LeaveType)
                    .FirstOrDefaultAsync(l => l.LeaveId == id);

                if (leave == null)
                    return Json(new { success = false, message = "Leave not found" });

                if (leave.Status == status)
                    return Json(new { success = false, message = $"Leave is already {status}" });

                // Check balance for approval
                if (status == "Approved")
                {
                    var balance = await _context.LeaveBalances
                        .FirstOrDefaultAsync(b => b.UserId == leave.UserId &&
                                                b.LeaveTypeId == leave.LeaveTypeId);

                    if (balance == null)
                        return Json(new { success = false, message = "Leave balance not found" });

                    if (leave.TotalDays > balance.Remaining)
                        return Json(new { success = false, message = $"Insufficient balance. Available: {balance.Remaining}, Requested: {leave.TotalDays}" });

                    // Deduct balance
                    balance.Used += leave.TotalDays;
                    balance.Remaining = balance.TotalAssigned - balance.Used;
                }

                leave.Status = status;
                leave.ManagerComments = $"Updated via Calendar: {comments}";
                leave.ActionDate = DateTime.Now;

                // Create notification
                var notification = new Notification
                {
                    UserId = leave.UserId,
                    Message = $"Your {leave.LeaveType?.Name ?? "leave"} status has been changed to {status} by Admin.",
                    CreatedOn = DateTime.Now
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Leave status updated to {status}",
                    color = GetColorByStatus(status)
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ✅ NEW: Debug Database Leaves
        [HttpGet]
        public async Task<JsonResult> DebugDatabaseLeaves()
        {
            try
            {
                var leaves = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Include(l => l.LeaveType)
                    .OrderByDescending(l => l.AppliedOn)
                    .ToListAsync();

                return Json(new
                {
                    totalCount = leaves.Count,
                    leaves = leaves.Select(l => new
                    {
                        id = l.LeaveId,
                        employee = l.User?.FullName,
                        leaveType = l.LeaveType?.Name,
                        status = l.Status,
                        start = l.StartDate.ToString("yyyy-MM-dd"),
                        end = l.EndDate.ToString("yyyy-MM-dd"),
                        days = (l.EndDate - l.StartDate).Days + 1,
                        applied = l.AppliedOn.ToString("yyyy-MM-dd")
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ---------------------------
        // ✅ NEW: Leave Policy Management Actions
        // ---------------------------

        // GET: Leave Policies List
        public async Task<IActionResult> LeavePolicies()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var leaveTypes = await _context.LeaveTypes
                .OrderBy(l => l.Name)
                .ToListAsync();

            return View(leaveTypes);
        }

        // GET: Create/Edit Leave Policy
        public async Task<IActionResult> ManageLeavePolicy(int? id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            LeaveType model;
            if (id == null)
            {
                model = new LeaveType();
            }
            else
            {
                model = await _context.LeaveTypes.FindAsync(id);
                if (model == null) return NotFound();
            }

            return View(model);
        }

        // POST: Create/Edit Leave Policy
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageLeavePolicy(LeaveType model)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                // Check for duplicate leave type name (case insensitive)
                var existing = await _context.LeaveTypes
                    .AnyAsync(l => l.Name.ToLower() == model.Name.ToLower() &&
                                  l.LeaveTypeId != model.LeaveTypeId);

                if (existing)
                {
                    ModelState.AddModelError("Name", "Leave type with this name already exists.");
                    return View(model);
                }

                if (model.LeaveTypeId == 0)
                {
                    // Create new leave type
                    _context.LeaveTypes.Add(model);
                    await _context.SaveChangesAsync();

                    // Create leave balances for ALL existing users
                    await CreateLeaveBalancesForAllUsers(model.LeaveTypeId);

                    TempData["SuccessMessage"] = $"Leave type '{model.Name}' added successfully!";
                }
                else
                {
                    // Update existing leave type
                    var existingType = await _context.LeaveTypes.FindAsync(model.LeaveTypeId);
                    if (existingType == null) return NotFound();

                    // Store old max value for balance update
                    int oldMax = existingType.MaxPerYear;
                    existingType.Name = model.Name;
                    existingType.MaxPerYear = model.MaxPerYear;

                    await _context.SaveChangesAsync();

                    // Update leave balances if MaxPerYear changed
                    if (oldMax != model.MaxPerYear)
                    {
                        await UpdateLeaveBalances(model.LeaveTypeId, oldMax, model.MaxPerYear);
                    }

                    TempData["SuccessMessage"] = $"Leave type '{model.Name}' updated successfully!";
                }

                return RedirectToAction("LeavePolicies");
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
                return View(model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Unexpected error: {ex.Message}");
                return View(model);
            }
        }

        // POST: Delete Leave Policy
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLeavePolicy(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            try
            {
                var leaveType = await _context.LeaveTypes
                    .FirstOrDefaultAsync(l => l.LeaveTypeId == id);

                if (leaveType == null)
                {
                    TempData["ErrorMessage"] = "Leave type not found.";
                    return RedirectToAction("LeavePolicies");
                }

                // Check if this leave type is used in any leave applications
                bool hasApplications = await _context.LeaveApplications
                    .AnyAsync(la => la.LeaveTypeId == id);

                if (hasApplications)
                {
                    int appCount = await _context.LeaveApplications
                        .CountAsync(la => la.LeaveTypeId == id);

                    TempData["ErrorMessage"] = $"Cannot delete '{leaveType.Name}' because it is being used in {appCount} leave application(s).";
                    return RedirectToAction("LeavePolicies");
                }

                // Delete associated leave balances first
                var balances = await _context.LeaveBalances
                    .Where(lb => lb.LeaveTypeId == id)
                    .ToListAsync();

                if (balances.Any())
                {
                    _context.LeaveBalances.RemoveRange(balances);
                }

                // Now delete the leave type
                _context.LeaveTypes.Remove(leaveType);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Leave type '{leaveType.Name}' deleted successfully!";
            }
            catch (DbUpdateException ex)
            {
                // Handle foreign key constraint errors
                TempData["ErrorMessage"] = $"Cannot delete leave type. It may be in use. Error: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting leave type: {ex.Message}";
            }

            return RedirectToAction("LeavePolicies");
        }

        // Helper: Create leave balances for all users EXCEPT Admin for a new leave type
        private async Task CreateLeaveBalancesForAllUsers(int leaveTypeId)
        {
            // ✅ Only get NON-ADMIN users (Employees and Managers)
            var users = await _context.Users
                .Where(u => u.Role != "Admin" && u.IsActive)  
                .ToListAsync();

            var leaveType = await _context.LeaveTypes.FindAsync(leaveTypeId);

            if (leaveType == null) return;

            foreach (var user in users)
            {
                // Check if balance already exists
                var existingBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.UserId == user.UserId &&
                                             b.LeaveTypeId == leaveTypeId);

                if (existingBalance == null)
                {
                    var balance = new LeaveBalance
                    {
                        UserId = user.UserId,
                        LeaveTypeId = leaveTypeId,
                        TotalAssigned = leaveType.MaxPerYear,
                        Used = 0,
                        Remaining = leaveType.MaxPerYear
                    };
                    _context.LeaveBalances.Add(balance);
                }
            }

            await _context.SaveChangesAsync();
        }

        // Helper: Update leave balances when MaxPerYear changes (Exclude Admin)
        private async Task UpdateLeaveBalances(int leaveTypeId, int oldMax, int newMax)
        {
            // ✅ Get balances for NON-ADMIN users only
            var balances = await _context.LeaveBalances
                .Include(lb => lb.User)
                .Where(lb => lb.LeaveTypeId == leaveTypeId &&
                            lb.User.Role != "Admin") // ✅ Admin excluded
                .ToListAsync();

            foreach (var balance in balances)
            {
                // Adjust remaining balance based on the difference
                int difference = newMax - oldMax;
                balance.TotalAssigned = newMax;
                balance.Remaining = Math.Max(0, balance.Remaining + difference);

                // Ensure Used doesn't exceed new total
                if (balance.Used > newMax)
                {
                    balance.Used = newMax;
                    balance.Remaining = 0;
                }
            }

            await _context.SaveChangesAsync();
        }


        // ---------------------------
        // ✅ NEW: Comprehensive Admin Report (All in One) - FIXED VERSION
        // ---------------------------
        public async Task<IActionResult> Reports()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;

            // ========== SECTION 1: ORGANIZATION SUMMARY ==========
            ViewBag.TotalActiveEmployees = await _context.Users
                .CountAsync(u => u.Role == "Employee" && u.IsActive);
            ViewBag.TotalActiveManagers = await _context.Users
                .CountAsync(u => u.Role == "Manager" && u.IsActive);
            ViewBag.TotalDepartments = await _context.Departments.CountAsync();
            ViewBag.TotalLeaveTypes = await _context.LeaveTypes.CountAsync();

            // Current Month Statistics
            ViewBag.CurrentMonthLeaves = await _context.LeaveApplications
                .CountAsync(l => l.StartDate.Month == currentMonth &&
                                l.StartDate.Year == currentYear);
            ViewBag.CurrentMonthApproved = await _context.LeaveApplications
                .CountAsync(l => l.StartDate.Month == currentMonth &&
                                l.StartDate.Year == currentYear &&
                                l.Status == "Approved");
            ViewBag.CurrentMonthPending = await _context.LeaveApplications
                .CountAsync(l => l.StartDate.Month == currentMonth &&
                                l.StartDate.Year == currentYear &&
                                l.Status == "Pending");

            // ========== SECTION 2: YEAR-WISE STATISTICS ==========
            var yearWiseStats = await _context.LeaveApplications
                .Where(l => l.StartDate.Year >= currentYear - 2) // Last 3 years
                .GroupBy(l => l.StartDate.Year)
                .Select(g => new
                {
                    Year = g.Key,
                    TotalLeaves = g.Count(),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected"),
                    TotalDays = g.Sum(x => x.TotalDays),
                    AvgDays = g.Average(x => x.TotalDays)
                })
                .OrderByDescending(g => g.Year)
                .ToListAsync();

            ViewBag.YearWiseStats = yearWiseStats;

            // ========== SECTION 3: DEPARTMENT-WISE ANALYSIS ==========
            var deptStats = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Where(l => l.StartDate.Year == currentYear)
                .GroupBy(l => l.User.Department.Name)
                .Select(g => new
                {
                    Department = g.Key ?? "No Department",
                    EmployeeCount = g.Select(x => x.UserId).Distinct().Count(),
                    TotalLeaves = g.Count(),
                    TotalDays = g.Sum(x => x.TotalDays),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected"),
                    AvgDays = g.Average(x => x.TotalDays)
                })
                .OrderByDescending(g => g.TotalLeaves)
                .ToListAsync();

            ViewBag.DepartmentStats = deptStats;

            // ========== SECTION 4: ALL USERS LEAVE REPORT (Employees + Managers) ==========
            var allUsersLeaveReport = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.User.Manager)
                .Where(l => l.StartDate.Year == currentYear &&
                            l.User.Role != "Admin") // ✅ Admin को exclude करें
                .GroupBy(l => new {
                    UserId = l.UserId,
                    FullName = l.User.FullName,
                    Role = l.User.Role,
                    Department = l.User.Department.Name,
                    ManagerName = l.User.Manager.FullName
                })
                .Select(g => new
                {
                    UserId = g.Key.UserId,
                    UserName = g.Key.FullName,
                    Role = g.Key.Role,
                    Department = g.Key.Department,
                    ManagerName = g.Key.ManagerName,
                    TotalLeaves = g.Count(),
                    TotalDays = g.Sum(x => x.TotalDays),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected"),
                    ApprovalRate = (g.Count(x => x.Status == "Approved") * 100.0) / (g.Count() > 0 ? g.Count() : 1)
                })
                .OrderByDescending(g => g.TotalDays)
                .ThenBy(g => g.UserName)
                .ToListAsync();

            // ✅ Zero leaves वाले users भी include करने के लिए
            var allActiveUsers = await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Manager)
                .Where(u => u.Role != "Admin" && u.IsActive) // ✅ Only non-admin users
                .Select(u => new
                {
                    UserId = u.UserId,
                    UserName = u.FullName,
                    Role = u.Role,
                    Department = u.Department.Name,
                    ManagerName = u.Manager.FullName
                })
                .ToListAsync();

            var completeUserReports = new List<dynamic>();

            foreach (var user in allActiveUsers)
            {
                // Find user's leave report
                var userLeaveReport = allUsersLeaveReport
                    .FirstOrDefault(r => r.UserId == user.UserId);

                if (userLeaveReport != null)
                {
                    completeUserReports.Add(userLeaveReport);
                }
                else
                {
                    // Zero leaves वाले users के लिए
                    completeUserReports.Add(new
                    {
                        user.UserId,
                        user.UserName,
                        user.Role,
                        user.Department,
                        user.ManagerName,
                        TotalLeaves = 0,
                        TotalDays = 0,
                        Approved = 0,
                        Pending = 0,
                        Rejected = 0,
                        ApprovalRate = 0.0
                    });
                }
            }

            ViewBag.AllUsersLeaveReport = completeUserReports
                .OrderByDescending(u => u.TotalDays)
                .ThenBy(u => u.UserName)
                .ToList();

            // ========== SECTION 5: MANAGER PERFORMANCE - SIMPLIFIED VERSION ==========
            var managers = await _context.Users
                .Include(u => u.Department)
                .Where(u => u.Role == "Manager" && u.IsActive)
                .ToListAsync();

            var managerReports = new List<dynamic>();

            foreach (var manager in managers)
            {
                var teamLeaves = await _context.LeaveApplications
                    .Include(l => l.User)
                    .Where(l => l.User.ManagerId == manager.UserId &&
                               l.StartDate.Year == currentYear)
                    .ToListAsync();

                // Calculate approval rate
                var processedRequests = teamLeaves.Count(l => l.Status != "Pending");
                var approvalRate = processedRequests > 0 ?
                    Math.Round((teamLeaves.Count(l => l.Status == "Approved") * 100.0) / processedRequests, 1) : 0;

                managerReports.Add(new
                {
                    Manager = manager,
                    Department = manager.Department?.Name ?? "No Department",
                    TeamSize = await _context.Users
                        .CountAsync(u => u.ManagerId == manager.UserId && u.IsActive),
                    TeamLeaves = teamLeaves.Count,
                    TeamApproved = teamLeaves.Count(l => l.Status == "Approved"),
                    TeamPending = teamLeaves.Count(l => l.Status == "Pending"),
                    TeamRejected = teamLeaves.Count(l => l.Status == "Rejected"), // ✅ नया
                    ApprovalRate = approvalRate
                    // ✅ AvgApprovalTime removed
                });
            }

            ViewBag.ManagerReports = managerReports
                .OrderByDescending(m => m.ApprovalRate)
                .ToList();


            // ========== SECTION 6: LEAVE TYPE DISTRIBUTION ==========
            var leaveTypeStats = await _context.LeaveApplications
                .Include(l => l.LeaveType)
                .Where(l => l.StartDate.Year == currentYear)
                .GroupBy(l => new { l.LeaveTypeId, l.LeaveType.Name })
                .Select(g => new
                {
                    LeaveTypeId = g.Key.LeaveTypeId,
                    LeaveType = g.Key.Name,
                    Count = g.Count(),
                    TotalDays = g.Sum(x => x.TotalDays),
                    AvgDays = g.Average(x => x.TotalDays),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending")
                })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            ViewBag.LeaveTypeStats = leaveTypeStats;

            // ========== SECTION 7: MONTHLY TRENDS ==========
            var monthlyTrends = await _context.LeaveApplications
                .Where(l => l.StartDate.Year == currentYear)
                .GroupBy(l => l.StartDate.Month)
                .Select(g => new
                {
                    Month = g.Key,
                    Total = g.Count(),
                    Approved = g.Count(x => x.Status == "Approved"),
                    Pending = g.Count(x => x.Status == "Pending"),
                    Rejected = g.Count(x => x.Status == "Rejected")
                })
                .OrderBy(g => g.Month)
                .ToListAsync();

            ViewBag.MonthlyTrends = monthlyTrends;

            // ========== SECTION 8: LEAVE BALANCE OVERVIEW ==========
            var leaveBalanceOverview = await _context.LeaveBalances
                .Include(lb => lb.LeaveType)
                .Include(lb => lb.User)
                .Where(lb => lb.User.IsActive)
                .GroupBy(lb => lb.LeaveType.Name)
                .Select(g => new
                {
                    LeaveType = g.Key,
                    TotalEmployees = g.Select(x => x.UserId).Distinct().Count(),
                    TotalAssigned = g.Sum(x => x.TotalAssigned),
                    TotalUsed = g.Sum(x => x.Used),
                    TotalRemaining = g.Sum(x => x.Remaining)
                })
                .ToListAsync();

            // Calculate utilization rate client-side
            var balanceReports = new List<dynamic>();
            foreach (var balance in leaveBalanceOverview)
            {
                var utilizationRate = balance.TotalAssigned > 0 ?
                    Math.Round((balance.TotalUsed * 100.0) / balance.TotalAssigned, 1) : 0;

                balanceReports.Add(new
                {
                    balance.LeaveType,
                    balance.TotalEmployees,
                    balance.TotalAssigned,
                    balance.TotalUsed,
                    balance.TotalRemaining,
                    UtilizationRate = utilizationRate
                });
            }

            ViewBag.LeaveBalanceOverview = balanceReports;

            // ========== SECTION 9: RECENT LEAVES (Last 7 days) ==========
            var recentLeaves = await _context.LeaveApplications
                .Include(l => l.User)
                    .ThenInclude(u => u.Department)
                .Include(l => l.LeaveType)
                .Where(l => l.AppliedOn >= DateTime.Now.AddDays(-7))
                .OrderByDescending(l => l.AppliedOn)
                .Take(10)
                .ToListAsync();

            // Format for view
            var recentLeaveReports = recentLeaves.Select(l => new
            {
                l.LeaveId,
                EmployeeName = l.User.FullName,
                Department = l.User.Department?.Name ?? "No Department",
                LeaveType = l.LeaveType.Name,
                l.StartDate,
                l.EndDate,
                l.TotalDays,
                l.Status,
                AppliedOn = l.AppliedOn.ToString("dd-MMM-yyyy HH:mm")
            }).ToList();

            ViewBag.RecentLeaves = recentLeaveReports;

            // ========== SECTION 10: PENDING REQUESTS SUMMARY ==========
            var pendingRequests = await _context.LeaveApplications
                .Include(l => l.User)
                .Include(l => l.LeaveType)
                .Where(l => l.Status == "Pending")
                .ToListAsync();

            var pendingSummary = pendingRequests
                .GroupBy(l => l.User.Role)
                .Select(g => new
                {
                    Role = g.Key,
                    Count = g.Count(),
                    Employees = g.Select(x => new
                    {
                        Name = x.User.FullName,
                        LeaveType = x.LeaveType.Name,
                        Days = x.TotalDays
                    }).Take(5).ToList() // Take only 5 per role
                })
                .ToList();

            ViewBag.PendingSummary = pendingSummary;

            return View();
        }
    }
}