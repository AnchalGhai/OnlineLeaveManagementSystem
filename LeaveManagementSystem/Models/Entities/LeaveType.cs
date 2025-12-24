using System.ComponentModel.DataAnnotations;

namespace LeaveManagementSystem.Models.Entities
{
    public class LeaveType
    {
        [Key]
        public int LeaveTypeId { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = null!;

        [Required]
        public int MaxPerYear { get; set; }
    }
}
