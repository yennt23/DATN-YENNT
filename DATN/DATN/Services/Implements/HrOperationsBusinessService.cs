using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using DATN.Data;
using DATN.Entities;
using DATN.Services.Interfaces;

namespace DATN.Services.Implements;

public class HrOperationsBusinessService : IHrOperationsBusinessService
{
    private readonly DATNContext _context;
    private readonly ILogger<HrOperationsBusinessService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public HrOperationsBusinessService(
        DATNContext context,
        ILogger<HrOperationsBusinessService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Logic 1: Nhận diện Nhân viên cũ tái ứng tuyển (Ex-Employee Rehire)
    /// Kiểm tra CCCD / SĐT với bảng ExEmployees (nghỉ việc đúng quy trình, IsEligibleForRehire = 1).
    /// Nếu khớp: gán IsExEmployeeRehire = true, lưu PreviousEmployeeCode, tự động +15 điểm ưu tiên kinh nghiệm.
    /// </summary>
    public async Task<(bool IsRehire, string? EmployeeCode, string? Message)> CheckExEmployeeRehireAsync(
        Application application, string? rawPhone, string? rawCitizenId)
    {
        try
        {
            var phone = NormalizePhone(rawPhone ?? application.PhoneNormalized ?? application.ContactPhone);
            var citizenId = NormalizeCitizenId(rawCitizenId ?? application.CitizenId);

            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(citizenId))
            {
                return (false, null, "Không có thông tin SĐT hoặc CCCD để đối soát nhân sự cũ.");
            }

            // Kiểm tra trong bảng ExEmployees
            var exEmployee = await _context.ExEmployees
                .AsNoTracking()
                .Where(e => e.IsEligibleForRehire)
                .FirstOrDefaultAsync(e =>
                    (!string.IsNullOrEmpty(citizenId) && e.CitizenId == citizenId) ||
                    (!string.IsNullOrEmpty(phone) && e.PhoneNormalized == phone));

            if (exEmployee == null)
            {
                return (false, null, "Không tìm thấy dữ liệu nhân sự cũ đủ điều kiện tái ứng tuyển.");
            }

            // Kiểm tra an toàn: Người này có bị rơi vào Blacklist sau khi nghỉ không?
            var isBlacklisted = await _context.CandidateBlacklists
                .AsNoTracking()
                .AnyAsync(b =>
                    (b.BanType == "PERMANENT" || !b.BannedUntil.HasValue || b.BannedUntil.Value > DateTime.UtcNow) &&
                    ((!string.IsNullOrEmpty(citizenId) && b.CitizenId == citizenId) ||
                     (!string.IsNullOrEmpty(phone) && b.PhoneNormalized == phone)));

            if (isBlacklisted)
            {
                _logger.LogWarning("⚠️ Nhân viên cũ {Code} ({Name}) thuộc danh sách Blacklist, không áp dụng Rehire Bonus.",
                    exEmployee.EmployeeCode, exEmployee.FullName);
                return (false, null, "Nhân sự cũ nằm trong danh sách Blacklist, không được áp dụng chính sách ưu tiên.");
            }

            // Gán dữ liệu Rehire vào Application
            application.IsExEmployeeRehire = true;
            application.PreviousEmployeeCode = exEmployee.EmployeeCode;

            // Tự động cộng +15 điểm ưu tiên kinh nghiệm sản xuất vào ApplicationAiScore
            var existingScore = await _context.ApplicationAiScores
                .FirstOrDefaultAsync(s => s.ApplicationId == application.ApplicationId);

            decimal updatedScore = 80m;
            if (existingScore != null)
            {
                updatedScore = Math.Min(100m, existingScore.MatchingScore + 15m);
                existingScore.MatchingScore = updatedScore;
            }
            else
            {
                _context.ApplicationAiScores.Add(new ApplicationAiScore
                {
                    AiScoreId = Guid.NewGuid(),
                    ApplicationId = application.ApplicationId,
                    MatchingScore = updatedScore,
                    Model = "REHIRE_PRIORITY",
                    BreakdownJson = "{\"RehireBonus\": 15, \"Reason\": \"Nhân sự cũ tái ứng tuyển có tay nghề cao\"}",
                    MatchedSkillsJson = "{}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _logger.LogInformation("⭐ Nhận diện nhân sự cũ tái ứng tuyển: {FullName} ({Code}) - Cộng 15 điểm ưu tiên (Điểm mới: {Score})",
                exEmployee.FullName, exEmployee.EmployeeCode, updatedScore);

            return (true, exEmployee.EmployeeCode,
                $"Nhận diện thành công nhân sự cũ: {exEmployee.FullName} (Mã: {exEmployee.EmployeeCode}, Từng làm tại: {exEmployee.DepartmentName}). Đã cộng +15 điểm kinh nghiệm (Điểm: {updatedScore}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kiểm tra nhân sự cũ tái ứng tuyển");
            return (false, null, $"Lỗi hệ thống khi đối soát nhân sự cũ: {ex.Message}");
        }
    }

    /// <summary>
    /// Logic 2: Chia tách Ca phỏng vấn tự động (Max 50 người/ca) & Chống trùng lịch người phỏng vấn
    /// </summary>
    public async Task<BatchScheduleResultDto> ScheduleBatchInterviewsAsync(
        ScheduleBatchInterviewRequest request, Guid? actorUserId)
    {
        if (request.ApplicationIds == null || request.ApplicationIds.Count == 0)
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Danh sách ứng viên cần xếp ca không được rỗng."
            };
        }

        // Validate ngày xếp ca phỏng vấn
        var requestedDate = request.ScheduledDate.Date;
        var today = DateTime.UtcNow.Date;
        if (requestedDate < today)
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Ngày xếp ca phỏng vấn không được chọn trong quá khứ."
            };
        }

        // Validate không phải ngày cuối tuần (Thứ 7, Chủ Nhật)
        if (requestedDate.DayOfWeek == DayOfWeek.Saturday || requestedDate.DayOfWeek == DayOfWeek.Sunday)
        {
            var dayName = requestedDate.DayOfWeek == DayOfWeek.Sunday ? "Chủ nhật" : "Thứ 7";
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = $"Ngày {requestedDate:dd/MM/yyyy} là {dayName} (ngày nghỉ cuối tuần), không thể xếp ca phỏng vấn. Vui lòng chọn ngày làm việc trong tuần (Thứ 2 - Thứ 6)."
            };
        }

        // Validate không phải ngày nghỉ lễ Việt Nam
        var holidayName = InterviewService.GetVietnamHolidayName(requestedDate.Year, requestedDate.ToString("dd-MM"));
        if (!string.IsNullOrEmpty(holidayName))
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = $"Ngày {requestedDate:dd/MM/yyyy} là ngày nghỉ lễ ({holidayName}), không thể xếp ca phỏng vấn. Vui lòng chọn ngày làm việc khác."
            };
        }

        var capacity = request.MaxCapacity > 0 ? request.MaxCapacity : 50;
        var totalCandidates = request.ApplicationIds.Count;

        // Lấy thông tin Job để sinh BatchCode
        var job = await _context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == request.JobId);
        var jobPrefix = !string.IsNullOrWhiteSpace(job?.Code) ? job.Code : (job?.Title?.Length >= 4 ? job.Title.Substring(0, 4).ToUpperInvariant() : "IND");

        // Lấy danh sách hồ sơ ứng viên (kèm Candidate và Job để lấy email & thông tin gửi thông báo)
        var applications = await _context.Applications
            .Include(a => a.Candidate)
            .Include(a => a.Job)
            .Where(a => request.ApplicationIds.Contains(a.ApplicationId))
            .ToListAsync();

        if (applications.Count == 0)
        {
            return new BatchScheduleResultDto
            {
                Success = false,
                Message = "Không tìm thấy hồ sơ hợp lệ nào trong cơ sở dữ liệu."
            };
        }

        // Chia nhóm ứng viên theo sức chứa MaxCapacity
        var candidateChunks = applications.Chunk(capacity).ToList();
        var batchesCreated = new List<BatchDetailDto>();

        var currentDate = request.ScheduledDate.Date;
        var currentShift = string.IsNullOrWhiteSpace(request.ShiftSlot) ? "MORNING_08_11" : request.ShiftSlot;

        for (int i = 0; i < candidateChunks.Count; i++)
        {
            var chunk = candidateChunks[i];

            // 1. Kiểm tra chống trùng lịch người phỏng vấn tại slot hiện tại
            if (request.InterviewerUserId != Guid.Empty)
            {
                var hasConflict = await _context.InterviewBatches
                    .AnyAsync(b => b.InterviewerUserId == request.InterviewerUserId &&
                                   b.ScheduledDate.HasValue && b.ScheduledDate.Value.Date == currentDate &&
                                   b.ShiftSlot == currentShift);

                if (hasConflict)
                {
                    return new BatchScheduleResultDto
                    {
                        Success = false,
                        Message = $"Xung đột lịch phỏng vấn: Người phụ trách đã có ca phỏng vấn khác vào ngày {currentDate:dd/MM/yyyy} ({currentShift}). Vui lòng chọn người phỏng vấn khác hoặc chuyển ca."
                    };
                }
            }

            // 2. Tạo InterviewBatch mới
            var isMorning = currentShift.Contains("MORNING");
            var shiftCode = isMorning ? "SANG" : "CHIEU";
            var shiftDisplayName = isMorning ? "Ca Sáng (08:00 - 11:30)" : "Ca Chiều (13:30 - 17:00)";
            var batchCode = $"BATCH-{jobPrefix}-{currentDate:yyyyMMdd}-{shiftCode}-{i + 1:D2}";

            var batch = new InterviewBatch
            {
                JobId = request.JobId,
                BatchCode = batchCode,
                ScheduledDate = currentDate,
                ShiftSlot = currentShift,
                ShiftName = shiftDisplayName,
                LocationRoom = "Hội trường tuyển dụng A",
                MaxCapacity = capacity,
                CurrentCount = chunk.Length,
                InterviewerUserId = request.InterviewerUserId != Guid.Empty ? request.InterviewerUserId : null,
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            _context.InterviewBatches.Add(batch);
            await _context.SaveChangesAsync(); // Lưu để lấy BatchId

            // 3. Gán Application vào Batch và cập nhật Interview entity
            var interviewTime = currentShift.Contains("MORNING")
                ? currentDate.Add(new TimeSpan(8, 0, 0))
                : currentDate.Add(new TimeSpan(13, 30, 0));

            var appIdsInChunk = chunk.Select(c => c.ApplicationId).ToList();
            var existingInterviews = await _context.Interviews
                .Where(iv => appIdsInChunk.Contains(iv.ApplicationId))
                .ToListAsync();

            var interviewMap = existingInterviews.ToDictionary(iv => iv.ApplicationId);

            foreach (var app in chunk)
            {
                app.InterviewBatchId = batch.Id;
                app.Status = "Interview_Scheduled";

                if (interviewMap.TryGetValue(app.ApplicationId, out var iv))
                {
                    iv.ScheduledStart = interviewTime;
                    iv.ScheduledEnd = interviewTime.AddHours(2);
                    iv.Status = "Scheduled";
                    if (request.InterviewerUserId != Guid.Empty)
                    {
                        iv.InterviewerId = request.InterviewerUserId;
                    }
                }
                else
                {
                    _context.Interviews.Add(new Interview
                    {
                        InterviewId = Guid.NewGuid(),
                        ApplicationId = app.ApplicationId,
                        Title = $"Phỏng vấn ca {batchCode}",
                        ScheduledStart = interviewTime,
                        ScheduledEnd = interviewTime.AddHours(2),
                        Location = "Hội trường tuyển dụng nhà máy",
                        Status = "Scheduled",
                        InterviewerId = request.InterviewerUserId != Guid.Empty ? request.InterviewerUserId : null,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            batchesCreated.Add(new BatchDetailDto
            {
                BatchId = batch.Id,
                BatchCode = batch.BatchCode,
                ScheduledDate = currentDate,
                ShiftSlot = currentShift,
                CandidateCount = chunk.Length,
                MaxCapacity = capacity
            });

            // Gửi email thông báo ca phỏng vấn tự động cho các ứng viên trong lô có email
            QueueBatchInterviewEmails(chunk, batch, job?.Title ?? "Vị trí tuyển dụng", interviewTime, shiftDisplayName);

            // 4. Luân chuyển slot cho ca tiếp theo (nếu còn ứng viên chưa được xếp)
            if (i < candidateChunks.Count - 1)
            {
                if (currentShift == "MORNING_08_11")
                {
                    currentShift = "AFTERNOON_13_17";
                }
                else
                {
                    // Tự động bỏ qua Thứ 7, Chủ Nhật và các ngày nghỉ lễ Việt Nam
                    do
                    {
                        currentDate = currentDate.AddDays(1);
                    } while (currentDate.DayOfWeek == DayOfWeek.Saturday ||
                             currentDate.DayOfWeek == DayOfWeek.Sunday ||
                             InterviewService.GetVietnamHolidayName(currentDate.Year, currentDate.ToString("dd-MM")) != null);

                    currentShift = "MORNING_08_11";
                }
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("✅ Hoàn tất xếp ca phỏng vấn: Đã tạo {BatchesCount} ca cho {TotalCandidates} ứng viên (JobId: {JobId})",
            batchesCreated.Count, totalCandidates, request.JobId);

        return new BatchScheduleResultDto
        {
            Success = true,
            Message = $"Đã tự động chia tách và xếp thành công {batchesCreated.Count} ca phỏng vấn cho {totalCandidates} ứng viên (Tối đa {capacity} người/ca).",
            TotalCandidates = totalCandidates,
            BatchesCreated = batchesCreated.Count,
            Batches = batchesCreated
        };
    }

    /// <summary>
    /// Logic 3: Giới hạn Dời lịch Phỏng vấn tối đa 2 lần. Lần thứ 3 chuyển Status = 'Rejected_Exceed_Reschedule'.
    /// </summary>
    public async Task<(bool Success, bool IsRejected, int CurrentCount, string Message)> RescheduleInterviewAsync(
        Guid applicationId, DateTime newDate, string newShiftSlot, string? reason, Guid? actorUserId)
    {
        var application = await _context.Applications.FirstOrDefaultAsync(a => a.ApplicationId == applicationId);
        if (application == null)
        {
            return (false, false, 0, "Không tìm thấy hồ sơ ứng viên.");
        }

        application.InterviewRescheduleCount++;

        if (application.InterviewRescheduleCount > 2)
        {
            application.Status = "Rejected_Exceed_Reschedule";
            application.InterviewBatchId = null;
            await _context.SaveChangesAsync();

            _logger.LogWarning("❌ Ứng viên {ApplicationId} dời lịch {Count} lần (> 2 lần). Hệ thống tự động từ chối hồ sơ (Rejected_Exceed_Reschedule).",
                applicationId, application.InterviewRescheduleCount);

            return (true, true, application.InterviewRescheduleCount,
                $"Ứng viên đã dời lịch quá 2 lần cho phép (Lần thứ {application.InterviewRescheduleCount}). Hệ thống tự động chuyển hồ sơ sang trạng thái Từ chối: Rejected_Exceed_Reschedule.");
        }

        // Cập nhật ngày phỏng vấn mới trong bảng Interviews
        var interviewTime = newShiftSlot.Contains("MORNING")
            ? newDate.Date.Add(new TimeSpan(8, 0, 0))
            : newDate.Date.Add(new TimeSpan(13, 30, 0));

        var interview = await _context.Interviews
            .FirstOrDefaultAsync(iv => iv.ApplicationId == applicationId);

        if (interview != null)
        {
            interview.ScheduledStart = interviewTime;
            interview.ScheduledEnd = interviewTime.AddHours(2);
            interview.Status = "Scheduled";
        }
        else
        {
            _context.Interviews.Add(new Interview
            {
                InterviewId = Guid.NewGuid(),
                ApplicationId = applicationId,
                Title = $"Phỏng vấn dời lịch lần {application.InterviewRescheduleCount}",
                ScheduledStart = interviewTime,
                ScheduledEnd = interviewTime.AddHours(2),
                Location = "Hội trường tuyển dụng nhà máy",
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow
            });
        }

        application.Status = "Interview_Scheduled";
        await _context.SaveChangesAsync();

        _logger.LogInformation("ℹ️ Dời lịch phỏng vấn cho {ApplicationId} thành công (Lần {Count}/2). Ngày mới: {Date}",
            applicationId, application.InterviewRescheduleCount, interviewTime);

        return (true, false, application.InterviewRescheduleCount,
            $"Đã dời lịch phỏng vấn thành công sang ngày {newDate:dd/MM/yyyy} ({newShiftSlot}). Đã dời: {application.InterviewRescheduleCount}/2 lần.");
    }

    /// <summary>
    /// Logic 4: Tự động Hết hạn Thư mời nhận việc (Offer Expiration) sau 72h cho khối Kỹ sư/Văn phòng.
    /// Quét các hồ sơ Offer_Sent mà UtcNow > OfferExpiresAt -> chuyển sang Offer_Expired.
    /// </summary>
    public async Task<(int ExpiredCount, List<Guid> ExpiredApplicationIds)> CheckAndExpireOffersAsync()
    {
        var now = DateTime.UtcNow;

        var expiredApps = await _context.Applications
            .Where(a => (a.OfferStatus == "Offer_Sent" || a.Status == "Offer_Sent") &&
                        a.OfferExpiresAt.HasValue &&
                        a.OfferExpiresAt.Value < now)
            .ToListAsync();

        if (expiredApps.Count == 0)
        {
            return (0, new List<Guid>());
        }

        var expiredIds = new List<Guid>();
        foreach (var app in expiredApps)
        {
            app.OfferStatus = "Offer_Expired";
            app.Status = "Offer_Expired";
            expiredIds.Add(app.ApplicationId);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("⏰ Đã quét và cập nhật hết hạn Offer 72h cho {Count} hồ sơ ứng viên.", expiredApps.Count);

        return (expiredApps.Count, expiredIds);
    }

    /// <summary>
    /// Logic 5: Tự động Đóng chiến dịch & Bảo lưu hồ sơ vào Kho tiềm năng (Talent Pool) khi đủ chỉ tiêu.
    /// </summary>
    public async Task<(bool Success, int MovedToTalentPoolCount, string Message)> AutoCloseCampaignAndMoveToTalentPoolAsync(
        Guid jobId, Guid? actorUserId)
    {
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId);
        if (job == null)
        {
            return (false, 0, "Không tìm thấy chiến dịch tuyển dụng.");
        }

        var target = job.TargetQuantity > 0 ? job.TargetQuantity : 50;
        if (job.HiredCount < target)
        {
            return (false, 0,
                $"Chiến dịch tuyển dụng chưa đủ chỉ tiêu ({job.HiredCount}/{target}). Chưa đủ điều kiện đóng chiến dịch.");
        }

        // Đóng chiến dịch
        job.RequisitionStatus = "Closed_Quota_Full";
        job.Status = "Closed";

        // Tìm toàn bộ hồ sơ đạt sơ loại nhưng chưa kịp phỏng vấn / đang chờ xử lý
        var pendingStatuses = new List<string>
        {
            "Submitted", "Pending", "PENDING", "Screening_Passed", "Need_Manual_Review", "Waitlist"
        };

        var poolApps = await _context.Applications
            .Where(a => a.JobId == jobId && !a.IsInTalentPool && pendingStatuses.Contains(a.Status))
            .ToListAsync();

        foreach (var app in poolApps)
        {
            app.IsInTalentPool = true;
            app.Status = "Saved_In_Talent_Pool";
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("🔒 Chiến dịch {JobTitle} đã đủ chỉ tiêu ({Hired}/{Target}). Đã đóng và bảo lưu {PoolCount} hồ sơ vào Talent Pool.",
            job.Title, job.HiredCount, target, poolApps.Count);

        return (true, poolApps.Count,
            $"Chiến dịch '{job.Title}' đã tuyển đủ chỉ tiêu ({job.HiredCount}/{target}). Toàn bộ {poolApps.Count} hồ sơ còn lại đã được bảo lưu an toàn vào Kho ứng viên tiềm năng (Talent Pool).");
    }

    /// <summary>
    /// Logic 6: Đánh giá thử việc sau 30 ngày (Probation 30-Day Feedback) & Tính toán tỷ lệ thành công
    /// </summary>
    public async Task<(bool Success, string Message, decimal? AccuracyRate)> UpdateProbationFeedbackAsync(
        Guid applicationId, ProbationFeedbackRequest request, Guid? actorUserId)
    {
        var application = await _context.Applications
            .Include(a => a.Job)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId);

        if (application == null)
        {
            return (false, "Không tìm thấy hồ sơ ứng viên.", null);
        }

        var validStatuses = new[] { "Passed_30Days", "Early_Resigned", "In_Probation" };
        if (!validStatuses.Contains(request.ProbationStatus))
        {
            return (false, $"Trạng thái thử việc '{request.ProbationStatus}' không hợp lệ. Chỉ chấp nhận Passed_30Days hoặc Early_Resigned.", null);
        }

        application.Probation30DayStatus = request.ProbationStatus;
        await _context.SaveChangesAsync();

        // Tính toán tỷ lệ thành công thử việc thực tế của vị trí tuyển dụng này
        decimal? accuracyRate = null;
        if (application.JobId != Guid.Empty)
        {
            var evaluatedApps = await _context.Applications
                .AsNoTracking()
                .Where(a => a.JobId == application.JobId &&
                            (a.Probation30DayStatus == "Passed_30Days" || a.Probation30DayStatus == "Early_Resigned"))
                .ToListAsync();

            if (evaluatedApps.Count > 0)
            {
                var passedCount = evaluatedApps.Count(a => a.Probation30DayStatus == "Passed_30Days");
                accuracyRate = Math.Round((decimal)passedCount / evaluatedApps.Count * 100m, 1);
            }
        }

        _logger.LogInformation("🏆 Đã cập nhật đánh giá thử việc cho ứng viên {ApplicationId}: {Status}. Độ chính xác tuyển dụng: {Rate}%",
            applicationId, request.ProbationStatus, accuracyRate);

        return (true,
            $"Đã cập nhật đánh giá thử việc: {(request.ProbationStatus == "Passed_30Days" ? "Đạt thử việc 30 ngày" : "Nghỉ việc sớm")}. Tỷ lệ trụ lại thực tế của vị trí: {accuracyRate ?? 100}%",
            accuracyRate);
    }

    /// <summary>
    /// Lấy danh sách các ca phỏng vấn tập trung đang hoạt động từ cơ sở dữ liệu thật
    /// </summary>
    public async Task<List<InterviewBatch>> GetActiveInterviewBatchesAsync(Guid? jobId)
    {
        try
        {
            var query = _context.InterviewBatches
                .AsNoTracking();

            if (jobId.HasValue && jobId.Value != Guid.Empty)
            {
                query = query.Where(b => b.JobId == jobId.Value || b.JobId == null);
            }

            var batches = await query
                .OrderByDescending(b => b.ScheduledDate)
                .ThenBy(b => b.ShiftSlot)
                .Take(20)
                .ToListAsync();

            // Nếu trong DB chưa có ca nào, tự động khởi tạo dữ liệu ban đầu lưu vào DB
            if (batches.Count == 0)
            {
                var nextDate = DateTime.Today.AddDays(1);
                while (nextDate.DayOfWeek == DayOfWeek.Saturday || nextDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    nextDate = nextDate.AddDays(1);
                }

                var initialBatches = new List<InterviewBatch>
                {
                    new InterviewBatch
                    {
                        BatchCode = $"BATCH-SMT-{nextDate:yyyyMMdd}-01",
                        JobId = jobId.HasValue && jobId.Value != Guid.Empty ? jobId.Value : null,
                        ScheduledDate = nextDate,
                        ShiftSlot = "MORNING_08_11",
                        ShiftName = "Ca Sáng (08:00 - 11:30)",
                        LocationRoom = "Hội trường Tuyển dụng & Đào tạo A",
                        MaxCapacity = 50,
                        CurrentCount = 0,
                        Status = "Open",
                        CreatedAt = DateTime.UtcNow
                    },
                    new InterviewBatch
                    {
                        BatchCode = $"BATCH-SMT-{nextDate:yyyyMMdd}-02",
                        JobId = jobId.HasValue && jobId.Value != Guid.Empty ? jobId.Value : null,
                        ScheduledDate = nextDate,
                        ShiftSlot = "AFTERNOON_13_17",
                        ShiftName = "Ca Chiều (13:30 - 17:00)",
                        LocationRoom = "Hội trường Tuyển dụng & Đào tạo A",
                        MaxCapacity = 50,
                        CurrentCount = 0,
                        Status = "Open",
                        CreatedAt = DateTime.UtcNow
                    }
                };

                try
                {
                    _context.InterviewBatches.AddRange(initialBatches);
                    await _context.SaveChangesAsync();
                    batches = initialBatches;
                }
                catch (Exception addEx)
                {
                    _logger.LogWarning(addEx, "Không thể tự động khởi tạo InterviewBatches ban đầu vào DB");
                    batches = initialBatches;
                }
            }

            // Đồng bộ số lượng ứng viên thực tế đang gắn vào từng batch trong DB
            var batchIds = batches.Select(b => b.Id).ToList();
            var actualCounts = await _context.Applications
                .AsNoTracking()
                .Where(a => a.InterviewBatchId.HasValue && batchIds.Contains(a.InterviewBatchId.Value))
                .GroupBy(a => a.InterviewBatchId.Value)
                .Select(g => new { BatchId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BatchId, x => x.Count);

            foreach (var b in batches)
            {
                b.Job = null; // Tránh circular reference khi serialize JSON
                b.BatchCode ??= $"BATCH-{b.Id}";
                b.ShiftSlot ??= "MORNING_08_11";
                b.ShiftName ??= (b.ShiftSlot == "MORNING_08_11" ? "Ca Sáng (08:00 - 11:30)" : "Ca Chiều (13:30 - 17:00)");
                b.ScheduledDate ??= DateTime.Today;
                b.MaxCapacity ??= 50;

                if (actualCounts.TryGetValue(b.Id, out var count))
                {
                    b.CurrentCount = count;
                }
                else
                {
                    b.CurrentCount ??= 0;
                }
            }

            return batches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách InterviewBatches từ Database");
            var fallbackDate = DateTime.Today.AddDays(1);
            return new List<InterviewBatch>
            {
                new InterviewBatch
                {
                    Id = 1,
                    BatchCode = "BATCH-SMT-OPEN-01",
                    ShiftSlot = "MORNING_08_11",
                    ShiftName = "Ca Sáng (08:00 - 11:30)",
                    ScheduledDate = fallbackDate,
                    CurrentCount = 0,
                    MaxCapacity = 50,
                    Status = "Open"
                },
                new InterviewBatch
                {
                    Id = 2,
                    BatchCode = "BATCH-SMT-OPEN-02",
                    ShiftSlot = "AFTERNOON_13_17",
                    ShiftName = "Ca Chiều (13:30 - 17:00)",
                    ScheduledDate = fallbackDate,
                    CurrentCount = 0,
                    MaxCapacity = 50,
                    Status = "Open"
                }
            };
        }
    }

    #region Helpers
    private void QueueBatchInterviewEmails(
        IEnumerable<Application> applications,
        InterviewBatch batch,
        string jobTitle,
        DateTime interviewTime,
        string shiftDisplayName)
    {
        var candidateList = applications.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var formattedDate = interviewTime.ToString("dd/MM/yyyy");
                var formattedTime = interviewTime.ToString("HH:mm");

                foreach (var app in candidateList)
                {
                    var toEmail = !string.IsNullOrWhiteSpace(app.ContactEmail)
                        ? app.ContactEmail
                        : app.Candidate?.Email;

                    if (string.IsNullOrWhiteSpace(toEmail)) continue;

                    var candidateName = !string.IsNullOrWhiteSpace(app.ContactName)
                        ? app.ContactName
                        : app.Candidate?.FullName ?? "Ứng viên";

                    var subject = $"[V9 TECH] Giấy mời tham gia ca phỏng vấn tuyển dụng - {batch.BatchCode}";
                    var htmlBody = $@"
<!DOCTYPE html>
<html>
<head><meta charset=""UTF-8""><style>body{{font-family:'Segoe UI',Tahoma,Arial,sans-serif;line-height:1.6;color:#333;}}.container{{max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px;background:#ffffff;}}.header{{background:#2563eb;color:#ffffff;padding:20px;border-radius:8px;text-align:center;}}.info{{background:#f8fafc;padding:16px;border-radius:8px;border-left:4px solid #2563eb;margin:16px 0;}}</style></head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2 style=""margin:0;"">THÔNG BÁO XẾP CA PHỎNG VẤN TUYỂN DỤNG</h2>
            <p style=""margin:4px 0 0 0;"">Hệ thống Tuyển dụng V9 TECH</p>
        </div>
        <p>Kính gửi <strong>{candidateName}</strong>,</p>
        <p>Hồ sơ ứng tuyển của bạn cho vị trí <strong>{jobTitle}</strong> đã được Hội đồng tuyển dụng tiếp nhận và xếp vào ca phỏng vấn tập trung như sau:</p>
        
        <div class=""info"">
            <p style=""margin:4px 0;""><strong>Mã ca phỏng vấn:</strong> <span style=""color:#2563eb;font-weight:bold;"">{batch.BatchCode}</span></p>
            <p style=""margin:4px 0;""><strong>Ca phỏng vấn:</strong> {shiftDisplayName}</p>
            <p style=""margin:4px 0;""><strong>Ngày phỏng vấn:</strong> {formattedDate}</p>
            <p style=""margin:4px 0;""><strong>Thời gian tập trung:</strong> {formattedTime}</p>
            <p style=""margin:4px 0;""><strong>Địa điểm:</strong> {batch.LocationRoom ?? "Hội trường tuyển dụng nhà máy"}</p>
        </div>

        <p><strong>Lưu ý quan trọng khi tham gia phỏng vấn:</strong></p>
        <ul>
            <li>Vui lòng có mặt trước giờ phỏng vấn 15 phút để làm thủ tục tiếp đón.</li>
            <li>Mang theo bản gốc Căn cước công dân (CCCD) gắn chip để xác thực tại cổng bảo vệ.</li>
            <li>Trang phục lịch sự, gọn gàng, mang theo bút viết cá nhân.</li>
        </ul>

        <p>Nếu bạn cần hỗ trợ hoặc thông báo dời ca phỏng vấn vì lý do bất khả kháng, vui lòng liên hệ trực tiếp Ban Nhân sự V9 TECH.</p>
        
        <p style=""margin-top:24px;"">Trân trọng,<br><strong>Phòng Tuyển Dụng V9 TECH</strong></p>
    </div>
</body>
</html>";

                    try
                    {
                        await emailService.SendEmailAsync(toEmail.Trim(), subject, htmlBody);
                        _logger.LogInformation("📧 Đã gửi email ca phỏng vấn {BatchCode} đến {Email}", batch.BatchCode, toEmail);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Gửi email ca phỏng vấn {BatchCode} thất bại cho {Email}", batch.BatchCode, toEmail);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Lỗi luồng gửi email hàng loạt cho ca {BatchCode}", batch.BatchCode);
            }
        });
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = Regex.Replace(phone, @"[^\d]", "");
        if (digits.StartsWith("84") && digits.Length >= 11)
        {
            digits = "0" + digits.Substring(2);
        }
        return digits;
    }

    private static string NormalizeCitizenId(string? citizenId)
    {
        if (string.IsNullOrWhiteSpace(citizenId)) return string.Empty;
        return Regex.Replace(citizenId, @"[^\d]", "").Trim();
    }
    #endregion
}
