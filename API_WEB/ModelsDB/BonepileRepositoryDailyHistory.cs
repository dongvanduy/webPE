using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("BonepileRepositoryDailyHistory")]
    public class BonepileRepositoryDailyHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime SnapshotDate { get; set; }

        [Required]
        [MaxLength(20)]
        public string Repository { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
