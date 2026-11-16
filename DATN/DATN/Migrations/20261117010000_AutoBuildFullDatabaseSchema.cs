using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DATN.Data;

#nullable disable

namespace DATN.Migrations
{
    [DbContext(typeof(DATNContext))]
    [Migration("20261117010000_AutoBuildFullDatabaseSchema")]
    public partial class AutoBuildFullDatabaseSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- =========================================================================================
-- 1. GỠ BỎ CHECK CONSTRAINTS CŨ (ĐẢM BẢO KHÔNG CHẶN CÁC TRẠNG THÁI MỚI)
-- =========================================================================================
DECLARE @dropCheckSql NVARCHAR(MAX) = N'';
SELECT @dropCheckSql = @dropCheckSql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(13)
FROM sys.check_constraints
WHERE parent_object_id IN (
    OBJECT_ID('dbo.Applications'),
    OBJECT_ID('dbo.Candidates'),
    OBJECT_ID('dbo.Jobs'),
    OBJECT_ID('dbo.Interviews'),
    OBJECT_ID('dbo.PipelineStages'),
    OBJECT_ID('dbo.ApplicationNotes'),
    OBJECT_ID('dbo.ApplicationStageHistory')
);
IF LEN(@dropCheckSql) > 0
    EXEC sp_executesql @dropCheckSql;

-- =========================================================================================
-- 2. BẢNG Departments
-- =========================================================================================
IF OBJECT_ID('dbo.Departments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Departments
    (
        DepartmentId   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Departments PRIMARY KEY DEFAULT (NEWID()),
        DepartmentCode NVARCHAR(50)     NOT NULL,
        DepartmentName NVARCHAR(200)    NOT NULL,
        Description    NVARCHAR(500)    NULL,
        IsActive       BIT              NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1),
        CreatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      DATETIME2        NULL
    );
    CREATE UNIQUE INDEX UX_Departments_DepartmentCode ON dbo.Departments(DepartmentCode);
END;

-- =========================================================================================
-- 3. BẢNG CandidateBlacklists
-- =========================================================================================
IF OBJECT_ID('dbo.CandidateBlacklists', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CandidateBlacklists') AND name = 'BlacklistId')
       OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CandidateBlacklists') AND name = 'PhoneNormalized')
    BEGIN
        DROP TABLE dbo.CandidateBlacklists;
    END
END;

IF OBJECT_ID('dbo.CandidateBlacklists', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CandidateBlacklists
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CandidateBlacklists PRIMARY KEY,
        CitizenId       NVARCHAR(20)      NULL,
        PhoneNormalized NVARCHAR(20)      NULL,
        FullName        NVARCHAR(150)     NOT NULL,
        Reason          NVARCHAR(500)     NOT NULL,
        BanType         NVARCHAR(50)      NOT NULL CONSTRAINT DF_CandidateBlacklists_BanType DEFAULT ('PERMANENT'),
        BannedUntil     DATETIME2         NULL,
        CreatedBy       NVARCHAR(100)     NULL,
        CreatedAt       DATETIME2         NOT NULL CONSTRAINT DF_CandidateBlacklists_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_CandidateBlacklists_CitizenId ON dbo.CandidateBlacklists(CitizenId) WHERE CitizenId IS NOT NULL;
    CREATE INDEX IX_CandidateBlacklists_PhoneNormalized ON dbo.CandidateBlacklists(PhoneNormalized) WHERE PhoneNormalized IS NOT NULL;
END;

-- =========================================================================================
-- 4. BẢNG InterviewBatches
-- =========================================================================================
IF OBJECT_ID('dbo.InterviewBatches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InterviewBatches
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewBatches PRIMARY KEY,
        JobId             UNIQUEIDENTIFIER  NULL,
        BatchCode         NVARCHAR(50)      NOT NULL,
        ScheduledDate     DATE              NOT NULL,
        ShiftSlot         NVARCHAR(30)      NOT NULL,
        ShiftName         NVARCHAR(100)     NULL CONSTRAINT DF_InterviewBatches_ShiftName DEFAULT (N'Ca phỏng vấn tập trung'),
        LocationRoom      NVARCHAR(200)     NULL CONSTRAINT DF_InterviewBatches_LocationRoom DEFAULT (N'Hội trường tuyển dụng A'),
        MaxCapacity       INT               NOT NULL CONSTRAINT DF_InterviewBatches_MaxCapacity DEFAULT (50),
        CurrentCount      INT               NOT NULL CONSTRAINT DF_InterviewBatches_CurrentCount DEFAULT (0),
        InterviewerUserId UNIQUEIDENTIFIER  NULL,
        Status            NVARCHAR(30)      NOT NULL CONSTRAINT DF_InterviewBatches_Status DEFAULT ('Open'),
        CreatedAt         DATETIME2         NOT NULL CONSTRAINT DF_InterviewBatches_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;
ELSE
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

-- =========================================================================================
-- 5. BẢNG ExEmployees
-- =========================================================================================
IF OBJECT_ID('dbo.ExEmployees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExEmployees
    (
        Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExEmployees PRIMARY KEY,
        EmployeeCode        NVARCHAR(50)      NOT NULL,
        CitizenId           NVARCHAR(20)      NULL,
        PhoneNormalized     NVARCHAR(20)      NULL,
        FullName            NVARCHAR(150)     NOT NULL,
        DepartmentName      NVARCHAR(100)     NULL,
        ResignedDate        DATE              NULL,
        ResignationReason   NVARCHAR(255)     NULL,
        IsEligibleForRehire BIT               NOT NULL CONSTRAINT DF_ExEmployees_IsEligibleForRehire DEFAULT (1),
        CreatedAt           DATETIME2         NOT NULL CONSTRAINT DF_ExEmployees_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;
ELSE
BEGIN
    IF COL_LENGTH('dbo.ExEmployees', 'CitizenId') IS NULL ALTER TABLE dbo.ExEmployees ADD CitizenId NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'PhoneNormalized') IS NULL ALTER TABLE dbo.ExEmployees ADD PhoneNormalized NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'DepartmentName') IS NULL ALTER TABLE dbo.ExEmployees ADD DepartmentName NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'ResignedDate') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignedDate DATE NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'ResignationReason') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignationReason NVARCHAR(255) NULL;
END;

-- =========================================================================================
-- 6. BẢNG SlaStageConfiguration
-- =========================================================================================
IF OBJECT_ID('dbo.SlaStageConfiguration', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SlaStageConfiguration
    (
        SlaStageConfigId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SlaStageConfiguration PRIMARY KEY DEFAULT NEWID(),
        StageCode         NVARCHAR(100)    NOT NULL CONSTRAINT UQ_SlaStageConfiguration_StageCode UNIQUE,
        StageName         NVARCHAR(200)    NOT NULL,
        SlaMaxDays        INT              NOT NULL CONSTRAINT DF_SlaStageConfiguration_SlaMaxDays DEFAULT 7,
        SlaWarnBeforeDays INT              NOT NULL CONSTRAINT DF_SlaStageConfiguration_SlaWarnBeforeDays DEFAULT 2,
        IsActive          BIT              NOT NULL CONSTRAINT DF_SlaStageConfiguration_IsActive DEFAULT 1,
        OrderSequence     INT              NOT NULL CONSTRAINT DF_SlaStageConfiguration_OrderSequence DEFAULT 0,
        CreatedAt         DATETIME2        NOT NULL CONSTRAINT DF_SlaStageConfiguration_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt         DATETIME2        NULL,
        CreatedBy         UNIQUEIDENTIFIER NULL
    );
END;

-- =========================================================================================
-- 7. BẢNG Users
-- =========================================================================================
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Users', 'NormalizedEmail') IS NULL ALTER TABLE dbo.Users ADD NormalizedEmail NVARCHAR(320) NULL;
    IF COL_LENGTH('dbo.Users', 'AvatarUrl') IS NULL ALTER TABLE dbo.Users ADD AvatarUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Users', 'AuthProvider') IS NULL ALTER TABLE dbo.Users ADD AuthProvider NVARCHAR(50) NULL DEFAULT ('LOCAL');
    IF COL_LENGTH('dbo.Users', 'DepartmentId') IS NULL ALTER TABLE dbo.Users ADD DepartmentId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Users', 'ManagerId') IS NULL ALTER TABLE dbo.Users ADD ManagerId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Users', 'LockedAt') IS NULL ALTER TABLE dbo.Users ADD LockedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Users', 'LockedById') IS NULL ALTER TABLE dbo.Users ADD LockedById UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Users', 'LockedByName') IS NULL ALTER TABLE dbo.Users ADD LockedByName NVARCHAR(255) NULL;
    IF COL_LENGTH('dbo.Users', 'LockReason') IS NULL ALTER TABLE dbo.Users ADD LockReason NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyName') IS NULL ALTER TABLE dbo.Users ADD CompanyName NVARCHAR(255) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyWebsite') IS NULL ALTER TABLE dbo.Users ADD CompanyWebsite NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyIndustry') IS NULL ALTER TABLE dbo.Users ADD CompanyIndustry NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyAddress') IS NULL ALTER TABLE dbo.Users ADD CompanyAddress NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyDescription') IS NULL ALTER TABLE dbo.Users ADD CompanyDescription NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Users', 'CompanyLogoUrl') IS NULL ALTER TABLE dbo.Users ADD CompanyLogoUrl NVARCHAR(500) NULL;
END;

-- =========================================================================================
-- 8. BẢNG Candidates
-- =========================================================================================
IF OBJECT_ID('dbo.Candidates', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Candidates', 'NormalizedEmail') IS NULL ALTER TABLE dbo.Candidates ADD NormalizedEmail NVARCHAR(320) NULL;
    IF COL_LENGTH('dbo.Candidates', 'Location') IS NULL ALTER TABLE dbo.Candidates ADD Location NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Candidates', 'Source') IS NULL ALTER TABLE dbo.Candidates ADD Source NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Candidates', 'LinkedIn') IS NULL ALTER TABLE dbo.Candidates ADD LinkedIn NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Candidates', 'GitHub') IS NULL ALTER TABLE dbo.Candidates ADD GitHub NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Candidates', 'Avatar') IS NULL ALTER TABLE dbo.Candidates ADD Avatar NVARCHAR(500) NULL;
    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints dc
        INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.Candidates') AND c.name = 'IsDeleted'
    )
    BEGIN
        ALTER TABLE dbo.Candidates ADD CONSTRAINT DF_Candidates_IsDeleted_Sync DEFAULT (0) FOR IsDeleted;
    END
END;

-- =========================================================================================
-- 9. BẢNG Jobs
-- =========================================================================================
IF OBJECT_ID('dbo.Jobs', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Jobs', 'DepartmentId') IS NULL ALTER TABLE dbo.Jobs ADD DepartmentId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Jobs', 'ClosedById') IS NULL ALTER TABLE dbo.Jobs ADD ClosedById UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Jobs', 'ClosedByName') IS NULL ALTER TABLE dbo.Jobs ADD ClosedByName NVARCHAR(255) NULL;
    IF COL_LENGTH('dbo.Jobs', 'CampaignFormSource') IS NULL ALTER TABLE dbo.Jobs ADD CampaignFormSource NVARCHAR(30) NULL;
    IF COL_LENGTH('dbo.Jobs', 'GoogleFormId') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormId NVARCHAR(128) NULL;
    IF COL_LENGTH('dbo.Jobs', 'GoogleFormPublicUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormPublicUrl NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.Jobs', 'GoogleFormEditUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormEditUrl NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.Jobs', 'QrCodeImagePath') IS NULL ALTER TABLE dbo.Jobs ADD QrCodeImagePath NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Jobs', 'RecruitmentTrack') IS NULL ALTER TABLE dbo.Jobs ADD RecruitmentTrack NVARCHAR(30) NOT NULL DEFAULT ('PROFESSIONAL');
    IF COL_LENGTH('dbo.Jobs', 'TargetQuantity') IS NULL ALTER TABLE dbo.Jobs ADD TargetQuantity INT NOT NULL DEFAULT (50);
    IF COL_LENGTH('dbo.Jobs', 'HiredCount') IS NULL ALTER TABLE dbo.Jobs ADD HiredCount INT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Jobs', 'WaitlistBufferPercent') IS NULL ALTER TABLE dbo.Jobs ADD WaitlistBufferPercent INT NOT NULL DEFAULT (15);
    IF COL_LENGTH('dbo.Jobs', 'CurrentScoringWeightVersion') IS NULL ALTER TABLE dbo.Jobs ADD CurrentScoringWeightVersion INT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Jobs', 'GoogleFormUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Jobs', 'RequisitionStatus') IS NULL ALTER TABLE dbo.Jobs ADD RequisitionStatus NVARCHAR(30) NOT NULL DEFAULT ('Approved');
    IF COL_LENGTH('dbo.Jobs', 'RequestedByUserId') IS NULL ALTER TABLE dbo.Jobs ADD RequestedByUserId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Jobs', 'ApprovedByUserId') IS NULL ALTER TABLE dbo.Jobs ADD ApprovedByUserId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Jobs', 'ApprovedAt') IS NULL ALTER TABLE dbo.Jobs ADD ApprovedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Jobs', 'RequisitionNote') IS NULL ALTER TABLE dbo.Jobs ADD RequisitionNote NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Jobs', 'RequireNightShift') IS NULL ALTER TABLE dbo.Jobs ADD RequireNightShift BIT NOT NULL DEFAULT (0);
END;

-- =========================================================================================
-- 10. BẢNG Applications
-- =========================================================================================
IF OBJECT_ID('dbo.Applications', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Applications', 'InterviewBatchId') IS NULL ALTER TABLE dbo.Applications ADD InterviewBatchId INT NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactName') IS NULL ALTER TABLE dbo.Applications ADD ContactName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactEmail') IS NULL ALTER TABLE dbo.Applications ADD ContactEmail NVARCHAR(320) NULL;
    IF COL_LENGTH('dbo.Applications', 'ContactPhone') IS NULL ALTER TABLE dbo.Applications ADD ContactPhone NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'LastViewedAt') IS NULL ALTER TABLE dbo.Applications ADD LastViewedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Applications', 'LastViewedBy') IS NULL ALTER TABLE dbo.Applications ADD LastViewedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Applications', 'ApplicationSource') IS NULL ALTER TABLE dbo.Applications ADD ApplicationSource NVARCHAR(30) NOT NULL DEFAULT ('PDF_UPLOAD');
    IF COL_LENGTH('dbo.Applications', 'ScoringMethod') IS NULL ALTER TABLE dbo.Applications ADD ScoringMethod NVARCHAR(30) NOT NULL DEFAULT ('GEMINI_AI');
    IF COL_LENGTH('dbo.Applications', 'CvFileUrl') IS NULL ALTER TABLE dbo.Applications ADD CvFileUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'CitizenId') IS NULL ALTER TABLE dbo.Applications ADD CitizenId NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.Applications', 'PhoneNormalized') IS NULL ALTER TABLE dbo.Applications ADD PhoneNormalized NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.Applications', 'DuplicateCount') IS NULL ALTER TABLE dbo.Applications ADD DuplicateCount INT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Applications', 'PreviousApplicationId') IS NULL ALTER TABLE dbo.Applications ADD PreviousApplicationId INT NULL;
    IF COL_LENGTH('dbo.Applications', 'IsBlacklisted') IS NULL ALTER TABLE dbo.Applications ADD IsBlacklisted BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'BlacklistWarningNote') IS NULL ALTER TABLE dbo.Applications ADD BlacklistWarningNote NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'IsScannedOrUnparseablePdf') IS NULL ALTER TABLE dbo.Applications ADD IsScannedOrUnparseablePdf BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'RawResumeText') IS NULL ALTER TABLE dbo.Applications ADD RawResumeText NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Applications', 'AiAnalysisSummary') IS NULL ALTER TABLE dbo.Applications ADD AiAnalysisSummary NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Applications', 'AppliedScoringVersion') IS NULL ALTER TABLE dbo.Applications ADD AppliedScoringVersion INT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Applications', 'AppliedScoringConfigJson') IS NULL ALTER TABLE dbo.Applications ADD AppliedScoringConfigJson NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Applications', 'OralInterviewChecklistJson') IS NULL ALTER TABLE dbo.Applications ADD OralInterviewChecklistJson NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.Applications', 'MedicalCheckStatus') IS NULL ALTER TABLE dbo.Applications ADD MedicalCheckStatus NVARCHAR(30) NOT NULL DEFAULT ('Pending');
    IF COL_LENGTH('dbo.Applications', 'BankAccountNumber') IS NULL ALTER TABLE dbo.Applications ADD BankAccountNumber NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'BankName') IS NULL ALTER TABLE dbo.Applications ADD BankName NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Applications', 'OnboardingDocsCompleted') IS NULL ALTER TABLE dbo.Applications ADD OnboardingDocsCompleted BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'ConvertedEmployeeCode') IS NULL ALTER TABLE dbo.Applications ADD ConvertedEmployeeCode NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'SlaDeadlineAt') IS NULL ALTER TABLE dbo.Applications ADD SlaDeadlineAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Applications', 'IsSlaEscalated') IS NULL ALTER TABLE dbo.Applications ADD IsSlaEscalated BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'SlaOverrideReason') IS NULL ALTER TABLE dbo.Applications ADD SlaOverrideReason NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'DateOfBirth') IS NULL ALTER TABLE dbo.Applications ADD DateOfBirth DATE NULL;
    IF COL_LENGTH('dbo.Applications', 'IsPiiMaskedBeforeAi') IS NULL ALTER TABLE dbo.Applications ADD IsPiiMaskedBeforeAi BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.Applications', 'InterviewScore') IS NULL ALTER TABLE dbo.Applications ADD InterviewScore DECIMAL(5,2) NULL;
    IF COL_LENGTH('dbo.Applications', 'IsAiOverriddenByHuman') IS NULL ALTER TABLE dbo.Applications ADD IsAiOverriddenByHuman BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'AiOverrideReason') IS NULL ALTER TABLE dbo.Applications ADD AiOverrideReason NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'OfferStatus') IS NULL ALTER TABLE dbo.Applications ADD OfferStatus NVARCHAR(30) NOT NULL DEFAULT ('Not_Required');
    IF COL_LENGTH('dbo.Applications', 'OfferedSalary') IS NULL ALTER TABLE dbo.Applications ADD OfferedSalary DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.Applications', 'OfferResponseNote') IS NULL ALTER TABLE dbo.Applications ADD OfferResponseNote NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'TransferredFromJobId') IS NULL ALTER TABLE dbo.Applications ADD TransferredFromJobId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Applications', 'TransferReason') IS NULL ALTER TABLE dbo.Applications ADD TransferReason NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Applications', 'IsExEmployeeRehire') IS NULL ALTER TABLE dbo.Applications ADD IsExEmployeeRehire BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'PreviousEmployeeCode') IS NULL ALTER TABLE dbo.Applications ADD PreviousEmployeeCode NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.Applications', 'InterviewRescheduleCount') IS NULL ALTER TABLE dbo.Applications ADD InterviewRescheduleCount INT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'OfferExpiresAt') IS NULL ALTER TABLE dbo.Applications ADD OfferExpiresAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Applications', 'IsInTalentPool') IS NULL ALTER TABLE dbo.Applications ADD IsInTalentPool BIT NOT NULL DEFAULT (0);
    IF COL_LENGTH('dbo.Applications', 'Probation30DayStatus') IS NULL ALTER TABLE dbo.Applications ADD Probation30DayStatus NVARCHAR(30) NULL DEFAULT ('Not_Started');
    IF COL_LENGTH('dbo.Applications', 'RowVersion') IS NULL ALTER TABLE dbo.Applications ADD RowVersion ROWVERSION;
END;

-- =========================================================================================
-- 11. BẢNG PHỤ TRỢ (Notes, History, Documents, Files, Interviews, Feedback...)
-- =========================================================================================
IF OBJECT_ID('dbo.ApplicationNotes', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationNotes ALTER COLUMN CreatedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.ApplicationNotes', 'Note') IS NULL AND COL_LENGTH('dbo.ApplicationNotes', 'Content') IS NOT NULL
        EXEC sp_rename 'dbo.ApplicationNotes.Content', 'Note', 'COLUMN';
    IF COL_LENGTH('dbo.ApplicationNotes', 'Note') IS NULL
        ALTER TABLE dbo.ApplicationNotes ADD Note NVARCHAR(MAX) NULL;
END;

IF OBJECT_ID('dbo.ApplicationStageHistory', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationStageHistory ALTER COLUMN ChangedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.ApplicationStageHistory', 'ChangedAt') IS NULL ALTER TABLE dbo.ApplicationStageHistory ADD ChangedAt DATETIME2 NOT NULL CONSTRAINT DF_AppStageHist_ChangedAt_V6 DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.ApplicationStageHistory', 'Note') IS NULL ALTER TABLE dbo.ApplicationStageHistory ADD Note NVARCHAR(1000) NULL;
END;

IF OBJECT_ID('dbo.CandidateDocuments', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CandidateDocuments', 'CandidateDocumentId') IS NULL AND COL_LENGTH('dbo.CandidateDocuments', 'DocumentId') IS NOT NULL
        EXEC sp_rename 'dbo.CandidateDocuments.DocumentId', 'CandidateDocumentId', 'COLUMN';
    IF COL_LENGTH('dbo.CandidateDocuments', 'DocType') IS NULL AND COL_LENGTH('dbo.CandidateDocuments', 'DocumentType') IS NOT NULL
        EXEC sp_rename 'dbo.CandidateDocuments.DocumentType', 'DocType', 'COLUMN';
    IF COL_LENGTH('dbo.CandidateDocuments', 'Title') IS NULL ALTER TABLE dbo.CandidateDocuments ADD Title NVARCHAR(250) NULL;
    IF COL_LENGTH('dbo.CandidateDocuments', 'ParsedJson') IS NULL ALTER TABLE dbo.CandidateDocuments ADD ParsedJson NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.CandidateDocuments', 'DisplayName') IS NULL ALTER TABLE dbo.CandidateDocuments ADD DisplayName NVARCHAR(255) NULL;
END;

IF OBJECT_ID('dbo.CandidateEducations', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CandidateEducations', 'School') IS NULL ALTER TABLE dbo.CandidateEducations ADD School NVARCHAR(250) NULL;
    IF COL_LENGTH('dbo.CandidateEducations', 'Field') IS NULL ALTER TABLE dbo.CandidateEducations ADD Field NVARCHAR(200) NULL;
END;

IF OBJECT_ID('dbo.CandidateExperiences', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CandidateExperiences', 'Company') IS NULL ALTER TABLE dbo.CandidateExperiences ADD Company NVARCHAR(250) NULL;
    IF COL_LENGTH('dbo.CandidateExperiences', 'Title') IS NULL ALTER TABLE dbo.CandidateExperiences ADD Title NVARCHAR(100) NULL;
END;

IF OBJECT_ID('dbo.CandidateSkills', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CandidateSkills', 'Level') IS NULL ALTER TABLE dbo.CandidateSkills ADD Level TINYINT NULL;
    IF COL_LENGTH('dbo.CandidateSkills', 'Years') IS NULL ALTER TABLE dbo.CandidateSkills ADD Years DECIMAL(4,1) NULL;
END;

IF OBJECT_ID('dbo.Files', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Files', 'Provider') IS NULL ALTER TABLE dbo.Files ADD Provider NVARCHAR(50) NOT NULL CONSTRAINT DF_Files_Provider_V6 DEFAULT ('LOCAL');
    IF COL_LENGTH('dbo.Files', 'OriginalFileName') IS NULL ALTER TABLE dbo.Files ADD OriginalFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_OriginalFileName_V6 DEFAULT ('cv.pdf');
    IF COL_LENGTH('dbo.Files', 'StoredFileName') IS NULL ALTER TABLE dbo.Files ADD StoredFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_StoredFileName_V6 DEFAULT ('cv.pdf');
    IF COL_LENGTH('dbo.Files', 'SizeBytes') IS NULL ALTER TABLE dbo.Files ADD SizeBytes BIGINT NOT NULL CONSTRAINT DF_Files_SizeBytes_V6 DEFAULT (0);
    IF COL_LENGTH('dbo.Files', 'Sha256') IS NULL ALTER TABLE dbo.Files ADD Sha256 CHAR(64) NULL;
    IF COL_LENGTH('dbo.Files', 'Url') IS NULL ALTER TABLE dbo.Files ADD Url NVARCHAR(1000) NOT NULL CONSTRAINT DF_Files_Url_V6 DEFAULT ('');
END;

IF OBJECT_ID('dbo.Interviewers', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Interviewers', 'Id') IS NULL ALTER TABLE dbo.Interviewers ADD Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Interviewers_Id_V6 DEFAULT (NEWID());
    IF COL_LENGTH('dbo.Interviewers', 'ResponseAt') IS NULL ALTER TABLE dbo.Interviewers ADD ResponseAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Interviewers', 'IsPrimary') IS NULL ALTER TABLE dbo.Interviewers ADD IsPrimary BIT NOT NULL CONSTRAINT DF_Interviewers_IsPrimary_V6 DEFAULT (0);
END;

IF OBJECT_ID('dbo.InterviewFeedback', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewFeedback', 'FeedbackId') IS NULL AND COL_LENGTH('dbo.InterviewFeedback', 'InterviewFeedbackId') IS NOT NULL
        EXEC sp_rename 'dbo.InterviewFeedback.InterviewFeedbackId', 'FeedbackId', 'COLUMN';
    IF COL_LENGTH('dbo.InterviewFeedback', 'OverallRating') IS NULL ALTER TABLE dbo.InterviewFeedback ADD OverallRating INT NOT NULL CONSTRAINT DF_IntFeedback_OverallRating_V6 DEFAULT (3);
    IF COL_LENGTH('dbo.InterviewFeedback', 'Recommendation') IS NULL ALTER TABLE dbo.InterviewFeedback ADD Recommendation NVARCHAR(50) NOT NULL CONSTRAINT DF_IntFeedback_Recommendation_V6 DEFAULT ('Hire');
    IF COL_LENGTH('dbo.InterviewFeedback', 'Summary') IS NULL ALTER TABLE dbo.InterviewFeedback ADD Summary NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewFeedback', 'RedFlags') IS NULL ALTER TABLE dbo.InterviewFeedback ADD RedFlags NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewFeedback', 'SubmittedAt') IS NULL ALTER TABLE dbo.InterviewFeedback ADD SubmittedAt DATETIME2 NOT NULL CONSTRAINT DF_IntFeedback_SubmittedAt_V6 DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.InterviewFeedback', 'UpdatedAt') IS NULL ALTER TABLE dbo.InterviewFeedback ADD UpdatedAt DATETIME2 NULL;
END;

IF OBJECT_ID('dbo.InterviewEvaluations', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewEvaluations', 'EvaluationId') IS NULL AND COL_LENGTH('dbo.InterviewEvaluations', 'InterviewEvaluationId') IS NOT NULL
        EXEC sp_rename 'dbo.InterviewEvaluations.InterviewEvaluationId', 'EvaluationId', 'COLUMN';
    IF COL_LENGTH('dbo.InterviewEvaluations', 'CriterionName') IS NOT NULL
        ALTER TABLE dbo.InterviewEvaluations ALTER COLUMN CriterionName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'InterviewerId') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD InterviewerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_IntEval_InterviewerId_V6 DEFAULT ('30000000-0000-0000-0000-000000000020');
    IF COL_LENGTH('dbo.InterviewEvaluations', 'SubmittedById') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD SubmittedById UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Comment') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Comment NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Result') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Result NVARCHAR(20) NOT NULL CONSTRAINT DF_IntEval_Result_V6 DEFAULT ('Passed');
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Details') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Details NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'CreatedAt') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_IntEval_CreatedAt_V6 DEFAULT (SYSUTCDATETIME());
END;

IF OBJECT_ID('dbo.NotificationSettings', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationSettings') AND name = 'SettingId')
        EXEC sp_rename 'dbo.NotificationSettings.SettingId', 'Id', 'COLUMN';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationSettings') AND name = 'EmailNotifications')
        EXEC sp_rename 'dbo.NotificationSettings.EmailNotifications', 'ChannelEmail', 'COLUMN';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationSettings') AND name = 'PushNotifications')
        EXEC sp_rename 'dbo.NotificationSettings.PushNotifications', 'ChannelPush', 'COLUMN';
    IF COL_LENGTH('dbo.NotificationSettings', 'ChannelEmail') IS NULL ALTER TABLE dbo.NotificationSettings ADD ChannelEmail BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.NotificationSettings', 'ChannelPush') IS NULL ALTER TABLE dbo.NotificationSettings ADD ChannelPush BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.NotificationSettings', 'NotifyJobOpportunities') IS NULL ALTER TABLE dbo.NotificationSettings ADD NotifyJobOpportunities BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.NotificationSettings', 'NotifyApplicationUpdates') IS NULL ALTER TABLE dbo.NotificationSettings ADD NotifyApplicationUpdates BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.NotificationSettings', 'NotifySecurityAlerts') IS NULL ALTER TABLE dbo.NotificationSettings ADD NotifySecurityAlerts BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.NotificationSettings', 'NotifyMarketing') IS NULL ALTER TABLE dbo.NotificationSettings ADD NotifyMarketing BIT NOT NULL DEFAULT (0);
END;

IF OBJECT_ID('dbo.Notifications', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notifications') AND name = 'NotificationId')
        EXEC sp_rename 'dbo.Notifications.NotificationId', 'Id', 'COLUMN';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notifications') AND name = 'Content')
        EXEC sp_rename 'dbo.Notifications.Content', 'Message', 'COLUMN';
    IF COL_LENGTH('dbo.Notifications', 'Type') IS NULL ALTER TABLE dbo.Notifications ADD Type NVARCHAR(100) NOT NULL CONSTRAINT DF_Notifications_Type_V6 DEFAULT ('SYSTEM');
    IF COL_LENGTH('dbo.Notifications', 'ReferenceId') IS NULL ALTER TABLE dbo.Notifications ADD ReferenceId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Notifications', 'RelatedId') IS NULL ALTER TABLE dbo.Notifications ADD RelatedId NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Notifications', 'UpdatedAt') IS NULL ALTER TABLE dbo.Notifications ADD UpdatedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActionUrl') IS NULL ALTER TABLE dbo.Notifications ADD ActionUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActorId') IS NULL ALTER TABLE dbo.Notifications ADD ActorId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActorName') IS NULL ALTER TABLE dbo.Notifications ADD ActorName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Notifications', 'MetadataJson') IS NULL ALTER TABLE dbo.Notifications ADD MetadataJson NVARCHAR(MAX) NULL;
END;

IF OBJECT_ID('dbo.PipelineStages', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PipelineStages', 'IsSlaEnabled') IS NULL ALTER TABLE dbo.PipelineStages ADD IsSlaEnabled BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.PipelineStages', 'SlaMaxDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaMaxDays INT NULL DEFAULT (3);
    IF COL_LENGTH('dbo.PipelineStages', 'SlaWarnBeforeDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaWarnBeforeDays INT NULL DEFAULT (1);
END;

IF OBJECT_ID('dbo.ApplicationAiScores', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ApplicationAiScores', 'BreakdownJson') IS NULL ALTER TABLE dbo.ApplicationAiScores ADD BreakdownJson NVARCHAR(MAX) NULL;
END;

IF OBJECT_ID('dbo.Interviews', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Interviews', 'Title') IS NULL ALTER TABLE dbo.Interviews ADD Title NVARCHAR(250) NULL;
    IF COL_LENGTH('dbo.Interviews', 'InterviewerId') IS NULL ALTER TABLE dbo.Interviews ADD InterviewerId UNIQUEIDENTIFIER NULL;
END;

IF OBJECT_ID('dbo.Assessments', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Assessments', 'RandomQuestionCount') IS NULL ALTER TABLE dbo.Assessments ADD RandomQuestionCount INT NULL;
    IF COL_LENGTH('dbo.Assessments', 'RandomTags') IS NULL ALTER TABLE dbo.Assessments ADD RandomTags NVARCHAR(500) NULL;
END;

-- =========================================================================================
-- 12. TẠO INDEXES TỐI ƯU TRA CỨU
-- =========================================================================================
IF OBJECT_ID('dbo.Applications', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Applications_CitizenId' AND object_id = OBJECT_ID('dbo.Applications'))
        CREATE INDEX IX_Applications_CitizenId ON dbo.Applications(CitizenId) WHERE CitizenId IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Applications_PhoneNormalized' AND object_id = OBJECT_ID('dbo.Applications'))
        CREATE INDEX IX_Applications_PhoneNormalized ON dbo.Applications(PhoneNormalized) WHERE PhoneNormalized IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Applications_ConvertedEmployeeCode' AND object_id = OBJECT_ID('dbo.Applications'))
        CREATE UNIQUE INDEX UX_Applications_ConvertedEmployeeCode ON dbo.Applications(ConvertedEmployeeCode) WHERE ConvertedEmployeeCode IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Applications_OfferStatus' AND object_id = OBJECT_ID('dbo.Applications'))
        CREATE INDEX IX_Applications_OfferStatus ON dbo.Applications(OfferStatus);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Applications_IsInTalentPool' AND object_id = OBJECT_ID('dbo.Applications'))
        CREATE INDEX IX_Applications_IsInTalentPool ON dbo.Applications(IsInTalentPool) WHERE IsInTalentPool = 1;
END;

IF OBJECT_ID('dbo.Jobs', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Jobs_RequisitionStatus' AND object_id = OBJECT_ID('dbo.Jobs'))
        CREATE INDEX IX_Jobs_RequisitionStatus ON dbo.Jobs(RequisitionStatus);
END;

-- =========================================================================================
-- 13. ĐỒNG BỘ SỐ LƯỢNG TRÚNG TUYỂN THỰC TẾ
-- =========================================================================================
UPDATE j
SET j.HiredCount = ISNULL((
    SELECT COUNT(1)
    FROM dbo.Applications a
    WHERE a.JobId = j.JobId
      AND (a.Status = 'Hired' OR a.ConvertedEmployeeCode IS NOT NULL)
), 0)
FROM dbo.Jobs j;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
