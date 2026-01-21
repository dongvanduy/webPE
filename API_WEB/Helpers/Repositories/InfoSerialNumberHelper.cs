using Oracle.ManagedDataAccess.Client;

namespace API_WEB.Helpers.Repositories
{
    public static class InfoSerialNumberHelper
    {
        public static async Task<(string ProductLine, string ModelName)?> GetSNInfoHelperAsync(string serialNumber, string oracleConnectionString)
        {
            if(string.IsNullOrEmpty(serialNumber)) return null;
            await using var connection = new OracleConnection(oracleConnectionString);
            await connection.OpenAsync();
            string query = @"
                SELECT a.MODEL_NAME, b.PRODUCT_LINE
                FROM sfism4.r107 a
                INNER JOIN sfis1.c_model_desc_t b ON a.MODEL_NAME = b.MODEL_NAME
                WHERE a.SERIAL_NUMBER = :serialNumber AND ROWNUM = 1";
            await using var cmd = new OracleCommand(query, connection);
            cmd.Parameters.Add("serialNumber", OracleDbType.Varchar2).Value = serialNumber.Trim().ToUpperInvariant();

            await using var reader = await cmd.ExecuteReaderAsync();
            if(await reader.ReadAsync())
            {
                return (
                    reader["PRODUCT_LINE"]?.ToString() ?? "",
                    reader["MODEL_NAME"]?.ToString() ?? "");
            }
            return null;
        }

        //BATCH CHO TẤT CẢ API
        public static async Task<Dictionary<string, (string ProductLine, string ModelName)>>
            GetBatchSNInfoAsync(IEnumerable<string> serialNumbers, string oracleConnectionString)
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            var tasks = serialNumbers
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(async sn =>
                {
                    try
                    {
                        var info = await GetSNInfoHelperAsync(sn, oracleConnectionString);
                        return (sn, info);
                    }
                    catch
                    {
                        return (sn, (string.Empty, string.Empty));
                    }
                })
                .ToArray();

            var completedResults = await Task.WhenAll(tasks);

            foreach (var r in completedResults)
            {
                result[r.sn] = r.info.HasValue ? r.info.Value : (string.Empty, string.Empty);
            }

            return result;
        }
    }
}
