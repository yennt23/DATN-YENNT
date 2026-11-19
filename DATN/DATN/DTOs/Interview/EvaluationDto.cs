namespace DATN.DTOs.Interview;

public class EvaluationDto
{
    public Guid InterviewId { get; set; }

    public Guid InterviewerId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }

    /// <summary>
    /// Kết quả: "Passed", "Failed", "Consider"
    /// </summary>
    public string Result { get; set; } = null!;

    /// <summary>
    /// Chi tiết đánh giá từng câu hỏi dạng JSON
    /// </summary>
    public string? Details { get; set; }

    public Guid? SubmittedById { get; set; }

    public string? SubmittedByName { get; set; }

    public bool IsBelated { get; set; }

    /// <summary>
    /// Mức lương thỏa thuận (Deal lương - VNĐ/tháng) khi đạt phỏng vấn
    /// </summary>
    public decimal? OfferedSalary { get; set; }

    /// <summary>
    /// Ngày dự kiến nhận việc
    /// </summary>
    public DateTime? ExpectedStartDate { get; set; }

    /// <summary>
    /// Ghi chú đãi ngộ / điều khoản deal lương
    /// </summary>
    public string? OfferNote { get; set; }

    /// <summary>
    /// Cờ gửi email thông báo trúng tuyển & Deal lương cho ứng viên
    /// </summary>
    public bool SendOfferEmail { get; set; } = true;
}

public class AiJudgeRequestDto
{
    public string Question { get; set; } = null!;

    public string CandidateAnswer { get; set; } = null!;
}

public class AiJudgeResponseDto
{
    public int Score { get; set; }

    public string Assessment { get; set; } = null!;
}
