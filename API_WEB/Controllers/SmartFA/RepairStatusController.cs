using System;
using API_WEB.ModelsOracle;
using API_WEB.Services.SmartFA;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Security;
using System.Text;

[ApiController]
[Route("[controller]")]
public class RepairStatusController : ControllerBase
{
    public RepairStatusController(OracleDbContext oracleDbContext, ICellUpdateTracker cellUpdateTracker)
    {
        _oracleDbContext = oracleDbContext ?? throw new ArgumentNullException(nameof(oracleDbContext));
        _cellUpdateTracker = cellUpdateTracker ?? throw new ArgumentNullException(nameof(cellUpdateTracker));
        // Bỏ qua kiểm tra chứng chỉ SSL
        ServicePointManager.ServerCertificateValidationCallback =
            new RemoteCertificateValidationCallback(delegate { return true; });
    }
    private readonly OracleDbContext _oracleDbContext;
    private readonly ICellUpdateTracker _cellUpdateTracker;
    private static readonly HttpClientHandler _httpClientHandler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
    };

    private readonly HttpClient _httpClient = new HttpClient(_httpClientHandler);


    // Gọi tới API repair-status của IT
    [HttpPost("repair-status")]
    public async Task<IActionResult> UpdateRepairStatus([FromBody] RepairStatusRequest request)
    {
        try
        {
            var apiUrl = "https://sfc-portal.cns.myfiinet.com:443/SfcSmartRepair/api/repair_Status";
            return await SendPostRequest(apiUrl, request);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
    private const string ResetLocationEndpoint = "https://sfc-portal.cns.myfiinet.com:443/sfcSmartRepair/api/receiving_status";

    [HttpPost("receiving-status/reset-location")]
    public async Task<IActionResult> ResetLocationForData18Async()
    {
        try
        {
            var serialOwners = await _oracleDbContext.OracleDataRepairTask
                .Where(task => !string.IsNullOrEmpty(task.DATA18) && !string.IsNullOrEmpty(task.SERIAL_NUMBER))
                .Select(task => new
                {
                    task.SERIAL_NUMBER,
                    Owner = task.TESTER ?? string.Empty
                })
                .GroupBy(task => task.SERIAL_NUMBER)
                .Select(group => new
                {
                    serialnumber = group.Key,
                    Owner = group.Select(item => item.Owner)
                        .FirstOrDefault(owner => !string.IsNullOrEmpty(owner)) ?? string.Empty
                })
                .ToListAsync();

            if (serialOwners.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    message = "No serial numbers found with DATA18 value.",
                    processed = 0
                });
            }

            var results = new List<object>();
            var successCount = 0;

            foreach (var task in serialOwners)
            {
                try
                {
                    var (isSuccess, responseMessage) = await PostResetLocationAsync(task.serialnumber, task.Owner);
                    if (isSuccess)
                    {
                        successCount++;
                    }

                    results.Add(new
                    {
                        serialNumber = task.serialnumber,
                        owner = task.Owner,
                        success = isSuccess,
                        message = responseMessage
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        serialNumber = task.serialnumber,
                        owner = task.Owner,
                        success = false,
                        message = ex.Message
                    });
                }
            }

            return Ok(new
            {
                success = true,
                processed = serialOwners.Count,
                succeeded = successCount,
                failed = serialOwners.Count - successCount,
                results
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private async Task<(bool success, string message)> PostResetLocationAsync(string serialNumber, string owner)
    {
        var payload = new
        {
            serialnumbers = serialNumber,
            location = string.Empty,
            owner = owner,
            tag = "Reset Location"
        };

        using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(ResetLocationEndpoint, content);
        var responseMessage = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode, responseMessage);
    }

    // Gọi tới API hand_over_status của IT
    [HttpPost("hand-over-status")]
    public async Task<IActionResult> UpdateHandOverStatus([FromBody] HandOverStatusRequest request)
    {
        try
        {
            var apiUrl = "https://sfc-portal.cns.myfiinet.com:443/SfcSmartRepair/api/hand_over_status";
            return await SendPostRequest(apiUrl, request);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // Gọi tới API receiving_status của IT
    [HttpPost("receiving-status")]
    public async Task<IActionResult> UpdateReceivingStatus([FromBody] ReceivingStatusRequest request)
    {
        try
        {
            var apiUrl = "https://sfc-portal.cns.myfiinet.com:443/SfcSmartRepair/api/receiving_status";
            var result = await SendPostRequest(apiUrl, request);

            if (result is OkObjectResult)
            {
                TrackLocationUpdate(request);
            }

            return result;
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private void TrackLocationUpdate(ReceivingStatusRequest? request)
    {
        if (request == null)
        {
            return;
        }

        var location = request.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            return;
        }

        var type = request.Type;
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("UPDATE_LOCATION", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _cellUpdateTracker.MarkUpdated(location, DateTime.Now);
    }

    // Gọi tới API info-allpart để lấy thông tin TEM ALLPARTS
    [HttpPost("info-allpart")]
    public async Task<IActionResult> GetInfoAllpart([FromBody] InfoAllPartRequest request)
    {
        try
        {
            // Kiểm tra dữ liệu đầu vào
            if (request == null || string.IsNullOrEmpty(request.var_1) || string.IsNullOrEmpty(request.type))
            {
                return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ: type và var_1 là bắt buộc." });
            }

            var apiUrl = "https://vnmbd-apapi-cns.myfiinet.com/all_api/api/public/table";
            var payload = new
            {
                type = request.type,
                item = request.item ?? "",
                var_1 = request.var_1
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Gửi yêu cầu POST tới API gốc
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsStringAsync();
                // Phân tích JSON và ánh xạ dữ liệu
                var jsonResponse = JObject.Parse(responseData);
                var resultArray = jsonResponse["result"] as JArray;

                if (resultArray != null && resultArray.Count > 0)
                {
                    var firstResult = resultArray[0];
                    var mappedResponse = new
                    {
                        part_number = firstResult["CUST_KP_NO"]?.ToString(),
                        vendor_code = firstResult["MFR_KP_NO"]?.ToString(),
                        vendor_name = firstResult["MFR_NAME"]?.ToString(),
                        date_code = firstResult["DATE_CODE"]?.ToString(),
                        lot_code = firstResult["LOT_CODE"]?.ToString(),
                        quantity = firstResult["EXT_QTY"]?.ToString()
                    };

                    return Ok(mappedResponse);
                }
                else
                {
                    return NotFound(new { success = false, message = "Không tìm thấy dữ liệu cho var_1 đã cung cấp." });
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return BadRequest(new { success = false, message = error });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // Phiên bản nạp chồng của API info-allpart (sử dụng cả var_1 và var_2)
    [HttpPost("info-allpart/with-refdes")]
    public async Task<IActionResult> GetInfoAllpartWithRefDes([FromBody] InfoAllPartRequest request)
    {
        try
        {
            // Kiểm tra dữ liệu đầu vào
            if (request == null || string.IsNullOrEmpty(request.type) || string.IsNullOrEmpty(request.var_1) || string.IsNullOrEmpty(request.var_2))
            {
                return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ: type, var_1 và var_2 là bắt buộc." });
            }

            var apiUrl = "https://vnmbd-apapi-cns.myfiinet.com/all_api/api/public/table";
            var payload = new
            {
                type = request.type,
                item = request.item ?? "BY-RE-SN",
                var_1 = request.var_1,
                var_2 = request.var_2
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Gửi yêu cầu POST tới API gốc
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsStringAsync();
                // Phân tích JSON và ánh xạ dữ liệu
                var jsonResponse = JObject.Parse(responseData);
                var resultArray = jsonResponse["result"] as JArray;

                if (resultArray != null && resultArray.Count > 0)
                {
                    var mappedResponse = resultArray.Select(result => new
                    {
                        p_sn = result["P_SN"]?.ToString(),
                        wo = result["WO"]?.ToString(),
                        p_no = result["P_NO"]?.ToString(),
                        tr_sn = result["TR_SN"]?.ToString(),
                        kp_no = result["KP_NO"]?.ToString(),
                        mfr_kp_no = result["MFR_KP_NO"]?.ToString(),
                        date_code = result["DATE_CODE"]?.ToString(),
                        lot_code = result["LOT_CODE"]?.ToString(),
                        mfr_name = result["MFR_NAME"]?.ToString(),
                        process_flag = result["PROCESS_FLAG"]?.ToString(),
                        station = result["STATION"]?.ToString(),
                        group_name = result["GROUP_NAME"]?.ToString(),
                        work_time = result["WORK_TIME"]?.ToString(),
                        ref_des = result["REF_DES"]?.ToString()
                    }).ToList();

                    return Ok(new { result = mappedResponse });
                }
                else
                {
                    return NotFound(new { success = false, message = "Không tìm thấy dữ liệu cho var_1 và var_2 đã cung cấp." });
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return BadRequest(new { success = false, message = error });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // Hàm dùng chung để gửi POST request
    private async Task<IActionResult> SendPostRequest(string apiUrl, object payload)
    {
        try
        {
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Gửi yêu cầu POST
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsStringAsync();
                return Ok(new { success = true, message = responseData });
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return BadRequest(new { success = false, message = error });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // API để kiểm tra Yr by DCLC
    [HttpPost("check-yr-by-dclc")]
    public async Task<IActionResult> CheckYrByDclc([FromBody] CheckYrByDclcRequest request)
    {
        try
        {
            // Kiểm tra dữ liệu đầu vào
            if (request == null ||
                string.IsNullOrEmpty(request.type) ||
                string.IsNullOrEmpty(request.item) ||
                string.IsNullOrEmpty(request.var_1) ||
                string.IsNullOrEmpty(request.var_2) ||
                string.IsNullOrEmpty(request.var_3) ||
                string.IsNullOrEmpty(request.var_4) ||
                string.IsNullOrEmpty(request.var_5) ||
                string.IsNullOrEmpty(request.var_6) ||
                string.IsNullOrEmpty(request.var_7))
            {
                return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ: Tất cả các trường type, item, var_1, var_2, var_3, var_4, var_5, var_6 và var_7 là bắt buộc." });
            }

            var apiUrl = "https://vnmbd-apapi-cns.myfiinet.com/all_api/api/public/table";
            var payload = new
            {
                TYPE = request.type,
                ITEM = request.item,
                VAR_1 = request.var_1,
                VAR_2 = request.var_2,
                VAR_3 = request.var_3,
                VAR_4 = request.var_4,
                VAR_5 = request.var_5,
                VAR_6 = request.var_6,
                VAR_7 = request.var_7
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Gửi yêu cầu POST tới API gốc
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsStringAsync();
                // Phân tích JSON và ánh xạ dữ liệu
                var jsonResponse = JObject.Parse(responseData);
                var resultArray = jsonResponse["result"] as JArray;

                if (resultArray != null && resultArray.Count > 0)
                {
                    if (request.item == "BY-FAIL-SN")
                    {
                        var mappedResponse = resultArray.Select(result => new
                        {
                            p_sn = result["P_SN"]?.ToString(),
                            wo = result["WO"]?.ToString(),
                            p_no = result["P_NO"]?.ToString(),
                            tr_sn = result["TR_SN"]?.ToString(),
                            kp_no = result["KP_NO"]?.ToString(),
                            mfr_kp_no = result["MFR_KP_NO"]?.ToString(),
                            date_code = result["DATE_CODE"]?.ToString(),
                            lot_code = result["LOT_CODE"]?.ToString(),
                            mfr_name = result["MFR_NAME"]?.ToString(),
                            process_flag = result["PROCESS_FLAG"]?.ToString(),
                            station = result["STATION"]?.ToString(),
                            group_name = result["GROUP_NAME"]?.ToString(),
                            work_time = result["WORK_TIME"]?.ToString(),
                            ref_des = result["REF_DES"]?.ToString(),
                            test_code = result["TEST_CODE"]?.ToString(),
                            test_group = result["TEST_GROUP"]?.ToString()
                        }).ToList();

                        return Ok(new { result = mappedResponse });
                    }
                    else if (request.item == "BY-FAIL-TOTAL")
                    {
                        var mappedResponse = resultArray.Select(result => new
                        {
                            p_no = result["P_NO"]?.ToString(),
                            kp_no = result["KP_NO"]?.ToString(),
                            date_code = result["DATE_CODE"]?.ToString(),
                            lot_code = result["LOT_CODE"]?.ToString(),
                            mfr_name = result["MFR_NAME"]?.ToString(),
                            input_qty = result["INPUT_QTY"]?.ToString(),
                            fail_qty = result["FAIL_QTY"]?.ToString(),
                            pass_qty = result["PASS_QTY"]?.ToString(),
                            dfr = result["DFR"]?.ToString()
                        }).ToList();

                        return Ok(new { result = mappedResponse });
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "Giá trị ITEM không hợp lệ. Chỉ hỗ trợ 'BY-FAIL-SN' hoặc 'BY-FAIL-TOTAL'." });
                    }
                }
                else
                {
                    return NotFound(new { success = false, message = "Không tìm thấy dữ liệu cho các tham số đã cung cấp." });
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return BadRequest(new { success = false, message = error });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

}

// Các lớp dữ liệu cho yêu cầu API
public class RepairStatusRequest
{
    [JsonProperty("serialnumbers")]
    public string? SerialNumbers { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("status")]
    public string? Status { get; set; }

    [JsonProperty("emp_id")]
    public string? EmployeeId { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("tag")]
    public string? Tag { get; set; }

    [JsonProperty("hand_over_status")]
    public string? HandOverStatus { get; set; }
}

public class HandOverStatusRequest
{
    [JsonProperty("serialnumbers")]
    public string? SerialNumbers { get; set; }

    [JsonProperty("hand_over_status")]
    public string? HandOverStatus { get; set; }

    [JsonProperty("tag")]
    public string? Tag { get; set; }
}

public class ReceivingStatusRequest
{
    [JsonProperty("serialnumbers")]
    public string? SerialNumbers { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("owner")]
    public string? Owner { get; set; }

    [JsonProperty("location")]
    public string? Location { get; set; }

    [JsonProperty("tag")]
    public string? Tag { get; set; }
}

public class InfoAllPartRequest
{
    [JsonProperty("type")]
    public string? type { get; set; }

    [JsonProperty("item")]
    public string? item { get; set; }

    [JsonProperty("var_1")]
    public string? var_1 { get; set; }

    [JsonProperty("var_2")]
    public string? var_2 { get; set; }
}

public class CheckYrByDclcRequest
{
    [JsonProperty("type")]
    public string? type { get; set; }

    [JsonProperty("item")]
    public string? item { get; set; }

    [JsonProperty("var_1")]
    public string? var_1 { get; set; }

    [JsonProperty("var_2")]
    public string? var_2 { get; set; }

    [JsonProperty("var_3")]
    public string? var_3 { get; set; }

    [JsonProperty("var_4")]
    public string? var_4 { get; set; }

    [JsonProperty("var_5")]
    public string? var_5 { get; set; }

    [JsonProperty("var_6")]
    public string? var_6 { get; set; }

    [JsonProperty("var_7")]
    public string? var_7 { get; set; }
}