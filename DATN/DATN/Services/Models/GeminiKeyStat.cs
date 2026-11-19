namespace DATN.Services.Models;

public class GeminiKeyStat
{
    public int KeyIndex { get; set; }
    public string KeyPreview { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public bool Disabled { get; set; }
    public List<string> AssignedModules { get; set; } = new();
    public string Status { get; set; } = "Healthy"; // Healthy, InvalidKey, Forbidden, RateLimited, ServiceUnavailable, Disabled, Error
    public string? ErrorMessage { get; set; }
    public System.DateTime? LastCheckedAt { get; set; }
}
