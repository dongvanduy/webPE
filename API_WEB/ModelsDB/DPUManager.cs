using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("DPUManager")]
    public class DPUManager
    {
        [Key]
        [Column("SERIAL_NUMBER")]
        [MaxLength(100)]
        public string SerialNumber { get; set; } = string.Empty;

        [Column("HB_MB")]
        [MaxLength(50)]
        public string HbMb { get; set; } = string.Empty;

        [Column("TYPE_BONEPILE")]
        [MaxLength(50)]
        public string TypeBonepile { get; set; } = string.Empty;

        [Column("FIRST_FAIL_TIME")]
        public DateTime? FirstFailTime { get; set; }

        [Column("DESC_FIRST_FAIL")]
        [MaxLength(4000)]
        public string? DescFirstFail { get; set; }

        [Column("MODEL_NAME")]
        [MaxLength(100)]
        public string? ModelName { get; set; }
    }
}
