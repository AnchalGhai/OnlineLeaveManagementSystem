using LeaveManagementSystem.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveManagementSystem.Models.Entities
{
    public class LeaveBalance
    {
        [Key]
        public int BalanceId { get; set; }

        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [Required]
        public int LeaveTypeId { get; set; }
        [ForeignKey("LeaveTypeId")]
        public LeaveType LeaveType { get; set; } = null!;

        public int TotalAssigned { get; set; }
        public int Used { get; set; }
        public int Remaining { get; set; }
    }
}