using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DATN.Data;

#nullable disable

namespace DATN.Migrations
{
    [DbContext(typeof(DATNContext))]
    [Migration("20261115233500_SyncIndustrialBatchesAndMissingColumns")]
    public partial class SyncIndustrialBatchesAndMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1. InterviewBatches: Bổ sung CurrentCount, MaxCapacity, LocationRoom, ShiftName, Status, InterviewerUserId
IF OBJECT_ID('dbo.InterviewBatches', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewBatches', 'JobId') IS NULL ALTER TABLE dbo.InterviewBatches ADD JobId UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.InterviewBatches', 'InterviewerUserId') IS NULL 
    BEGIN
        ALTER TABLE dbo.InterviewBatches ADD InterviewerUserId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH('dbo.InterviewBatches', 'InterviewerId') IS NOT NULL
            EXEC('UPDATE dbo.InterviewBatches SET InterviewerUserId = InterviewerId WHERE InterviewerUserId IS NULL');
    END;

    IF COL_LENGTH('dbo.InterviewBatches', 'MaxCapacity') IS NULL 
        ALTER TABLE dbo.InterviewBatches ADD MaxCapacity INT NOT NULL CONSTRAINT DF_InterviewBatches_MaxCapacity DEFAULT (50);

    IF COL_LENGTH('dbo.InterviewBatches', 'CurrentCount') IS NULL 
    BEGIN
        ALTER TABLE dbo.InterviewBatches ADD CurrentCount INT NOT NULL CONSTRAINT DF_InterviewBatches_CurrentCount DEFAULT (0);
        IF COL_LENGTH('dbo.InterviewBatches', 'CurrentEnrolled') IS NOT NULL
            EXEC('UPDATE dbo.InterviewBatches SET CurrentCount = CurrentEnrolled WHERE CurrentCount = 0');
    END;

    IF COL_LENGTH('dbo.InterviewBatches', 'LocationRoom') IS NULL 
        ALTER TABLE dbo.InterviewBatches ADD LocationRoom NVARCHAR(200) NULL CONSTRAINT DF_InterviewBatches_LocationRoom DEFAULT (N'Hội trường tuyển dụng A');
    ELSE
        ALTER TABLE dbo.InterviewBatches ALTER COLUMN LocationRoom NVARCHAR(200) NULL;

    IF COL_LENGTH('dbo.InterviewBatches', 'ShiftName') IS NULL 
        ALTER TABLE dbo.InterviewBatches ADD ShiftName NVARCHAR(100) NULL CONSTRAINT DF_InterviewBatches_ShiftName DEFAULT (N'Ca phỏng vấn tập trung');
    ELSE
        ALTER TABLE dbo.InterviewBatches ALTER COLUMN ShiftName NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.InterviewBatches', 'Status') IS NULL 
        ALTER TABLE dbo.InterviewBatches ADD Status NVARCHAR(30) NOT NULL CONSTRAINT DF_InterviewBatches_Status DEFAULT ('Open');
END;

-- 2. ApplicationNotes: Sửa CreatedBy cho phép NULL và đảm bảo có cột Note
IF OBJECT_ID('dbo.ApplicationNotes', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationNotes ALTER COLUMN CreatedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.ApplicationNotes', 'Note') IS NULL AND COL_LENGTH('dbo.ApplicationNotes', 'Content') IS NOT NULL
        EXEC sp_rename 'dbo.ApplicationNotes.Content', 'Note', 'COLUMN';
    IF COL_LENGTH('dbo.ApplicationNotes', 'Note') IS NULL
        ALTER TABLE dbo.ApplicationNotes ADD Note NVARCHAR(MAX) NULL;
END;

-- 3. Applications: Đồng bộ các cột nghiệp vụ ATS
IF OBJECT_ID('dbo.Applications', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Applications', 'InterviewBatchId') IS NULL ALTER TABLE dbo.Applications ADD InterviewBatchId INT NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactName') IS NULL ALTER TABLE dbo.Applications ADD ContactName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactEmail') IS NULL ALTER TABLE dbo.Applications ADD ContactEmail NVARCHAR(320) NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactPhone') IS NULL ALTER TABLE dbo.Applications ADD ContactPhone NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'ApplicationSource') IS NULL ALTER TABLE dbo.Applications ADD ApplicationSource NVARCHAR(30) NOT NULL DEFAULT ('PDF_UPLOAD');
    IF COL_LENGTH('dbo.Applications', 'ScoringMethod') IS NULL ALTER TABLE dbo.Applications ADD ScoringMethod NVARCHAR(30) NOT NULL DEFAULT ('GEMINI_AI');
    IF COL_LENGTH('dbo.Applications', 'CitizenId') IS NULL ALTER TABLE dbo.Applications ADD CitizenId NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.Applications', 'PhoneNormalized') IS NULL ALTER TABLE dbo.Applications ADD PhoneNormalized NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.Applications', 'DuplicateCount') IS NULL ALTER TABLE dbo.Applications ADD DuplicateCount INT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Applications', 'PreviousApplicationId') IS NULL ALTER TABLE dbo.Applications ADD PreviousApplicationId INT NULL;
    IF COL_LENGTH('dbo.Applications', 'IsBlacklisted') IS NULL ALTER TABLE dbo.Applications ADD IsBlacklisted BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'BlacklistWarningNote') IS NULL ALTER TABLE dbo.Applications ADD BlacklistWarningNote NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'OralInterviewChecklistJson') IS NULL ALTER TABLE dbo.Applications ADD OralInterviewChecklistJson NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Applications', 'MedicalCheckStatus') IS NULL ALTER TABLE dbo.Applications ADD MedicalCheckStatus NVARCHAR(30) NOT NULL DEFAULT ('Pending');
    IF COL_LENGTH('dbo.Applications', 'BankAccountNumber') IS NULL ALTER TABLE dbo.Applications ADD BankAccountNumber NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'BankName') IS NULL ALTER TABLE dbo.Applications ADD BankName NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Applications', 'OnboardingDocsCompleted') IS NULL ALTER TABLE dbo.Applications ADD OnboardingDocsCompleted BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'ConvertedEmployeeCode') IS NULL ALTER TABLE dbo.Applications ADD ConvertedEmployeeCode NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'SlaDeadlineAt') IS NULL ALTER TABLE dbo.Applications ADD SlaDeadlineAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Applications', 'IsSlaEscalated') IS NULL ALTER TABLE dbo.Applications ADD IsSlaEscalated BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'DateOfBirth') IS NULL ALTER TABLE dbo.Applications ADD DateOfBirth DATE NULL;
    IF COL_LENGTH('dbo.Applications', 'InterviewScore') IS NULL ALTER TABLE dbo.Applications ADD InterviewScore DECIMAL(5,2) NULL;
    IF COL_LENGTH('dbo.Applications', 'OfferStatus') IS NULL ALTER TABLE dbo.Applications ADD OfferStatus NVARCHAR(30) NOT NULL DEFAULT ('Not_Required');
    IF COL_LENGTH('dbo.Applications', 'OfferedSalary') IS NULL ALTER TABLE dbo.Applications ADD OfferedSalary DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.Applications', 'IsInTalentPool') IS NULL ALTER TABLE dbo.Applications ADD IsInTalentPool BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'Probation30DayStatus') IS NULL ALTER TABLE dbo.Applications ADD Probation30DayStatus NVARCHAR(30) NULL DEFAULT ('Not_Started');
END;

-- 4. Jobs: Đồng bộ cột tuyển dụng
IF OBJECT_ID('dbo.Jobs', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Jobs', 'QrCodeImagePath') IS NULL ALTER TABLE dbo.Jobs ADD QrCodeImagePath NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Jobs', 'RecruitmentTrack') IS NULL ALTER TABLE dbo.Jobs ADD RecruitmentTrack NVARCHAR(30) NOT NULL DEFAULT ('PROFESSIONAL');
    IF COL_LENGTH('dbo.Jobs', 'TargetQuantity') IS NULL ALTER TABLE dbo.Jobs ADD TargetQuantity INT NOT NULL DEFAULT (50);
    IF COL_LENGTH('dbo.Jobs', 'HiredCount') IS NULL ALTER TABLE dbo.Jobs ADD HiredCount INT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Jobs', 'WaitlistBufferPercent') IS NULL ALTER TABLE dbo.Jobs ADD WaitlistBufferPercent INT NOT NULL DEFAULT (15);
    IF COL_LENGTH('dbo.Jobs', 'CurrentScoringWeightVersion') IS NULL ALTER TABLE dbo.Jobs ADD CurrentScoringWeightVersion INT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Jobs', 'GoogleFormUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Jobs', 'RequisitionStatus') IS NULL ALTER TABLE dbo.Jobs ADD RequisitionStatus NVARCHAR(30) NOT NULL DEFAULT ('Approved');
    IF COL_LENGTH('dbo.Jobs', 'RequireNightShift') IS NULL ALTER TABLE dbo.Jobs ADD RequireNightShift BIT NOT NULL DEFAULT (0);
END;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
