using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("Cookbook_Steps")]
    public class CookbookStep
    {
        [Key]
        [Column("step_id")]
        public Guid StepId { get; set; }

        [Column("cookbook_id")]
        public Guid CookbookId { get; set; }

        [Column("step_order")]
        public int StepOrder { get; set; }

        [Required]
        [Column("step_text")]
        public string StepText { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        public Cookbook? Cookbook { get; set; }
    }
}
