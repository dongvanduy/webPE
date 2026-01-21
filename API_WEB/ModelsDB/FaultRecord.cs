using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("Fault_Records")]
    public class FaultRecord
    {
        [Key]
        [Column("fault_id")]
        public Guid FaultId { get; set; }

        [Required]
        [Column("serial_number")]
        [MaxLength(50)]
        public string SerialNumber { get; set; } = string.Empty;

        [Required]
        [Column("product_line")]
        [MaxLength(50)]
        public string ProductLine { get; set; } = string.Empty;

        [Required]
        [Column("model_name")]
        [MaxLength(50)]
        public string ModelName { get; set; } = string.Empty;

        [Required]
        [Column("error_code")]
        [MaxLength(500)]
        public string ErrorCode { get; set; } = string.Empty;

        [Column("error_desc")]
        [MaxLength(500)]
        public string? ErrorDesc { get; set; }

        [Column("test_group")]
        [MaxLength(50)]
        public string? TestGroup { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}
