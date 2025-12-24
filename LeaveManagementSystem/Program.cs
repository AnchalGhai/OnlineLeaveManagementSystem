using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// Register DbContext
builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LMSConnection")));

// Add Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Seed Departments (optional, but required for Manager/Employee)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    if (!db.Departments.Any())
    {
        db.Departments.AddRange(
            new Department { Name = "HR" },
            new Department { Name = "IT" },
            new Department { Name = "Finance" }
        );
        db.SaveChanges();
        Console.WriteLine("Seeded Departments: HR, IT, Finance");
    }

    if (!db.LeaveTypes.Any())
    {
        db.LeaveTypes.AddRange(
         new LeaveType
         { Name = "Casual Leave", MaxPerYear = 12 },
         new LeaveType
         {  Name = "Sick Leave",    MaxPerYear = 10 },
         new LeaveType
         { Name = "Earned Leave", MaxPerYear = 15 }
         );
 
         db.SaveChanges();
        Console.WriteLine("Seeded LeaveTyoes: Sick,Casual,Earned");
    }

    // Seed Admin user if not exists
    if (!db.Users.Any(u => u.Role == "Admin"))
    {
        var admin = new User
        {
            FullName = "KulsoomJawed",
            Email = "kulsoom@company.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("kulsoom@123"),
            Role = "Admin",
            IsActive = true,
            DateOfJoining = DateTime.Now,
            DepartmentId = null // Admin has no department
        };

        db.Users.Add(admin);
        db.SaveChanges();
        Console.WriteLine("Seed Admin created: kulsoom@company.com / kulsoom@123");
    }
}

// Configure middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable Session
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
