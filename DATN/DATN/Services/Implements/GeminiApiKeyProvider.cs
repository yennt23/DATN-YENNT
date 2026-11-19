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
                    if (!string.IsNullOrWhiteSpace(k) && k.Trim().StartsWith("AIzaSy"))
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

        if (!string.IsNullOrWhiteSpace(singleKey) && singleKey.Trim().StartsWith("AIzaSy"))
        {
            var trimmed = singleKey.Trim();
            if (!_apiKeys.Any(x => x.Key == trimmed))
            {
                _apiKeys.Add(new ApiKeyInfo { Key = trimmed });
            }
        }

        if (_apiKeys.Count == 0)
        {
            _logger.LogWarning("⚠️ Không tìm thấy key bắt đầu bằng AIzaSy trong config, thêm placeholder key");
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
            try
            {
                // 1. Kiểm tra tính hợp lệ của API Key bằng Get Models
                var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={key}";
                var response = await client.GetAsync(apiUrl);
                var content = await response.Content.ReadAsStringAsync();

                lock (_lock)
                {
                    if (idx < _apiKeys.Count)
                    {
                        var info = _apiKeys[idx];
                        info.LastCheckedAt = DateTime.UtcNow;

                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && 
                            (content.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || content.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)))
                        {
                            info.Status = "InvalidKey";
                            info.ErrorMessage = "API Key không hợp lệ hoặc đã bị xoá trên Google AI Studio";
                            info.FailureCount++;
                            continue;
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            info.Status = "Forbidden";
                            info.ErrorMessage = "Tài khoản Google chưa kích hoạt Billing hoặc bị chặn (403)";
                            info.FailureCount++;
                            continue;
                        }
                        else if ((int)response.StatusCode == 429)
                        {
                            info.Status = "RateLimited";
                            info.ErrorMessage = "Hết hạn ngạch / Bị giới hạn tốc độ (429 Too Many Requests)";
                            info.FailureCount++;
                            continue;
                        }
                    }
                }

                // 2. Thử nghiệm trực tiếp một truy vấn gọi sinh nội dung thực tế (probe request)
                try
                {
                    var probeBody = new
                    {
                        contents = new[]
                        {
                            new { parts = new[] { new { text = "ping" } } }
                        }
                    };
                    var jsonBody = JsonSerializer.Serialize(probeBody);

                    // Thử gemini-2.5-flash trước
                    var probeUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={key}";
                    var probeReq = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                    var probeRes = await client.PostAsync(probeUrl, probeReq);

                    lock (_lock)
                    {
                        if (idx < _apiKeys.Count)
                        {
                            var info = _apiKeys[idx];
                            if (probeRes.IsSuccessStatusCode)
                            {
                                info.Status = "Healthy";
                                info.ErrorMessage = null;
                            }
                            else if (probeRes.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // Tài khoản bị 404 với gemini-2.5-flash do Google deprecate cho new users, thử fallback sang gemini-3-flash-preview
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var fbUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={key}";
                                        var fbReq = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                                        var fbRes = await client.PostAsync(fbUrl, fbReq);
                                        lock (_lock)
                                        {
                                            if (fbRes.IsSuccessStatusCode)
                                            {
                                                info.Status = "Healthy";
                                                info.ErrorMessage = "Hoạt động tốt (Tự động định tuyến qua Gemini Flash Preview)";
                                            }
                                            else
                                            {
                                                info.Status = "ModelNotFound";
                                                info.ErrorMessage = "Mô hình Gemini không khả dụng cho tài khoản này (404)";
                                                info.FailureCount++;
                                            }
                                        }
                                    }
                                    catch { }
                                });
                            }
                            else if ((int)probeRes.StatusCode == 429)
                            {
                                info.Status = "RateLimited";
                                info.ErrorMessage = "Hết hạn ngạch / Bị giới hạn tốc độ (429 Too Many Requests)";
                                info.FailureCount++;
                            }
                            else if (probeRes.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            {
                                info.Status = "Forbidden";
                                info.ErrorMessage = "Tài khoản Google chưa kích hoạt Billing hoặc bị chặn (403)";
                                info.FailureCount++;
                            }
                            else if (probeRes.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                            {
                                info.Status = "ServiceUnavailable";
                                info.ErrorMessage = "Google AI tạm thời quá tải (503)";
                            }
                            else
                            {
                                info.Status = "Error";
                                info.ErrorMessage = $"Lỗi gọi API: HTTP {(int)probeRes.StatusCode}";
                                info.FailureCount++;
                            }
                        }
                    }
                }
                catch (Exception probeEx)
                {
                    lock (_lock)
                    {
                        if (idx < _apiKeys.Count)
                        {
                            var info = _apiKeys[idx];
                            info.Status = "Error";
                            info.ErrorMessage = $"Lỗi kết nối probe: {probeEx.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    if (idx < _apiKeys.Count)
                    {
                        _apiKeys[idx].Status = "Error";
                        _apiKeys[idx].ErrorMessage = $"Lỗi kết nối mạng: {ex.Message}";
                        _apiKeys[idx].LastCheckedAt = DateTime.UtcNow;
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

    private async Task<bool> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;
        var trimmed = apiKey.Trim();
        if (!trimmed.StartsWith("AIzaSy") || trimmed.Length < 35) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("GeminiClient");
            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={trimmed}";
            var response = await client.GetAsync(apiUrl);

            if (response.IsSuccessStatusCode) return true;

            var errorContent = await response.Content.ReadAsStringAsync();
            // Nếu Google báo API_KEY_INVALID (400) thì key sai hoàn toàn
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && errorContent.Contains("API_KEY_INVALID"))
            {
                _logger.LogWarning("❌ Google API xác nhận Key không hợp lệ (API_KEY_INVALID).");
                return false;
            }

            // Nếu lỗi 403 (chưa kích hoạt billing / quota), key vẫn hợp lệ về mặt cấu trúc
            _logger.LogWarning("⚠️ Google API phản hồi {Status} ({Error}). Key cấu trúc hợp lệ, cho phép lưu.", response.StatusCode, errorContent);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Không thể kết nối tới Google API để xác thực, chấp nhận theo định dạng chuẩn.");
            return true;
        }
    }

    public async Task<bool> AddKeyAsync(string key, List<string>? assignedModules = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var cleanKey = key.Trim();

        lock (_lock)
        {
            if (_apiKeys.Any(i => i.Key == cleanKey))
            {
                _logger.LogWarning("⚠️ API Key đã tồn tại: {Key}", TruncateKey(cleanKey));
                return false;
            }
        }

        var isValid = await ValidateApiKeyAsync(cleanKey);
        if (!isValid) return false;

        lock (_lock)
        {
            if (_apiKeys.Any(i => i.Key == cleanKey)) return false;

            _apiKeys.Add(new ApiKeyInfo { Key = cleanKey });
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
