using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("BonepileAfterTotalHistory")]
    public class BonepileAfterTotalHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime SnapshotDate { get; set; }

        public int TotalCount { get; set; }
    }
}
