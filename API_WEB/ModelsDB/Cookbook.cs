using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("Cookbooks")]
    public class Cookbook
    {
        [Key]
        [Column("cookbook_id")]
        public Guid CookbookId { get; set; }

        [Required]
        [Column("type_cb")]
        [MaxLength(10)]
        public string TypeCb { get; set; } = string.Empty;

        [Required]
        [Column("model_name")]
        [MaxLength(100)]
        public string ModelName { get; set; } = string.Empty;

        [Required]
        [Column("error_code")]
        [MaxLength(50)]
        public string ErrorCode { get; set; } = string.Empty;

        [Required]
        [Column("test_group")]
        [MaxLength(50)]
        public string TestGroup { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        public ICollection<CookbookStep> Steps { get; set; } = new List<CookbookStep>();
    }
}
