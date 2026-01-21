using System;
using System.Collections.Generic;

namespace API_WEB.Models.SmartFA
{
    public class ViReConfirmDetailDto
    {
        public string SerialNumber { get; set; } = string.Empty;
        public string MONumber { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public DateTime? TestTime { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string? Tester { get; set; }
        public string TestGroup { get; set; } = string.Empty;
        public string? ErrorDesc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PreStatus { get; set; } = string.Empty;
        public DateTime Date3 { get; set; }
    }

    public class ViReConfirmOwnerDto
    {
        public string Owner { get; set; } = string.Empty;
        public int Count { get; set; }
        public int OkCount { get; set; }
        public int NgCount { get; set; }
        public List<ViReConfirmDetailDto> Details { get; set; } = new();
    }
}
