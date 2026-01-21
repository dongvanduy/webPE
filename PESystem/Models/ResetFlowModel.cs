using System.ComponentModel.DataAnnotations;

namespace PESystem.Models
{
    public class ResetFlowModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [StringLength(6, MinimumLength = 6)]
        public string? Otp { get; set; }

        [MinLength(6)]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }

        [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp")]
        [DataType(DataType.Password)]
        public string? ConfirmPassword { get; set; }

        public bool OtpSent { get; set; }
        public string? Message { get; set; }
    }
}
