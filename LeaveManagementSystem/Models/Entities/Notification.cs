using LeaveManagementSystem.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveManagementSystem.Models.Entities
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public string Message { get; set; } = null!;
        public DateTime CreatedOn { get; set; } = DateTime.Now;

        public bool IsRead { get; set; } = false;
    }
}
