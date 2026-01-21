using System;
using System.Collections.Generic;

namespace API_WEB.Models.SmartFA
{
    public class RepairStepStatusRequest
    {
        public Guid fault_id { get; set; }
        public Guid? cookbook_id { get; set; }
        public Guid? step_id { get; set; }
        public string? user_repair { get; set; }
        public string? status { get; set; }
        public bool is_suggestion { get; set; }
        public string? suggestion_text { get; set; }
    }

    public class RepairStepDto
    {
        public Guid StepId { get; set; }
        public int Order { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? User { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CookbookDto
    {
        public Guid CookbookId { get; set; }
        public string? Type { get; set; }
        public List<RepairStepDto> Steps { get; set; } = new();
        public string? TestResultNote { get; set; }
    }

    public class FaultDto
    {
        public Guid FaultId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string ProductLine { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string? ErrorDesc { get; set; }
        public string? TestGroup { get; set; }
    }

    public class TestResultDto
    {
        public DateTime? PassDate { get; set; }
        public string? Data5 { get; set; }
        public bool ShowData5 { get; set; }
        public string? DisplayValue { get; set; }
    }

    public class CreateCookbookRequest
    {
        public Guid FaultId { get; set; }
        public string? StepText { get; set; }
    }

    public class RepairResponse
    {
        public FaultDto Fault { get; set; } = new FaultDto();
        public CookbookDto? NvCookbook { get; set; }
        public CookbookDto? FxnCookbook { get; set; }
        public TestResultDto? TestResult { get; set; }
    }
}
