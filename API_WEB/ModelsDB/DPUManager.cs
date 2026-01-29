using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

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


        // Trong class DPUManager
        public void UpdateBusinessLogic()
        {
            // 1. Tính QTY_RAM_FAIL dựa trên DDRToolResult
            // Ví dụ: "5400:M113~5,M221; 5600:M223" -> M113, M114, M115, M221, M223 -> QTY = 5
            if (!string.IsNullOrWhiteSpace(DDRToolResult))
            {
                int totalQty = 0;
                // Tách theo dấu chấm phẩy hoặc khoảng trắng để lấy các cụm dữ liệu
                var groups = DDRToolResult.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var group in groups)
                {
                    // Bỏ phần tiền tố trạm (ví dụ "5400:") nếu có
                    var content = group.Contains(':') ? group.Split(':')[1] : group;

                    // Tách các linh kiện bằng dấu phẩy
                    var components = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var item in components)
                    {
                        var part = item.Trim();
                        if (string.IsNullOrEmpty(part)) continue;

                        if (part.Contains('~'))
                        {
                            // Xử lý dải: M113~5
                            var sides = part.Split('~');
                            var startMatch = Regex.Match(sides[0], @"M(\d+)", RegexOptions.IgnoreCase);

                            if (startMatch.Success && sides.Length > 1)
                            {
                                string startNumStr = startMatch.Groups[1].Value; // "113"
                                string endNumPart = sides[1].Trim(); // "5" hoặc "115"

                                int startNum = int.Parse(startNumStr);
                                int endNum;

                                // Nếu phía sau ~ chỉ là 1 chữ số (ví dụ 5), ta thay chữ số cuối của startNum
                                if (endNumPart.Length < startNumStr.Length)
                                {
                                    string prefix = startNumStr.Substring(0, startNumStr.Length - endNumPart.Length);
                                    endNum = int.Parse(prefix + endNumPart);
                                }
                                else
                                {
                                    endNum = int.Parse(endNumPart);
                                }

                                totalQty += (endNum - startNum + 1);
                            }
                        }
                        else if (Regex.IsMatch(part, @"^M\d+", RegexOptions.IgnoreCase))
                        {
                            // Linh kiện đơn lẻ: M221
                            totalQty += 1;
                        }
                    }
                }
                this.QTY_RAM_FAIL = totalQty;
            }

            // 2. Tính TYPE dựa trên ReworkFXV
            if (!string.IsNullOrWhiteSpace(ReworkFXV))
            {
                var val = ReworkFXV.Trim().ToUpper();
                if (val == "U1" || val == "U2")
                {
                    this.TYPE = "BGA";
                }
                else if (val.Contains("M"))
                {
                    this.TYPE = "RAM";
                }
            }

            this.Updated_At = DateTime.Now;
        }
    }
}