using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace API_WEB.Controllers.SFC
{
    [Route("[controller]")]
    [ApiController]
    public class SFCController : ControllerBase
    {
        private readonly string _connectionString = "User Id=TE;Password=B05te;Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=10.220.130.220)(PORT=1521)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=vnsfc)))";

        [HttpPost("history-error-by-sn")]
        public async Task<IActionResult> GetErrorHistory([FromBody] HistoryErrorRequest request)
        {
            try
            {
                if (request == null || request.SerialNumbers == null || !request.SerialNumbers.Any() || (request.TypeValue != 1 && request.TypeValue != 2))
                {
                    return BadRequest("Invalid request: SerialNumbers and TypeValue are required. TypeValue must be 1 or 2.");
                }

                // Construct parameter placeholders
                var snPlaceholders = string.Join(", ", request.SerialNumbers.Select((_, i) => $":sn{i}"));
                string query = request.TypeValue == 1 ? GetSqlQueryType1(snPlaceholders) : GetSqlQueryType2(snPlaceholders);

                var results = new List<Dictionary<string, object>>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(query, connection))
                    {
                        // Add parameters to prevent SQL injection
                        for (int i = 0; i < request.SerialNumbers.Count; i++)
                        {
                            command.Parameters.Add(new OracleParameter($"sn{i}", request.SerialNumbers[i]));
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int j = 0; j < reader.FieldCount; j++)
                                {
                                    row[reader.GetName(j)] = reader.IsDBNull(j) ? null : reader.GetValue(j);
                                }
                                results.Add(row);
                            }
                        }
                    }
                }

                if (!results.Any())
                {
                    return NotFound("No error history found for the provided serial numbers.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpPost("search-ult-data")]
        public async Task<IActionResult> SearchULTData([FromBody] ULTDataRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.Type) || request.Data == null || !request.Data.Any())
                {
                    return BadRequest("Invalid request: Type and Data are required.");
                }

                if (request.Type != "SN" && request.Type != "ULT")
                {
                    return BadRequest("Invalid Type: Type must be 'SN' or 'ULT'.");
                }

                // Construct parameter placeholders
                var dataPlaceholders = string.Join(", ", request.Data.Select((_, i) => $":data{i}"));
                string query = request.Type == "SN"
                    ? $"SELECT SERIAL_NUMBER, MODEL_NAME, GROUP_NAME, PASS_TIME TEST_TIME, ULT, ATE_STATION_NO STATION_NAME, DATA1 TEST_ON, DATA2 TEST_OFF, DATA5 TEST_RESULT FROM sfism4.R_ULT_RESULT_T WHERE SERIAL_NUMBER IN ({dataPlaceholders})"
                    : $"SELECT SERIAL_NUMBER, MODEL_NAME, GROUP_NAME, PASS_TIME TEST_TIME, ULT, ATE_STATION_NO STATION_NAME, DATA1 TEST_ON, DATA2 TEST_OFF, DATA5 TEST_RESULT FROM sfism4.R_ULT_RESULT_T WHERE ULT IN ({dataPlaceholders})";

                var results = new List<Dictionary<string, object>>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(query, connection))
                    {
                        // Add parameters to prevent SQL injection
                        for (int i = 0; i < request.Data.Count; i++)
                        {
                            command.Parameters.Add(new OracleParameter($"data{i}", request.Data[i]));
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int j = 0; j < reader.FieldCount; j++)
                                {
                                    row[reader.GetName(j)] = reader.IsDBNull(j) ? null : reader.GetValue(j);
                                }
                                results.Add(row);
                            }
                        }
                    }
                }

                if (!results.Any())
                {
                    return NotFound($"No data found for the provided {request.Type}.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpPost("export-ft-fail")]
        public async Task<IActionResult> ExportFtFailAsync([FromBody] SerialNumbersExportRequest request)
        {
            try
            {
                if (request?.SerialNumbers == null || !request.SerialNumbers.Any())
                {
                    return BadRequest("SerialNumbers is required.");
                }

                var serialNumbers = request.SerialNumbers
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Select(sn => sn.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();

                if (!serialNumbers.Any())
                {
                    return BadRequest("SerialNumbers is required.");
                }

                var results = new List<FtFailRecord>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    const int batchSize = 900; // Oracle supports maximum 1000 parameters per query

                    for (var i = 0; i < serialNumbers.Count; i += batchSize)
                    {
                        var batch = serialNumbers.Skip(i).Take(batchSize).ToList();
                        var parameterPlaceholders = batch.Select((_, index) => $":sn{index}").ToArray();

                        var query = $@"
                            SELECT 
                                COALESCE(a.serial_number, b.SERIAL_NUMBER) as serial_number,
                                a.mo_number, 
                                a.model_name, 
                                a.group_name, 
                                a.error_code, 
                                a.data4, 
                                a.data1 as retest_code, 
                                a.IN_STATION_TIME, 
                                b.WIP_GROUP
                            FROM SFISM4.R107 b
                            LEFT JOIN SFISM4.R_FAIL_ATEDATA_T a
                                ON b.SERIAL_NUMBER = a.SERIAL_NUMBER
                                AND a.GROUP_NAME = 'FT'
                                AND a.DATA1 = 'Fail'
                                AND a.RETEST_COUNT = 1
                            WHERE b.SERIAL_NUMBER IN ({string.Join(", ", parameterPlaceholders)})";

                        using (var command = new OracleCommand(query, connection))
                        {
                            for (var j = 0; j < batch.Count; j++)
                            {
                                command.Parameters.Add(new OracleParameter($"sn{j}", OracleDbType.Varchar2)
                                {
                                    Value = batch[j]
                                });
                            }

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    results.Add(new FtFailRecord
                                    {
                                        SerialNumber = reader["SERIAL_NUMBER"]?.ToString() ?? string.Empty,
                                        MoNumber = reader["MO_NUMBER"]?.ToString() ?? string.Empty,
                                        ModelName = reader["MODEL_NAME"]?.ToString() ?? string.Empty,
                                        GroupName = reader["GROUP_NAME"]?.ToString() ?? string.Empty,
                                        ErrorCode = reader["ERROR_CODE"]?.ToString() ?? string.Empty,
                                        Data4 = reader["DATA4"]?.ToString() ?? string.Empty,
                                        RetestCode = reader["RETEST_CODE"]?.ToString() ?? string.Empty,
                                        TimeTest = reader["IN_STATION_TIME"] as DateTime?,
                                        WipGroup = reader["WIP_GROUP"]?.ToString() ?? string.Empty
                                    });
                                }
                            }
                        }
                    }
                }

                if (!results.Any())
                {
                    return NotFound(new { success = false, message = "Không tìm thấy dữ liệu phù hợp." });
                }

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("FT_Fail");
                    var row = 1;

                    worksheet.Cell(row, 1).Value = "SERIAL_NUMBER";
                    worksheet.Cell(row, 2).Value = "MO_NUMBER";
                    worksheet.Cell(row, 3).Value = "MODEL_NAME";
                    worksheet.Cell(row, 4).Value = "GROUP_NAME";
                    worksheet.Cell(row, 5).Value = "ERROR_CODE";
                    worksheet.Cell(row, 6).Value = "DATA4";
                    worksheet.Cell(row, 7).Value = "RETEST_CODE";
                    worksheet.Cell(row, 8).Value = "IN_STATION_TIME";
                    worksheet.Cell(row, 9).Value = "WIP_GROUP";

                    foreach (var item in results)
                    {
                        row++;
                        worksheet.Cell(row, 1).Value = item.SerialNumber;
                        worksheet.Cell(row, 2).Value = item.MoNumber;
                        worksheet.Cell(row, 3).Value = item.ModelName;
                        worksheet.Cell(row, 4).Value = item.GroupName;
                        worksheet.Cell(row, 5).Value = item.ErrorCode;
                        worksheet.Cell(row, 6).Value = item.Data4;
                        worksheet.Cell(row, 7).Value = item.RetestCode;
                        worksheet.Cell(row, 8).Value = item.TimeTest;
                        worksheet.Cell(row, 9).Value = item.WipGroup;
                    }

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        var fileName = $"ft_fail_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
                        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                    }
                }
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi Oracle: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        [HttpGet("summary-retest-result")]
        public async Task<IActionResult> GetSummaryRetestResult()
        {
            try
            {
                string query = @"
                    SELECT
                        t.shift_date AS test_date,
                        t.time_frame,
                        COUNT(t.SERIAL_NUMBER) AS total_sn_tested,
                        SUM(CASE WHEN t.DATA2 = 'Pass' THEN 1 ELSE 0 END) AS pass_count,
                        SUM(CASE WHEN t.DATA2 = 'Fail' THEN 1 ELSE 0 END) AS fail_count
                    FROM (
                        SELECT
                            A.SERIAL_NUMBER,
                            A.PASS_TIME,
                            A.DATA1,
                            A.DATA2,
                            B.MODEL_SERIAL,
                            TRUNC((A.PASS_TIME - TRUNC(A.PASS_TIME)) * 24 * 60) AS minute_of_day,
                            CASE
                                WHEN TRUNC((A.PASS_TIME - TRUNC(A.PASS_TIME)) * 24 * 60) BETWEEN 450 AND 1169
                                THEN '07:30–19:30'
                                ELSE '19:30–07:30'
                            END AS time_frame,
                            CASE
                                WHEN TRUNC((A.PASS_TIME - TRUNC(A.PASS_TIME)) * 24 * 60) < 450
                                THEN TRUNC(A.PASS_TIME) - 1
                                ELSE TRUNC(A.PASS_TIME)
                            END AS shift_date
                        FROM sfism4.R_ULT_RESULT_T A
                        JOIN SFIS1.C_MODEL_DESC_T B
                          ON A.MODEL_NAME = B.MODEL_NAME
                        WHERE A.PASS_TIME >= TRUNC(SYSDATE - 6) + INTERVAL '7:30' HOUR TO MINUTE
                          AND A.PASS_TIME < TRUNC(SYSDATE + 1) + INTERVAL '7:30' HOUR TO MINUTE
                          AND B.MODEL_SERIAL = 'ADAPTER'
                          AND A.DATA1 = 'N/A'
                    ) t
                    GROUP BY t.shift_date, t.time_frame
                    ORDER BY t.shift_date, t.time_frame";

                var results = new List<Dictionary<string, object>>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int j = 0; j < reader.FieldCount; j++)
                                {
                                    row[reader.GetName(j)] = reader.IsDBNull(j) ? null : reader.GetValue(j);
                                }
                                results.Add(row);
                            }
                        }
                    }
                }

                if (!results.Any())
                {
                    return NotFound("No retest summary data found for the specified criteria.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpPost("summary-retest-model")]
        public async Task<IActionResult> GetSummaryRetestModel([FromBody] RetestModelRequest request)
        {
            try
            {
                if (request == null || request.StartTime == default || request.EndTime == default)
                {
                    return BadRequest("Invalid request: StartTime and EndTime are required.");
                }

                if (request.EndTime <= request.StartTime)
                {
                    return BadRequest("Invalid request: EndTime must be greater than StartTime.");
                }

                string query = @"
                    SELECT 
                        A.MODEL_NAME,
                        COUNT(A.SERIAL_NUMBER) AS TOTAL_TESTS,
                        SUM(CASE WHEN A.DATA2 = 'Pass' THEN 1 ELSE 0 END) AS PASS_COUNT,
                        SUM(CASE WHEN A.DATA2 = 'Fail' THEN 1 ELSE 0 END) AS FAIL_COUNT
                    FROM 
                        sfism4.R_ULT_RESULT_T A
                    INNER JOIN 
                        SFIS1.C_MODEL_DESC_T B
                    ON 
                        A.MODEL_NAME = B.MODEL_NAME
                    WHERE 
                        A.PASS_TIME BETWEEN :startTime AND :endTime
                        AND B.MODEL_SERIAL = 'ADAPTER'
                        AND A.DATA1 = 'N/A'
                    GROUP BY 
                        A.MODEL_NAME
                    ORDER BY 
                        A.MODEL_NAME";

                var results = new List<Dictionary<string, object>>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(query, connection))
                    {
                        // Add parameters to prevent SQL injection
                        command.Parameters.Add(new OracleParameter("startTime", request.StartTime));
                        command.Parameters.Add(new OracleParameter("endTime", request.EndTime));

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int j = 0; j < reader.FieldCount; j++)
                                {
                                    row[reader.GetName(j)] = reader.IsDBNull(j) ? null : reader.GetValue(j);
                                }
                                results.Add(row);
                            }
                        }
                    }
                }

                if (!results.Any())
                {
                    return NotFound("No retest model summary data found for the specified time range.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpPost("yield-rate-by-time")]
        public async Task<IActionResult> GetYieldRateByTime([FromBody] YieldRateRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.StartTime) || string.IsNullOrWhiteSpace(request.EndTime))
                {
                    return BadRequest("Invalid request: StartTime and EndTime are required (YYYYMMDDHH24).");
                }

                if (request.StartTime.Length != 10 || request.EndTime.Length != 10)
                {
                    return BadRequest("Invalid request: StartTime and EndTime must follow YYYYMMDDHH24 format.");
                }

                const string summaryQuery = @"
                    WITH PARAMS AS (
                        SELECT
                            TO_DATE(:startTime, 'YYYYMMDDHH24') AS START_TIME,
                            TO_DATE(:endTime, 'YYYYMMDDHH24') AS END_TIME
                        FROM DUAL
                    ),
                    TARGET_MODELS AS (
                        SELECT DISTINCT MODEL_NAME
                        FROM SFIS1.C_MODEL_DESC_T
                        WHERE MODEL_SERIAL = 'ADAPTER'
                    ),
                    PROD_DATA AS (
                        SELECT
                            r.MODEL_NAME,
                            r.GROUP_NAME,
                            SUM(NVL(r.FIRST_PASS_QTY, 0)) AS SUM_FIRST_PASS,
                            SUM(NVL(r.SECOND_PASS_QTY, 0)) AS SUM_SECOND_PASS,
                            SUM(NVL(r.FAIL_QTY, 0)) AS SUM_FAIL,
                            SUM(NVL(r.FIRST_FAIL_QTY, 0)) AS SUM_FIRST_FAIL
                        FROM SFISM4.r_station_rec_t r
                        INNER JOIN TARGET_MODELS tm ON r.MODEL_NAME = tm.MODEL_NAME
                        CROSS JOIN PARAMS p
                        WHERE TO_DATE(r.WORK_DATE || LPAD(r.WORK_SECTION, 2, '0'), 'YYYYMMDDHH24')
                              BETWEEN p.START_TIME AND p.END_TIME
                        GROUP BY r.MODEL_NAME, r.GROUP_NAME
                    ),
                    REPAIR_RAW_SORTED AS (
                        SELECT
                            rep.MODEL_NAME,
                            rep.TEST_GROUP,
                            rep.SERIAL_NUMBER,
                            ROW_NUMBER() OVER (
                                PARTITION BY rep.SERIAL_NUMBER, rep.MODEL_NAME, rep.TEST_GROUP
                                ORDER BY rep.TIME_REPAIR DESC
                            ) AS RN
                        FROM SFISM4.R109 rep
                        INNER JOIN TARGET_MODELS tm ON rep.MODEL_NAME = tm.MODEL_NAME
                        CROSS JOIN PARAMS p
                        WHERE rep.REASON_CODE IS NOT NULL
                          AND rep.TIME_REPAIR BETWEEN p.START_TIME AND p.END_TIME
                    ),
                    REPAIR_FINAL_DATA AS (
                        SELECT
                            MODEL_NAME,
                            TEST_GROUP AS GROUP_NAME,
                            COUNT(SERIAL_NUMBER) AS REPAIRED_QTY
                        FROM REPAIR_RAW_SORTED
                        WHERE RN = 1
                        GROUP BY MODEL_NAME, TEST_GROUP
                    )
                    SELECT
                        m.MODEL_NAME,
                        m.GROUP_NAME,
                        m.SUM_FIRST_PASS,
                        m.SUM_SECOND_PASS,
                        m.SUM_FAIL,
                        NVL(r.REPAIRED_QTY, 0) AS REPAIRED_QTY,
                        ROUND(m.SUM_FIRST_PASS / NULLIF(m.SUM_FIRST_PASS + m.SUM_FIRST_FAIL, 0) * 100, 2) AS FPY,
                        ROUND((m.SUM_FIRST_PASS + m.SUM_SECOND_PASS) /
                              NULLIF(m.SUM_FIRST_PASS + m.SUM_SECOND_PASS + m.SUM_FAIL, 0) * 100, 2) AS SPY,
                        ROUND((m.SUM_FIRST_PASS + m.SUM_SECOND_PASS + LEAST(NVL(r.REPAIRED_QTY, 0), m.SUM_FAIL)) /
                              NULLIF(m.SUM_FIRST_PASS + m.SUM_SECOND_PASS + m.SUM_FAIL, 0) * 100, 2) AS LPY
                    FROM PROD_DATA m
                    LEFT JOIN REPAIR_FINAL_DATA r
                        ON m.MODEL_NAME = r.MODEL_NAME
                       AND m.GROUP_NAME = r.GROUP_NAME
                    ORDER BY m.MODEL_NAME, m.GROUP_NAME";

                const string failDetailQuery = @"
                    WITH PARAMS AS (
                        SELECT
                            TO_DATE(:startTime, 'YYYYMMDDHH24') AS START_TIME,
                            TO_DATE(:endTime, 'YYYYMMDDHH24') AS END_TIME
                        FROM DUAL
                    ),
                    TARGET_MODELS AS (
                        SELECT DISTINCT MODEL_NAME
                        FROM SFIS1.C_MODEL_DESC_T
                        WHERE MODEL_SERIAL = 'ADAPTER'
                    ),
                    TARGET_FAIL_MOS AS (
                        SELECT DISTINCT
                            r.MO_NUMBER,
                            r.MODEL_NAME,
                            r.GROUP_NAME
                        FROM SFISM4.r_station_rec_t r
                        INNER JOIN TARGET_MODELS tm ON r.MODEL_NAME = tm.MODEL_NAME
                        CROSS JOIN PARAMS p
                        WHERE TO_DATE(r.WORK_DATE || LPAD(r.WORK_SECTION, 2, '0'), 'YYYYMMDDHH24')
                              BETWEEN p.START_TIME AND p.END_TIME
                          AND r.FAIL_QTY > 0
                    ),
                    SN_LATEST_STATUS AS (
                        SELECT
                            rep.SERIAL_NUMBER,
                            rep.MO_NUMBER,
                            rep.MODEL_NAME,
                            rep.TEST_GROUP,
                            rep.TEST_CODE,
                            rep.DATA1,
                            rep.REASON_CODE,
                            rep.TEST_TIME,
                            ROW_NUMBER() OVER (
                                PARTITION BY rep.SERIAL_NUMBER
                                ORDER BY rep.TEST_TIME DESC
                            ) AS RN
                        FROM SFISM4.R109 rep
                        INNER JOIN TARGET_FAIL_MOS t
                            ON rep.MO_NUMBER = t.MO_NUMBER
                           AND rep.MODEL_NAME = t.MODEL_NAME
                           AND rep.TEST_GROUP = t.GROUP_NAME
                        WHERE rep.MO_NUMBER IS NOT NULL
                    )
                    SELECT
                        SERIAL_NUMBER,
                        MO_NUMBER,
                        MODEL_NAME,
                        TEST_GROUP AS FAIL_STATION,
                        TEST_CODE,
                        DATA1 AS ERROR_DESC,
                        TEST_TIME AS FAIL_TIME
                    FROM SN_LATEST_STATUS
                    WHERE RN = 1
                      AND REASON_CODE IS NULL
                    ORDER BY MO_NUMBER, FAIL_TIME DESC";

                var summary = new List<YieldRateSummaryItem>();
                var failDetails = new List<YieldRateFailDetailItem>();

                using (var connection = new OracleConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var summaryCommand = new OracleCommand(summaryQuery, connection))
                    {
                        summaryCommand.Parameters.Add(new OracleParameter("startTime", OracleDbType.Varchar2) { Value = request.StartTime });
                        summaryCommand.Parameters.Add(new OracleParameter("endTime", OracleDbType.Varchar2) { Value = request.EndTime });

                        using (var reader = await summaryCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                summary.Add(new YieldRateSummaryItem
                                {
                                    ModelName = reader["MODEL_NAME"]?.ToString() ?? string.Empty,
                                    GroupName = reader["GROUP_NAME"]?.ToString() ?? string.Empty,
                                    SumFirstPass = reader["SUM_FIRST_PASS"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["SUM_FIRST_PASS"]),
                                    SumSecondPass = reader["SUM_SECOND_PASS"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["SUM_SECOND_PASS"]),
                                    SumFail = reader["SUM_FAIL"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["SUM_FAIL"]),
                                    RepairedQty = reader["REPAIRED_QTY"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["REPAIRED_QTY"]),
                                    Fpy = reader["FPY"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["FPY"]),
                                    Spy = reader["SPY"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["SPY"]),
                                    Lpy = reader["LPY"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["LPY"])
                                });
                            }
                        }
                    }

                    using (var failDetailCommand = new OracleCommand(failDetailQuery, connection))
                    {
                        failDetailCommand.Parameters.Add(new OracleParameter("startTime", OracleDbType.Varchar2) { Value = request.StartTime });
                        failDetailCommand.Parameters.Add(new OracleParameter("endTime", OracleDbType.Varchar2) { Value = request.EndTime });

                        using (var reader = await failDetailCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                failDetails.Add(new YieldRateFailDetailItem
                                {
                                    SerialNumber = reader["SERIAL_NUMBER"]?.ToString() ?? string.Empty,
                                    MoNumber = reader["MO_NUMBER"]?.ToString() ?? string.Empty,
                                    ModelName = reader["MODEL_NAME"]?.ToString() ?? string.Empty,
                                    FailStation = reader["FAIL_STATION"]?.ToString() ?? string.Empty,
                                    TestCode = reader["TEST_CODE"]?.ToString() ?? string.Empty,
                                    ErrorDesc = reader["ERROR_DESC"]?.ToString() ?? string.Empty,
                                    FailTime = reader["FAIL_TIME"] as DateTime?
                                });
                            }
                        }
                    }
                }

                return Ok(new YieldRateResponse
                {
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    Summary = summary,
                    PendingFailDetails = failDetails
                });
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi Oracle: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        private string GetSqlQueryType1(string snPlaceholders)
        {
            return $@"
                SELECT * FROM
                (
                    SELECT 
                        r.SERIAL_NUMBER,
                        ref.KEY_PART_SN
                    FROM (
                        SELECT column_value AS KEY_PART_SN
                        FROM TABLE(SYS.ODCIVARCHAR2LIST({snPlaceholders}))
                    ) ref
                    LEFT JOIN sfism4.r_wip_keyparts_t r
                        ON ref.KEY_PART_SN = r.KEY_PART_SN
                    UNION ALL
                    SELECT 
                        p.SERIAL_NUMBER,
                        ref.KEY_PART_SN
                    FROM (
                        SELECT column_value AS KEY_PART_SN
                        FROM TABLE(SYS.ODCIVARCHAR2LIST({snPlaceholders}))
                    ) ref
                    LEFT JOIN sfism4.p_wip_keyparts_t p
                        ON ref.KEY_PART_SN = p.KEY_PART_SN
                    WHERE p.SERIAL_NUMBER IS NOT NULL
                ) a
                LEFT JOIN
                (
                    SELECT 
                        SERIAL_NUMBER,
                        MO_NUMBER,
                        MODEL_NAME,
                        TEST_TIME,
                        TEST_GROUP,
                        TEST_CODE,
                        DATA1,
                        REASON_CODE 
                    FROM SFISM4.R109 
                    WHERE SERIAL_NUMBER IN ({snPlaceholders})
                    AND TEST_GROUP IN ('ICT', 'FT', 'CTO')
                    UNION ALL
                    SELECT 
                        r.SERIAL_NUMBER,
                        r.MO_NUMBER,
                        r.MODEL_NAME,
                        r.TEST_TIME,
                        r.TEST_GROUP,
                        r.TEST_CODE,
                        r.DATA1,
                        r.REASON_CODE
                    FROM SFISM4.R109 r
                    WHERE r.SERIAL_NUMBER IN (
                        SELECT t.SERIAL_NUMBER
                        FROM sfism4.r_wip_keyparts_t t
                        WHERE t.KEY_PART_SN IN ({snPlaceholders})
                        UNION ALL
                        SELECT t.SERIAL_NUMBER
                        FROM sfism4.p_wip_keyparts_t t
                        WHERE t.KEY_PART_SN IN ({snPlaceholders})
                    )
                    AND r.TEST_GROUP IN ('ICT', 'FT', 'CTO')
                ) k
                ON (k.SERIAL_NUMBER = a.SERIAL_NUMBER OR k.SERIAL_NUMBER = a.KEY_PART_SN)
                WHERE TEST_GROUP IS NOT NULL";
        }

        private string GetSqlQueryType2(string snPlaceholders)
        {
            return $@"
                SELECT * FROM 
                (
                    SELECT SERIAL_NUMBER, KEY_PART_SN 
                    FROM SFISM4.R_WIP_KEYPARTS_T 
                    WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                    AND KEY_PART_NO LIKE 'SFG%'
                    UNION ALL
                    SELECT SERIAL_NUMBER, KEY_PART_SN 
                    FROM SFISM4.P_WIP_KEYPARTS_T 
                    WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                    AND KEY_PART_NO LIKE 'SFG%'
                ) a
                LEFT JOIN
                (
                    SELECT 
                        SERIAL_NUMBER,
                        MO_NUMBER,
                        MODEL_NAME,
                        TEST_TIME,
                        TEST_GROUP,
                        TEST_CODE,
                        DATA1,
                        REASON_CODE 
                    FROM SFISM4.R109 
                    WHERE SERIAL_NUMBER IN (
                        SELECT SERIAL_NUMBER 
                        FROM SFISM4.R_WIP_KEYPARTS_T 
                        WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                        AND KEY_PART_NO LIKE 'SFG%'
                        UNION
                        SELECT KEY_PART_SN 
                        FROM SFISM4.R_WIP_KEYPARTS_T 
                        WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                        AND KEY_PART_NO LIKE 'SFG%'
                        UNION
                        SELECT SERIAL_NUMBER 
                        FROM SFISM4.P_WIP_KEYPARTS_T 
                        WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                        AND KEY_PART_NO LIKE 'SFG%'
                        UNION
                        SELECT KEY_PART_SN 
                        FROM SFISM4.P_WIP_KEYPARTS_T 
                        WHERE SERIAL_NUMBER IN ({snPlaceholders}) 
                        AND KEY_PART_NO LIKE 'SFG%'
                    ) 
                    AND TEST_GROUP IN ('ICT', 'FT', 'CTO')
                ) b
                ON (a.SERIAL_NUMBER = b.SERIAL_NUMBER OR a.KEY_PART_SN = b.SERIAL_NUMBER)";
        }
    }

    public class HistoryErrorRequest
    {
        public List<string> SerialNumbers { get; set; }
        public int TypeValue { get; set; }
    }

    public class ULTDataRequest
    {
        public string Type { get; set; }
        public List<string> Data { get; set; }
    }

    public class RetestModelRequest
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class SerialNumbersExportRequest
    {
        public List<string> SerialNumbers { get; set; }
    }

    public class YieldRateRequest
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    public class YieldRateSummaryItem
    {
        public string ModelName { get; set; }
        public string GroupName { get; set; }
        public decimal SumFirstPass { get; set; }
        public decimal SumSecondPass { get; set; }
        public decimal SumFail { get; set; }
        public decimal RepairedQty { get; set; }
        public decimal? Fpy { get; set; }
        public decimal? Spy { get; set; }
        public decimal? Lpy { get; set; }
    }

    public class YieldRateFailDetailItem
    {
        public string SerialNumber { get; set; }
        public string MoNumber { get; set; }
        public string ModelName { get; set; }
        public string FailStation { get; set; }
        public string TestCode { get; set; }
        public string ErrorDesc { get; set; }
        public DateTime? FailTime { get; set; }
    }

    public class YieldRateResponse
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public List<YieldRateSummaryItem> Summary { get; set; }
        public List<YieldRateFailDetailItem> PendingFailDetails { get; set; }
    }

    public class FtFailRecord
    {
        public string SerialNumber { get; set; }
        public string MoNumber { get; set; }
        public string ModelName { get; set; }
        public string GroupName { get; set; }
        public string ErrorCode { get; set; }
        public string Data4 { get; set; }
        public string RetestCode { get; set; }
        public DateTime? TimeTest { get; set; }
        public string WipGroup { get; set; }
    }
}
