using System.ComponentModel.DataAnnotations;

namespace LeaveManagementSystem.Models.Entities
{
    public class Department
    {
        [Key]
        public int DepartmentId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        // Navigation
        public ICollection<User>? Users { get; set; }
    }
}
