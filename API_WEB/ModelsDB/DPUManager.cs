using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("DPU_Management")]
    public class DPUManager
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

        [Required]
        [StringLength(50)]
        public string SerialNumber { get; set; }

        [StringLength(20)]
        public string? TypeBonpile { get; set; }

        [StringLength(70)]
        public string? ModelName { get; set; }

        [StringLength(50)]
        public string? HB_MB { get; set; }

        [StringLength(50)]
        public string? TYPE { get; set; }

        public DateTime? First_Fail_Time { get; set; }

        public string? DescFirstFail { get; set; }

        public string? DDRToolResult { get; set; }

        public int QTY_RAM_FAIL { get; set; } = 0;

        public string? NV_Instruction { get; set; }

        public string? ReworkFXV { get; set; }

        [StringLength(100)]
        public string? CutInBP2 { get; set; }

        [StringLength(200)]
        public string? CurrentStatus { get; set; }

        public string? Remark { get; set; }

        public string? Remark2 { get; set; }

        public DateTime Created_At { get; set; } = DateTime.Now;

        public DateTime Updated_At { get; set; } = DateTime.Now;
    }
}