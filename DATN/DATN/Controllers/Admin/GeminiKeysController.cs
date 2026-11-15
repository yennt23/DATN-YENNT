using Microsoft.AspNetCore.Mvc;
using DATN.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DATN.Controllers.Admin;

[ApiController]
[Route("api/admin/gemini-keys")]
[Authorize(Roles = "ADMIN,HR")]
public class GeminiKeysController : ControllerBase
{
    private readonly IGeminiApiKeyProvider _provider;

    public GeminiKeysController(IGeminiApiKeyProvider provider)
    {
        _provider = provider;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var stats = _provider.GetKeyStats();
        return Ok(stats);
    }

    [HttpGet("supported-modules")]
    public IActionResult GetSupportedModules()
    {
        var modules = _provider.GetSupportedModules();
        return Ok(modules);
    }

    [HttpPost("{keyIndex:int}/disable")]
    public IActionResult Disable(int keyIndex)
    {
        var ok = _provider.DisableKey(keyIndex);
        if (!ok)
        {
            return NotFound(new { message = "Không tìm thấy Gemini API key." });
        }

        return Ok(new { message = "Đã vô hiệu hóa Gemini API key." });
    }

    [HttpPost("{keyIndex:int}/enable")]
    public IActionResult Enable(int keyIndex)
    {
        var ok = _provider.EnableKey(keyIndex);
        if (!ok)
        {
            return NotFound(new { message = "Không tìm thấy Gemini API key." });
        }

        return Ok(new { message = "Đã kích hoạt lại Gemini API key." });
    }

    [HttpDelete("{keyIndex:int}")]
    public IActionResult Delete(int keyIndex)
    {
        var ok = _provider.DeleteKey(keyIndex);
        if (!ok)
        {
            return BadRequest(new { message = "Không thể xóa API key. Key có thể không tồn tại hoặc hệ thống cần giữ lại ít nhất 1 key." });
        }

        return Ok(new { message = "Đã xóa Gemini API key và cập nhật appsettings.json thành công." });
    }

    [HttpPut("{keyIndex:int}/modules")]
    public IActionResult AssignModules(int keyIndex, [FromBody] AssignModulesRequest request)
    {
        if (request == null || request.Modules == null)
        {
            return BadRequest(new { message = "Danh sách module không hợp lệ." });
        }

        var ok = _provider.AssignModulesToKey(keyIndex, request.Modules);
        if (!ok)
        {
            return NotFound(new { message = "Không tìm thấy Gemini API key để phân bổ module." });
        }

        return Ok(new { message = "Đã cập nhật phân bổ tính năng API và lưu vào appsettings.json thành công." });
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] AddGeminiKeyRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Key))
        {
            return BadRequest(new { message = "Gemini API key không được để trống." });
        }

        var ok = await _provider.AddKeyAsync(request.Key, request.AssignedModules);
        if (!ok)
        {
            return BadRequest(new { message = "Thêm key thất bại. Key có thể đã tồn tại hoặc không đúng định dạng của Google Gemini API (AIzaSy...)." });
        }

        return Ok(new { message = "Đã thêm Gemini API key mới và lưu vào appsettings.json thành công." });
    }
}

public class AddGeminiKeyRequest
{
    public string Key { get; set; } = string.Empty;
    public List<string>? AssignedModules { get; set; }
}

public class AssignModulesRequest
{
    public List<string> Modules { get; set; } = new();
}
