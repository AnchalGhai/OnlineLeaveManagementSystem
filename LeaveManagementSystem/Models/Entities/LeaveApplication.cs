using LeaveManagementSystem.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveManagementSystem.Models.Entities
{
    public class LeaveApplication
    {
        [Key]
        public int LeaveId { get; set; }

        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [Required]
        public int LeaveTypeId { get; set; }
        [ForeignKey("LeaveTypeId")]
        public LeaveType LeaveType { get; set; } = null!;

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        public int TotalDays { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Reason must be between 10 and 500 characters")]
        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";
        // Pending | Approved | Rejected

        public string? ManagerComments { get; set; }

        public DateTime AppliedOn { get; set; } = DateTime.Now;
        public DateTime? ActionDate { get; set; }
    }
}