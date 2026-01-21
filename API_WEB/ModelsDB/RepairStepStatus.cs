using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("Repair_Step_Status")]
    public class RepairStepStatus
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("fault_id")]
        public Guid FaultId { get; set; }

        [Column("cookbook_id")]
        public Guid CookbookId { get; set; }

        [Column("step_id")]
        public Guid StepId { get; set; }

        [Column("user_repair")]
        [MaxLength(50)]
        public string? UserRepair { get; set; }

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
