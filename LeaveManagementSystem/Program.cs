using LeaveManagementSystem.Models;
using LeaveManagementSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ✅ 1. ADD AUTHORIZATION SERVICES (MISSING)
builder.Services.AddAuthorization();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Cookies["jwt_token"];
                if (!string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },

            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("Authentication failed: " + context.Exception.Message);
                if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                {
                    context.Response.Cookies.Delete("jwt_token");
                    context.Response.Redirect("/?error=expiry"); // ✅ Redirect to home
                }
                else
                    context.Response.Redirect("/?error=exception"); // ✅ Redirect to home
                return Task.CompletedTask;
            },

            // ✅ FIX FOR 401 WHEN NO TOKEN
            OnChallenge = context =>
            {
                // Only redirect if not already on home page
                if (context.Request.Path != "/" &&
                    !context.Request.Path.StartsWithSegments("/Home"))
                {
                    context.Response.Redirect("/"); // ✅ Redirect to home page
                    context.HandleResponse(); // Stop default 401 response
                }
                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("OnlineLeaveManagementSystemJWTSecretKey12345"))
        };
    });

// Add services
builder.Services.AddControllersWithViews();

// Database context
builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LMSConnection")));

var app = builder.Build();

// ✅ 3. CORRECT MIDDLEWARE ORDER
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting(); // ✅ ROUTING FIRST

app.UseAuthentication(); // ✅ AUTHENTICATION SECOND
app.UseAuthorization();  // ✅ AUTHORIZATION THIRD

// Seed code (keep as is)
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
         { Name = "Sick Leave", MaxPerYear = 10 },
         new LeaveType
         { Name = "Earned Leave", MaxPerYear = 15 }
         );

        db.SaveChanges();
        Console.WriteLine("Seeded LeaveTypes: Sick, Casual, Earned");
    }

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
            DepartmentId = null
        };

        db.Users.Add(admin);
        db.SaveChanges();
        Console.WriteLine("Seed Admin created: kulsoom@company.com / kulsoom@123");
    }
}



app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();