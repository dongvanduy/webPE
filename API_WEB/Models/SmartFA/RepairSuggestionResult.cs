namespace API_WEB.Models.SmartFA
{
    public class RepairSuggestionResult
    {
        public string ReasonCode { get; set; }
        public string ErrorItems { get; set; }
        public int Count { get; set; }
        public decimal RatePercent { get; set; }
    }
}
