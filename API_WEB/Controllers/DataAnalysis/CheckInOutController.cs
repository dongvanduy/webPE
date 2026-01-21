using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Microsoft.EntityFrameworkCore;
using API_WEB.ModelsOracle;
using API_WEB.ModelsDB;
using API_WEB.Controllers.Repositories;
using Newtonsoft.Json;
using DocumentFormat.OpenXml.Drawing.Charts;

namespace API_WEB.Controllers.SmartFA
{
    [ApiController]
    [Route("[controller]")]
    public class CheckInOutController : ControllerBase
    {
        private readonly OracleDbContext _oracleContext;
        private readonly CSDL_NE _sqlContext;
        public CheckInOutController(OracleDbContext oracleContext, CSDL_NE sqlContext)
        {
            _oracleContext = oracleContext;
            _sqlContext = sqlContext;
        }

        public class CheckInOutRequest
        {
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
        }

        public class CheckInRecord
        {
            public string SERIAL_NUMBER { get; set; } = string.Empty;
            public string SFG { get; set; } = string.Empty;
            public string FG { get; set; } = string.Empty;
            public string MO_NUMBER { get; set; } = string.Empty;
            public string PRODUCT_LINE { get; set; } = string.Empty;
            public string MODEL_NAME { get; set; } = string.Empty;
            public string P_SENDER { get; set; } = string.Empty;
            public string STATION_NAME { get; set; } = string.Empty;
            public string ERROR_CODE { get; set; } = string.Empty;
            public string REPAIRER { get; set; } = string.Empty;
            public DateTime? IN_DATETIME { get; set; }
            public DateTime? OUT_DATETIME { get; set; }
            public DateTime? TEST_TIME { get; set; }
            public string ERROR_DESC { get; set; } = string.Empty;
            public string ERROR_FLAG { get; set; } = string.Empty;
            public string WIP_GROUP { get; set; } = string.Empty;
            public string WORK_FLAG { get; set; } = string.Empty;
            public string TYPE { get; set; } = string.Empty;
            public string AGING_HOURS { get; set; } = string.Empty;
        }

        private class FindLocationsResponse
        {
            public bool Success { get; set; }

            [JsonProperty("data")]
            public List<LocationItem> Data { get; set; } = new();

            [JsonProperty("notFoundSerialNumbers")]
            public List<string> NotFoundSerialNumbers { get; set; } = new();
        }

        private class LocationItem
        {
            [JsonProperty("serialNumber")]
            public string SerialNumber { get; set; } = string.Empty;

            [JsonProperty("location")]
            public string Location { get; set; } = string.Empty;
        }

        public class CheckOutRecord
        {
            public string SERIAL_NUMBER { get; set; } = string.Empty;
            public string MODEL_NAME { get; set; } = string.Empty;
            public string PRODUCT_LINE { get; set; } = string.Empty;
            public string P_SENDER { get; set; } = string.Empty;
            public string REPAIRER { get; set; } = string.Empty;
            public string STATION_NAME { get; set; } = string.Empty;
            public DateTime? IN_DATETIME { get; set; }
            public DateTime? OUT_DATETIME { get; set; }
            public string ERROR_CODE { get; set; } = string.Empty;
            public string ERROR_DESC { get; set; } = string.Empty;
            public string CHECKIN_STATUS { get; set; } = string.Empty;
            public string ERROR_FLAG { get; set; } = string.Empty;
            public string WIP_GROUP { get; set; } = string.Empty;
            public string WORK_FLAG { get; set; } = string.Empty;
            public string MO_NUMBER { get; set; } = string.Empty;
        }

        public class SAPRecord
        {
            public string SERIAL_NUMBER { get; set; }
            public string SHIPPING_SN2 { get; set; }
            public string GROUP_NAME { get; set; }
            public DateTime? IN_STATION_TIME { get; set; }
            public string MO_NUMBER { get; set; }
            public string MODEL_NAME { get; set; }
            public string KEY_PART_NO { get; set; }
            public string PRODUCT_LINE { get; set; }
            public string MSN { get; set; }
            public string ATE_STATION_NO { get; set; }
            public string EMP_NO { get; set; }
            public string WIP_GROUP { get; set; }
        }


        [HttpGet("GetCheckInOutBeforeKanban")]
        public async Task<IActionResult> GetCheckInOutBeforeKanban(DateTime? startDate, DateTime? endDate)
        {
            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            try
            {
                await connection.OpenAsync();

                var defaultEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 7, 30, 0);
                var end = endDate ?? defaultEnd;
                var start = startDate ?? end.AddDays(-1);

                var checkInQuery = @"SELECT a.SERIAL_NUMBER AS SFG,
                                            a.MO_NUMBER,
                                            a.MODEL_NAME,
                                            b.PRODUCT_LINE,
                                            r107.MO_NUMBER,
                                            r107.WIP_GROUP,
                                            r107.ERROR_FLAG,
                                            r107.WORK_FLAG,
                                            r107.WIP_GROUP,
                                            a.STATION_NAME,
                                            a.P_SENDER,
                                            b.PRODUCT_LINE,
                                            a.REMARK AS ERROR_CODE,
                                            a.IN_DATETIME,
                                            c.ERROR_DESC
                                       FROM SFISM4.R_REPAIR_IN_OUT_T a
                                       INNER JOIN SFIS1.C_MODEL_DESC_T b ON a.MODEL_NAME = b.MODEL_NAME
                                       INNER JOIN SFISM4.R107 r107 ON a.serial_number = r107.serial_number
                                       INNER JOIN SFIS1.C_ERROR_CODE_T c ON a.REMARK = c.ERROR_CODE
                                       WHERE b.MODEL_SERIAL = 'ADAPTER'
                                         AND a.P_SENDER IN ('V0904136','V3209541', 'V0945375', 'V0928908', 'V3245384', 'V3211693','V1097872', 'V3231778')
                                         AND a.IN_DATETIME BETWEEN :startDate AND :endDate
                                         AND NOT REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
                                         AND a.REMARK NOT IN ('CK00')
                                         AND NOT EXISTS (
                                              SELECT 1
                                              FROM sfism4.z_kanban_tracking_t z
                                              WHERE z.serial_number = a.serial_number)";

                var checkInList = new List<CheckInRecord>();
                await using (var cmd = new OracleCommand(checkInQuery, connection))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add(new OracleParameter(":startDate", OracleDbType.Date) { Value = start });
                    cmd.Parameters.Add(new OracleParameter(":endDate", OracleDbType.Date) { Value = end });
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        checkInList.Add(new CheckInRecord
                        {
                            SERIAL_NUMBER = reader["SFG"].ToString() ?? string.Empty,
                            MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                            MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                            WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                            WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                            STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                            P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                            ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                            IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                            ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty
                        });
                    }
                }

                var checkOutQuery = @"SELECT 
    a.SERIAL_NUMBER AS SFG,
    a.MODEL_NAME,
    b.PRODUCT_LINE,
    a.P_SENDER,
    a.REPAIRER,
    a.IN_DATETIME,
    a.OUT_DATETIME,
    a.STATION_NAME,
    r107.MO_NUMBER,
    r107.WIP_GROUP,
    r107.ERROR_FLAG,
    r107.WORK_FLAG,
    a.REMARK AS ERROR_CODE,
    c.ERROR_DESC,
    CASE 
        WHEN a.IN_DATETIME BETWEEN :startDate AND :endDate
        THEN 'CHECKOUT_TRONG_NGAY'
        ELSE 'CHECKOUT_TON'
    END AS CHECKOUT_STATUS
FROM sfism4.r_repair_in_out_t a
INNER JOIN sfis1.c_model_desc_t b 
    ON a.model_name = b.model_name
INNER JOIN sfis1.c_error_code_t c 
    ON a.REMARK = c.ERROR_CODE
INNER JOIN SFISM4.R107 r107 
    ON a.serial_number = r107.serial_number
WHERE 
    b.MODEL_SERIAL = 'ADAPTER'
    AND a.P_SENDER IN ('V0904136','V0945375','V3209541','V0928908','V3245384','V3211693','V1097872')
    AND a.REPAIRER IS NOT NULL
    AND a.IN_DATETIME < a.OUT_DATETIME
    AND r107.ERROR_FLAG != '8'
    AND a.REMARK NOT IN ('CK00')
    AND a.OUT_DATETIME BETWEEN :startDate AND :endDate
    AND NOT REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
    AND NOT EXISTS (
        SELECT 1 
        FROM sfism4.z_kanban_tracking_t z
        WHERE z.serial_number = a.serial_number
    )

UNION ALL

SELECT DISTINCT
    B.SERIAL_NUMBER AS SFG,
    B.MODEL_NAME,
    C.PRODUCT_LINE,
    IN_OUT.P_SENDER,
    B.EMP_NO AS REPAIRER,
    IN_OUT.IN_DATETIME,
    B.IN_STATION_TIME AS OUT_DATETIME,
    IN_OUT.STATION_NAME,
    A.MO_NUMBER,
    A.WIP_GROUP,
    A.ERROR_FLAG,
    A.WORK_FLAG,
    IN_OUT.ERROR_CODE,
    E.ERROR_DESC,
    CASE 
        WHEN IN_OUT.IN_DATETIME BETWEEN :startDate AND :endDate
        THEN 'CHECKOUT_TRONG_NGÀY'
        ELSE 'CHECKOUT_TON'
    END AS CHECKOUT_STATUS
FROM SFISM4.R117 B
LEFT JOIN SFISM4.R107 A 
    ON A.SERIAL_NUMBER = B.SERIAL_NUMBER
LEFT JOIN (
    SELECT 
        R.SERIAL_NUMBER,
        MAX(R.IN_DATETIME) AS IN_DATETIME,
        MAX(R.STATION_NAME) KEEP (DENSE_RANK LAST ORDER BY R.STATION_NAME) AS STATION_NAME,
        MAX(R.P_SENDER)      KEEP (DENSE_RANK LAST ORDER BY R.P_SENDER)      AS P_SENDER,
        MAX(R.REMARK)        KEEP (DENSE_RANK LAST ORDER BY R.REMARK)        AS ERROR_CODE
    FROM SFISM4.R_REPAIR_IN_OUT_T R
    WHERE R.IN_DATETIME IS NOT NULL
    GROUP BY R.SERIAL_NUMBER
) IN_OUT 
    ON IN_OUT.SERIAL_NUMBER = B.SERIAL_NUMBER
LEFT JOIN SFIS1.C_MODEL_DESC_T C 
    ON B.MODEL_NAME = C.MODEL_NAME
INNER JOIN SFIS1.C_ERROR_CODE_T E 
    ON IN_OUT.ERROR_CODE = E.ERROR_CODE
WHERE 
    C.MODEL_SERIAL = 'ADAPTER'
    AND B.WIP_GROUP LIKE '%B31M'
    AND B.IN_STATION_TIME BETWEEN :startDate AND :endDate
    -- ❗ Loại bỏ serial đã có trong truy vấn 1
    AND B.SERIAL_NUMBER NOT IN (
        SELECT a.SERIAL_NUMBER
        FROM sfism4.r_repair_in_out_t a
        INNER JOIN sfis1.c_model_desc_t b ON a.model_name = b.model_name
        INNER JOIN SFISM4.R107 r107       ON a.serial_number = r107.serial_number
        WHERE 
            b.MODEL_SERIAL = 'ADAPTER'
            AND a.P_SENDER IN ('V0904136','V0945375','V3209541','V0928908','V3245384','V3211693','V1097872')
            AND a.REPAIRER IS NOT NULL
            AND a.IN_DATETIME < a.OUT_DATETIME
            AND r107.ERROR_FLAG != '8'
            AND a.REMARK NOT IN ('CK00')
            AND a.OUT_DATETIME BETWEEN :startDate AND :endDate
            AND NOT REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
            AND NOT EXISTS (
                SELECT 1 
                FROM sfism4.z_kanban_tracking_t z
                WHERE z.serial_number = a.serial_number
            )
    )";

                var checkOutList = new List<CheckOutRecord>();
                await using (var cmd = new OracleCommand(checkOutQuery, connection))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add(new OracleParameter(":startDate", OracleDbType.Date) { Value = start });
                    cmd.Parameters.Add(new OracleParameter(":endDate", OracleDbType.Date) { Value = end });
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        checkOutList.Add(new CheckOutRecord
                        {
                            SERIAL_NUMBER = reader["SFG"].ToString() ?? string.Empty,
                            MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                            WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                            MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                            WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                            P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                            REPAIRER = reader["REPAIRER"].ToString() ?? string.Empty,
                            STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                            IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                            OUT_DATETIME = reader["OUT_DATETIME"] as DateTime?,
                            ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                            ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty,
                            CHECKIN_STATUS = reader["CHECKOUT_STATUS"].ToString() ?? string.Empty
                        });
                    }
                }

                var checkOutTrongNgay = checkOutList
                    .Where(c => c.CHECKIN_STATUS == "CHECKOUT_TRONG_NGAY")
                    .ToList();
                var checkOutTonKhoCu = checkOutList
                    .Where(c => c.CHECKIN_STATUS == "CHECKOUT_TON")
                    .ToList();

                //var checkOutSerials = new HashSet<string>(checkOutList.Select(co => co.SERIAL_NUMBER));
                //var tonKhoTrongNgay = checkInList
                //    .Where(ci => !checkOutSerials.Contains(ci.SERIAL_NUMBER))
                //    .ToList();
                var latestCheckOutSerials = checkOutList
                .GroupBy(co => co.SERIAL_NUMBER)
                .Select(g => g.OrderByDescending(x => x.OUT_DATETIME).First())
                .Select(x => x.SERIAL_NUMBER)
                .ToHashSet();

                var tonKhoTrongNgay = checkInList
                    .Where(ci => !latestCheckOutSerials.Contains(ci.SERIAL_NUMBER))
                    .ToList();

                var response = new
                {
                    checkIn = new { count = checkInList.Count, data = checkInList },
                    checkOut = new
                    {
                        count = checkOutList.Count,
                        trongNgay = new { count = checkOutTrongNgay.Count, data = checkOutTrongNgay },
                        tonKhoCu = new { count = checkOutTonKhoCu.Count, data = checkOutTonKhoCu }
                    },
                    tonKhoTrongNgay = new { count = tonKhoTrongNgay.Count, data = tonKhoTrongNgay }
                };

                return Ok(response);
            }
            catch (OracleException ex)
            {
                return StatusCode(500, $"Database error: {ex.Message}");
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        [HttpPost("get-fail-ate")]
        public async Task<IActionResult> GetFailAte([FromBody] string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return BadRequest(new { success = false, message = "serialNumber is required." });
            }

            var sn = serialNumber.Trim().ToUpper();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            try
            {
                await connection.OpenAsync();

                const string sql = @"
            SELECT SERIAL_NUMBER, MODEL_NAME, LINE_NAME, SECTION_NAME, GROUP_NAME, STATION_NAME, ERROR_CODE, DATA4 AS ERROR_DESC,
            DATA12, PASS_COUNT, RETEST_COUNT,DATA6, TEST_VALUE, IN_STATION_TIME, DATA1 AS RESULT, DATA2, TEST_MODE, SCOF_MIN 
            FROM SFISM4.R_FAIL_ATEDATA_T where SERIAL_NUMBER = :sn";

                var results = new List<object>();

                await using var cmd = new OracleCommand(sql, connection);
                cmd.BindByName = true;
                cmd.Parameters.Add(":sn", OracleDbType.Varchar2).Value = sn;

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        serialNumber = reader["SERIAL_NUMBER"]?.ToString(),
                        modelName = reader["MODEL_NAME"]?.ToString(),
                        lineName = reader["LINE_NAME"]?.ToString(),
                        sectionName = reader["SECTION_NAME"]?.ToString(),
                        groupName = reader["GROUP_NAME"]?.ToString(),
                        stationName = reader["STATION_NAME"]?.ToString(),
                        errorCode = reader["ERROR_CODE"]?.ToString(),
                        errorDesc = reader["ERROR_DESC"]?.ToString(),
                        data12 = reader["DATA12"]?.ToString(),
                        data6 = reader["DATA6"]?.ToString(),
                        passCount = reader["PASS_COUNT"]?.ToString(),
                        retestCount = reader["RETEST_COUNT"]?.ToString(),
                        testValue = reader["TEST_VALUE"]?.ToString(),
                        inStationTime = reader["IN_STATION_TIME"] as DateTime?,
                        result = reader["RESULT"]?.ToString(),
                        data2 = reader["DATA2"]?.ToString(),
                        testMode = reader["TEST_MODE"]?.ToString(),
                        scofMin = reader["SCOF_MIN"]?.ToString()
                    });
                }

                return Ok(new
                {
                    success = true,
                    serialNumber = sn,
                    count = results.Count,
                    data = results
                });
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Database error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"System error: {ex.Message}" });
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
            }
        }

        [HttpGet("GetCheckInAfterKanBan")]
        public async Task<IActionResult> getCheckInAfterKanBan(DateTime? startDate, DateTime? endDate)
        {
            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            try
            {
                await connection.OpenAsync();
                var defaultEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 7, 30, 0);
                var end = endDate ?? defaultEnd;
                var start = startDate ?? end.AddDays(-1);
                var checkInQuery = @"
                    SELECT 
                        CASE 
                            WHEN REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)') 
                                 THEN NVL(kp.KEY_PART_SN, NVL(kr.KEY_PART_SN, a.SERIAL_NUMBER))
                            ELSE a.SERIAL_NUMBER
                        END AS SFG,
                        a.SERIAL_NUMBER AS FG,
                        a.MODEL_NAME,
                        d.PRODUCT_LINE,
                        a.MO_NUMBER,
                        a.P_SENDER,
                        a.REPAIRER,
                        a.STATION_NAME,
                        a.REMARK AS ERROR_CODE,
                        c.ERROR_DESC,
                        a.IN_DATETIME,
                        a.OUT_DATETIME,
                        NVL(r107.WIP_GROUP, r107_v2.WIP_GROUP) AS WIP_GROUP,
                        NVL(r107.ERROR_FLAG, r107_v2.ERROR_FLAG) AS ERROR_FLAG,
                        NVL(r107.WORK_FLAG, r107_v2.WORK_FLAG) AS WORK_FLAG
                    FROM sfism4.R_REPAIR_IN_OUT_T a
                    LEFT JOIN SFISM4.R107 r107
                        ON r107.SERIAL_NUMBER = a.SERIAL_NUMBER
                    LEFT JOIN (
                        SELECT SERIAL_NUMBER, KEY_PART_SN
                        FROM (
                            SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                   ROW_NUMBER() OVER (PARTITION BY kp.SERIAL_NUMBER ORDER BY kp.WORK_TIME DESC) rn
                            FROM sfism4.P_WIP_KEYPARTS_T kp
                            WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                              AND LENGTH(kp.SERIAL_NUMBER) IN (12,18,20,21,23)
                              AND LENGTH(kp.KEY_PART_SN) IN (13,14)
                        ) WHERE rn = 1
                    ) kp ON a.SERIAL_NUMBER = kp.SERIAL_NUMBER
                    LEFT JOIN (
                        SELECT SERIAL_NUMBER, KEY_PART_SN
                        FROM (
                            SELECT kr.SERIAL_NUMBER, kr.KEY_PART_SN,
                                   ROW_NUMBER() OVER (PARTITION BY kr.SERIAL_NUMBER ORDER BY kr.WORK_TIME DESC) rn
                            FROM sfism4.R_WIP_KEYPARTS_T kr
                            WHERE kr.GROUP_NAME = 'SFG_LINK_FG'
                              AND LENGTH(kr.SERIAL_NUMBER) IN (12,18,20,21,23)
                              AND LENGTH(kr.KEY_PART_SN) IN (13,14)
                        ) WHERE rn = 1
                    ) kr ON a.SERIAL_NUMBER = kr.SERIAL_NUMBER
                    LEFT JOIN SFISM4.R107 r107_v2
                        ON r107_v2.SERIAL_NUMBER = NVL(kp.KEY_PART_SN, kr.KEY_PART_SN)
                    LEFT JOIN SFIS1.C_ERROR_CODE_T c
                        ON c.ERROR_CODE = a.REMARK
                    INNER JOIN SFIS1.C_MODEL_DESC_T d
                        ON d.MODEL_NAME = a.MODEL_NAME
                    WHERE 
                        (
                            (REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
                             AND a.P_SENDER IN ('V3209541','V0928908','V0945375','V3211693','V0904136','V1097872', 'V3231778')
                             AND a.STATION_NAME NOT LIKE '%REPAIR_B36R')
                            OR
                            (a.MO_NUMBER LIKE '8%'
                             AND a.STATION_NAME NOT LIKE '%REPAIR_B36R'
                             AND a.P_SENDER IN ('V3209541','V0928908','V3211693','V0904136','V1097872', 'V3231778'))
                        )
                      AND a.IN_DATETIME BETWEEN :startDate AND :endDate";
                var checkInList = new List<CheckInRecord>();
                await using (var cmd = new OracleCommand(checkInQuery, connection))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add(new OracleParameter(":startDate", OracleDbType.Date) { Value = start });
                    cmd.Parameters.Add(new OracleParameter(":endDate", OracleDbType.Date) { Value = end });
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        checkInList.Add(new CheckInRecord
                        {
                            SFG = reader["SFG"].ToString() ?? string.Empty,
                            FG = reader["FG"].ToString() ?? string.Empty,
                            MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                            MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                            P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                            REPAIRER = reader["REPAIRER"].ToString() ?? string.Empty,
                            WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                            WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                            STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                            ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                            ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty,
                            IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                            OUT_DATETIME = reader["OUT_DATETIME"] as DateTime?
                        });
                    }
                }
                var response = new
                {
                    count = checkInList.Count,
                    data = checkInList
                };
                return Ok(response);
            }
            catch (OracleException ex)
            {
                return StatusCode(500, $"Database error: {ex.Message}");
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        //[HttpGet("getCheckInRepair")]
        //public async Task<IActionResult> getCheckInRepair()
        //{
        //    await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
        //    try
        //    {
        //        await connection.OpenAsync();
        //        var checkInQuery = @"
        //            SELECT 
        //                A.SERIAL_NUMBER,
        //                B.PRODUCT_LINE,
        //                C.MODEL_NAME,
        //                C.MO_NUMBER,
        //                A.TEST_GROUP AS STATION_NAME,
        //                A.TEST_TIME,
        //                A.TEST_CODE AS ERROR_CODE,
        //                D.ERROR_DESC,
        //                C.WIP_GROUP,
        //                C.ERROR_FLAG,
        //                C.WORK_FLAG,
        //                TRUNC(SYSDATE - A.TEST_TIME) as AGING_HOURS
        //            FROM SFISM4.R_REPAIR_TASK_T A
        //            INNER JOIN SFIS1.C_MODEL_DESC_T B
        //                ON A.MODEL_NAME = B.MODEL_NAME
        //            LEFT JOIN SFISM4.R107 C
        //                ON A.SERIAL_NUMBER = C.SERIAL_NUMBER
        //            LEFT JOIN SFIS1.C_ERROR_CODE_T D
        //                ON A.TEST_CODE = D.ERROR_CODE
        //            LEFT JOIN (
        //                SELECT SERIAL_NUMBER, P_SENDER, IN_DATETIME
        //                FROM (
        //                    SELECT 
        //                        E.SERIAL_NUMBER,
        //                        E.P_SENDER,
        //                        E.IN_DATETIME,
        //                        ROW_NUMBER() OVER (PARTITION BY E.SERIAL_NUMBER ORDER BY E.IN_DATETIME DESC) AS RN
        //                    FROM SFISM4.R_REPAIR_IN_OUT_T E
        //                )
        //                WHERE RN = 1
        //            ) E ON E.SERIAL_NUMBER = A.SERIAL_NUMBER
        //            WHERE B.MODEL_SERIAL = 'ADAPTER'
        //              AND NOT REGEXP_LIKE(C.WIP_GROUP, 'BR2C|BCFA')
        //              AND (
        //                    (
        //                        NOT EXISTS (
        //                            SELECT 1
        //                            FROM SFISM4.Z_KANBAN_TRACKING_T Z
        //                            WHERE Z.SERIAL_NUMBER = A.SERIAL_NUMBER
        //                        )
        //                        AND E.P_SENDER IN (
        //                            'V0904136','V3209541','V0945375','V0928908','V3245384','V3211693'
        //                        )
        //                    )
        //                    OR EXISTS (
        //                        SELECT 1
        //                        FROM SFISM4.Z_KANBAN_TRACKING_T Z
        //                        WHERE Z.SERIAL_NUMBER = A.SERIAL_NUMBER
        //                    )
        //                 )";
        //        var checkInList = new List<CheckInRecord>();
        //        await using (var cmd = new OracleCommand(checkInQuery, connection))
        //        {
        //            await using var reader = await cmd.ExecuteReaderAsync();
        //            while (await reader.ReadAsync())
        //            {
        //                checkInList.Add(new CheckInRecord
        //                {
        //                    SERIAL_NUMBER = reader["SERIAL_NUMBER"].ToString() ?? string.Empty,
        //                    MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
        //                    PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
        //                    MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
        //                    WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
        //                    ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
        //                    WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
        //                    STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
        //                    ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
        //                    ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty,
        //                    TEST_TIME = reader["TEST_TIME"] as DateTime?,
        //                    AGING_HOURS = reader["AGING_HOURS"].ToString() ?? string.Empty,
        //                });
        //            }
        //        }
        //        var filteredList = await FilterMissingLocationsAsync(checkInList);
        //        var response = new
        //        {
        //            count = filteredList.Count,
        //            data = filteredList
        //        };
        //        return Ok(response);
        //    }
        //    catch (OracleException ex)
        //    {
        //        return StatusCode(500, $"Database error: {ex.Message}");
        //    }
        //    finally
        //    {
        //        if (connection.State == ConnectionState.Open)
        //        {
        //            await connection.CloseAsync();
        //        }
        //    }
        //}

        [HttpGet("getLackLocation")]
        public async Task<IActionResult> getLackLocation()
        {
            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            try
            {
                await connection.OpenAsync();
                var checkInQuery = @"
                    SELECT *
                    FROM (
                        SELECT t.*,
                               ROW_NUMBER() OVER (
                                   PARTITION BY t.SFG
                                   ORDER BY t.IN_DATETIME DESC
                               ) rn
                        FROM (
                            -------------------------------------------------------------------
                            -- Query 1: từ R_REPAIR_IN_OUT_T
                            -------------------------------------------------------------------
                            SELECT 
                                CASE
                                    WHEN REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)') 
                                         THEN COALESCE(kp.KEY_PART_SN, kr.KEY_PART_SN, a.SERIAL_NUMBER)
                                    ELSE a.SERIAL_NUMBER
                                END AS SFG,
                                a.SERIAL_NUMBER AS FG,
                                a.MODEL_NAME,
                                b.PRODUCT_LINE,
                                a.MO_NUMBER,
                                a.P_SENDER,
                                a.REPAIRER,
                                a.STATION_NAME,
                                a.REMARK AS ERROR_CODE,
                                c.ERROR_DESC,
                                a.IN_DATETIME,
                                r107.WIP_GROUP,
                                r107.ERROR_FLAG,
                                r107.WORK_FLAG
                            FROM SFISM4.R_REPAIR_IN_OUT_T a
                            INNER JOIN SFIS1.C_MODEL_DESC_T b 
                                ON a.MODEL_NAME = b.MODEL_NAME
                            INNER JOIN SFIS1.C_ERROR_CODE_T c 
                                ON a.REMARK = c.ERROR_CODE
                            LEFT JOIN SFISM4.R107 r107 
                                ON a.SERIAL_NUMBER = r107.SERIAL_NUMBER
                            LEFT JOIN (
                                SELECT SERIAL_NUMBER, KEY_PART_SN
                                FROM (
                                    SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                           ROW_NUMBER() OVER (PARTITION BY kp.SERIAL_NUMBER ORDER BY kp.WORK_TIME DESC) rn
                                    FROM sfism4.P_WIP_KEYPARTS_T kp 
                                    WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                                      AND LENGTH(kp.SERIAL_NUMBER) IN (12,18,20,21,23) 
                                      AND LENGTH(kp.KEY_PART_SN) IN (13,14)
                                )
                                WHERE rn = 1
                            ) kp ON a.SERIAL_NUMBER = kp.SERIAL_NUMBER
                            LEFT JOIN (
                                SELECT SERIAL_NUMBER, KEY_PART_SN
                                FROM (
                                    SELECT kr.SERIAL_NUMBER, kr.KEY_PART_SN,
                                           ROW_NUMBER() OVER (PARTITION BY kr.SERIAL_NUMBER ORDER BY kr.WORK_TIME DESC) rn
                                    FROM sfism4.R_WIP_KEYPARTS_T kr 
                                    WHERE kr.GROUP_NAME = 'SFG_LINK_FG'
                                      AND LENGTH(kr.SERIAL_NUMBER) IN (12,18,20,21,23) 
                                      AND LENGTH(kr.KEY_PART_SN) IN (13,14)
                                )
                                WHERE rn = 1
                            ) kr ON a.SERIAL_NUMBER = kr.SERIAL_NUMBER
                            WHERE r107.WIP_GROUP NOT LIKE '%BR2C%'
                              AND r107.WIP_GROUP NOT LIKE '%BCFA%'
                              AND a.P_SENDER IN ('V0904136','V0945375','V3245384','V3211693', 'V3209541', 'V0928908', 'V1097872')
                              AND a.REPAIRER IS NULL

                            UNION ALL

                            -------------------------------------------------------------------
                            -- Query 2: từ Z_KANBAN_TRACKING_T
                            -------------------------------------------------------------------
                            SELECT 
                                zkb.SERIAL_NUMBER AS SFG,
                                COALESCE(kp.SERIAL_NUMBER, zkb.SERIAL_NUMBER) AS FG,
                                r107.MODEL_NAME,
                                cmd.PRODUCT_LINE,
                                r107.MO_NUMBER,
                                zkb.EMP_NO AS P_SENDER,
                                NULL AS REPAIRER,
                                rp.TEST_GROUP AS STATION_NAME,
                                rp.TEST_CODE AS ERROR_CODE,
                                c.ERROR_DESC,
                                zkb.IN_STATION_TIME AS IN_DATETIME,
                                r107.WIP_GROUP,
                                r107.ERROR_FLAG,
                                r107.WORK_FLAG
                            FROM SFISM4.Z_KANBAN_TRACKING_T zkb
                            INNER JOIN SFIS1.C_MODEL_DESC_T cmd
                                ON cmd.MODEL_NAME = zkb.MODEL_NAME
                            INNER JOIN SFISM4.R107 r107
                                ON r107.SERIAL_NUMBER = zkb.SERIAL_NUMBER
                            LEFT JOIN SFISM4.R_REPAIR_TASK_T rp
                                ON rp.SERIAL_NUMBER = zkb.SERIAL_NUMBER
                            LEFT JOIN SFIS1.C_ERROR_CODE_T c 
                                ON rp.TEST_CODE = c.ERROR_CODE
                            LEFT JOIN (
                                SELECT SERIAL_NUMBER, KEY_PART_SN
                                FROM (
                                    SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                           ROW_NUMBER() OVER (
                                               PARTITION BY kp.KEY_PART_SN 
                                               ORDER BY kp.WORK_TIME DESC
                                           ) rn
                                    FROM sfism4.P_WIP_KEYPARTS_T kp
                                    WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                                      AND LENGTH(kp.SERIAL_NUMBER) IN (12,18,20,21, 23)
                                      AND LENGTH(kp.KEY_PART_SN) IN (13,14)
                                )
                                WHERE rn = 1
                            ) kp ON zkb.SERIAL_NUMBER = kp.KEY_PART_SN
                            WHERE cmd.MODEL_SERIAL = 'ADAPTER'
                              AND zkb.WIP_GROUP LIKE '%B36R'
                              AND r107.WIP_GROUP NOT LIKE '%BR2C%'
                              AND r107.WIP_GROUP NOT LIKE '%BCFA%'
                        ) t
                    )
                    WHERE rn = 1
                    ORDER BY IN_DATETIME DESC";
                var checkInList = new List<CheckInRecord>();
                await using (var cmd = new OracleCommand(checkInQuery, connection))
                {
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        checkInList.Add(new CheckInRecord
                        {
                            SFG = reader["SFG"].ToString() ?? string.Empty,
                            FG = reader["FG"].ToString() ?? string.Empty,
                            MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                            WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                            WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                            MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                            REPAIRER = reader["REPAIRER"].ToString() ?? string.Empty,
                            STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                            P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                            ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                            IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                            ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty
                        });
                    }
                }
                if (checkInList.Count > 0)
                {
                    var serialNumbers = checkInList
                        .Select(r => r.SFG)
                        .Where(sn => !string.IsNullOrWhiteSpace(sn))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (serialNumbers.Count > 0)
                    {
                        var productSerials = await _sqlContext.Products
                        .Where(p => serialNumbers.Contains(p.SerialNumber))
                        .Select(p => p.SerialNumber)
                        .ToListAsync();

                        var khoScrapSerials = await _sqlContext.KhoScraps
                            .Where(p => serialNumbers.Contains(p.SERIAL_NUMBER))
                            .Select(p => p.SERIAL_NUMBER)
                            .ToListAsync();

                        var khoOkSerials = await _sqlContext.KhoOks
                            .Where(p => serialNumbers.Contains(p.SERIAL_NUMBER))
                            .Select(p => p.SERIAL_NUMBER)
                            .ToListAsync();

                        //await Task.WhenAll(productSerials, khoScrapSerials, khoOkSerials);

                        var excludedSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var serial in productSerials)
                            excludedSerials.Add(serial);

                        foreach (var serial in khoScrapSerials)
                            excludedSerials.Add(serial);

                        foreach (var serial in khoOkSerials)
                            excludedSerials.Add(serial);

                        var exportRecords = await _sqlContext.Exports
                            .Where(e => serialNumbers.Contains(e.SerialNumber))
                            .Select(e => new
                            {
                                e.SerialNumber,
                                e.ExportDate,
                                e.CheckingB36R
                            })
                            .ToListAsync();

                        var exportSerials = exportRecords
                            .GroupBy(e => e.SerialNumber, StringComparer.OrdinalIgnoreCase)
                            .Where(group =>
                            {
                                var latest = group
                                    .OrderByDescending(x => x.ExportDate ?? DateTime.MinValue)
                                    .FirstOrDefault();
                                return latest != null && (latest.CheckingB36R == 3 || latest.CheckingB36R == 2);
                            })
                            .Select(group => group.Key);

                        foreach (var serial in exportSerials)
                        {
                            excludedSerials.Add(serial);
                        }

                        checkInList = checkInList
                            .Where(record => !excludedSerials.Contains(record.SFG))
                            .ToList();
                    }
                }
                var response = new
                {
                    count = checkInList.Count,
                    data = checkInList
                };
                return Ok(response);
            }
            catch (OracleException ex)
            {
                return StatusCode(500, $"Database error: {ex.Message}");
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        //private async Task<List<CheckInRecord>> FilterMissingLocationsAsync(List<CheckInRecord> source)
        //{
        //    if (source == null || source.Count == 0)
        //    {
        //        return source ?? new List<CheckInRecord>();
        //    }

        //    try
        //    {
        //        var serialNumbers = source
        //            .Select(r => (r.SERIAL_NUMBER ?? string.Empty).Trim().ToUpperInvariant())
        //            .Where(sn => !string.IsNullOrWhiteSpace(sn))
        //            .Distinct(StringComparer.OrdinalIgnoreCase)
        //            .ToList();

        //        if (!serialNumbers.Any())
        //        {
        //            return source;
        //        }

        //        var searchController = new SearchController(_sqlContext, _oracleContext);
        //        var findLocationResult = await searchController.FindLocations(serialNumbers);

        //        if (findLocationResult is not OkObjectResult okResult || okResult.Value == null)
        //        {
        //            return source;
        //        }

        //        var serialized = JsonConvert.SerializeObject(okResult.Value);
        //        var locationData = JsonConvert.DeserializeObject<FindLocationsResponse>(serialized);

        //        if (locationData == null)
        //        {
        //            return source;
        //        }

        //        var missingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //        if (locationData.NotFoundSerialNumbers != null)
        //        {
        //            foreach (var sn in locationData.NotFoundSerialNumbers)
        //            {
        //                if (!string.IsNullOrWhiteSpace(sn))
        //                {
        //                    missingSet.Add(sn.Trim().ToUpperInvariant());
        //                }
        //            }
        //        }

        //        if (locationData.Data != null)
        //        {
        //            foreach (var item in locationData.Data)
        //            {
        //                var serialNumber = (item?.SerialNumber ?? string.Empty).Trim().ToUpperInvariant();
        //                if (string.IsNullOrWhiteSpace(serialNumber))
        //                {
        //                    continue;
        //                }

        //                var location = item?.Location?.Trim();
        //                if (string.IsNullOrWhiteSpace(location) ||
        //                    string.Equals(location, "Borrowed", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    missingSet.Add(serialNumber);
        //                }
        //                else
        //                {
        //                    missingSet.Remove(serialNumber);
        //                }
        //            }
        //        }

        //        if (!missingSet.Any())
        //        {
        //            return new List<CheckInRecord>();
        //        }

        //        return source
        //            .Where(record => missingSet.Contains((record.SERIAL_NUMBER ?? string.Empty).Trim().ToUpperInvariant()))
        //            .ToList();
        //    }
        //    catch
        //    {
        //        return source;
        //    }
        //}

        //Số lượng tồn kho của Before Kanban và After Kanban
        [HttpGet("GetTonKhoSummary")]
        public async Task<IActionResult> GetTonKhoSummary(DateTime? startDate, DateTime? endDate)
        {
            var defaultEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 7, 30, 0);
            var end = endDate ?? defaultEnd;
            var start = startDate ?? end.AddDays(-1);

            var beforeResult = await GetTonKhoBeforeInternal(start, end);
            var afterResult = await GetTonKhoAfterInternal(start, end);

            var response = new
            {
                beforeKanban = new { count = beforeResult.Count, data = beforeResult },
                afterKanban = new { count = afterResult.Count, data = afterResult }
            };

            return Ok(response);
        }

        private async Task<List<CheckInRecord>> GetTonKhoBeforeInternal(DateTime start, DateTime end)
        {
            var checkInList = new List<CheckInRecord>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            var checkInQuery = @"
                SELECT a.SERIAL_NUMBER AS SFG,
                       a.MO_NUMBER,
                       a.MODEL_NAME,
                       b.PRODUCT_LINE,
                       r107.MO_NUMBER,
                       r107.WIP_GROUP,
                       r107.ERROR_FLAG,
                       r107.WORK_FLAG,
                       a.STATION_NAME,
                       a.P_SENDER,
                       a.REMARK AS ERROR_CODE,
                       a.IN_DATETIME,
                       a.OUT_DATETIME,
                       a.REPAIRER,
                       c.ERROR_DESC,
                       ROUND((SYSDATE - a.IN_DATETIME) * 24, 2) AS AGING_HOURS
                FROM SFISM4.R_REPAIR_IN_OUT_T a
                INNER JOIN SFIS1.C_MODEL_DESC_T b ON a.MODEL_NAME = b.MODEL_NAME
                INNER JOIN SFISM4.R107 r107 ON a.serial_number = r107.serial_number
                INNER JOIN SFIS1.C_ERROR_CODE_T c ON a.REMARK = c.ERROR_CODE
                WHERE b.MODEL_SERIAL = 'ADAPTER'
                  AND r107.ERROR_FLAG not in ('0', '1')
                  AND a.P_SENDER IN ('V0904136','V3209541','V0945375','V0928908','V3245384','V3211693', 'V3231778')
                  AND a.IN_DATETIME BETWEEN :startDate AND :endDate
                  AND NOT REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
                  AND a.REMARK NOT IN ('CK00')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM sfism4.z_kanban_tracking_t z
                      WHERE z.serial_number = a.serial_number)";

            await using (var cmd = new OracleCommand(checkInQuery, connection))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add(new OracleParameter(":startDate", OracleDbType.Date) { Value = start });
                cmd.Parameters.Add(new OracleParameter(":endDate", OracleDbType.Date) { Value = end });
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    checkInList.Add(new CheckInRecord
                    {
                        SFG = reader["SFG"].ToString() ?? string.Empty,
                        MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                        MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                        WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                        WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                        ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                        PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                        STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                        P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                        ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                        IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                        OUT_DATETIME = reader["OUT_DATETIME"] as DateTime?,
                        REPAIRER = reader["REPAIRER"].ToString() ?? string.Empty,
                        ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty,
                        TYPE = "BEFORE",
                        AGING_HOURS = reader["AGING_HOURS"].ToString() ?? string.Empty
                    });
                }
            }

            return checkInList;
        }

        private async Task<List<CheckInRecord>> GetTonKhoAfterInternal(DateTime start, DateTime end)
        {
            var checkInList = new List<CheckInRecord>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            // === Query Check-In After ===
            var checkInQuery = @"
                SELECT 
                    CASE 
                        WHEN REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)') 
                             THEN NVL(kp.KEY_PART_SN, NVL(kr.KEY_PART_SN, a.SERIAL_NUMBER))
                        ELSE a.SERIAL_NUMBER
                    END AS SFG,

                    CASE 
                        WHEN REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)') 
                             THEN NVL(kr.SERIAL_NUMBER, a.SERIAL_NUMBER)
                        ELSE a.SERIAL_NUMBER
                    END AS FG,

                    a.MODEL_NAME,
                    d.PRODUCT_LINE,
                    a.MO_NUMBER,
                    a.P_SENDER,
                    a.REPAIRER,
                    a.STATION_NAME,
                    a.REMARK AS ERROR_CODE,
                    c.ERROR_DESC,
                    a.IN_DATETIME,
                    a.OUT_DATETIME,
                    NVL(r107.WIP_GROUP, r107_v2.WIP_GROUP) AS WIP_GROUP,
                    NVL(r107.ERROR_FLAG, r107_v2.ERROR_FLAG) AS ERROR_FLAG,
                    NVL(r107.WORK_FLAG, r107_v2.WORK_FLAG) AS WORK_FLAG,
                    ROUND((SYSDATE - a.IN_DATETIME) * 24, 2) AS AGING_HOURS

                FROM sfism4.R_REPAIR_IN_OUT_T a
                INNER JOIN SFIS1.C_MODEL_DESC_T d 
                    ON d.MODEL_NAME = a.MODEL_NAME
                LEFT JOIN SFIS1.C_ERROR_CODE_T c 
                    ON c.ERROR_CODE = a.REMARK

                --KeyPart từ P_WIP
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, KEY_PART_SN
                    FROM (
                        SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                               ROW_NUMBER() OVER (PARTITION BY kp.SERIAL_NUMBER ORDER BY kp.WORK_TIME DESC) rn
                        FROM sfism4.P_WIP_KEYPARTS_T kp 
                        WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                          AND LENGTH(kp.SERIAL_NUMBER) IN (11,12,18,20,21,23)
                          AND LENGTH(kp.KEY_PART_SN) IN (13,14)
                    ) WHERE rn = 1
                ) kp ON a.SERIAL_NUMBER = kp.SERIAL_NUMBER

                --KeyPart từ R_WIP
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, KEY_PART_SN
                    FROM (
                        SELECT kr.SERIAL_NUMBER, kr.KEY_PART_SN,
                               ROW_NUMBER() OVER (PARTITION BY kr.SERIAL_NUMBER ORDER BY kr.WORK_TIME DESC) rn
                        FROM sfism4.R_WIP_KEYPARTS_T kr 
                        WHERE kr.GROUP_NAME = 'SFG_LINK_FG'
                          AND LENGTH(kr.SERIAL_NUMBER) IN (12,18,20,21,23)
                          AND LENGTH(kr.KEY_PART_SN) IN (13,14)
                    ) WHERE rn = 1
                ) kr ON a.SERIAL_NUMBER = kr.SERIAL_NUMBER

                --Thông tin R107 (FG)
                LEFT JOIN SFISM4.R107 r107
                    ON r107.SERIAL_NUMBER = a.SERIAL_NUMBER

                --Thông tin R107_v2 (SFG)
                LEFT JOIN SFISM4.R107 r107_v2
                    ON r107_v2.SERIAL_NUMBER = NVL(kp.KEY_PART_SN, kr.KEY_PART_SN)

                WHERE 
                    (
                        (
                            REGEXP_LIKE(a.MODEL_NAME, '^(900|692|930)')
                            AND a.P_SENDER IN ('V3209541', 'V0928908', 'V0945375', 'V3211693', 'V0904136', 'V1097872', 'V3231778')
                            AND (r107.ERROR_FLAG NOT IN ('0','1') OR r107.SERIAL_NUMBER IS NULL)
                            AND a.STATION_NAME NOT LIKE '%REPAIR_B36R%'
                            AND (r107.wip_group not like '%STOCKIN-KANBAN_OUT' or r107_v2.WIP_GROUP like '%B36R')
                        )
                        OR
                        (
                            a.MO_NUMBER LIKE '8%'
                            AND a.P_SENDER IN ('V3209541', 'V0928908', 'V3211693', 'V0904136', 'V1097872', 'V3231778')
                            AND a.STATION_NAME NOT LIKE '%REPAIR_B36R%'
                            AND r107.WIP_GROUP LIKE '%B36R%'
                        )
                    )
                    AND a.IN_DATETIME BETWEEN :startDate and :endDate
                ORDER BY a.IN_DATETIME DESC";

            await using (var cmd = new OracleCommand(checkInQuery, connection))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add(new OracleParameter(":startDate", OracleDbType.Date) { Value = start });
                cmd.Parameters.Add(new OracleParameter(":endDate", OracleDbType.Date) { Value = end });
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    checkInList.Add(new CheckInRecord
                    {
                        SFG = reader["SFG"].ToString() ?? string.Empty,
                        FG = reader["FG"].ToString() ?? string.Empty,
                        MODEL_NAME = reader["MODEL_NAME"].ToString() ?? string.Empty,
                        PRODUCT_LINE = reader["PRODUCT_LINE"].ToString() ?? string.Empty,
                        MO_NUMBER = reader["MO_NUMBER"].ToString() ?? string.Empty,
                        P_SENDER = reader["P_SENDER"].ToString() ?? string.Empty,
                        REPAIRER = reader["REPAIRER"].ToString() ?? string.Empty,
                        WIP_GROUP = reader["WIP_GROUP"].ToString() ?? string.Empty,
                        ERROR_FLAG = reader["ERROR_FLAG"].ToString() ?? string.Empty,
                        WORK_FLAG = reader["WORK_FLAG"].ToString() ?? string.Empty,
                        STATION_NAME = reader["STATION_NAME"].ToString() ?? string.Empty,
                        ERROR_CODE = reader["ERROR_CODE"].ToString() ?? string.Empty,
                        ERROR_DESC = reader["ERROR_DESC"].ToString() ?? string.Empty,
                        IN_DATETIME = reader["IN_DATETIME"] as DateTime?,
                        OUT_DATETIME = reader["OUT_DATETIME"] as DateTime?,
                        TYPE = "AFTER",
                        AGING_HOURS = reader["AGING_HOURS"].ToString() ?? string.Empty
                    });
                }
            }

            // Lấy Exports
            var serialNumbers = checkInList
                .Where(r => !string.IsNullOrWhiteSpace(r.SFG))
                .Select(r => r.SFG)
                .Distinct()
                .ToList();

            var exportRecords = await _sqlContext.Exports
                .Where(e => serialNumbers.Contains(e.SerialNumber) &&
                            (e.CheckingB36R == 1 || e.CheckingB36R == 2 || e.CheckingB36R == 3))
                .Select(e => new { e.SerialNumber, e.ExportDate, e.CheckingB36R })
                .ToListAsync();

            var exportDict = exportRecords
                .GroupBy(e => e.SerialNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.ExportDate ?? DateTime.MinValue).First(),
                    StringComparer.OrdinalIgnoreCase);

            var tonKhoAfter = checkInList
                .Where(ci => !exportDict.ContainsKey(ci.SFG)
                          || (exportDict[ci.SFG].ExportDate != null && exportDict[ci.SFG].ExportDate <= ci.IN_DATETIME))
                .ToList();

            return tonKhoAfter;
        }

        [HttpGet("GetSAPInOut")]
        public async Task<IActionResult> GetSAPInOut(DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue || !endDate.HasValue)
            {
                return BadRequest(new { success = false, message = "startDate và endDate là bắt buộc." });
            }

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);

            try
            {
                await connection.OpenAsync();

                string query = @"
            SELECT 
                a.SERIAL_NUMBER,
                CASE
                      WHEN a.SHIPPING_SN2 = 'N/A' 
                      THEN COALESCE(a.SERIAL_NUMBER, a.SHIPPING_SN2)
                      ELSE a.SHIPPING_SN2
                    END AS SHIPPING_SN2,
                a.GROUP_NAME,
                a.IN_STATION_TIME,
                a.MO_NUMBER,
                a.MODEL_NAME,
                a.KEY_PART_NO,
                b.PRODUCT_LINE,
                a.MSN,
                a.ATE_STATION_NO,
                a.EMP_NO,
                a.WIP_GROUP
            FROM SFISM4.R_SN_TRANSFER_SAP_T a
            INNER JOIN SFIS1.C_MODEL_DESC_T b
                ON a.MODEL_NAME = b.MODEL_NAME
            WHERE b.MODEL_SERIAL = 'ADAPTER'
              AND a.IN_STATION_TIME BETWEEN :startDate AND :endDate
              AND a.FINISH_FLAG = '0'
              AND REGEXP_LIKE(a.GROUP_NAME, 'B30M|B28M|B36R')";

                var cmd = new OracleCommand(query, connection);
                cmd.Parameters.Add(new OracleParameter("startDate", OracleDbType.Date) { Value = startDate });
                cmd.Parameters.Add(new OracleParameter("endDate", OracleDbType.Date) { Value = endDate });

                var groupDetails = new List<SAPRecord>();

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    groupDetails.Add(new SAPRecord
                    {
                        SERIAL_NUMBER = reader["SERIAL_NUMBER"]?.ToString() ?? "",
                        SHIPPING_SN2 = reader["SHIPPING_SN2"]?.ToString() ?? "",
                        GROUP_NAME = reader["GROUP_NAME"]?.ToString() ?? "",
                        IN_STATION_TIME = reader["IN_STATION_TIME"] == DBNull.Value ? null : Convert.ToDateTime(reader["IN_STATION_TIME"]),
                        MO_NUMBER = reader["MO_NUMBER"]?.ToString() ?? "",
                        MODEL_NAME = reader["MODEL_NAME"]?.ToString() ?? "",
                        KEY_PART_NO = reader["KEY_PART_NO"]?.ToString() ?? "",
                        PRODUCT_LINE = reader["PRODUCT_LINE"]?.ToString() ?? "",
                        MSN = reader["MSN"]?.ToString() ?? "",
                        ATE_STATION_NO = reader["ATE_STATION_NO"]?.ToString() ?? "",
                        EMP_NO = reader["EMP_NO"]?.ToString() ?? "",
                        WIP_GROUP = reader["WIP_GROUP"]?.ToString() ?? ""
                    });
                }

                // ✅ Tạo thống kê tổng hợp theo GROUP_NAME
                var summary = groupDetails
                    .GroupBy(x => x.GROUP_NAME)
                    .Select(g => new
                    {
                        GroupName = g.Key,
                        Count = g.Count(),
                        Details = g.ToList()
                    })
                    .ToList();

                return Ok(new
                {
                    success = true,
                    total = groupDetails.Count,
                    groups = summary
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

        [HttpPost("get-test-result-list")]
        public async Task<IActionResult> GetTestResultList([FromBody] List<string> serialNumbers)
        {
            if (serialNumbers == null || serialNumbers.Count == 0)
                return BadRequest(new { success = false, message = "serialNumbers list is required." });

            var snList = serialNumbers
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim().ToUpper())
                .Distinct()
                .ToList();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            try
            {
                await connection.OpenAsync();

                // 1️⃣ Generate placeholder params :sn0, :sn1, ...
                var paramNames = snList.Select((sn, i) => $":sn{i}").ToList();

                // 2️⃣ Query: lấy SFG và FG test mới nhất, chỉ lấy GROUP_NAME LIKE '%OFF%'
                var sql = $@"
            SELECT 
                FINAL.KEY_PART_SN,
                FINAL.TEST_SN,
                FINAL.GROUP_NAME,
                FINAL.DATA1,
                FINAL.DATA2,
                FINAL.PASS_DATE
            FROM (
                SELECT
                    KP.KEY_PART_SN,
                    R.SERIAL_NUMBER AS TEST_SN,
                    R.GROUP_NAME,
                    R.DATA1,
                    R.DATA2,
                    R.PASS_DATE,
                    ROW_NUMBER() OVER (PARTITION BY KP.KEY_PART_SN ORDER BY R.PASS_DATE DESC) rn
                FROM SFISM4.R_ULT_RESULT_T R
                JOIN (
                    SELECT KEY_PART_SN, SN
                    FROM (
                        SELECT B.KEY_PART_SN, B.KEY_PART_SN AS SN
                        FROM (
                            SELECT {string.Join(" AS KEY_PART_SN FROM DUAL UNION ALL SELECT ", snList.Select(s => $"'{s}'"))} AS KEY_PART_SN FROM DUAL
                        ) B
                        UNION ALL
                        SELECT B.KEY_PART_SN, LKP.SERIAL_NUMBER AS SN
                        FROM (
                            SELECT {string.Join(" AS KEY_PART_SN FROM DUAL UNION ALL SELECT ", snList.Select(s => $"'{s}'"))} AS KEY_PART_SN FROM DUAL
                        ) B
                        LEFT JOIN (
                            SELECT x.KEY_PART_SN, x.SERIAL_NUMBER
                            FROM (
                                SELECT 
                                    kp.KEY_PART_SN,
                                    kp.SERIAL_NUMBER,
                                    ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                                FROM SFISM4.P_WIP_KEYPARTS_T kp
                                WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                            ) x WHERE x.rn = 1
                        ) LKP ON LKP.KEY_PART_SN = B.KEY_PART_SN
                        WHERE LKP.SERIAL_NUMBER IS NOT NULL
                    ) KP0
                    GROUP BY KEY_PART_SN, SN
                ) KP
                  ON R.SERIAL_NUMBER = KP.SN
            ) FINAL
            WHERE FINAL.rn = 1
            ORDER BY FINAL.PASS_DATE DESC";

                var results = new List<object>();

                await using var cmd = new OracleCommand(sql, connection);
                cmd.BindByName = true;

                // 3️⃣ Execute
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        serialNumber = reader["KEY_PART_SN"]?.ToString(),
                        testSn = reader["TEST_SN"]?.ToString(),
                        groupName = reader["GROUP_NAME"]?.ToString(),
                        data1 = reader["DATA1"]?.ToString(),
                        data2 = reader["DATA2"]?.ToString(),
                        passDate = reader["PASS_DATE"] as DateTime?
                    });
                }

                return Ok(new
                {
                    success = true,
                    count = results.Count,
                    data = results
                });
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Database error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"System error: {ex.Message}" });
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
            }
        }


    }
}
