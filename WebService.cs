using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace OPCWebServer
{
    public class WebService : IDisposable
    {
        private HttpListener _listener;
        private readonly WebSettings _settings;
        private readonly DataPollingService _pollingService;
        private readonly DbService _dbService;
        private bool _isRunning;

        public WebService(WebSettings settings, DataPollingService pollingService, DbService dbService = null)
        {
            _settings = settings;
            _pollingService = pollingService;
            _dbService = dbService;
        }

        public void Start()
        {
            if (!_settings.Enabled) return;

            _listener = new HttpListener();
            // Важно: для прослушивания всех IP нужны права администратора или настройка urlacl
            _listener.Prefixes.Add($"http://*:{_settings.Port}/");
            _listener.Start();
            _isRunning = true;

            Task.Run(() => ListenLoop());
            Console.WriteLine($"Сервер запущен на порту {_settings.Port}");
        }

        private async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    ProcessRequest(context);
                }
                catch (Exception ex) 
                { 
                    // Логирование ошибки
                    if (!_isRunning) break;
                }
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Эмуляция CORS (AllowAll)
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();
                return;
            }

            try
            {
                string url = request.Url.AbsolutePath.ToLower();

                // Роут GET /api/tags
                if (url == "/api/tags" && request.HttpMethod == "GET")
                {
                    string json = _pollingService?.LastJsonData ?? "[]";
                    SendResponse(response, json, "application/json");
                }
                // Роут GET /api/db/schema - returns DB schema info
                else if (url == "/api/db/schema" && request.HttpMethod == "GET")
                {
                    var schema = new { 
                        table = "raw_data",
                        columns = new[] { 
                            new { name = "timestamp", type = "INTEGER" },
                            new { name = "tagIndex", type = "INTEGER" },
                            new { name = "value", type = "TEXT" }
                        }
                    };
                    string json = System.Text.Json.JsonSerializer.Serialize(schema);
                    SendResponse(response, json, "application/json");
                }
                // Роут GET /api/db/data?from=timestamp&to=timestamp&tagIndex=index
                else if (url.StartsWith("/api/db/data") && request.HttpMethod == "GET")
                {
                    if (_dbService == null)
                    {
                        SendResponse(response, "{\"error\":\"DB service not available\"}", "application/json", 503);
                        return;
                    }

                    var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                    long fromTs = 0, toTs = 0;
                    int? tagIndex = null;

                    if (!string.IsNullOrEmpty(query["from"])) long.TryParse(query["from"], out fromTs);
                    if (!string.IsNullOrEmpty(query["to"])) long.TryParse(query["to"], out toTs);
                    if (!string.IsNullOrEmpty(query["tagIndex"])) int.TryParse(query["tagIndex"], out var idx);

                    var data = GetDbData(fromTs, toTs, tagIndex);
                    SendResponse(response, data, "application/json");
                }
                // Роут GET /api/db/tags - returns list of tags with indices
                else if (url == "/api/db/tags" && request.HttpMethod == "GET")
                {
                    // Return tag configuration for frontend to map indices to addresses
                    var tagList = new System.Collections.Generic.List<object>();
                    // We need access to config tags - this will be passed via constructor or we use a callback
                    // For now, return empty array - actual implementation needs config reference
                    SendResponse(response, "[]", "application/json");
                }
                // Роут POST /api/save
                else if (url == "/api/save" && request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string content = await reader.ReadToEndAsync();
                        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.StaticFolder, "dashboard.json");
                        File.WriteAllText(filePath, content);
                    }
                    SendResponse(response, "{\"message\":\"Saved\"}", "application/json");
                }
                // Раздача статики
                else
                {
                    ServeStaticFile(url, response);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                SendResponse(response, ex.Message, "text/plain");
            }
        }

        private void ServeStaticFile(string url, HttpListenerResponse response)
        {
            string fileName = url == "/" ? "index.html" : url.TrimStart('/');
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.StaticFolder, fileName);

            if (File.Exists(localPath))
            {
                byte[] buffer = File.ReadAllBytes(localPath);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
            }
        }

        private void SendResponse(HttpListenerResponse response, string text, string contentType, int statusCode = 200)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            response.ContentType = contentType;
            response.StatusCode = statusCode;
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private string GetDbData(long fromTs, long toTs, int? tagIndex)
        {
            try
            {
                var connectionString = $"Data Source={System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "archive.db")};Version=3;";
                var result = new System.Collections.Generic.List<object>();

                using (var conn = new System.Data.SQLite.SQLiteConnection(connectionString))
                {
                    conn.Open();
                    
                    string sql = "SELECT timestamp, tagIndex, value FROM raw_data WHERE 1=1";
                    if (fromTs > 0) sql += " AND timestamp >= @from";
                    if (toTs > 0) sql += " AND timestamp <= @to";
                    if (tagIndex.HasValue) sql += " AND tagIndex = @idx";
                    sql += " ORDER BY timestamp ASC";

                    using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
                    {
                        if (fromTs > 0) cmd.Parameters.AddWithValue("@from", fromTs);
                        if (toTs > 0) cmd.Parameters.AddWithValue("@to", toTs);
                        if (tagIndex.HasValue) cmd.Parameters.AddWithValue("@idx", tagIndex.Value);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(new
                                {
                                    timestamp = reader.GetInt64(0),
                                    tagIndex = reader.GetInt32(1),
                                    value = ParseValue(reader.GetString(2))
                                });
                            }
                        }
                    }
                }

                return System.Text.Json.JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                return System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private object ParseValue(string val)
        {
            if (double.TryParse(val, out var d)) return d;
            if (bool.TryParse(val, out var b)) return b;
            return val;
        }

        public void Stop()
        {
            _isRunning = false;
            if (_listener != null)
            {
                _listener.Stop();
                _listener.Close();
                _listener = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
