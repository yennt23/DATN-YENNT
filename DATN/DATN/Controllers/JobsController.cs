using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DATN.Data;
using DATN.DTOs.Job;
using DATN.Services.Interfaces;

namespace DATN.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly DATNContext _context;
    private readonly ILogger<JobsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeminiApiKeyProvider _apiKeyProvider;

    public JobsController(
        IJobService jobService,
        DATNContext context,
        ILogger<JobsController> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IGeminiApiKeyProvider apiKeyProvider)
    {
        _jobService = jobService;
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _apiKeyProvider = apiKeyProvider;
    }

    /// <summary>
    /// API đăng tin tuyển dụng
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        try
        {
            // Validate model
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Lấy UserId từ token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác thực người dùng" });
            }

            var result = await _jobService.CreateJobAsync(request, userId);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(new { message = result.Message ?? "Đăng tin tuyển dụng thất bại" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API cập nhật tin tuyển dụng
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateJob(Guid id, [FromBody] UpdateJobRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _jobService.UpdateJobAsync(id, request);

            if (result)
            {
                return Ok(new { message = "Cập nhật tin tuyển dụng thành công" });
            }
            else
            {
                return BadRequest(new { message = "Cập nhật thất bại hoặc không tìm thấy tin tuyển dụng" });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API xóa tin tuyển dụng
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteJob(Guid id)
    {
        try
        {
            var result = await _jobService.DeleteJobAsync(id);

            if (result)
            {
                return Ok(new { message = "Xóa tin tuyển dụng thành công" });
            }
            else
            {
                return BadRequest(new { message = "Xóa thất bại hoặc không tìm thấy tin tuyển dụng" });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API đóng tin tuyển dụng (Ngừng đăng)
    /// </summary>
    [HttpPut("{id}/close")]
    public async Task<IActionResult> CloseJob(Guid id)
    {
        try
        {
            // Lấy thông tin người thực hiện
            var closedByIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(closedByIdClaim, out Guid closedById))
                return Unauthorized();
            var fullName = User.FindFirst("FullName")?.Value ?? "User";
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";
            var closedByName = $"{fullName} {role}";

            var result = await _jobService.CloseJobAsync(id, closedById, closedByName);

            if (result)
                return Ok(new { message = "Dã ngưng đăng tin tuyển dụng" });
            else
                return BadRequest(new { message = "Thao tác thất bại hoặc không tìm thấy tin tuyển dụng" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API mở lại tin tuyển dụng
    /// </summary>
    [HttpPut("{id}/open")]
    public async Task<IActionResult> OpenJob(Guid id)
    {
        try
        {
            var result = await _jobService.OpenJobAsync(id);

            if (result)
            {
                return Ok(new { message = "Đã mở lại tin tuyển dụng" });
            }
            else
            {
                return BadRequest(new { message = "Thao tác thất bại hoặc không tìm thấy tin tuyển dụng" });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lấy Google Form + mã QR của chiến dịch
    /// </summary>
    [HttpGet("{id}/campaign-qr")]
    public async Task<IActionResult> GetCampaignQr(Guid id, [FromQuery] string? publicApiUrl = null, [FromQuery] string? publicClientUrl = null)
    {
        try
        {
            var campaign = await _jobService.GetCampaignQrAsync(id, publicApiUrl, publicClientUrl);
            if (campaign == null)
                return NotFound(new { message = "Không tìm thấy tin tuyển dụng." });
            return Ok(campaign);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// Tạo hoặc làm mới Google Form + QR cho chiến dịch đã có
    /// </summary>
    [HttpPost("{id}/campaign-qr")]
    public async Task<IActionResult> EnsureCampaignQr(
        Guid id,
        [FromQuery] bool force = false,
        [FromQuery] string? publicApiUrl = null,
        [FromQuery] string? publicClientUrl = null)
    {
        try
        {
            var campaign = await _jobService.EnsureCampaignQrAsync(id, force, publicApiUrl, publicClientUrl);
            return Ok(campaign);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// Phê duyệt định biên ngân sách chiến dịch (Dành cho SuperAdmin, HR_Manager)
    /// </summary>
    [HttpPost("{id}/approve-requisition")]
    [Authorize]
    public async Task<IActionResult> ApproveRequisition(Guid id)
    {
        try
        {
            var approverIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(approverIdClaim, out Guid approverId);

            var success = await _jobService.ApproveRequisitionAsync(id, approverId);
            if (!success)
                return NotFound(new { message = "Không tìm thấy chiến dịch tuyển dụng." });

            return Ok(new { success = true, message = "Phê duyệt định biên chiến dịch thành công. Trạng thái đã mở công khai." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi khi phê duyệt định biên: {ex.Message}" });
        }
    }

    /// <summary>
    /// Cập nhật cấu hình Public / ngrok URL toàn hệ thống và sinh lại link / Apps Script cho Job
    /// </summary>
    [HttpPut("{id}/campaign-qr/public-config")]
    public async Task<IActionResult> UpdatePublicConfig(Guid id, [FromBody] UpdatePublicIntegrationConfigRequest request)
    {
        try
        {
            var campaign = await _jobService.EnsureCampaignQrAsync(id, forceRecreate: false, request.PublicApiBaseUrl, request.PublicClientBaseUrl);
            return Ok(campaign);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gán đường link Google Form có sẵn (hoặc form tùy chọn) vào Job và sinh lại mã QR ngay trên web
    /// </summary>
    [HttpPut("{id}/campaign-qr/custom-form")]
    public async Task<IActionResult> UpdateCustomFormUrl(Guid id, [FromBody] UpdateCustomFormUrlRequest request)
    {
        try
        {
            var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == id && !j.IsDeleted);
            if (job == null) return NotFound(new { message = "Không tìm thấy tin tuyển dụng." });

            if (string.IsNullOrWhiteSpace(request.FormUrl))
            {
                job.GoogleFormPublicUrl = null;
                job.GoogleFormUrl = null;
                job.GoogleFormId = null;
                job.GoogleFormEditUrl = null;
                job.CampaignFormSource = "GOOGLE_FORM";
            }
            else
            {
                var cleanUrl = request.FormUrl.Trim();
                job.GoogleFormPublicUrl = cleanUrl;
                job.GoogleFormUrl = cleanUrl;
                job.CampaignFormSource = "GOOGLE_FORM";
            }

            await _context.SaveChangesAsync();
            // forceRecreate: true để sinh lại mã QR ngay lập tức cho URL mới vừa cập nhật
            var campaign = await _jobService.EnsureCampaignQrAsync(id, forceRecreate: true);
            return Ok(campaign);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API lấy danh sách tất cả job (cho admin/HR quản lý)
    /// </summary>
    [HttpGet]
    // [Authorize(Roles = "ADMIN,HR")] 
    public async Task<IActionResult> GetAllJobs()
    {
        try
        {
            var jobs = await _jobService.GetAllJobsAsync();
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API lấy danh sách job mới nhất cho trang chủ
    /// Hỗ trợ tìm kiếm theo keyword (title, company, skills) và location
    /// Nếu user đã đăng nhập, sẽ include thông tin đã ứng tuyển (HasApplied, AppliedAt)
    /// </summary>
    [HttpGet("latest/{count}")]
    [AllowAnonymous] // Cho phép truy cập không cần token
    public async Task<IActionResult> GetLatestJobs(
        int count = 10,
        [FromQuery] string? keyword = null,
        [FromQuery] string? location = null)
    {
        try
        {
            var jobs = await _jobService.GetLatestJobsAsync(count, keyword, location);
            
            // Nếu user đã đăng nhập, thêm thông tin đã ứng tuyển
            var token = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
                    {
                        var candidate = await _context.Candidates.FirstOrDefaultAsync(c => c.UserId == userGuid);
                        if (candidate != null)
                        {
                            var appliedJobs = await _context.Applications
                                .Where(a => a.CandidateId == candidate.CandidateId)
                                .Select(a => new { a.JobId, a.AppliedAt })
                                .ToListAsync();

                            var appliedJobDictionary = appliedJobs.ToDictionary(a => a.JobId, a => a.AppliedAt);

                            foreach (var job in jobs)
                            {
                                if (appliedJobDictionary.TryGetValue(job.JobId, out var appliedAt))
                                {
                                    job.HasApplied = true;
                                    job.AppliedAt = appliedAt;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi lấy thông tin đã ứng tuyển");
                }
            }

            return Ok(jobs);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// API lấy chi tiết job theo ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous] 
    public async Task<IActionResult> GetJobById(Guid id)
    {
        try
        {
            var job = await _jobService.GetJobByIdAsync(id);
            
            if (job == null)
            {
                return NotFound(new { message = "Không tìm thấy công việc" });
            }
            
            return Ok(job);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi: {ex.Message}" });
        }
    }

    /// <summary>
    /// AI sinh nội dung Job Description (description, requirements, benefits)
    /// dựa trên tiêu đề, cấp độ, yêu cầu chính và mức lương
    /// </summary>
    [HttpPost("ai-generate-jd")]
    [AllowAnonymous]
    public async Task<IActionResult> AiGenerateJd([FromBody] AiGenerateJdRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Tiêu đề vị trí là bắt buộc." });

        _logger.LogInformation("[AI JD] Generating JD for: {Title} / {Level}", request.Title, request.Level);

        var salaryText = (request.SalaryMin.HasValue || request.SalaryMax.HasValue)
            ? $"{request.SalaryMin?.ToString("N0") ?? "?"} – {request.SalaryMax?.ToString("N0") ?? "?"} {request.Currency ?? "VNĐ"}/tháng"
            : "Thỏa thuận theo năng lực";

        // Thử gọi qua Google Gemini API
        var apiKey = _apiKeyProvider.GetApiKey("JD");
        if (!string.IsNullOrEmpty(apiKey))
        {
            var prompt = $@"Bạn là chuyên gia tuyển dụng nhân sự nhà máy, khu công nghiệp và kỹ thuật sản xuất tại Việt Nam.
Hãy viết Job Description thực tế, chuẩn xác cho vị trí sau:

- Tên vị trí tuyển dụng: {request.Title}
- Cấp độ/Kinh nghiệm: {(string.IsNullOrWhiteSpace(request.Level) ? "Phổ thông / Trung cấp / Kỹ sư" : request.Level)}
- Yêu cầu chính / Nhu cầu chi tiết: {(string.IsNullOrWhiteSpace(request.KeyRequirements) ? "Không có" : request.KeyRequirements)}
- Mức lương dự kiến: {salaryText}

Yêu cầu định dạng:
1. Viết bằng tiếng Việt, rõ ràng, thực tế, hấp dẫn người lao động và ứng viên kỹ thuật
2. description: Giới thiệu công việc, nhiệm vụ chính và môi trường làm việc (150-250 từ)
3. requirements: Danh sách các gạch đầu dòng yêu cầu kỹ năng, sức khỏe, tác phong, độ tuổi (mỗi điểm trên 1 dòng bắt đầu bằng •)
4. benefits: Danh sách các quyền lợi, chế độ đãi ngộ, bảo hiểm, ăn ca, phụ cấp, thưởng (mỗi điểm trên 1 dòng bắt đầu bằng •)

CHỈ trả về JSON thuần (không markdown, không code block):
{{
  ""description"": ""<nội dung mô tả>"",
  ""requirements"": ""<danh sách yêu cầu, mỗi điểm trên 1 dòng bắt đầu bằng • >"",
  ""benefits"": ""<danh sách quyền lợi, mỗi điểm trên 1 dòng bắt đầu bằng • >""
}}";

            try
            {
                var httpClient = _httpClientFactory.CreateClient("GeminiClient");
                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage? response = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var currentKey = attempt == 0 ? apiKey : _apiKeyProvider.GetNextApiKey();
                    var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={currentKey}";
                    
                    response = await httpClient.PostAsync(apiUrl, jsonContent);
                    if (response.IsSuccessStatusCode) break;
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < 2) continue;
                    break;
                }

                if (response != null && response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseText);
                    var rawText = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString() ?? "";

                    rawText = rawText.Trim();
                    if (rawText.StartsWith("```json")) rawText = rawText[7..];
                    if (rawText.StartsWith("```")) rawText = rawText[3..];
                    if (rawText.EndsWith("```")) rawText = rawText[..^3];
                    rawText = rawText.Trim();

                    using var resultDoc = JsonDocument.Parse(rawText);
                    return Ok(new
                    {
                        description = resultDoc.RootElement.GetProperty("description").GetString() ?? "",
                        requirements = resultDoc.RootElement.GetProperty("requirements").GetString() ?? "",
                        benefits = resultDoc.RootElement.GetProperty("benefits").GetString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AI JD] Gemini external API unavailable, switching to intelligent industrial fallback generator.");
            }
        }

        // Tự động chuyển đổi mượt sang Bộ sinh JD thông minh (Fallback Intelligent Generator)
        _logger.LogInformation("[AI JD] Generating intelligent industrial JD for: {Title}", request.Title);
        var fallbackJd = GenerateSmartIndustrialJd(request, salaryText);

        return Ok(new
        {
            description = fallbackJd.description,
            requirements = fallbackJd.requirements,
            benefits = fallbackJd.benefits
        });
    }

    private static (string description, string requirements, string benefits) GenerateSmartIndustrialJd(AiGenerateJdRequest request, string salaryText)
    {
        var title = request.Title.Trim();
        var keyReq = string.IsNullOrWhiteSpace(request.KeyRequirements) ? "" : request.KeyRequirements.Trim();
        var level = string.IsNullOrWhiteSpace(request.Level) ? "Nhân viên / Công nhân" : request.Level.Trim();

        bool isTech = title.ToLower().Contains("plc") || title.ToLower().Contains("kỹ sư") || 
                      title.ToLower().Contains("bảo trì") || title.ToLower().Contains("qa") || 
                      title.ToLower().Contains("qc") || title.ToLower().Contains("it") ||
                      title.ToLower().Contains("tự động hóa");

        string desc;
        string reqs;
        string benefits;

        if (isTech)
        {
            desc = $"Doanh nghiệp đang tuyển dụng vị trí {title} (Cấp bậc: {level}) làm việc tại nhà máy sản xuất công nghệ cao.\n" +
                   $"Vị trí này chịu trách nhiệm trực tiếp trong việc vận hành, bảo trì, giám sát kỹ thuật và tối ưu hóa hệ thống dây chuyền sản xuất hiện đại. " +
                   $"Bạn sẽ được làm việc trong môi trường chuyên nghiệp, trang thiết bị tiên tiến chuẩn quốc tế và lộ trình thăng tiến rõ ràng.";

            reqs = $"• Trình độ: Tốt nghiệp Trung cấp/Cao đẳng/Đại học chuyên ngành Kỹ thuật, Cơ điện tử, Tự động hóa hoặc lĩnh vực liên quan\n" +
                   $"• Kinh nghiệm: Phù hợp cấp bậc {level}, ưu tiên ứng viên có kinh nghiệm thực tế tại các nhà máy, xưởng sản xuất KCN\n" +
                   (string.IsNullOrWhiteSpace(keyReq) ? "" : $"• Yêu cầu cụ thể: {keyReq}\n") +
                   $"• Kỹ năng chuyên môn: Đọc hiểu bản vẽ kỹ thuật, sơ đồ hệ thống, thao tác xử lý lỗi và bảo dưỡng thiết bị định kỳ\n" +
                   $"• Tác phong làm việc cẩn thận, tuân thủ an toàn lao động (5S/Kaizen), khả năng phối hợp nhóm tốt\n" +
                   $"• Sẵn sàng làm việc theo ca hoặc xử lý sự cố kỹ thuật theo yêu cầu điều hành của xưởng";

            benefits = $"• Mức lương: {salaryText} (xem xét tăng lương định kỳ theo năng lực chuyên môn)\n" +
                       $"• Phụ cấp chuyên môn, phụ cấp ca đêm, chuyên cần và hỗ trợ ăn ca miễn phí tại nhà máy\n" +
                       $"• Đóng đầy đủ BHXH, BHYT, BHTN theo Luật lao động ngay sau khi hoàn thành thử việc\n" +
                       $"• Thưởng tháng lương thứ 13, thưởng năng suất KPI và các dịp lễ tết\n" +
                       $"• Xe đưa đón nhân viên theo các tuyến cố định và hỗ trợ ký túc xá cho nhân sự ở xa\n" +
                       $"• Môi trường làm việc an toàn, trang bị đầy đủ bảo hộ lao động đạt chuẩn chất lượng";
        }
        else
        {
            desc = $"Nhà máy tuyển dụng vị trí {title} phục vụ kế hoạch mở rộng quy mô sản xuất và hoàn thành các đơn hàng lớn của khu công nghiệp.\n" +
                   $"Công việc ổn định lâu dài, phù hợp cho lao động phổ thông, nhân công xưởng và các vị trí vận hành trực tiếp. " +
                   $"Được hướng dẫn và đào tạo tận tình từ đầu, môi trường làm việc thân thiện, sạch sẽ, thoáng mát và đảm bảo an toàn tuyệt đối.";

            reqs = $"• Độ tuổi từ 18 - 45 tuổi, sức khỏe tốt, nhanh nhẹn, chăm chỉ và có trách nhiệm\n" +
                   $"• Tinh thần làm việc nghiêm túc, nhiệt tình, sẵn sàng hỗ trợ các bộ phận trong ca trực\n" +
                   (string.IsNullOrWhiteSpace(keyReq) ? "" : $"• Nhu cầu chi tiết: {keyReq}\n") +
                   $"• Có khả năng đứng hoặc ngồi làm việc theo dây chuyền, chấp hành lịch phân ca (ca 8h hoặc ca 12h theo đăng ký)\n" +
                   $"• Tuân thủ nghiêm ngặt nội quy nhà máy, quy định an toàn lao động và trang phục bảo hộ\n" +
                   $"• Lý lịch rõ ràng, có Căn cước công dân gắn chip hợp lệ";

            benefits = $"• Thu nhập bình quân: {salaryText} (Lương cơ bản + Phụ cấp chuyên cần + Thưởng tăng ca)\n" +
                       $"• Hỗ trợ tiền ăn ca, xăng xe đi lại, phụ cấp nhà ở và tiền thưởng chuyên cần hàng tháng\n" +
                       $"• Ký hợp đồng lao động chính thức, tham gia đầy đủ chế độ BHXH, BHYT sau 30 ngày thử việc\n" +
                       $"• Lương tháng 13, thưởng các ngày lễ, tết, quà sinh nhật và chế độ hiếu hỷ đầy đủ\n" +
                       $"• Ký túc xá miễn phí có điều hòa, wifi cho công nhân viên ở xa\n" +
                       $"• Cấp phát đồng phục, giày bảo hộ và đồ dùng làm việc hoàn toàn miễn phí";
        }

        return (desc, reqs, benefits);
    }
}

public class AiGenerateJdRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Level { get; set; }
    public string? KeyRequirements { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? Currency { get; set; }
}
