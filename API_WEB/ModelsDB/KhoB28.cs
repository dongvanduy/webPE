using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("KhoB28")]
    public class KhoB28
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("ID")]
        public int Id { get; set; }

        [Column("SERIAL_NUMBER")]
        [StringLength(100)]
        public string SerialNumber { get; set; } = null!;

        [Column("MODEL_NAME")]
        [StringLength(100)]
        public string? ModelName { get; set; }

        [StringLength(100)]
        public string? Location { get; set; }

        [StringLength(100)]
        public string? InBy { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? InDate { get; set; }

        [StringLength(100)]
        public string? Borrower { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? BorrowTime { get; set; }

        public string? Note { get; set; }
        public string? Status { get; set; }
    }
}
