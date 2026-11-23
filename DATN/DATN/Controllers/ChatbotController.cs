using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using DATN.Models;
using DATN.Data;
using DATN.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using DATN.Services.Interfaces;
using DATN.Services.Implements;

namespace DATN.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ChatbotController> _logger;
        private readonly IConfiguration _configuration;
        private readonly DATNContext _context;
        private readonly IMemoryCache _cache;
        private readonly IGeminiApiKeyProvider _apiKeyProvider;
        private readonly IntentClassifier _intentClassifier;
        private readonly EntityExtractor _entityExtractor;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DATN.Services.Background.IBackgroundTaskQueue _taskQueue;

        public ChatbotController(
            IHttpClientFactory httpClientFactory, 
            ILogger<ChatbotController> logger, 
            IConfiguration configuration,
            DATNContext context,
            IMemoryCache cache,
            IGeminiApiKeyProvider apiKeyProvider,
            IntentClassifier intentClassifier,
            EntityExtractor entityExtractor,
            IServiceScopeFactory scopeFactory,
            DATN.Services.Background.IBackgroundTaskQueue taskQueue)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
            _context = context;
            _cache = cache;
            _apiKeyProvider = apiKeyProvider;
            _intentClassifier = intentClassifier;
            _entityExtractor = entityExtractor;
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _taskQueue = taskQueue ?? throw new ArgumentNullException(nameof(taskQueue));
        }

        [HttpGet("admin/faqs")]
        [Authorize(Roles = "ADMIN,HR")]
        public async Task<IActionResult> GetFaqs([FromQuery] string? search = null, [FromQuery] bool? isActive = null)
        {
            var query = _context.ChatbotFaqs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().ToLower();
                query = query.Where(x => x.Question.ToLower().Contains(keyword)
                    || x.Answer.ToLower().Contains(keyword)
                    || (x.Keywords != null && x.Keywords.ToLower().Contains(keyword))
                    || x.Category.ToLower().Contains(keyword));
            }

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            var items = await query
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.Question)
                .Select(x => new ChatbotFaqAdminDto
                {
                    FaqId = x.FaqId,
                    Question = x.Question,
                    Answer = x.Answer,
                    Category = x.Category,
                    Keywords = x.Keywords,
                    Priority = x.Priority,
                    IsActive = x.IsActive,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        [HttpPost("admin/faqs")]
        [Authorize(Roles = "ADMIN,HR")]
        public async Task<IActionResult> CreateFaq([FromBody] UpsertChatbotFaqDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Question) || string.IsNullOrWhiteSpace(dto.Answer))
            {
                return BadRequest(new { message = "Question và Answer là bắt buộc." });
            }

            var faq = new ChatbotFaq
            {
                FaqId = Guid.NewGuid(),
                Question = dto.Question.Trim(),
                Answer = dto.Answer.Trim(),
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim(),
                Keywords = NormalizeKeywords(dto.Keywords),
                Priority = dto.Priority,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = GetCurrentUserIdOrNull()
            };

            _context.ChatbotFaqs.Add(faq);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo FAQ thành công.", faqId = faq.FaqId });
        }

        [HttpPut("admin/faqs/{faqId:guid}")]
        [Authorize(Roles = "ADMIN,HR")]
        public async Task<IActionResult> UpdateFaq(Guid faqId, [FromBody] UpsertChatbotFaqDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Question) || string.IsNullOrWhiteSpace(dto.Answer))
            {
                return BadRequest(new { message = "Question và Answer là bắt buộc." });
            }

            var faq = await _context.ChatbotFaqs.FirstOrDefaultAsync(x => x.FaqId == faqId);
            if (faq == null)
            {
                return NotFound(new { message = "Không tìm thấy FAQ." });
            }

            faq.Question = dto.Question.Trim();
            faq.Answer = dto.Answer.Trim();
            faq.Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim();
            faq.Keywords = NormalizeKeywords(dto.Keywords);
            faq.Priority = dto.Priority;
            faq.IsActive = dto.IsActive;
            faq.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật FAQ thành công." });
        }

        [HttpPatch("admin/faqs/{faqId:guid}/toggle")]
        [Authorize(Roles = "ADMIN,HR")]
        public async Task<IActionResult> ToggleFaq(Guid faqId)
        {
            var faq = await _context.ChatbotFaqs.FirstOrDefaultAsync(x => x.FaqId == faqId);
            if (faq == null)
            {
                return NotFound(new { message = "Không tìm thấy FAQ." });
            }

            faq.IsActive = !faq.IsActive;
            faq.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = faq.IsActive ? "Đã bật FAQ." : "Đã tắt FAQ.", isActive = faq.IsActive });
        }

        [HttpDelete("admin/faqs/{faqId:guid}")]
        [Authorize(Roles = "ADMIN,HR")]
        public async Task<IActionResult> DeleteFaq(Guid faqId)
        {
            var faq = await _context.ChatbotFaqs.FirstOrDefaultAsync(x => x.FaqId == faqId);
            if (faq == null)
            {
                return NotFound(new { message = "Không tìm thấy FAQ." });
            }

            _context.ChatbotFaqs.Remove(faq);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã xóa FAQ." });
        }

        [HttpPost("ask")]
        public async Task Ask([FromBody] ChatRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                await WriteStreamEvent(Response, "Vui lòng nhập lời nhắn của bạn.");
                return;
            }

            _logger.LogInformation($"[Chatbot] Nhận yêu cầu: {request.Message}");

            try
            {
                // BẮT BUỘC: Đặt Timeout tối đa lên 120s dể phòng trường hợp AI sinh text lên tới 5 trang giấy
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

                // Lớp trả lời nhanh: ưu tiên FAQ trước khi gọi LLM để giảm độ "tù" và giảm token.
                var faqAnswer = await TryGetFaqAnswerAsync(request.Message, cts.Token);
                if (!string.IsNullOrWhiteSpace(faqAnswer))
                {
                    await WriteStreamEvent(Response, faqAnswer);
                    return;
                }

                var sw = Stopwatch.StartNew();
                var httpClient = _httpClientFactory.CreateClient("GeminiClient");

                // Ensure we have a session id and persist the user message
                var sessionId = request.SessionId ?? Guid.NewGuid();
                var savedUserMessage = await SaveUserMessageAsync(request.Message, sessionId, cts.Token);

                var apiKey = _apiKeyProvider.GetApiKey("Chatbot");
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Gemini API Key is not configured.");
                    await WriteStreamEvent(Response, "Lỗi Server: Chưa cấu hình Gemini API Key.");
                    return;
                }

                // 1. Phân luồng dữ liệu (RAG Keyword Router)
                var msgLower = request.Message.ToLower();
                // Nhận diện từ khóa tuyển dụng nhà máy, KCN và kỹ thuật
                var isJobQuery = Regex.IsMatch(msgLower, @"(việc|job|công việc|công nhân|kỹ sư|smt|bảo trì|qa|qc|kho|lái xe|sản xuất|lao động|phụ kho|lắp ráp|vận hành|tuyển|vị trí|chân chạy|thời vụ|chính thức)");
                var isSkillQuery = Regex.IsMatch(msgLower, @"(kỹ năng|skill|yêu cầu|tiêu chuẩn|bằng cấp|chứng chỉ|tiêu chí|kinh nghiệm)");
                // Nới lỏng Regex: Chấp nhận các câu chào ngắn gọn (dưới 30 ký tự)
                var isSmallTalk = request.Message.Length < 35 && Regex.IsMatch(msgLower, @"^(hi|hello|chào|xin chào|ê|alo|bye|tạm biệt|hi there|hey|tư vấn|bạn là ai|giúp|trợ giúp)");

                string systemPrompt = "";

                if (isSmallTalk)
                {
                    Response.ContentType = "text/event-stream";
                    Response.Headers["Cache-Control"] = "no-cache";
                    Response.Headers["Connection"] = "keep-alive";

                    var greetingMsg = "Chào bạn! Tôi là ATS Assistant - Trợ lý tuyển dụng thông minh của Hệ thống Tuyển dụng KCN & Nhà Máy ATS Industrial. Tôi có thể hỗ trợ bạn tra cứu vị trí việc làm, chế độ lương thưởng, phụ cấp chuyên cần, ca kíp nhà máy, hoặc hướng dẫn hồ sơ tiếp nhận và thử việc 30 ngày. Bạn đang quan tâm đến công việc hoặc xưởng nào?";
                    var words = greetingMsg.Split(' ');
                    foreach (var word in words)
                    {
                        var chunkObj = new { text = word + " " };
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunkObj)}\n\n", cts.Token);
                        await Response.Body.FlushAsync(cts.Token);
                        await Task.Delay(25); 
                    }
                    await Response.WriteAsync("data: [DONE]\n\n", cts.Token);
                    await Response.Body.FlushAsync(cts.Token);
                    return; 
                }
                else
                {
                    systemPrompt = $@"Bạn là ATS Assistant - AI tư vấn tuyển dụng & quản trị nhân sự thông minh của Hệ thống Tuyển dụng KCN ATS Industrial. 
Hôm nay là {DateTime.Now:dd/MM/yyyy HH:mm:ss} (Năm {DateTime.Now.Year}).

=== PERSONA & TONE ===
Bạn là chuyên gia tư vấn tuyển dụng nhà máy & khu công nghiệp thân thiện, thực tế, ân cần và chuyên nghiệp:
• Tư vấn rõ ràng, dễ hiểu cho người lao động, công nhân sản xuất và kỹ sư nhà máy
• Thấu hiểu nhu cầu: thu nhập thực tế, tiền ăn ca, phụ cấp chuyên cần, xe đưa đón, ký túc xá, chế độ BHXH
• Hướng dẫn chi tiết thủ tục hồ sơ (CCCD gắn chip, giấy khám sức khỏe KSK đạt chuẩn KCN, sơ yếu lý lịch)
• Giải thích quy trình tiếp nhận: cấp mã nhân viên, mở tài khoản ngân hàng, 30 ngày thử việc và ký hợp đồng chính thức
• Khích lệ và hỗ trợ ứng viên tự tin tham gia phỏng vấn

=== SCOPE & GUARDRAILS ===
CHỈ ĐƯỢC PHÉP tư vấn về:
✓ Các vị trí việc làm nhà máy: Công nhân SMT, Lắp ráp cơ khí, Kỹ thuật bảo trì PLC, Chuyên viên QA/QC, Kho vận, Lái xe nâng...
✓ Chế độ đãi ngộ: Lương cơ bản, phụ cấp ca kíp, tăng ca OT, tiền ăn, nhà ở ký túc xá, thưởng lễ tết
✓ Quy trình tuyển dụng KCN: Tiếp nhận qua QR Form, phỏng vấn, khám sức khỏe, onboard, thử việc 30 ngày
✓ Nội quy an toàn lao động, phòng sạch, đồng phục và bảo hộ lao động

NẾU người dùng hỏi ngoài phạm vi (chính trị, giải trí không liên quan, code ngẫu nhiên...):
→ TỪ CHỐI LỊCH SỰ: 'Xin lỗi, tôi là trợ lý chuyên trách tuyển dụng của ATS Industrial. Bạn có câu hỏi nào về các vị trí việc làm hoặc chế độ tại các nhà máy KCN không?'

=== NGUYÊN TẮC TRẢ LỜI ===
• Trả lời ngắn gọn, thực tế, đi thẳng vào vấn đề bằng tiếng Việt tự nhiên
• Gạch đầu dòng rõ ràng các quyền lợi, yêu cầu và mức lương cụ thể
• Nhắc nhở người lao động chuẩn bị đầy đủ CCCD và giấy khám sức khỏe khi đi phỏng vấn";
                }

                if (isJobQuery)
                {
                    // Lấy Real-time job details (not just titles) để AI có full context
                    var utcNow = DateTime.UtcNow;
                    var activeJobs = await _context.Jobs
                        .Where(j => j.Status == "OPEN" && !j.IsDeleted && (j.Deadline == null || j.Deadline >= utcNow))
                        .OrderByDescending(j => j.CreatedAt)
                        .Take(8)
                        .Select(j => new { j.Title, j.Description, j.Requirements, j.SalaryMin, j.SalaryMax, j.Currency })
                        .ToListAsync(cts.Token);
                    
                    _logger.LogInformation($"[Chatbot] Đã tìm thấy {activeJobs.Count} jobs KCN cho RAG.");
                    
                    var jobsContext = activeJobs.Any()
                        ? "=== CÁC CHIẾN DỊCH VIỆC LÀM ĐANG TUYỂN DỤNG TẠI ATS INDUSTRIAL ===\n" + string.Join("\n---\n", activeJobs.Select((j, idx) =>
                            $"Vị trí #{idx + 1}: {j.Title}\nMô tả: {(j.Description?.Length > 200 ? j.Description.Substring(0, 200) + "..." : j.Description)}\nYêu cầu: {(j.Requirements?.Length > 200 ? j.Requirements.Substring(0, 200) + "..." : j.Requirements)}\nThu nhập: {j.SalaryMin?.ToString("N0") ?? "?"}-{j.SalaryMax?.ToString("N0") ?? "?"} {j.Currency ?? "VNĐ"}/tháng"))
                        : "Hiện tại nhà máy đang mở các đợt tiếp nhận liên tục cho công nhân sản xuất và kỹ thuật viên.";
                    
                    systemPrompt += $"\n{jobsContext}\n\nKhi ứng viên hỏi về công việc:\n• Nêu rõ vị trí phù hợp, mức thu nhập và quyền lợi\n• Hướng dẫn ứng viên đăng ký qua form online hoặc quét mã QR tuyển dụng của nhà máy";
                }
                else if (isSkillQuery)
                {
                    // Cache kỹ năng tận 60 phút vì nó hiếm khi thay đổi
                    if (!_cache.TryGetValue("Chatbot_TopSkills", out List<string>? skills) || skills == null)
                    {
                        // Lấy top các kỹ năng chưa bị khóa
                         skills = await _context.Skills
                            .Where(s => !s.IsDeleted)
                            .Take(15)
                            .Select(s => s.Name)
                            .ToListAsync(cts.Token);

                        var cacheEntryOptions = new MemoryCacheEntryOptions()
                             .SetAbsoluteExpiration(TimeSpan.FromMinutes(60));
                         _cache.Set("Chatbot_TopSkills", skills, cacheEntryOptions);
                    }
                    
                    var skillsStr = skills.Any() ? string.Join(", ", skills) : "Chưa cập nhật kỹ năng.";
                    systemPrompt += $"\nDanh sách Công nghệ/Kỹ năng công ty hay dùng:\n{skillsStr}";
                }

                // 2. Chuẩn bị Payload chuẩn Google (Dùng system_instruction để tách rào chắn ra token riêng)
                var contentsList = new List<object>();
                
                // Thêm lịch sử (2 câu gần nhất - đủ để AI hiểu ngữ cảnh mà không gây nặng Quota)
                if (request.History != null && request.History.Any())
                {
                    foreach (var h in request.History.TakeLast(2))
                    {
                        if (string.IsNullOrWhiteSpace(h.Content)) continue;
                        contentsList.Add(new 
                        { 
                            role = h.Role.ToLower() == "bot" ? "model" : "user", 
                            parts = new[] { new { text = h.Content } } 
                        });
                    }
                }

                // Tin nhắn hiện tại (SẠCH HOÀN TOÀN - không có system prompt nhồi vào)
                contentsList.Add(new 
                { 
                    role = "user", 
                    parts = new[] { new { text = request.Message } } 
                });

                // system_instruction: Tách rào chắn thành token riêng (Cách Google khuyến nghị nhất)
                // Cần dùng Dictionary vì C# không cho phép gach_dưới trong anonymous type identifier
                var payloadObj = new Dictionary<string, object>
                {
                    ["system_instruction"] = new { parts = new[] { new { text = systemPrompt } } },
                    ["contents"] = contentsList.ToArray()
                };
                var payloadJson = JsonSerializer.Serialize(payloadObj);

                // 3. Khởi tạo SSE HTTP Headers trước khi gọi Google
                Response.ContentType = "text/event-stream";
                Response.Headers["Cache-Control"] = "no-cache";
                Response.Headers["Connection"] = "keep-alive";
                await Response.Body.FlushAsync(cts.Token);

                // Gửi sessionId về client ngay lập tức để frontend lưu lại (nếu client cần)
                try
                {
                    var sessObj = new { sessionId = sessionId };
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(sessObj)}\n\n", cts.Token);
                    await Response.Body.FlushAsync(cts.Token);
                }
                catch { }

                // 4. Hàm thực thi HTTP Call hỗ trợ Streaming với retry + per-key reporting
                async Task<HttpResponseMessage> CallGeminiWithRetry(string baseModelUrl, string json, string moduleName)
                {
                    var maxAttempts = 3;
                    var attempt = 0;
                    var delayMs = 1000;

                    while (attempt < maxAttempts && !cts.IsCancellationRequested)
                    {
                        attempt++;
                        string key;
                        try
                        {
                            key = _apiKeyProvider.GetApiKey(moduleName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi khi lấy Gemini API key");
                            throw;
                        }

                        var urlWithKey = DATN.Services.Implements.GeminiApiKeyProvider.BuildGenerateContentUrl(key, "gemini-2.5-flash", isStream: true);
                        try
                        {
                            var req = new HttpRequestMessage(HttpMethod.Post, urlWithKey)
                            {
                                Content = new StringContent(json, Encoding.UTF8, "application/json")
                            };

                            _logger.LogInformation("[Gemini] Gọi API (attempt {Attempt}) với key {KeyPreview}", attempt, TruncateKeyForLogs(key));
                            var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                            if (resp.IsSuccessStatusCode)
                            {
                                _apiKeyProvider.ReportSuccess(key);
                                return resp;
                            }

                            // Report failure for 429/5xx
                            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                            {
                                _apiKeyProvider.ReportFailure(key);
                                _logger.LogWarning("[Gemini] Non-success status {Status} on attempt {Attempt} (key preview: {KeyPreview})", resp.StatusCode, attempt, TruncateKeyForLogs(key));
                                resp.Dispose();

                                // jittered backoff
                                var jitter = new Random().Next(0, 300);
                                await Task.Delay(delayMs + jitter, cts.Token);
                                delayMs *= 2;
                                continue;
                            }

                            // Other non-success (4xx) - no retry
                            return resp;
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _apiKeyProvider.ReportFailure(key);
                            _logger.LogWarning(ex, "Lỗi khi gọi Gemini trên attempt {Attempt}", attempt);
                            var jitter = new Random().Next(0, 300);
                            await Task.Delay(delayMs + jitter, cts.Token);
                            delayMs *= 2;
                            continue;
                        }
                    }

                    throw new HttpRequestException("Failed to call Gemini after multiple attempts");
                }

                var baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse";
                _logger.LogInformation($"[Gemini AI] Gọi API... Tokens ước tính: {payloadJson.Length} chars.");
                HttpResponseMessage response;
                try
                {
                    response = await CallGeminiWithRetry(baseUrl, payloadJson, "Chatbot");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gọi Gemini thất bại sau retry, kích hoạt bộ phản hồi thông minh KCN dự phòng");
                    var smartAnswer = GenerateSmartFallbackAnswer(request.Message);
                    await WriteStreamEvent(Response, smartAnswer);
                    await SaveAssistantMessageAsync(smartAnswer, sessionId, cts.Token);
                    return;
                }
                // 5. Đọc Stream liên tục và đẩy về luồng Console/Angular
                using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var reader = new StreamReader(responseStream);

                var assistantSb = new StringBuilder();

                while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                    var jsonPayload = line.Substring("data: ".Length).Trim();
                    if (jsonPayload == "[DONE]") break; // Xong phiên chat

                    try 
                    {
                        using var document = JsonDocument.Parse(jsonPayload);
                        var candidates = document.RootElement.GetProperty("candidates");
                        if (candidates.GetArrayLength() > 0)
                        {
                            var contentProp = candidates[0].GetProperty("content");
                            if (contentProp.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                if (parts[0].TryGetProperty("text", out var textElement))
                                {
                                    var textChunk = textElement.GetString();
                                    if (!string.IsNullOrEmpty(textChunk))
                                    {
                                        assistantSb.Append(textChunk);
                                        var chunkObj = new { text = textChunk };
                                        var chunkJson = JsonSerializer.Serialize(chunkObj);
                                        await Response.WriteAsync($"data: {chunkJson}\n\n", cts.Token);
                                        await Response.Body.FlushAsync(cts.Token);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Bỏ qua chunk JSON dị dạng từ Gemini: {ex.Message}");
                    }
                }
                
                // Kết thúc Stream bình thường, gửi cờ DONE để nhả Frontend Loader
                await Response.WriteAsync("data: [DONE]\n\n", cts.Token);
                await Response.Body.FlushAsync(cts.Token);

                // Save assistant full message to DB (non-blocking safety)
                try
                {
                    var assistantText = assistantSb.ToString();
                    if (!string.IsNullOrWhiteSpace(assistantText))
                    {
                        _taskQueue.QueueBackgroundWorkItem(ct => SaveAssistantMessageAsync(assistantText, sessionId, ct));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving assistant message");
                }
            }
            // Khối Fallback 
            catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException)
            {
                _logger.LogWarning(ex, "Gemini API Streaming Timeout/Error.");
                await WriteStreamEvent(Response, "Hệ thống đang quá tải hoặc kết nối bị gián đoạn. Bạn vui lòng thử lại sau nhé!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Server khi xử lý Chatbot Streaming.");
                await WriteStreamEvent(Response, "Có lỗi nội bộ xảy ra.");
            }
        }

        // Hàm tiện ích hỗ trợ nhả Text Event Stream một lần nếu bắt lỗi sớm
        private async Task WriteStreamEvent(HttpResponse response, string message)
        {
            if (!response.HasStarted)
            {
                response.ContentType = "text/event-stream";
                response.Headers["Cache-Control"] = "no-cache";
                response.Headers["Connection"] = "keep-alive";
            }
            var chunkObj = new { text = message };
            await response.WriteAsync($"data: {JsonSerializer.Serialize(chunkObj)}\n\n");
            await response.WriteAsync("data: [DONE]\n\n");
            await response.Body.FlushAsync();
        }

        private async Task<string?> TryGetFaqAnswerAsync(string message, CancellationToken cancellationToken)
        {
            var normalized = message.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var candidates = await _context.ChatbotFaqs
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Question)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var faq in candidates)
            {
                var question = faq.Question?.Trim().ToLower() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(question)
                    && (normalized.Contains(question) || question.Contains(normalized)))
                {
                    return faq.Answer;
                }

                if (string.IsNullOrWhiteSpace(faq.Keywords))
                {
                    continue;
                }

                var keywords = faq.Keywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLower())
                    .Where(x => x.Length >= 2);

                if (keywords.Any(k => normalized.Contains(k)))
                {
                    return faq.Answer;
                }
            }

            return null;
        }

        private static string NormalizeKeywords(string? rawKeywords)
        {
            if (string.IsNullOrWhiteSpace(rawKeywords))
            {
                return string.Empty;
            }

            var normalized = rawKeywords
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join(",", normalized);
        }

        private Guid? GetCurrentUserIdOrNull()
        {
            var claim = User?.Claims?.FirstOrDefault(c => c.Type == "sub"
                || c.Type == ClaimTypes.NameIdentifier
                || c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");

            if (claim == null)
            {
                return null;
            }

            return Guid.TryParse(claim.Value, out var userId) ? userId : null;
        }

        private static string TruncateKeyForLogs(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(empty)";
            if (key.Length <= 8) return key;
            return key.Substring(0, 4) + "..." + key.Substring(key.Length - 4);
        }

        /// <summary>
        /// Helper: Lưu user message vào database
        /// </summary>
        private async Task<ChatMessage?> SaveUserMessageAsync(string message, Guid sessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                // If session row doesn't exist in DB (requires ApplicationId FK),
                // do not attempt to create a placeholder ChatSession here.
                // Instead leave ChatSessionId null so FK is not violated.
                var existingSession = await _context.ChatSessions.FindAsync(new object[] { sessionId }, cancellationToken);

                var chatMessage = new ChatMessage
                {
                    ChatMessageId = Guid.NewGuid(),
                    ChatSessionId = existingSession != null ? sessionId : (Guid?)null,
                    Sender = "user",
                    Message = message,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync(cancellationToken);

                // Phân loại intent non-blocking: enqueue cho background worker
                _taskQueue.QueueBackgroundWorkItem(ct => ClassifyAndSaveAnalyticsAsync(chatMessage, ct));

                return chatMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user message");
                return null;
            }
        }

        /// <summary>
        /// Helper: Phân loại intent và lưu analytics
        /// </summary>
        private async Task ClassifyAndSaveAnalyticsAsync(ChatMessage message, CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DATNContext>();
                var intentClassifier = scope.ServiceProvider.GetRequiredService<IntentClassifier>();
                var entityExtractor = scope.ServiceProvider.GetRequiredService<EntityExtractor>();

                var sw = Stopwatch.StartNew();

                var intentResult = await intentClassifier.ClassifyAsync(message.Message);
                var entities = await entityExtractor.ExtractAsync(message.Message);

                sw.Stop();

                var analytics = new ChatAnalytics
                {
                    AnalyticsId = Guid.NewGuid(),
                    MessageId = message.ChatMessageId,
                    Intent = intentResult.Intent,
                    IntentConfidence = (decimal)intentResult.Confidence,
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                    EntityCount = entities.Count,
                    EntityConfidence = entities.Count > 0 ? (decimal)entities.Average(e => e.Confidence) : null,
                    CreatedAt = DateTime.UtcNow,
                    WasEscalated = false
                };

                db.ChatAnalytics.Add(analytics);
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogDebug($"[Analytics] Saved for message {message.ChatMessageId}: Intent={intentResult.Intent} ({intentResult.Confidence:P})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error classifying and saving analytics");
            }
        }

        /// <summary>
        /// Helper: Lưu assistant response message
        /// </summary>
        private async Task<ChatMessage?> SaveAssistantMessageAsync(string message, Guid sessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DATNContext>();

                var existingSession = await db.ChatSessions.FindAsync(new object[] { sessionId }, cancellationToken);

                var chatMessage = new ChatMessage
                {
                    ChatMessageId = Guid.NewGuid(),
                    ChatSessionId = existingSession != null ? sessionId : (Guid?)null,
                    Sender = "assistant",
                    Message = message,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.ChatMessages.Add(chatMessage);
                await db.SaveChangesAsync(cancellationToken);

                return chatMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving assistant message");
                return null;
            }
        }

        private string GenerateSmartFallbackAnswer(string userMessage)
        {
            var msg = (userMessage ?? string.Empty).ToLower();

            if (msg.Contains("lương") || msg.Contains("thu nhập") || msg.Contains("thưởng") || msg.Contains("phụ cấp") || msg.Contains("tiền"))
            {
                return "Chế độ thu nhập tại các nhà máy KCN thuộc ATS Industrial được đảm bảo rất cạnh tranh:\n" +
                       "• Thu nhập bình quân: 8.500.000 – 14.000.000 VNĐ/tháng (Công nhân) và 14.000.000 – 25.000.000 VNĐ/tháng (Kỹ sư/Kỹ thuật viên).\n" +
                       "• Phụ cấp chuyên cần: 500.000 – 800.000 VNĐ/tháng.\n" +
                       "• Hỗ trợ ăn ca miễn phí tại nhà ăn công ty đảm bảo VSATTP.\n" +
                       "• Tiền tăng ca (OT): 150% ngày thường, 200% ngày nghỉ và 300% ngày lễ tết theo Luật Lao Động.\n" +
                       "• Lương tháng 13, thưởng sản lượng và quà các dịp lễ tết đầy đủ.";
            }

            if (msg.Contains("hồ sơ") || msg.Contains("giấy tờ") || msg.Contains("khám sức khỏe") || msg.Contains("ksk") || msg.Contains("cccd"))
            {
                return "Để tham gia phỏng vấn và hoàn tất thủ tục tiếp nhận, bạn cần chuẩn bị:\n" +
                       "1. Căn cước công dân (CCCD) gắn chip hợp lệ.\n" +
                       "2. Giấy khám sức khỏe (KSK) khổ A3 có dán ảnh, còn hạn trong vòng 06 tháng.\n" +
                       "3. Sơ yếu lý lịch tự thuật có xác nhận của địa phương.\n" +
                       "4. Bằng cấp/Chứng chỉ nghề (nếu ứng tuyển vị trí kỹ thuật, bảo trì, QA/QC).\n" +
                       "5. Ảnh thẻ 3x4 hoặc 4x6 (2-4 ảnh) làm thẻ nhân viên.";
            }

            if (msg.Contains("thử việc") || msg.Contains("tiếp nhận") || msg.Contains("onboard") || msg.Contains("hợp đồng") || msg.Contains("mã nv") || msg.Contains("mã nhân viên"))
            {
                return "Quy trình tiếp nhận và thử việc tại nhà máy gồm:\n" +
                       "1. Cấp mã nhân viên & mở tài khoản thẻ ngân hàng nhận lương ngay khi đạt điều kiện KSK.\n" +
                       "2. Đào tạo hội nhập, an toàn lao động và hướng dẫn thực hành tại xưởng sản xuất.\n" +
                       "3. Thời gian thử việc: 30 ngày hưởng lương thử việc theo quy định.\n" +
                       "4. Đánh giá thử việc đạt: Ký hợp đồng lao động chính thức dài hạn, hưởng 100% lương và đóng đầy đủ BHXH, BHYT, BHTN.";
            }

            if (msg.Contains("ca") || msg.Contains("giờ làm") || msg.Contains("xe") || msg.Contains("đưa đón") || msg.Contains("bus"))
            {
                return "Thời gian làm việc và phương tiện di chuyển:\n" +
                       "• Ca hành chính: 08:00 – 17:00 (Nghỉ trưa 60 phút).\n" +
                       "• Ca sản xuất 12h (Xoay ca tuần hoặc 2 tuần/lần): Ca ngày (07:30 – 19:30), Ca đêm (19:30 – 07:30).\n" +
                       "• Xe đưa đón: Công ty bố trí hệ thống xe bus đưa đón miễn phí theo nhiều tuyến đường chính từ trung tâm các huyện, tỉnh lân cận về thẳng nhà máy.";
            }

            if (msg.Contains("ký túc xá") || msg.Contains("ktx") || msg.Contains("ở xa") || msg.Contains("nhà trọ") || msg.Contains("chỗ ở"))
            {
                return "Nhà máy có khu Ký túc xá miễn phí dành cho công nhân viên ở xa:\n" +
                       "• Phòng ở sạch sẽ, trang bị điều hòa nhiệt độ, bình nóng lạnh, quạt mát và wifi tốc độ cao.\n" +
                       "• Có bảo vệ an ninh 24/7 và khu vực thể thao, căng tin tiện lợi.";
            }

            if (msg.Contains("vị trí") || msg.Contains("việc làm") || msg.Contains("tuyển") || msg.Contains("công việc") || msg.Contains("smt") || msg.Contains("bảo trì") || msg.Contains("chân chạy"))
            {
                return "Các vị trí việc làm KCN đang tuyển dụng liên tục:\n" +
                       "• Công nhân vận hành dây chuyền SMT & Lắp ráp linh kiện (Số lượng lớn, nhận cả người chưa có kinh nghiệm).\n" +
                       "• Kỹ sư Tự động hóa PLC & Kỹ thuật bảo trì máy móc dây chuyền.\n" +
                       "• Chuyên viên Quản lý chất lượng IQC / OQC / QA (ISO, 5S).\n" +
                       "• Nhân viên kho vận, thủ kho và lái xe nâng Reach Truck.\n" +
                       "Bạn có thể quét mã QR chiến dịch hoặc nộp hồ sơ trực tuyến ngay trên hệ thống ATS Industrial!";
            }

            return "Chào bạn, tôi là ATS Assistant - Trợ lý Tuyển dụng KCN ATS Industrial. Bạn có thể hỏi tôi về các vị trí việc làm đang tuyển dụng, mức lương thưởng, phụ cấp chuyên cần, thủ tục hồ sơ (CCCD/KSK), xe đưa đón hoặc quy trình thử việc 30 ngày tại nhà máy!";
        }
    }

    public class UpsertChatbotFaqDto
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string? Keywords { get; set; }
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;
    }

    public class ChatbotFaqAdminDto
    {
        public Guid FaqId { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Keywords { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
