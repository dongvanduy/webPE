using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("BonepileAfterStatusHistory")]
    public class BonepileAfterStatusHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime SnapshotDate { get; set; }

        [Required]
        [MaxLength(100)]
        public string Status { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
