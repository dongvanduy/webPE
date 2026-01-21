using API_WEB.ModelsOracle;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace API_WEB.Helpers
{
    /// <summary>
    /// Tiện ích dùng để xác định mối liên kết giữa FG ↔ SFG trong bảng R_WIP_KEYPARTS_T.
    /// Hỗ trợ tự nhận biết đầu vào là FG hay SFG và truy ngược chiều còn lại.
    /// </summary>
    public static class SerialLinkResolver
    {
        /// <summary>
        /// Kết quả phân tích chuỗi serial.
        /// </summary>
        public sealed class SerialLinkInfo
        {
            /// <summary>Serial “chính” (canonical) – thường là FG.</summary>
            public string CanonicalSerial { get; init; } = string.Empty;

            /// <summary>Các serial có liên hệ (FG + SFG...)</summary>
            public IReadOnlyCollection<string> RelatedSerials { get; init; } = Array.Empty<string>();

            /// <summary>Danh sách ưu tiên sắp xếp theo logic (FG trước, rồi SFG...)</summary>
            public IReadOnlyList<string> PrioritySerials { get; init; } = Array.Empty<string>();

            /// <summary>FG được liên kết (nếu đầu vào là SFG).</summary>
            public string? LinkedFgSerial { get; init; }

            /// <summary>SFG được liên kết (nếu đầu vào là FG).</summary>
            public string? LinkedSfgSerial { get; init; }

            /// <summary>Serial lưu trữ (storage) – fallback nếu không xác định được rõ.</summary>
            public string StorageSerial { get; init; } = string.Empty;
        }

        /// <summary>
        /// Tự động phân tích 1 serial để tìm FG/SFG tương ứng.
        /// </summary>
        public static async Task<SerialLinkInfo> ResolveAsync(OracleDbContext oracleContext, string serialNumber)
        {
            if (oracleContext == null)
                throw new ArgumentNullException(nameof(oracleContext));

            if (string.IsNullOrWhiteSpace(serialNumber))
                throw new ArgumentException("Serial number cannot be null or whitespace.", nameof(serialNumber));

            serialNumber = serialNumber.Trim();

            var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { serialNumber };
            string? fgSerial = null;
            string? sfgSerial = null;

            var connection = oracleContext.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                // 1️⃣ Kiểm tra loại serial (FG hay SFG)
                const string checkTypeSql = @"
                    SELECT
                        CASE
                            WHEN EXISTS (
                                SELECT 1 FROM sfism4.R_WIP_KEYPARTS_T
                                WHERE SERIAL_NUMBER = :serialNumber 
                                  AND GROUP_NAME = 'SFG_LINK_FG'
                            ) THEN 'FG'
                            WHEN EXISTS (
                                SELECT 1 FROM sfism4.R_WIP_KEYPARTS_T
                                WHERE KEY_PART_SN = :serialNumber 
                                  AND GROUP_NAME = 'SFG_LINK_FG'
                            ) THEN 'SFG'
                            ELSE 'UNKNOWN'
                        END AS SERIAL_TYPE
                    FROM DUAL";

                string? serialType = await ExecuteScalarAsync(connection, checkTypeSql, serialNumber);

                // 2️⃣ Truy vấn theo loại serial
                if (serialType == "FG")
                {
                    // Serial hiện tại là FG → tìm SFG liên kết mới nhất
                    sfgSerial = await ExecuteScalarAsync(connection, @"
                        SELECT KEY_PART_SN FROM (
                            SELECT KEY_PART_SN, ROW_NUMBER() OVER (ORDER BY WORK_TIME DESC) rn
                            FROM sfism4.R_WIP_KEYPARTS_T
                            WHERE SERIAL_NUMBER = :serialNumber
                              AND GROUP_NAME = 'SFG_LINK_FG'
                              AND LENGTH(SERIAL_NUMBER) IN (12, 18, 20, 21)
                              AND LENGTH(KEY_PART_SN) IN (13, 14)
                        ) WHERE rn = 1", serialNumber);

                    if (!string.IsNullOrWhiteSpace(sfgSerial))
                        related.Add(sfgSerial.Trim());
                }
                else if (serialType == "SFG")
                {
                    // Serial hiện tại là SFG → tìm FG liên kết mới nhất
                    fgSerial = await ExecuteScalarAsync(connection, @"
                        SELECT SERIAL_NUMBER FROM (
                            SELECT SERIAL_NUMBER, ROW_NUMBER() OVER (ORDER BY WORK_TIME DESC) rn
                            FROM sfism4.R_WIP_KEYPARTS_T
                            WHERE KEY_PART_SN = :serialNumber
                              AND GROUP_NAME = 'SFG_LINK_FG'
                              AND LENGTH(SERIAL_NUMBER) IN (12, 18, 20, 21)
                              AND LENGTH(KEY_PART_SN) IN (13, 14)
                        ) WHERE rn = 1", serialNumber);

                    if (!string.IsNullOrWhiteSpace(fgSerial))
                        related.Add(fgSerial.Trim());
                }
            }
            finally
            {
                if (shouldClose && connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
            }

            // 3️⃣ Xác định canonical serial & danh sách ưu tiên
            var canonicalSerial = !string.IsNullOrWhiteSpace(fgSerial) ? fgSerial.Trim() : serialNumber;
            var storageSerial = !string.IsNullOrWhiteSpace(sfgSerial) ? sfgSerial.Trim() : serialNumber;

            var priorityList = new List<string>();

            if (!string.IsNullOrWhiteSpace(canonicalSerial))
                priorityList.Add(canonicalSerial);

            if (!string.IsNullOrWhiteSpace(storageSerial) &&
                !priorityList.Any(p => string.Equals(p, storageSerial, StringComparison.OrdinalIgnoreCase)))
                priorityList.Add(storageSerial);

            foreach (var candidate in related)
            {
                if (!priorityList.Any(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase)))
                    priorityList.Add(candidate);
            }

            return new SerialLinkInfo
            {
                CanonicalSerial = canonicalSerial,
                RelatedSerials = related.ToList(),
                PrioritySerials = priorityList,
                LinkedFgSerial = fgSerial,
                LinkedSfgSerial = sfgSerial,
                StorageSerial = storageSerial
            };
        }

        /// <summary>
        /// Thực thi truy vấn trả về 1 giá trị duy nhất (chuỗi hoặc null).
        /// </summary>
        private static async Task<string?> ExecuteScalarAsync(DbConnection connection, string query, string serialNumber)
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;

            if (command is OracleCommand oracleCommand)
                oracleCommand.BindByName = true;

            var parameter = new OracleParameter("serialNumber", OracleDbType.Varchar2, serialNumber, ParameterDirection.Input);
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
    }
}
