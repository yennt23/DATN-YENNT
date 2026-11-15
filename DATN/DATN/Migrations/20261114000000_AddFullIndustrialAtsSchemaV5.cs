using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DATN.Data;

#nullable disable

namespace DATN.Migrations
{
    [DbContext(typeof(DATNContext))]
    [Migration("20261114000000_AddFullIndustrialAtsSchemaV5")]
    public partial class AddFullIndustrialAtsSchemaV5 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
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

IF OBJECT_ID('dbo.InterviewBatches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InterviewBatches
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewBatches PRIMARY KEY,
        JobId             UNIQUEIDENTIFIER  NULL,
        BatchCode         NVARCHAR(50)      NOT NULL,
        ScheduledDate     DATE              NOT NULL,
        ShiftSlot         NVARCHAR(30)      NOT NULL,
        MaxCapacity       INT               NOT NULL CONSTRAINT DF_InterviewBatches_MaxCapacity DEFAULT (50),
        CurrentCount      INT               NOT NULL CONSTRAINT DF_InterviewBatches_CurrentCount DEFAULT (0),
        InterviewerUserId UNIQUEIDENTIFIER  NULL,
        CreatedAt         DATETIME2         NOT NULL CONSTRAINT DF_InterviewBatches_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;

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

IF OBJECT_ID('dbo.Applications', 'U') IS NOT NULL
BEGIN
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
    IF COL_LENGTH('dbo.Applications', 'InterviewBatchId') IS NULL ALTER TABLE dbo.Applications ADD InterviewBatchId INT NULL;
    IF COL_LENGTH('dbo.Applications', 'RowVersion') IS NULL ALTER TABLE dbo.Applications ADD RowVersion ROWVERSION;
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

IF OBJECT_ID('dbo.PipelineStages', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PipelineStages', 'IsSlaEnabled') IS NULL ALTER TABLE dbo.PipelineStages ADD IsSlaEnabled BIT NOT NULL DEFAULT (1);
    IF COL_LENGTH('dbo.PipelineStages', 'SlaMaxDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaMaxDays INT NULL DEFAULT (3);
    IF COL_LENGTH('dbo.PipelineStages', 'SlaWarnBeforeDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaWarnBeforeDays INT NULL DEFAULT (1);
END;

IF OBJECT_ID('dbo.ApplicationNotes', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationNotes ALTER COLUMN CreatedBy UNIQUEIDENTIFIER NULL;
END;

IF OBJECT_ID('dbo.ApplicationStageHistory', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationStageHistory ALTER COLUMN ChangedBy UNIQUEIDENTIFIER NULL;
END;

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
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
