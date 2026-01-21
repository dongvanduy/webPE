using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsOracle
{
    [Table("R_ULT_RESULT_T", Schema = "SFISM4")]
    public class SFISM4_R_ULT_RESULT_T
    {
        [Column("SERIAL_NUMBER")]
        public string SERIAL_NUMBER { get; set; } = string.Empty;

        [Column("GROUP_NAME")]
        public string GROUP_NAME { get; set; } = string.Empty;

        [Column("PASS_DATE")]
        public DateTime? PASS_DATE { get; set; }

        [Column("DATA5")]
        public string? DATA5 { get; set; }
    }
}
