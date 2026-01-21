using API_WEB.Models.Bonepile;
using API_WEB.ModelsOracle;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace API_WEB.Services.Bonepile
{
    public class BonepileAfterRepository : IBonepileAfterRepository
    {
        private readonly OracleDbContext _oracleContext;

        public BonepileAfterRepository(OracleDbContext oracleContext)
        {
            _oracleContext = oracleContext;
        }

        public async Task<List<BonepileAfterKanbanResult>> ExecuteBonepileAfterKanbanBasicQueryAsync(CancellationToken cancellationToken)
        {
            var result = new List<BonepileAfterKanbanResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
            SELECT /*+ LEADING(A) USE_NL(A B R107 KP R109X R109_OLD) */
                  A.SERIAL_NUMBER,
                  KP.PARENT_SN AS FG,
                  R107.MO_NUMBER,
                  A.MODEL_NAME,
                  B.PRODUCT_LINE,
                  A.WIP_GROUP AS WIP_GROUP_KANBAN,
                  R107.WIP_GROUP AS WIP_GROUP_SFC,
                  R107.ERROR_FLAG,
                  R107.WORK_FLAG,

                  R109X.TEST_GROUP,
                  R109X.TEST_TIME,
                  R109X.TEST_CODE,
                  R109X.ERROR_ITEM_CODE,
                  E.ERROR_DESC,

                  -- AGING theo test_time mới nhất
                  TRUNC(SYSDATE) - TRUNC(R109X.TEST_TIME) AS AGING,

                  -- AGING theo test_time cũ nhất
                  TRUNC(SYSDATE) - TRUNC(R109_OLD.TEST_TIME) AS AGING_OLDEST

                FROM SFISM4.Z_KANBAN_TRACKING_T A
                JOIN SFIS1.C_MODEL_DESC_T B ON A.MODEL_NAME = B.MODEL_NAME
                JOIN SFISM4.R107 R107 ON R107.SERIAL_NUMBER = A.SERIAL_NUMBER

                /* mapping Parent_SN theo WORK_TIME mới nhất */
                LEFT JOIN (
                  SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                  FROM (
                    SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                           ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                    FROM SFISM4.P_WIP_KEYPARTS_T kp
                    WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                      AND LENGTH(kp.SERIAL_NUMBER) IN (11,12,18,20,21,23)
                      AND LENGTH(kp.KEY_PART_SN)   IN (13,14)
                  ) WHERE rn = 1
                ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER

                /* Test mới nhất (SN ∪ Parent_SN) */
                LEFT JOIN (
                  SELECT *
                  FROM (
                    SELECT
                      base.SN,
                      r.TEST_GROUP,
                      r.TEST_TIME,
                      r.TEST_CODE,
                      r.ERROR_ITEM_CODE,
                      ROW_NUMBER() OVER (
                        PARTITION BY base.SN
                        ORDER BY r.TEST_TIME DESC, r.TEST_CODE DESC
                      ) AS rn
                    FROM (
                      SELECT A.SERIAL_NUMBER AS SN, A.SERIAL_NUMBER AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      UNION ALL
                      SELECT A.SERIAL_NUMBER AS SN, KP.PARENT_SN AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      JOIN (
                        SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                        FROM (
                          SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                 ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                          FROM SFISM4.P_WIP_KEYPARTS_T kp
                          WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                        ) WHERE rn = 1
                      ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER
                    ) base
                    JOIN SFISM4.R109 r
                      ON r.SERIAL_NUMBER = base.CAND_SN
                    WHERE r.TEST_TIME IS NOT NULL
                  )
                  WHERE rn = 1
                ) R109X ON R109X.SN = A.SERIAL_NUMBER

                /* Test cũ nhất (SN ∪ Parent_SN) */
                LEFT JOIN (
                  SELECT *
                  FROM (
                    SELECT
                      base.SN,
                      r.TEST_TIME,
                      ROW_NUMBER() OVER (
                        PARTITION BY base.SN
                        ORDER BY r.TEST_TIME ASC
                      ) AS rn
                    FROM (
                      SELECT A.SERIAL_NUMBER AS SN, A.SERIAL_NUMBER AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      UNION ALL
                      SELECT A.SERIAL_NUMBER AS SN, KP.PARENT_SN AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      JOIN (
                        SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                        FROM (
                          SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                 ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                          FROM SFISM4.P_WIP_KEYPARTS_T kp
                          WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                        ) WHERE rn = 1
                      ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER
                    ) base
                    JOIN SFISM4.R109 r
                      ON r.SERIAL_NUMBER = base.CAND_SN
                    WHERE r.TEST_TIME IS NOT NULL
                  )
                  WHERE rn = 1
                ) R109_OLD ON R109_OLD.SN = A.SERIAL_NUMBER

                LEFT JOIN SFIS1.C_ERROR_CODE_T E ON E.ERROR_CODE = R109X.TEST_CODE

                WHERE
                  A.WIP_GROUP LIKE '%B36R%'
                  AND B.MODEL_SERIAL = 'ADAPTER'
                  AND R107.WIP_GROUP NOT LIKE '%BR2C%'
                  AND R107.WIP_GROUP NOT LIKE '%BCFA%'";

            await using var command = new OracleCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new BonepileAfterKanbanResult
                {
                    SERIAL_NUMBER = reader["SERIAL_NUMBER"]?.ToString(),
                    MO_NUMBER = reader["MO_NUMBER"]?.ToString(),
                    MODEL_NAME = reader["MODEL_NAME"]?.ToString(),
                    PRODUCT_LINE = reader["PRODUCT_LINE"]?.ToString(),
                    WIP_GROUP_KANBAN = reader["WIP_GROUP_KANBAN"]?.ToString(),
                    WIP_GROUP_SFC = reader["WIP_GROUP_SFC"]?.ToString(),
                    ERROR_FLAG = reader["ERROR_FLAG"]?.ToString(),
                    WORK_FLAG = reader["WORK_FLAG"]?.ToString(),
                    TEST_GROUP = reader["TEST_GROUP"]?.ToString(),
                    TEST_TIME = reader["TEST_TIME"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["TEST_TIME"]),
                    TEST_CODE = reader["TEST_CODE"]?.ToString(),
                    ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"]?.ToString(),
                    ERROR_DESC = reader["ERROR_DESC"]?.ToString(),
                    AGING = reader["AGING"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AGING"])
                });
            }

            return result;
        }

        public List<string> GetExcludedSerialNumbers()
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            var filePath = Path.Combine(uploadsFolder, "ScrapOk.xlsx");
            var snList = new List<string>();

            if (!File.Exists(filePath))
            {
                return snList;
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null || worksheet.Dimension == null)
            {
                return snList;
            }

            var rowCount = worksheet.Dimension.Rows;
            for (var row = 1; row <= rowCount; row++)
            {
                var sn = worksheet.Cells[row, 1].Text.Trim();
                if (!string.IsNullOrEmpty(sn))
                {
                    snList.Add(sn.ToUpperInvariant());
                }
            }

            return snList;
        }
    }
}
