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
                    if (!info.Disabled)
                    {
                        _logger.LogDebug("🔑 Lấy API key cho module '{Module}' (Key #{Index})", moduleName, keyIndex + 1);
                        return info.Key;
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

        for (int i = 0; i < _apiKeys.Count; i++)
        {
            var idx = (_roundRobinIndex + i) % _apiKeys.Count;
            if (!_apiKeys[idx].Disabled)
            {
                var key = _apiKeys[idx].Key;
                _roundRobinIndex = (idx + 1) % _apiKeys.Count;
                return key;
            }
        }

        var fallback = _apiKeys[_roundRobinIndex % _apiKeys.Count].Key;
        _roundRobinIndex = (_roundRobinIndex + 1) % _apiKeys.Count;
        return fallback;
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
        lock (_lock)
        {
            var info = _apiKeys.FirstOrDefault(i => i.Key == apiKey);
            if (info == null) return;
            info.FailureCount++;
            info.LastFailureAt = DateTime.UtcNow;

            if (info.FailureCount >= _failureThreshold)
            {
                info.Disabled = true;
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

                return new DATN.Services.Models.GeminiKeyStat
                {
                    KeyIndex = index,
                    KeyPreview = TruncateKey(i.Key),
                    SuccessCount = i.SuccessCount,
                    FailureCount = i.FailureCount,
                    Disabled = i.Disabled,
                    AssignedModules = assigned
                };
            }).ToList();
        }
    }

    public bool DisableKey(int keyIndex)
    {
        lock (_lock)
        {
            if (keyIndex < 0 || keyIndex >= _apiKeys.Count) return false;
            _apiKeys[keyIndex].Disabled = true;
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
