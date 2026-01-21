using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("ScrapList")]
    public class ScrapList
    {
        [Key]
        [Column("SN")]
        [Required]
        [StringLength(50)]
        public string SN { get; set; } = null!;

        [Column("KanBanStatus")]
        [Required]
        [StringLength(50)]
        public string KanBanStatus { get; set; } = null!;

        [Column("Sloc")]
        [Required]
        [StringLength(50)]
        public string Sloc { get; set; } = null!;

        [Column("TaskNumber")]
        [StringLength(50)]
        public string? TaskNumber { get; set; } = null!;

        [Column("PO")]
        [StringLength(50)]
        public string? PO { get; set; }

        [Column("CreateBy")]
        [Required]
        [StringLength(50)]
        public string CreatedBy { get; set; } = null!;

        [Column("Cost")]
        [Required]
        [StringLength(50)]
        public string Cost { get; set; } = null!;

        [Column("InternalTask")]
        [Required]
        [StringLength(100)]
        public string InternalTask { get; set; } = null!;

        [Column("Description")]
        [Required]
        [StringLength(100)]
        public string Desc { get; set; } = null!;

        [Column("CreateTime")]
        [Required]
        public DateTime CreateTime { get; set; }

        [Column("ApproveScrapPerson")]
        [Required]
        [StringLength(50)]
        public string ApproveScrapperson { get; set; } = null!;

        [Column("ApplyTaskStatus")]
        [Required]
        public int ApplyTaskStatus { get; set; }

        [Column("FindBoardStatus")]
        [Required]
        [StringLength(100)]
        public string FindBoardStatus { get; set; } = null!;

        [Column("Remark")]
        [StringLength(100)]
        public string? Remark { get; set; }
                
        [Column("ModelName")]
        [StringLength(100)]
        public string? ModelName { get; set; }
        
        [Column("ModelType")]
        [StringLength(100)]
        public string? ModelType { get; set; }

        [Column("Purpose")]
        [Required]
        [StringLength(50)]
        public string Purpose { get; set; } = null!;

        [Column("Category")]
        [Required]
        [StringLength(100)]
        public string Category { get; set; } = null!;

        [Column("ApplyTime")]
        public DateTime? ApplyTime { get; set; }

        [Column("SpeApproveTime")]
        public string? SpeApproveTime { get; set; } = null!;
    }
}