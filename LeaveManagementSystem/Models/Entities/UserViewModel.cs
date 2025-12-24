using System.ComponentModel.DataAnnotations;

namespace LeaveManagementSystem.Models.ViewModels
{
    public class UserViewModel
    {
        public int UserId { get; set; }

        [Required, MaxLength(120)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string Role { get; set; } = "Employee";

        public int? ManagerId { get; set; }
        public int? DepartmentId { get; set; }

        public bool IsActive { get; set; } = true;

        // Password fields only for Add User form
        [DataType(DataType.Password)]
        [Required(ErrorMessage = "Password is required for new user")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
