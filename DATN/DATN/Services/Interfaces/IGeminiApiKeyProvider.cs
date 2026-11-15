using System.Collections.Generic;
using System.Threading.Tasks;

namespace DATN.Services.Interfaces;

/// <summary>
/// Interface để quản lý nhiều Gemini API keys
/// Hỗ trợ phân tán tải và gán module phụ trách theo từng API
/// </summary>
public interface IGeminiApiKeyProvider
{
    /// <summary>
    /// Lấy API key cho module cụ thể
    /// </summary>
    /// <param name="moduleName">Tên module (Chatbot, JD, Interview, CV, Email, Code)</param>
    /// <returns>API key</returns>
    string GetApiKey(string moduleName);

    /// <summary>
    /// Lấy API key tiếp theo bằng round-robin
    /// </summary>
    /// <returns>API key</returns>
    string GetNextApiKey();

    /// <summary>
    /// Lấy tất cả API keys được cấu hình
    /// </summary>
    /// <returns>List of API keys</returns>
    List<string> GetAllApiKeys();

    /// <summary>
    /// Lấy số lượng API keys được cấu hình
    /// </summary>
    /// <returns>Count of API keys</returns>
    int GetApiKeyCount();

    /// <summary>
    /// Báo cáo một request dùng key bị lỗi (status 429/5xx hoặc exception)
    /// </summary>
    void ReportFailure(string apiKey);

    /// <summary>
    /// Báo cáo một request dùng key thành công
    /// </summary>
    void ReportSuccess(string apiKey);

    /// <summary>
    /// Lấy thống kê các API keys (preview, success/failure counts, disabled flag, assigned modules)
    /// </summary>
    List<Models.GeminiKeyStat> GetKeyStats();

    /// <summary>
    /// Vô hiệu hóa Gemini API key theo index
    /// </summary>
    bool DisableKey(int keyIndex);

    /// <summary>
    /// Kích hoạt lại Gemini API key theo index
    /// </summary>
    bool EnableKey(int keyIndex);

    /// <summary>
    /// Đăng ký và thêm Gemini API key mới vào cấu hình hệ thống
    /// </summary>
    Task<bool> AddKeyAsync(string key, List<string>? assignedModules = null);

    /// <summary>
    /// Xóa Gemini API key theo index và cập nhật đồng bộ vào appsettings.json
    /// </summary>
    bool DeleteKey(int keyIndex);

    /// <summary>
    /// Phân bổ / Gán danh sách module (Chatbot, JD, Interview, CV, Email, Code) cho key theo index
    /// và lưu vào appsettings.json
    /// </summary>
    bool AssignModulesToKey(int keyIndex, List<string> modules);

    /// <summary>
    /// Lấy danh sách các module/API được hỗ trợ phân bổ
    /// </summary>
    List<ModuleInfo> GetSupportedModules();
}

public class ModuleInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
