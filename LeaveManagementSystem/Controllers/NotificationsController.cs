using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace LeaveManagementSystem.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly DatabaseContext _context;

        public NotificationsController(DatabaseContext context)
        {
            _context = context;
        }

        // ✅ GET: Get notifications for current user (AJAX)
        [HttpGet]
        public async Task<JsonResult> GetMyNotifications()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "Unauthorized" });

            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedOn)
                .Take(10)
                .Select(n => new
                {
                    id = n.NotificationId,
                    message = n.Message,
                    createdOn = n.CreatedOn.ToString("dd-MMM-yyyy HH:mm"),
                    isRead = n.IsRead
                })
                .ToListAsync();

            return Json(new { success = true, notifications });
        }

        // ✅ POST: Mark single notification as read (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> MarkAsRead(int id)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId == null)
                    return Json(new { success = false, message = "Unauthorized" });

                var notification = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == userId);

                if (notification == null)
                    return Json(new { success = false, message = "Notification not found" });

                notification.IsRead = true;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Notification marked as read"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        // ✅ POST: Mark all notifications as read (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> MarkAllAsRead()
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId == null)
                    return Json(new { success = false, message = "Unauthorized" });

                var unreadNotifications = await _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .ToListAsync();

                foreach (var notification in unreadNotifications)
                {
                    notification.IsRead = true;
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Marked {unreadNotifications.Count} notifications as read"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        // ✅ GET: Get unread notifications count (AJAX)
        [HttpGet]
        public async Task<JsonResult> GetUnreadCount()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { count = 0 });

            var count = await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Json(new { count });
        }

        // ✅ GET: Get all notifications for current user
        [HttpGet]
        public async Task<JsonResult> GetNotifications()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "Unauthorized" });

            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedOn)
                .Select(n => new
                {
                    id = n.NotificationId,
                    message = n.Message,
                    createdOn = n.CreatedOn.ToString("dd-MMM-yyyy HH:mm"),
                    isRead = n.IsRead
                })
                .ToListAsync();

            return Json(new { success = true, notifications });
        }

        // ✅ POST: Create notification (for Admin/System use)
        [HttpPost]
        public async Task<JsonResult> Create([FromBody] NotificationCreateModel model)
        {
            try
            {
                var notification = new Notification
                {
                    UserId = model.UserId,
                    Message = model.Message,
                    CreatedOn = DateTime.Now,
                    IsRead = false
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Notification created",
                    id = notification.NotificationId
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        // ✅ Helper class for creating notifications
        public class NotificationCreateModel
        {
            public int UserId { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        // ----------- CRUD views (Admin ke liye) -----------
        // GET: Notifications
        public async Task<IActionResult> Index()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "Home");

            var notifications = await _context.Notifications
                .Include(n => n.User)
                .OrderByDescending(n => n.CreatedOn)
                .ToListAsync();

            return View(notifications);
        }

        // GET: Notifications/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var notification = await _context.Notifications
                .Include(n => n.User)
                .FirstOrDefaultAsync(m => m.NotificationId == id);

            if (notification == null)
            {
                return NotFound();
            }

            return View(notification);
        }

        // GET: Notifications/Create
        public IActionResult Create()
        {
            ViewData["UserId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                _context.Users, "UserId", "FullName"
            );
            return View();
        }

        // POST: Notifications/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NotificationId,UserId,Message,IsRead,CreatedOn")] Notification notification)
        {
            if (ModelState.IsValid)
            {
                notification.CreatedOn = DateTime.Now;
                notification.IsRead = false;

                _context.Add(notification);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["UserId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                _context.Users, "UserId", "FullName", notification.UserId
            );
            return View(notification);
        }

        // GET: Notifications/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
            {
                return NotFound();
            }

            ViewData["UserId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                _context.Users, "UserId", "FullName", notification.UserId
            );

            return View(notification);
        }

        // POST: Notifications/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("NotificationId,UserId,Message,IsRead,CreatedOn")] Notification notification)
        {
            if (id != notification.NotificationId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(notification);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!NotificationExists(notification.NotificationId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            ViewData["UserId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                _context.Users, "UserId", "FullName", notification.UserId
            );

            return View(notification);
        }

        // GET: Notifications/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var notification = await _context.Notifications
                .Include(n => n.User)
                .FirstOrDefaultAsync(m => m.NotificationId == id);

            if (notification == null)
            {
                return NotFound();
            }

            return View(notification);
        }

        // POST: Notifications/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification != null)
            {
                _context.Notifications.Remove(notification);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool NotificationExists(int id)
        {
            return _context.Notifications.Any(e => e.NotificationId == id);
        }
    }
}