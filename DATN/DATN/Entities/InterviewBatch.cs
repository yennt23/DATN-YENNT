using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DATN.Entities;

[Table("InterviewBatches")]
public class InterviewBatch
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public Guid? JobId { get; set; }

    [Required]
    [MaxLength(50)]
    public string BatchCode { get; set; } = string.Empty;

    public DateTime ScheduledDate { get; set; }

    /// <summary>
    /// Ca phỏng vấn: 'MORNING_08_11' | 'AFTERNOON_13_17'
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string ShiftSlot { get; set; } = "MORNING_08_11";

    [MaxLength(100)]
    public string ShiftName { get; set; } = "Ca phỏng vấn tập trung";

    [MaxLength(200)]
    public string LocationRoom { get; set; } = "Hội trường tuyển dụng A";

    public int MaxCapacity { get; set; } = 50;

    public int CurrentCount { get; set; } = 0;

    public Guid? InterviewerUserId { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Job? Job { get; set; }
}

