using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveManagementSystem.Models.Entities
{
    public class User
    {
        [Key]
        public int UserId { get; set; }

        [Required, MaxLength(120)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string PasswordHash { get; set; } = null!;

        [Required]
        public string Role { get; set; } = "Employee"; // Employee | Manager | Admin

        public int? ManagerId { get; set; }
        [ForeignKey("ManagerId")]
        public User? Manager { get; set; }

        public int? DepartmentId { get; set; }
        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }

        public DateTime DateOfJoining { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        // Password fields for form (not stored in DB)
        [NotMapped]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [NotMapped]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }

        // Navigation Properties
        public ICollection<LeaveApplication>? LeaveApplications { get; set; }
        public ICollection<LeaveBalance>? LeaveBalances { get; set; } // ✅ Add this line
        public ICollection<Notification>? Notifications { get; set; }
    }
}