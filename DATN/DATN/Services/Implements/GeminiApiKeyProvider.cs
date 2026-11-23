using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DATN.Services.Interfaces;

namespace DATN.Services.Implements;

/// <summary>
/// Service quản lý nhiều Gemini API keys
/// Hỗ trợ phân tán tải, gán module linh hoạt và đồng bộ hóa với appsettings.json
/// </summary>
public class GeminiApiKeyProvider : IGeminiApiKeyProvider
{
    private readonly List<ApiKeyInfo> _apiKeys;
    private readonly Dictionary<string, int> _moduleToKeyIndex; // Module -> Key index mapping
    private readonly ILogger<GeminiApiKeyProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment? _hostEnvironment;
    private int _roundRobinIndex = 0;
    private readonly object _lock = new object();

    private readonly int _failureThreshold = 5; // số lần fail để disable
    private readonly TimeSpan _failureWindow = TimeSpan.FromMinutes(10);

    private class ApiKeyInfo
    {
        public string Key { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public bool Disabled { get; set; }
        public string Status { get; set; } = "Healthy";
        public string? ErrorMessage { get; set; }
        public DateTime? LastCheckedAt { get; set; }
    }

    private static readonly List<ModuleInfo> SupportedModules = new()
    {
        new ModuleInfo { Id = "Chatbot", Name = "Chatbot Tư Vấn & FAQ", Description = "Phản hồi câu hỏi ứng viên tự động" },
        new ModuleInfo { Id = "JD", Name = "Sinh JD Tuyển Dụng AI", Description = "Tạo mô tả công việc, quyền lợi và yêu cầu" },
        new ModuleInfo { Id = "Interview", Name = "Phỏng Vấn & Đánh Giá", Description = "Sinh câu hỏi phỏng vấn và chấm điểm" },
        new ModuleInfo { Id = "CV", Name = "Sàng Lọc & Khớp CV", Description = "Bóc tách PDF CV và chấm điểm kỹ năng" },
        new ModuleInfo { Id = "Email", Name = "Soạn Thảo Email Tuyển Dụng", Description = "Tạo thư mời phỏng vấn, thư trúng tuyển" },
        new ModuleInfo { Id = "Code", Name = "Chấm Bài Test Code", Description = "Đánh giá bài test lập trình kỹ thuật" }
    };

    public GeminiApiKeyProvider(
        IConfiguration configuration,
        ILogger<GeminiApiKeyProvider> logger,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment? hostEnvironment = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _hostEnvironment = hostEnvironment;
        _apiKeys = new List<ApiKeyInfo>();
        _moduleToKeyIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 1. Đọc danh sách API keys từ configuration
        var apiKeysSection = configuration.GetSection("GeminiAI:ApiKeys");
        var singleKey = configuration["GeminiAI:ApiKey"];

        if (apiKeysSection.Exists())
        {
            var keys = apiKeysSection.Get<List<string>>();
            if (keys != null && keys.Count > 0)
            {
                foreach (var k in keys)
                {
                    if (!string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 10)
                    {
                        var trimmed = k.Trim();
                        if (!_apiKeys.Any(x => x.Key == trimmed))
                        {
                            _apiKeys.Add(new ApiKeyInfo { Key = trimmed });
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(singleKey) && singleKey.Trim().Length >= 10)
        {
            var trimmed = singleKey.Trim();
            if (!_apiKeys.Any(x => x.Key == trimmed))
            {
                _apiKeys.Add(new ApiKeyInfo { Key = trimmed });
            }
        }

        if (_apiKeys.Count == 0)
        {
            _logger.LogWarning("⚠️ Không tìm thấy Gemini API key trong config, thêm placeholder key");
            _apiKeys.Add(new ApiKeyInfo { Key = "AIzaSyPlaceholderKeyForATSInitialization000" });
        }

        // 2. Đọc ModuleMapping từ configuration nếu có
        var mappingSection = configuration.GetSection("GeminiAI:ModuleMapping");
        if (mappingSection.Exists())
        {
            var mappingDict = mappingSection.Get<Dictionary<string, int>>();
            if (mappingDict != null)
            {
                foreach (var kvp in mappingDict)
                {
                    if (kvp.Value >= 0 && kvp.Value < _apiKeys.Count)
                    {
                        _moduleToKeyIndex[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // Đảm bảo tất cả supported modules đều có mapping
        EnsureAllModulesMapped();

        _logger.LogInformation("✅ GeminiApiKeyProvider khởi tạo thành công với {Count} API keys", _apiKeys.Count);

        // Tự động kiểm tra sức khỏe ban đầu nền cho các keys
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                await CheckAllKeysHealthAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi kiểm tra sức khỏe ban đầu của Gemini keys");
            }
        });
    }

    private void EnsureAllModulesMapped()
    {
        for (int i = 0; i < SupportedModules.Count; i++)
        {
            var modId = SupportedModules[i].Id;
            if (!_moduleToKeyIndex.TryGetValue(modId, out var mappedIndex) || mappedIndex < 0 || mappedIndex >= _apiKeys.Count)
            {
                _moduleToKeyIndex[modId] = _apiKeys.Count > 0 ? (i % _apiKeys.Count) : 0;
            }
        }
    }

    public List<ModuleInfo> GetSupportedModules()
    {
        return SupportedModules;
    }

    public string GetApiKey(string moduleName)
    {
        lock (_lock)
        {
            if (_apiKeys.Count == 0) return string.Empty;

            if (!string.IsNullOrEmpty(moduleName) && _moduleToKeyIndex.TryGetValue(moduleName, out var keyIndex))
            {
                if (keyIndex >= 0 && keyIndex < _apiKeys.Count)
                {
                    var info = _apiKeys[keyIndex];
                    if (!info.Disabled && info.Status != "InvalidKey" && info.Status != "Forbidden")
                    {
                        _logger.LogDebug("🔑 Lấy API key cho module '{Module}' (Key #{Index})", moduleName, keyIndex + 1);
                        return info.Key;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Key #{Index} được gán cho module '{Module}' đang ở trạng thái {Status}, tự động chuyển sang key dự phòng lành lặn.", keyIndex + 1, moduleName, info.Status);
                    }
                }
            }

            return GetNextApiKeyInternal();
        }
    }

    public string GetNextApiKey()
    {
        lock (_lock)
        {
            return GetNextApiKeyInternal();
        }
    }

    private string GetNextApiKeyInternal()
    {
        if (_apiKeys.Count == 0) return string.Empty;

        // Ưu tiên các key Healthy / chưa bị Disable và chưa bị InvalidKey
        for (int i = 0; i < _apiKeys.Count; i++)
        {
            var idx = (_roundRobinIndex + i) % _apiKeys.Count;
            var candidate = _apiKeys[idx];
            if (!candidate.Disabled && candidate.Status != "InvalidKey" && candidate.Status != "Forbidden")
            {
                var key = candidate.Key;
                _roundRobinIndex = (idx + 1) % _apiKeys.Count;
                return key;
            }
        }

        // Nếu tất cả các key đều lỗi hoặc bị disable, fallback sang key có ít lỗi nhất
        var bestFallback = _apiKeys
            .OrderBy(k => k.Disabled ? 1 : 0)
            .ThenBy(k => k.FailureCount)
            .FirstOrDefault()?.Key ?? _apiKeys[0].Key;

        _roundRobinIndex = (_roundRobinIndex + 1) % _apiKeys.Count;
        return bestFallback;
    }

    public List<string> GetAllApiKeys()
    {
        lock (_lock)
        {
            return _apiKeys.Select(i => i.Key).ToList();
        }
    }

    public int GetApiKeyCount()
    {
        lock (_lock)
        {
            return _apiKeys.Count;
        }
    }

    public void ReportFailure(string apiKey)
    {
        ReportFailure(apiKey, null);
    }

    public void ReportFailure(string apiKey, string? errorMessage = null)
    {
        lock (_lock)
        {
            var info = _apiKeys.FirstOrDefault(i => i.Key == apiKey);
            if (info == null) return;
            info.FailureCount++;
            info.LastFailureAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                info.ErrorMessage = errorMessage;
                info.Status = errorMessage.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || errorMessage.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ? "InvalidKey" :
                             errorMessage.Contains("429", StringComparison.OrdinalIgnoreCase) ? "RateLimited" :
                             errorMessage.Contains("403", StringComparison.OrdinalIgnoreCase) ? "Forbidden" :
                             errorMessage.Contains("404", StringComparison.OrdinalIgnoreCase) ? "ModelNotFound" :
                             errorMessage.Contains("503", StringComparison.OrdinalIgnoreCase) ? "ServiceUnavailable" : "Error";
            }

            if (info.Status == "InvalidKey")
            {
                info.Disabled = true;
                _logger.LogWarning("🚫 API key đã bị vô hiệu hóa vì Google xác nhận Key không hợp lệ: {Key}", TruncateKey(apiKey));
            }
            else if (info.FailureCount >= _failureThreshold)
            {
                info.Disabled = true;
                info.Status = "Disabled";
                _logger.LogWarning("🚫 API key disabled after repeated failures: {Key}", TruncateKey(apiKey));
            }
        }
    }

    public void ReportSuccess(string apiKey)
    {
        lock (_lock)
        {
            var info = _apiKeys.FirstOrDefault(i => i.Key == apiKey);
            if (info == null) return;
            info.SuccessCount++;
            info.FailureCount = Math.Max(0, info.FailureCount - 1);
            info.Status = "Healthy";
            info.ErrorMessage = null;
            if (info.Disabled && info.FailureCount == 0)
            {
                info.Disabled = false;
                _logger.LogInformation("✅ API key re-enabled after recovery: {Key}", TruncateKey(apiKey));
            }
        }
    }

    public List<DATN.Services.Models.GeminiKeyStat> GetKeyStats()
    {
        lock (_lock)
        {
            return _apiKeys.Select((i, index) => {
                var assigned = _moduleToKeyIndex
                    .Where(kvp => kvp.Value == index)
                    .Select(kvp => kvp.Key)
                    .ToList();

                var effectiveStatus = i.Disabled ? "Disabled" : (!string.IsNullOrWhiteSpace(i.Status) ? i.Status : "Healthy");

                return new DATN.Services.Models.GeminiKeyStat
                {
                    KeyIndex = index,
                    KeyPreview = TruncateKey(i.Key),
                    SuccessCount = i.SuccessCount,
                    FailureCount = i.FailureCount,
                    Disabled = i.Disabled,
                    AssignedModules = assigned,
                    Status = effectiveStatus,
                    ErrorMessage = i.ErrorMessage,
                    LastCheckedAt = i.LastCheckedAt
                };
            }).ToList();
        }
    }

    /// <summary>
    /// Xây dựng URL gọi Gemini API tự động tương thích với mọi loại Google API Key:
    /// - Key Vertex AI Express / Google Cloud (AQ....): aiplatform.googleapis.com
    /// - Key Google AI Studio (AIza...) hoặc các key khác: generativelanguage.googleapis.com
    /// </summary>
    public static string BuildGenerateContentUrl(string apiKey, string model, bool isStream = false)
    {
        var cleanKey = (apiKey ?? string.Empty).Trim();
        var action = isStream ? "streamGenerateContent?alt=sse&" : "generateContent?";
        if (cleanKey.StartsWith("AQ.", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://aiplatform.googleapis.com/v1/publishers/google/models/{model}:{action}key={Uri.EscapeDataString(cleanKey)}";
        }
        return $"https://generativelanguage.googleapis.com/v1beta/models/{model}:{action}key={Uri.EscapeDataString(cleanKey)}";
    }

    private async Task<(string Status, string? ErrorMessage)> ProbeSingleKeyLiveAsync(HttpClient client, string key)
    {
        var cleanKey = key.Trim();
        var probeBody = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = "ping" } } }
            }
        };
        var jsonBody = JsonSerializer.Serialize(probeBody);

        // Danh sách các endpoint thực tế của Google để kiểm tra (hỗ trợ cả AI Studio Key, Vertex AI Express Key AQ.*, OAuth Token...)
        var endpointsToTry = new List<string>();
        var encodedKey = Uri.EscapeDataString(cleanKey);

        if (cleanKey.StartsWith("AQ.", StringComparison.OrdinalIgnoreCase))
        {
            endpointsToTry.Add($"https://aiplatform.googleapis.com/v1/publishers/google/models/gemini-2.5-flash:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://aiplatform.googleapis.com/v1/publishers/google/models/gemini-2.0-flash:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={encodedKey}");
        }
        else
        {
            endpointsToTry.Add($"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={encodedKey}");
            endpointsToTry.Add($"https://aiplatform.googleapis.com/v1/publishers/google/models/gemini-2.5-flash:generateContent?key={encodedKey}");
        }

        string lastStatus = "Error";
        string? lastError = "Không thể xác thực API Key với máy chủ Google.";

        foreach (var url in endpointsToTry)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
                };

                if (cleanKey.StartsWith("ya29.", StringComparison.OrdinalIgnoreCase))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanKey);
                }

                using var res = await client.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var isPreviewFallback = url.Contains("gemini-3-flash-preview", StringComparison.OrdinalIgnoreCase);
                    var isVertex = url.Contains("aiplatform.googleapis.com", StringComparison.OrdinalIgnoreCase);
                    string? note = isVertex
                        ? "Hoạt động tốt (Google Cloud Vertex AI)"
                        : (isPreviewFallback ? "Hoạt động tốt (Định tuyến qua Gemini Flash Preview)" : null);
                    return ("Healthy", note);
                }

                var errContent = await res.Content.ReadAsStringAsync();

                if ((int)res.StatusCode == 429)
                {
                    return ("RateLimited", "Hết hạn ngạch / Bị giới hạn tốc độ (429 Too Many Requests)");
                }

                if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    lastStatus = "Forbidden";
                    lastError = "Tài khoản Google chưa kích hoạt Billing hoặc bị chặn quyền API (403)";
                    continue;
                }

                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    lastStatus = "ModelNotFound";
                    lastError = "Mô hình Gemini không khả dụng trên endpoint này (404)";
                    continue;
                }

                if (res.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                    (errContent.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                     errContent.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
                     errContent.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)))
                {
                    lastStatus = "InvalidKey";
                    lastError = "API Key không hợp lệ hoặc đã bị thu hồi trên Google Cloud / AI Studio";
                    continue;
                }

                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    lastStatus = "InvalidKey";
                    lastError = "API Key / Token không được ủy quyền hoặc đã hết hạn (401)";
                    continue;
                }

                if (res.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    return ("ServiceUnavailable", "Google AI tạm thời quá tải (503)");
                }

                lastStatus = "Error";
                lastError = $"Lỗi gọi API: HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex)
            {
                lastStatus = "Error";
                lastError = $"Lỗi kết nối mạng: {ex.Message}";
            }
        }

        return (lastStatus, lastError);
    }

    public async Task<List<DATN.Services.Models.GeminiKeyStat>> CheckAllKeysHealthAsync()
    {
        var keysToCheck = new List<(int Index, string Key)>();
        lock (_lock)
        {
            for (int i = 0; i < _apiKeys.Count; i++)
            {
                if (!_apiKeys[i].Disabled)
                {
                    keysToCheck.Add((i, _apiKeys[i].Key));
                }
                else
                {
                    _apiKeys[i].Status = "Disabled";
                    _apiKeys[i].ErrorMessage = _apiKeys[i].ErrorMessage ?? "Đã vô hiệu hóa bởi quản trị viên";
                    _apiKeys[i].LastCheckedAt = DateTime.UtcNow;
                }
            }
        }

        var client = _httpClientFactory.CreateClient("GeminiClient");
        client.Timeout = TimeSpan.FromSeconds(12);

        foreach (var (idx, key) in keysToCheck)
        {
            var (status, errorMsg) = await ProbeSingleKeyLiveAsync(client, key);
            lock (_lock)
            {
                if (idx < _apiKeys.Count)
                {
                    var info = _apiKeys[idx];
                    info.LastCheckedAt = DateTime.UtcNow;
                    info.Status = status;
                    info.ErrorMessage = errorMsg;
                    if (status != "Healthy")
                    {
                        info.FailureCount++;
                    }
                }
            }
        }

        return GetKeyStats();
    }

    public bool DisableKey(int keyIndex)
    {
        lock (_lock)
        {
            if (keyIndex < 0 || keyIndex >= _apiKeys.Count) return false;
            _apiKeys[keyIndex].Disabled = true;
            _apiKeys[keyIndex].Status = "Disabled";
            _apiKeys[keyIndex].ErrorMessage = "Đã vô hiệu hóa bởi quản trị viên";
            _logger.LogWarning("🚫 API key #{Index} disabled by admin", keyIndex + 1);
            return true;
        }
    }

    public bool EnableKey(int keyIndex)
    {
        lock (_lock)
        {
            if (keyIndex < 0 || keyIndex >= _apiKeys.Count) return false;
            var info = _apiKeys[keyIndex];
            info.Disabled = false;
            info.FailureCount = 0;
            info.LastFailureAt = null;
            info.Status = "Healthy";
            info.ErrorMessage = null;
            _logger.LogInformation("✅ API key #{Index} enabled by admin", keyIndex + 1);
            return true;
        }
    }

    public async Task<bool> AddKeyAsync(string key, List<string>? assignedModules = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var cleanKey = key.Trim();

        // Không hardcode tiền tố AIzaSy — Chấp nhận mọi định dạng Google API Key (AIza..., AQ...., ya29...., v.v.) miễn đủ độ dài tối thiểu và không chứa khoảng trắng
        if (cleanKey.Length < 10 || cleanKey.Any(char.IsWhiteSpace))
        {
            _logger.LogWarning("⚠️ API Key không hợp lệ (quá ngắn hoặc chứa khoảng trắng).");
            return false;
        }

        lock (_lock)
        {
            if (_apiKeys.Any(i => i.Key.Equals(cleanKey, StringComparison.Ordinal)))
            {
                _logger.LogWarning("⚠️ API Key đã tồn tại: {Key}", TruncateKey(cleanKey));
                return false;
            }
        }

        // Tự động kiểm tra thực tế (probe) tình trạng key trên các cổng Google AI Studio & Vertex AI
        string initialStatus = "Healthy";
        string? initialError = null;
        try
        {
            var client = _httpClientFactory.CreateClient("GeminiClient");
            client.Timeout = TimeSpan.FromSeconds(10);
            (initialStatus, initialError) = await ProbeSingleKeyLiveAsync(client, cleanKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Không thể probe key mới ngay lập tức, vẫn lưu key vào hệ thống.");
        }

        lock (_lock)
        {
            if (_apiKeys.Any(i => i.Key.Equals(cleanKey, StringComparison.Ordinal))) return false;

            _apiKeys.Add(new ApiKeyInfo
            {
                Key = cleanKey,
                Status = initialStatus,
                ErrorMessage = initialError,
                LastCheckedAt = DateTime.UtcNow,
                FailureCount = initialStatus == "Healthy" ? 0 : 1
            });
            var newIndex = _apiKeys.Count - 1;

            if (assignedModules != null && assignedModules.Any())
            {
                foreach (var mod in assignedModules)
                {
                    _moduleToKeyIndex[mod] = newIndex;
                }
            }

            EnsureAllModulesMapped();
            SaveToAppSettings();
            return true;
        }
    }

    public bool DeleteKey(int keyIndex)
    {
        lock (_lock)
        {
            if (keyIndex < 0 || keyIndex >= _apiKeys.Count) return false;
            if (_apiKeys.Count <= 1)
            {
                _logger.LogWarning("⚠️ Không thể xóa key duy nhất còn lại trong hệ thống.");
                return false;
            }

            _apiKeys.RemoveAt(keyIndex);

            // Cập nhật lại module mappings
            var modules = _moduleToKeyIndex.Keys.ToList();
            foreach (var mod in modules)
            {
                var curIdx = _moduleToKeyIndex[mod];
                if (curIdx == keyIndex)
                {
                    // Nếu module gán cho key vừa bị xóa, chuyển về key 0
                    _moduleToKeyIndex[mod] = 0;
                }
                else if (curIdx > keyIndex)
                {
                    _moduleToKeyIndex[mod] = curIdx - 1;
                }
            }

            EnsureAllModulesMapped();
            SaveToAppSettings();
            _logger.LogInformation("🗑️ Đã xóa API Key #{Index} và cập nhật appsettings.json", keyIndex + 1);
            return true;
        }
    }

    public bool AssignModulesToKey(int keyIndex, List<string> modules)
    {
        lock (_lock)
        {
            if (keyIndex < 0 || keyIndex >= _apiKeys.Count) return false;
            if (modules == null) return false;

            // Xóa phân bổ cũ của keyIndex cho những module không còn trong danh sách
            var currentMapped = _moduleToKeyIndex.Where(kvp => kvp.Value == keyIndex).Select(kvp => kvp.Key).ToList();
            foreach (var oldMod in currentMapped)
            {
                if (!modules.Contains(oldMod, StringComparer.OrdinalIgnoreCase))
                {
                    // Chuyển sang key khác nếu có
                    var fallbackKey = (keyIndex + 1) % _apiKeys.Count;
                    _moduleToKeyIndex[oldMod] = fallbackKey;
                }
            }

            // Gán các module mới được chọn cho keyIndex
            foreach (var mod in modules)
            {
                if (SupportedModules.Any(m => m.Id.Equals(mod, StringComparison.OrdinalIgnoreCase)))
                {
                    _moduleToKeyIndex[mod] = keyIndex;
                }
            }

            EnsureAllModulesMapped();
            SaveToAppSettings();
            _logger.LogInformation("📌 Cập nhật phân bổ API cho Key #{Index}: [{Modules}]", keyIndex + 1, string.Join(", ", modules));
            return true;
        }
    }

    private void SaveToAppSettings()
    {
        try
        {
            var contentRoot = _hostEnvironment?.ContentRootPath ?? Directory.GetCurrentDirectory();
            var filePath = Path.Combine(contentRoot, "appsettings.json");

            if (!File.Exists(filePath))
            {
                // Thử tìm trong thư mục cha hoặc current directory
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("⚠️ Không tìm thấy file appsettings.json tại {Path}", filePath);
                return;
            }

            var jsonText = File.ReadAllText(filePath);
            var jsonNode = JsonNode.Parse(jsonText);
            if (jsonNode == null) return;

            var geminiNode = jsonNode["GeminiAI"]?.AsObject();
            if (geminiNode == null)
            {
                geminiNode = new JsonObject();
                jsonNode["GeminiAI"] = geminiNode;
            }

            // Cập nhật ApiKeys
            var keysArray = new JsonArray();
            foreach (var k in _apiKeys)
            {
                keysArray.Add(k.Key);
            }
            geminiNode["ApiKeys"] = keysArray;

            // Cập nhật ModuleMapping
            var mappingObj = new JsonObject();
            foreach (var kvp in _moduleToKeyIndex)
            {
                mappingObj[kvp.Key] = kvp.Value;
            }
            geminiNode["ModuleMapping"] = mappingObj;

            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, jsonNode.ToJsonString(writeOptions));

            _logger.LogInformation("💾 Đã lưu thành công ApiKeys và ModuleMapping vào {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Lỗi khi lưu cấu hình vào appsettings.json");
        }
    }

    private string TruncateKey(string k)
    {
        if (string.IsNullOrEmpty(k)) return "(empty)";
        return k.Length <= 8 ? k : k[..4] + "..." + k[^4..];
    }
}
