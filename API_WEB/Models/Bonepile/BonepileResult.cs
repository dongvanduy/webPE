namespace API_WEB.Models.Bonepile
{
    public class BonepileResult
    {
        public string SN { get; set; }
        public string MODEL_NAME { get; set; }
        public string PRODUCT_LINE { get; set; }
        public string MO_NUMBER { get; set; }
        public string FAIL_STATION { get; set; }
        public string SYMPTOM { get; set; }
        public string ERROR_DESC { get; set; }
        public DateTime TIME { get; set; }
        public string FLAG { get; set; }
        public string PO_NO { get; set; }
        public string PO_ITEM { get; set; }
        public double FAILURE_AGING { get; set; }
        public string WIP_GROUP { get; set; }
        public string VERSION_CODE { get; set; }
        public string WORK_FLAG { get; set; }
        public string ERROR_FLAG { get; set; }
        public string MO_NEW { get; set; }
        public string STATUS { get; set; }
        public DateTime? CHECKIN_REPAIR_TIME { get; set; }
        public DateTime? CHECKOUT_REPAIR_TIME { get; set; }
        public string SCRAP_STATUS { get; set; }
    }

    public class StatusRequest
    {
        public List<string>? Statuses { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
    }

    // Response model for ScrapList Category
    public class ScrapListCategory
    {
        public string SN { get; set; }
        public string Category { get; set; }
    }

    public class StatusRequestBonepile
    {
        public List<string>? Statuses { get; set; }
    }

    public class AdapterRepairRecordDto
    {
        public string Sn { get; set; }
        public string ModelName { get; set; }
        public string MoNumber { get; set; }
        public string ProductLine { get; set; }
        public string ErrorFlag { get; set; }
        public string WorkFlag { get; set; }
        public string WipGroup { get; set; }
        public string Data11 { get; set; }
        public string Status { get; set; }
        public string TestTime { get; set; }
        public string TestCode { get; set; }
        public string ErrorCodeItem { get; set; }
        public string TestGroup { get; set; }
        public string ErrorDesc { get; set; }
        public string Repair { get; set; }
        public string AgingDay { get; set; }
        public DateTime? CheckInDate { get; set; }
        public string GroupTestOff { get; set; }
        public string TestResultOff { get; set; }
        public string DetailTestOff { get; set; }
        public string TimeTestOff { get; set; }
    }

    public class AdapterRepairStatusCountDto
    {
        public string Status { get; set; }
        public int Count { get; set; }
    }

    public class AdapterRepairOverviewResponse
    {
        public int TotalCount { get; set; }
        public List<AdapterRepairStatusCountDto> StatusCounts { get; set; }
        public int Count { get; set; }
        public List<AdapterRepairRecordDto> Data { get; set; }
    }

    public class RepairTaskResult
    {
        public string SERIAL_NUMBER { get; set; }
        public string MODEL_NAME { get; set; }
        public string PRODUCT_LINE { get; set; }
        public string MO_NUMBER { get; set; }
        public string TEST_CODE { get; set; }
        public string ERROR_DESC { get; set; }
        public string WIP_GROUP { get; set; }
        public string TEST_GROUP { get; set; }
        public string TEST_TIME { get; set; }
        public string VERSION_CODE { get; set; }
        public string WORK_FLAG { get; set; }
        public string ERROR_FLAG { get; set; }
        public string DATA11 { get; set; }
        public string REPAIR { get; set; }
        public string? STATUS { get; set; }
        public string? AGING_DAY { get; set; }
        public string? ERROR_ITEM_CODE { get; set; }
        public DateTime? CHECKIN_DATE { get; set; }
        public string? STATION_TEST { get; set; } //TRAM TEST OFF
        public string? DATA2 { get; set; } //KQ TEST OFF
        public string? DATA5 { get; set; } //CHI TIET KQ TEST OFF
        public string? PASS_TIME { get; set; } //THOI GIAN BAT DAU TEST OFF

    }
    // Result model for bonepile after kanban query
    public class BonepileAfterKanbanResult
    {
        public string SERIAL_NUMBER { get; set; }
        public string MO_NUMBER { get; set; }
        public string MODEL_NAME { get; set; }
        public string PRODUCT_LINE { get; set; }
        public string WIP_GROUP_KANBAN { get; set; }
        public string WIP_GROUP_SFC { get; set; }
        public string ERROR_FLAG { get; set; }
        public string WORK_FLAG { get; set; }
        public string TEST_GROUP { get; set; }
        public DateTime? TEST_TIME { get; set; }
        public string TEST_CODE { get; set; }
        public string ERROR_ITEM_CODE { get; set; }
        public string DATA19_COMBINED { get; set; }
        public string ERROR_DESC { get; set; }
        public double? AGING { get; set; }
        public double? AGING_OLD { get; set; }
    }

    public class BonepileAfterKanbanBasicRecord
    {
        public string SN { get; set; }
        public string ModelName { get; set; }
        public string MoNumber { get; set; }
        public string ProductLine { get; set; }
        public string WipGroupSFC { get; set; }
        public string WipGroupKANBAN { get; set; }
        public string ErrorFlag { get; set; }
        public string WorkFlag { get; set; }
        public string TestGroup { get; set; }
        public string TestTime { get; set; }
        public string TestCode { get; set; }
        public string ErrorCodeItem { get; set; }
        public string ErrorDesc { get; set; }
        public double? Aging { get; set; }
        public double? AgingOld { get; set; }
        public string Status { get; set; }
        public string StatusV2 { get; set; }
    }

    public class SerialNumberBonepileRequest
    {
        public List<string> SerialNumbers { get; set; }
    }

    public class BonepileAfterKanbanTestInfoResult
    {
        public string SERIAL_NUMBER { get; set; }
        public string TEST_GROUP { get; set; }
        public DateTime? TEST_TIME { get; set; }
        public string TEST_CODE { get; set; }
        public string ERROR_DESC { get; set; }
        public double? AGING { get; set; }
    }

    public class BonepileNPISerialBefore
    {
        public string SerialNumber { get; set; }
        public string ModelName { get; set; }
        public string ProductLine { get; set; }
        public string MoNumber { get; set; }
        public string ErrorFlag { get; set; }
        public string WorkFlag { get; set; }
        public string WipGroup { get; set; }
        public string TestGroup { get; set; }
        public DateTime? TestTime { get; set; }
        public string TestCode { get; set; }
        public string ErrorItemCode { get; set; }
        public string ErrorDesc { get; set; }
        public string Data11 { get; set; }
        public string Repair { get; set; }
        public string StationTest { get; set; }
        public string Data2 { get; set; }
        public string Data5 { get; set; }
        public DateTime? PassTime { get; set; }
        public DateTime? CheckinDate { get; set; }
        public string AgingDay { get; set; }
    }

    public class BonepileNPISerialAfter
    {
        public string SerialNumber { get; set; }
        public string Fg { get; set; }
        public string MoNumber { get; set; }
        public string ModelName { get; set; }
        public string ProductLine { get; set; }
        public string WipGroupKanban { get; set; }
        public string WipGroup { get; set; }
        public string ErrorFlag { get; set; }
        public string WorkFlag { get; set; }
        public string Repair { get; set; }
        public string TestGroup { get; set; }
        public DateTime? TestTime { get; set; }
        public string TestCode { get; set; }
        public string ErrorItemCode { get; set; }
        public string ErrorDesc { get; set; }
        public double? Aging { get; set; }
        public double? AgingOldest { get; set; }
    }

    public class BonepileNPIProductLineResponse
    {
        public string ProductLine { get; set; }
        public int BeforeCount { get; set; }
        public int AfterCount { get; set; }
        public List<BonepileNPISerialBefore> BeforeDetails { get; set; }
        public List<BonepileNPISerialAfter> AfterDetails { get; set; }
    }
}
