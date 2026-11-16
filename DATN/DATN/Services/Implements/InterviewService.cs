using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DATN.Data;
using DATN.DTOs.Interview;
using DATN.DTOs;
using DATN.Entities;
using DATN.Services.Interfaces;

namespace DATN.Services.Implements;

public class InterviewService : IInterviewService
{
    private readonly DATNContext _context;
    private readonly ILogger<InterviewService> _logger;
    private readonly INotificationService _notificationService;
    private readonly IServiceScopeFactory _scopeFactory;

    public InterviewService(
        DATNContext context, 
        ILogger<InterviewService> logger,
        INotificationService notificationService,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _logger = logger;
        _notificationService = notificationService;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Lên lịch phỏng vấn cho một Application
    /// </summary>
    public async Task<Guid> ScheduleInterviewAsync(ScheduleInterviewDto dto, Guid createdBy)
    {
        try
        {
            _logger.LogInformation("📅 Scheduling interview for ApplicationId: {ApplicationId}, InterviewerId: {InterviewerId}", 
                dto.ApplicationId, dto.InterviewerId);

            // 1. Validate Application tồn tại và lấy thông tin Candidate
            var application = await _context.Applications
                .Include(a => a.Candidate)
                .Include(a => a.Job)
                .FirstOrDefaultAsync(a => a.ApplicationId == dto.ApplicationId);

            if (application == null)
            {
                throw new InvalidOperationException($"Không tìm thấy Application với ID: {dto.ApplicationId}");
            }

            // 2. Validate InterviewerId tồn tại và có role INTERVIEWER
            var interviewer = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == dto.InterviewerId);

            if (interviewer == null)
            {
                _logger.LogWarning("⚠️ Không tìm thấy Interviewer với ID: {InterviewerId}. Hệ thống tự động fallback tìm nhân sự hợp lệ...", dto.InterviewerId);
                interviewer = await _context.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.UserRoles.Any(ur => ur.Role.Code == "INTERVIEWER" || ur.Role.Code == "HR" || ur.Role.Code == "ADMIN"));

                if (interviewer == null)
                {
                    interviewer = await _context.Users.FirstOrDefaultAsync(u => u.IsActive);
                }

                if (interviewer != null)
                {
                    dto.InterviewerId = interviewer.UserId;
                    _logger.LogInformation("✅ Đã tự động gán Interviewer thay thế: {FullName} ({UserId})", interviewer.FullName, interviewer.UserId);
                }
                else
                {
                    throw new InvalidOperationException($"Không tìm thấy Người phỏng vấn khả dụng trong hệ thống.");
                }
            }

            var hasInterviewerRole = interviewer.UserRoles == null || interviewer.UserRoles.Count == 0 || interviewer.UserRoles
                .Any(ur => ur.Role.Code == "INTERVIEWER" || ur.Role.Code == "HR" || ur.Role.Code == "ADMIN" || ur.Role.Code == "HR_TEAM");

            if (!hasInterviewerRole)
            {
                _logger.LogWarning("User '{FullName}' không có role INTERVIEWER/HR/ADMIN. Tự động tiếp tục phục vụ lịch phỏng vấn.", interviewer.FullName);
            }

            // 3. Lấy thông tin HR (người tạo lịch)
            var hrUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == createdBy);

            // 4. Validate thời gian phỏng vấn, ngày nghỉ lễ, cuối tuần, và giờ hành chính
            if (dto.ScheduledEnd <= dto.ScheduledStart)
            {
                throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu");
            }

            if (dto.ScheduledStart < DateTime.UtcNow.AddMinutes(-15))
            {
                throw new ArgumentException("Thời gian phỏng vấn không được chọn trong quá khứ");
            }

            // Giờ địa phương Việt Nam (UTC+7)
            var vnStart = dto.ScheduledStart.Kind == DateTimeKind.Utc ? dto.ScheduledStart.AddHours(7) : dto.ScheduledStart;
            var vnEnd = dto.ScheduledEnd.Kind == DateTimeKind.Utc ? dto.ScheduledEnd.AddHours(7) : dto.ScheduledEnd;

            // Kiểm tra ngày cuối tuần (Thứ 7, Chủ Nhật)
            if (vnStart.DayOfWeek == DayOfWeek.Saturday || vnStart.DayOfWeek == DayOfWeek.Sunday)
            {
                var dayName = vnStart.DayOfWeek == DayOfWeek.Sunday ? "Chủ nhật" : "Thứ 7";
                throw new ArgumentException($"Ngày {vnStart:dd/MM/yyyy} là {dayName} (ngày nghỉ cuối tuần), không thể xếp lịch phỏng vấn. Vui lòng chọn ngày làm việc trong tuần (Thứ 2 - Thứ 6).");
            }

            // Kiểm tra ngày nghỉ lễ Việt Nam
            var vnDayMonth = vnStart.ToString("dd-MM");
            var holidayName = GetVietnamHolidayName(vnStart.Year, vnDayMonth);
            if (!string.IsNullOrEmpty(holidayName))
            {
                throw new ArgumentException($"Ngày {vnStart:dd/MM/yyyy} là ngày nghỉ lễ ({holidayName}), không thể xếp lịch phỏng vấn. Vui lòng chọn ngày làm việc khác.");
            }

            // Kiểm tra giờ hành chính (08:00 - 18:00)
            if (vnStart.Hour < 8 || (vnStart.Hour == 18 && vnStart.Minute > 0) || vnStart.Hour > 18 || vnEnd.Hour > 18 || (vnEnd.Hour == 18 && vnEnd.Minute > 30))
            {
                throw new ArgumentException($"Thời gian phỏng vấn ({vnStart:HH:mm} - {vnEnd:HH:mm}) phải nằm trong khung giờ hành chính Việt Nam (08:00 - 18:00).");
            }

            // 5. Tạo bản ghi Interview
            var interview = new Interview
            {
                InterviewId = Guid.NewGuid(),
                ApplicationId = dto.ApplicationId,
                InterviewerId = dto.InterviewerId,
                Title = dto.Title,
                ScheduledStart = dto.ScheduledStart,
                ScheduledEnd = dto.ScheduledEnd,
                MeetingLink = dto.MeetingLink,
                Location = dto.Location,
                Status = "SCHEDULED",
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow
            };

            _context.Interviews.Add(interview);

            // 6. CẬP NHẬT STATUS APPLICATION → INTERVIEW
            application.Status = "INTERVIEW";
            application.LastStageChangedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ Interview scheduled successfully with ID: {InterviewId}. Application status updated to INTERVIEW.", interview.InterviewId);

            // 7. Gửi email thông báo với CC
            try
            {
                // Ưu tiên: CandidateEmail từ DTO → ContactEmail trên Application → Candidate.Email
                var candidateEmail = !string.IsNullOrWhiteSpace(dto.CandidateEmail)
                    ? dto.CandidateEmail.Trim()
                    : (!string.IsNullOrWhiteSpace(application.ContactEmail)
                        ? application.ContactEmail.Trim()
                        : application.Candidate?.Email?.Trim());

                // Nếu ContactEmail chưa có trong Application nhưng có candidateEmail, cập nhật ngay
                if (string.IsNullOrWhiteSpace(application.ContactEmail) && !string.IsNullOrWhiteSpace(candidateEmail))
                {
                    application.ContactEmail = candidateEmail;
                    await _context.SaveChangesAsync();
                }

                var candidateName = !string.IsNullOrWhiteSpace(application.ContactName)
                    ? application.ContactName
                    : application.Candidate?.FullName ?? "Ứng viên";
                var jobTitle = application.Job?.Title ?? "Vị trí tuyển dụng";

                // Tạo HTML template chuyên nghiệp hoặc sử dụng nội dung email tùy chỉnh từ HR
                var location = dto.MeetingLink ?? dto.Location ?? "Sẽ cung cấp sau";
                string emailBody;
                if (!string.IsNullOrWhiteSpace(dto.CustomEmailBody))
                {
                    // Nếu HR đã preview và chỉnh sửa, định dạng sang HTML chuẩn
                    var formattedCustom = dto.CustomEmailBody.Contains("<") && dto.CustomEmailBody.Contains(">")
                        ? dto.CustomEmailBody
                        : dto.CustomEmailBody.Replace("\r\n", "<br>").Replace("\n", "<br>");
                    
                    emailBody = $@"
<!DOCTYPE html>
<html>
<head><meta charset=""UTF-8""><style>body{{font-family:'Segoe UI',Tahoma,Arial,sans-serif;line-height:1.6;color:#333;}}.box{{max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px;background:#ffffff;}}</style></head>
<body><div class=""box"">{formattedCustom}</div></body>
</html>";
                }
                else
                {
                    emailBody = GenerateInterviewInvitationHtml(
                        candidateName,
                        jobTitle,
                        dto.ScheduledStart,
                        location,
                        dto.MeetingLink != null
                    );
                }

                var subject = $"Thư mời phỏng vấn - {candidateName} - V9 TECH";

                // Danh sách CC: Interviewer và HR
                var ccEmails = new List<string>();
                if (!string.IsNullOrEmpty(interviewer?.Email))
                {
                    ccEmails.Add(interviewer.Email);
                }
                if (hrUser != null && !string.IsNullOrEmpty(hrUser.Email))
                {
                    ccEmails.Add(hrUser.Email);
                }

                // Lọc sạch CC: loại trừ null/trắng, không trùng với ứng viên nhận mail, không trùng lặp
                var distinctCc = ccEmails
                    .Where(e => !string.IsNullOrWhiteSpace(e) && !e.Equals(candidateEmail, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(candidateEmail))
                {
                    QueueInterviewInvitationEmail(candidateEmail, distinctCc, subject, emailBody);
                    _logger.LogInformation("📨 Đã đưa email mời phỏng vấn vào hàng đợi gửi nền cho {CandidateEmail} (CC: {CcCount})",
                        candidateEmail, distinctCc.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ Không có email ứng viên cho ApplicationId: {ApplicationId}, bỏ qua gửi email", dto.ApplicationId);
                }
            }
            catch (Exception emailEx)
            {
                _logger.LogWarning(emailEx, "⚠️ Đã lên lịch phỏng vấn nhưng gửi email thất bại");
                // Không throw exception vì interview đã được tạo thành công
            }

            // 8. TẠO THÔNG BÁO CHO ỨNG VIÊN (NEW)
            if (application.Candidate != null && application.Candidate.UserId.HasValue)
            {
                try 
                {
                    await _notificationService.CreateNotificationAsync(
                        application.Candidate.UserId.Value,
                        "Lịch phỏng vấn mới",
                        $"Bạn có lịch phỏng vấn vị trí {application.Job?.Title ?? "N/A"} vào lúc {dto.ScheduledStart:HH:mm dd/MM/yyyy}",
                        "INTERVIEW",
                        application.ApplicationId.ToString()
                    );
                    _logger.LogInformation("🔔 Đã tạo thông báo cho Candidate UserId: {UserId}", application.Candidate.UserId);
                }
                catch (Exception notifEx)
                {
                    _logger.LogError(notifEx, " Lỗi khi tạo thông báo cho User {UserId}", application.Candidate.UserId);
                }
            }

            // 9. TẠO THÔNG BÁO CHO INTERVIEWER (NEW - USER REQUEST)
            try
            {
                await _notificationService.CreateNotificationAsync(
                    dto.InterviewerId,
                    "Lịch phỏng vấn mới được phân công",
                    $"Bạn được phân công phỏng vấn ứng viên {(!string.IsNullOrWhiteSpace(application.ContactName) ? application.ContactName : application.Candidate?.FullName ?? "N/A")} cho vị trí {application.Job?.Title ?? "N/A"} vào lúc {dto.ScheduledStart:HH:mm dd/MM/yyyy}",
                    "INTERVIEW_ASSIGNED",
                    interview.InterviewId.ToString()
                );
                _logger.LogInformation("🔔 Đã tạo thông báo cho Interviewer UserId: {UserId}", dto.InterviewerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Lỗi khi tạo thông báo cho Interviewer {UserId}", dto.InterviewerId);
            }

            return interview.InterviewId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Error scheduling interview for ApplicationId: {ApplicationId}", dto.ApplicationId);
            throw;
        }
    }

    private void QueueInterviewInvitationEmail(string toEmail, List<string> ccEmails, string subject, string htmlBody)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedEmailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await scopedEmailService.SendEmailWithCcAsync(toEmail, ccEmails, subject, htmlBody);

                _logger.LogInformation("📧 Đã gửi email mời phỏng vấn nền đến {ToEmail}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Gửi email mời phỏng vấn nền thất bại cho {ToEmail}", toEmail);
            }
        });
    }

    /// <summary>
    /// Tạo HTML template chuyên nghiệp cho email mời phỏng vấn
    /// </summary>
    private string GenerateInterviewInvitationHtml(
        string candidateName, 
        string jobTitle, 
        DateTime scheduledTime, 
        string location, 
        bool isOnline)
    {
        var formattedDate = scheduledTime.ToString("dddd, dd MMMM yyyy", new System.Globalization.CultureInfo("vi-VN"));
        var formattedTime = scheduledTime.ToString("HH:mm");
        var locationType = isOnline ? "Link Meeting" : "Địa điểm";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9f9f9; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .info-box {{ background-color: #f0f4ff; border-left: 4px solid #667eea; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .button {{ display: inline-block; padding: 12px 30px; background-color: #667eea; color: white; text-decoration: none; border-radius: 5px; margin-top: 20px; }}
        .footer {{ text-align: center; margin-top: 30px; color: #888; font-size: 12px; }}
        h1 {{ margin: 0; font-size: 28px; }}
        h2 {{ color: #667eea; margin-top: 0; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🎯 V9 TECH</h1>
            <p style=""margin: 10px 0 0 0; font-size: 16px;"">Thư mời phỏng vấn</p>
        </div>
        <div class=""content"">
            <h2>Kính gửi {candidateName},</h2>
            <p>Chúng tôi rất vui mừng thông báo rằng hồ sơ của bạn đã được đánh giá cao cho vị trí <strong>{jobTitle}</strong> tại <strong>V9 TECH</strong>.</p>
            <p>Chúng tôi muốn mời bạn tham gia buổi phỏng vấn với các thông tin sau:</p>
            
            <div class=""info-box"">
                <p style=""margin: 5px 0;""><strong>📅 Thời gian:</strong> {formattedDate} lúc {formattedTime}</p>
                <p style=""margin: 5px 0;""><strong>{(isOnline ? "💻" : "📍")} {locationType}:</strong> {location}</p>
                <p style=""margin: 5px 0;""><strong>👔 Vị trí ứng tuyển:</strong> {jobTitle}</p>
            </div>

            <p><strong>Lưu ý quan trọng:</strong></p>
            <ul>
                <li>Vui lòng xác nhận tham gia bằng cách trả lời email này</li>
                <li>{(isOnline ? "Vui lòng kiểm tra kết nối internet và thiết bị trước buổi phỏng vấn" : "Vui lòng đến đúng giờ và mang theo CV bản cứng")}</li>
                <li>Chuẩn bị các câu hỏi bạn muốn tìm hiểu về công ty và vị trí ứng tuyển</li>
            </ul>

            <p>Nếu bạn có bất kỳ thắc mắc nào hoặc cần thay đổi lịch phỏng vấn, vui lòng liên hệ với chúng tôi ngay.</p>
            
            <p style=""margin-top: 30px;"">Chúng tôi rất mong được gặp bạn!</p>
            
            <div class=""footer"">
                <p>Trân trọng,<br><strong>V9 TECH Recruitment Team</strong></p>
                <p style=""margin-top: 15px; font-size: 11px; color: #999;"">
                    Email này được gửi tự động từ hệ thống tuyển dụng V9 TECH.<br>
                    Vui lòng không trả lời trực tiếp email tự động này.
                </p>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Submit kết quả đánh giá phỏng vấn
    /// Tự động cập nhật trạng thái Application dựa trên Result
    /// </summary>
    public async Task<Guid> SubmitEvaluationAsync(EvaluationDto dto)
    {
        try
        {
            _logger.LogInformation("📝 Submitting evaluation for InterviewId: {InterviewId}", dto.InterviewId);

            // Validate Interview exists
            var interview = await _context.Interviews
                .Include(i => i.Application)
                .FirstOrDefaultAsync(i => i.InterviewId == dto.InterviewId);

            if (interview == null)
            {
                throw new InvalidOperationException($"Không tìm thấy Interview với ID: {dto.InterviewId}");
            }

            // Validate Result
            var validResults = new[] { "Passed", "Failed", "Consider" };
            if (!validResults.Contains(dto.Result))
            {
                throw new ArgumentException($"Result phải là một trong các giá trị: {string.Join(", ", validResults)}");
            }

            // Tạo InterviewEvaluation
            var evaluation = new InterviewEvaluation
            {
                EvaluationId = Guid.NewGuid(),
                InterviewId = dto.InterviewId,
                InterviewerId = dto.InterviewerId,
                Score = dto.Score,
                Comment = dto.Comment,
                Result = dto.Result,
                Details = dto.Details,
                SubmittedById = dto.SubmittedById,
                CreatedAt = DateTime.UtcNow
            };

            _context.InterviewEvaluations.Add(evaluation);

            // ⚡ CRITICAL: Update Interview Status to 'COMPLETED' (Uppercase standard)
            interview.Status = "COMPLETED";
            _logger.LogInformation("✅ Updated Interview {InterviewId} status to 'COMPLETED'", interview.InterviewId);

            // Tự động cập nhật trạng thái Application
            var application = interview.Application;
            if (application != null)
            {
                string newStatus = dto.Result switch
                {
                    "Passed" => "PASSED",
                    "Failed" => "Rejected",
                    "Consider" => "Waitlisted",
                    _ => application.Status
                };

                application.InterviewScore = dto.Score;

                if (application.Status != newStatus)
                {
                    var oldStatus = application.Status;
                    application.Status = newStatus;
                    application.LastStageChangedAt = DateTime.UtcNow;

                    var targetStageCode = dto.Result == "Passed" ? "PASSED" : dto.Result == "Failed" ? "REJECTED" : null;
                    if (targetStageCode != null)
                    {
                        var targetStage = await _context.PipelineStages.FirstOrDefaultAsync(s => s.Code == targetStageCode);
                        if (targetStage != null)
                        {
                            application.CurrentStageId = targetStage.StageId;
                        }
                    }

                    if (dto.Result == "Passed" && application.ApplicationSource == "PDF_UPLOAD" &&
                        (string.IsNullOrEmpty(application.OfferStatus) || application.OfferStatus == "Not_Required"))
                    {
                        application.OfferStatus = "Offer_Pending";
                    }

                    _logger.LogInformation("✅ Updated Application {AppId}: {OldStatus} → {NewStatus}", 
                        application.ApplicationId, oldStatus, newStatus);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ Evaluation submitted successfully with ID: {EvaluationId}", evaluation.EvaluationId);
            return evaluation.EvaluationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Error submitting evaluation for InterviewId: {InterviewId}", dto.InterviewId);
            throw;
        }
    }

    /// <summary>
    /// Lấy chi tiết đánh giá theo InterviewId
    /// </summary>
    public async Task<EvaluationDto?> GetEvaluationByInterviewIdAsync(Guid interviewId)
    {
        var evaluation = await _context.InterviewEvaluations
            .Include(e => e.Interview)
            .Include(e => e.SubmittedByNavigation)
            .FirstOrDefaultAsync(e => e.InterviewId == interviewId);

        if (evaluation == null)
            return null;

        return new EvaluationDto
        {
            InterviewId = evaluation.InterviewId,
            InterviewerId = evaluation.InterviewerId,
            Score = evaluation.Score,
            Comment = evaluation.Comment,
            Result = evaluation.Result,
            Details = evaluation.Details, // ✅ Include Details for frontend
            SubmittedById = evaluation.SubmittedById,
            SubmittedByName = evaluation.SubmittedByNavigation?.FullName,
            IsBelated = evaluation.CreatedAt > evaluation.Interview.ScheduledEnd
        };
    }

    /// <summary>
    /// Lấy danh sách lịch phỏng vấn của người phỏng vấn (Tối ưu truy vấn cao tốc AsNoTracking & Projection)
    /// </summary>
    public async Task<List<MyInterviewDto>> GetMyInterviewScheduleAsync(Guid interviewerId)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("🕐 START GetMyInterviewSchedule for InterviewerId: {InterviewerId}", interviewerId);

            // Kiểm tra thông tin User và quyền hạn
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == interviewerId);

            var roles = user?.UserRoles?.Select(ur => ur.Role?.Code?.ToUpperInvariant() ?? "").ToList() ?? new List<string>();
            var isFullAccess = roles.Contains("ADMIN") || roles.Contains("SUPERADMIN") || roles.Contains("HR") || roles.Contains("HR_MANAGER");
            var isManager = roles.Contains("MANAGER") || roles.Contains("DEPARTMENTMANAGER");

            var query = _context.Interviews.AsNoTracking();

            if (!isFullAccess)
            {
                if (isManager && user?.DepartmentId != null)
                {
                    query = query.Where(i => i.InterviewerId == interviewerId || (i.Application != null && i.Application.Job != null && i.Application.Job.DepartmentId == user.DepartmentId));
                }
                else
                {
                    // Interviewer hoặc nhân viên
                    var hasOwnInterviews = await _context.Interviews.AsNoTracking().AnyAsync(i => i.InterviewerId == interviewerId);
                    if (hasOwnInterviews)
                    {
                        query = query.Where(i => i.InterviewerId == interviewerId);
                    }
                }
            }

            // Tối ưu hóa: Dùng Projection trực tiếp từ SQL thay vì Include nhiều cấp nặng nề
            var rawList = await query
                .OrderBy(i => i.ScheduledStart)
                .Select(i => new
                {
                    i.InterviewId,
                    CandidateName = i.Application != null ? (i.Application.ContactName ?? (i.Application.Candidate != null ? i.Application.Candidate.FullName : "N/A")) : "N/A",
                    JobTitle = i.Application != null && i.Application.Job != null ? i.Application.Job.Title : "N/A",
                    ScheduledStart = i.ScheduledStart,
                    ScheduledEnd = i.ScheduledEnd,
                    Location = i.Location,
                    MeetingLink = i.MeetingLink,
                    RawStatus = i.Status,
                    CandidateEmail = i.Application != null ? (i.Application.ContactEmail ?? (i.Application.Candidate != null ? i.Application.Candidate.Email : null)) : null,
                    CandidatePhone = i.Application != null ? (i.Application.ContactPhone ?? (i.Application.Candidate != null ? i.Application.Candidate.Phone : null)) : null,
                    InterviewerName = i.InterviewerUser != null ? i.InterviewerUser.FullName : "Chưa phân công",
                    InterviewerEmail = i.InterviewerUser != null ? i.InterviewerUser.Email : null,
                    InterviewerId = i.InterviewerId ?? Guid.Empty
                })
                .ToListAsync();

            var now = DateTime.UtcNow;

            var result = rawList.Select(interview =>
            {
                string status;
                if (string.Equals(interview.RawStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    status = "completed";
                }
                else if (string.Equals(interview.RawStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    status = "cancelled";
                }
                else if (now > interview.ScheduledEnd)
                {
                    status = "overdue";
                }
                else if (now >= interview.ScheduledStart && now <= interview.ScheduledEnd)
                {
                    status = "ongoing";
                }
                else
                {
                    status = "upcoming";
                }

                return new MyInterviewDto
                {
                    InterviewId = interview.InterviewId,
                    CandidateName = interview.CandidateName,
                    JobTitle = interview.JobTitle,
                    Position = interview.JobTitle,
                    InterviewTime = interview.ScheduledStart,
                    FormattedTime = interview.ScheduledStart.ToString("HH:mm"),
                    FormattedDate = interview.ScheduledStart.ToString("dd/MM/yyyy"),
                    Location = interview.Location ?? interview.MeetingLink ?? "N/A",
                    MeetingLink = interview.MeetingLink,
                    LocationType = !string.IsNullOrEmpty(interview.MeetingLink) ? "online" : "offline",
                    Status = status,
                    CandidateEmail = interview.CandidateEmail,
                    CandidatePhone = interview.CandidatePhone,
                    InterviewerName = interview.InterviewerName,
                    InterviewerEmail = interview.InterviewerEmail,
                    InterviewerId = interview.InterviewerId
                };
            }).ToList();

            totalSw.Stop();
            _logger.LogInformation("✅ FINISH GetMyInterviewSchedule - Total Time: {TotalMs}ms - Count: {Count}", 
                totalSw.ElapsedMilliseconds, result.Count);

            return result;
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            _logger.LogError(ex, "❌ Error getting interview schedule for InterviewerId: {InterviewerId} - Time: {TotalMs}ms", 
                interviewerId, totalSw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Get Interview by ApplicationId (for CC interviewer emails)
    /// </summary>
    public async Task<InterviewWithInterviewerDto?> GetInterviewByApplicationIdAsync(Guid applicationId)
    {
        try
        {
            var interview = await _context.Interviews
                .Include(i => i.InterviewerUser)
                .Where(i => i.ApplicationId == applicationId)
                .OrderByDescending(i => i.CreatedAt) // Get latest interview if multiple exist
                .Select(i => new InterviewWithInterviewerDto
                {
                    InterviewId = i.InterviewId,
                    ApplicationId = i.ApplicationId,
                    InterviewerId = i.InterviewerId ?? Guid.Empty,
                    InterviewerEmail = i.InterviewerUser != null ? i.InterviewerUser.Email : null,
                    InterviewerName = i.InterviewerUser != null ? i.InterviewerUser.FullName : null,
                    ScheduledStart = i.ScheduledStart,
                    Status = i.Status
                })
                .FirstOrDefaultAsync();

            return interview;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting interview for ApplicationId: {ApplicationId}", applicationId);
            return null;
        }
    }

    /// <summary>
    /// Helper kiểm tra và trả về tên ngày lễ Việt Nam (nếu có)
    /// </summary>
    public static string? GetVietnamHolidayName(int year, string ddMm)
    {
        if (ddMm == "01-01" || ddMm == "02-01") return "Tết Dương Lịch";
        if (ddMm == "30-04") return "Ngày Giải phóng miền Nam";
        if (ddMm == "01-05" || ddMm == "02-05") return "Quốc tế Lao động";
        if (ddMm == "01-09" || ddMm == "02-09" || ddMm == "03-09") return "Quốc khánh Việt Nam";

        if (year == 2025)
        {
            if (ddMm is "25-01" or "26-01" or "27-01" or "28-01" or "29-01" or "30-01" or "31-01" or "01-02" or "02-02")
                return "Tết Nguyên Đán Ất Tỵ";
            if (ddMm == "07-04") return "Giỗ Tổ Hùng Vương";
        }
        else if (year == 2026)
        {
            if (ddMm is "15-02" or "16-02" or "17-02" or "18-02" or "19-02" or "20-02" or "21-02" or "22-02" or "23-02")
                return "Tết Nguyên Đán Bính Ngọ";
            if (ddMm == "26-04") return "Giỗ Tổ Hùng Vương";
        }
        else if (year == 2027)
        {
            if (ddMm is "05-02" or "06-02" or "07-02" or "08-02" or "09-02" or "10-02" or "11-02" or "12-02")
                return "Tết Nguyên Đán Đinh Mùi";
            if (ddMm == "15-04") return "Giỗ Tổ Hùng Vương";
        }

        return null;
    }
}
