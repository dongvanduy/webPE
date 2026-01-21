using API_WEB.ModelsOracle;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using System.Text;

namespace API_WEB.Helpers.Repositories
{
    public static class RemoveLocationHelper
    {
        public static async Task SendReceivingStatusAsync(IEnumerable<string> serialNumbers, string owner, string?location, string tag, OracleDbContext oracleContext)
        { 
            if (serialNumbers == null || !serialNumbers.Any()) return;
            try
            {
                var cleandSerials = serialNumbers.Where(sn => !string.IsNullOrWhiteSpace(sn)).Select(sn => sn.Trim().Replace("\r", "").Replace("\n", ""))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(!cleandSerials.Any()) return;
                var serialsWithData18 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var connection = (OracleConnection)oracleContext.Database.GetDbConnection();
                if(connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

                const int oracleParameterLimit = 999;
                for (var offset = 0; offset < cleandSerials.Count; offset += oracleParameterLimit)
                {
                    var batch = cleandSerials.Skip(offset).Take(oracleParameterLimit).ToList();
                    if(batch.Count == 0) continue;
                    var parameterNames = batch.Select((_, index) => $"p{offset + index}").ToArray();

                    var query = $@"SELECT SERIAL_NUMBER
                          FROM SFISM4.R_REPAIR_TASK_T
                         WHERE DATA18 IS NOT NULL
                           AND SERIAL_NUMBER IN ({string.Join(",", parameterNames.Select(name => $":{name}"))})";
                    using var command = connection.CreateCommand();
                    command.CommandText = query;
                    for (var i = 0; i < batch.Count; i++)
                        command.Parameters.Add(new OracleParameter(parameterNames[i], batch[i]));
                    
                    using var reader = command.ExecuteReader();
                    while(await reader.ReadAsync())
                    {
                        var serial = reader["SERIAL_NUMBER"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(serial)) serialsWithData18.Add(serial.Trim());
                    }
                }
                if (!serialsWithData18.Any()) return;

                location = String.Empty;

                var payload = new
                {
                    serialnumbers = string.Join(",", serialsWithData18),
                    owner = owner?.Trim() ?? string.Empty,
                    location,
                    tag = tag?.Trim() ?? string.Empty
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                //Bo proxy
                var handler = new HttpClientHandler { UseProxy = false };
                using var client = new HttpClient(handler);

                var response = await client.PostAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/RepairStatus/receiving-status", content);

                if (!response.IsSuccessStatusCode)
                {
                    var msg = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[SendReceivingStatusAsync] Failed: {response.StatusCode} - {msg}");
                }
                else
                {
                    Console.WriteLine($"[SendReceivingStatusAsync] Success for {serialsWithData18.Count} serials. Tag={tag}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendReceivingStatusAsync] Error: {ex.Message}");
            }
        }
    }
}

