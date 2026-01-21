using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("BonepileAfterProductLineAgingHistory")]
    public class BonepileAfterProductLineAgingHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime SnapshotDate { get; set; }

        [Required]
        [MaxLength(100)]
        public string ProductLine { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string AgeRange { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
