using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DATN.Data;

#nullable disable

namespace DATN.Migrations
{
    [DbContext(typeof(DATNContext))]
    [Migration("20261115103000_SyncFullAtsOperationsAndOnboardingWorkflow")]
    public partial class SyncFullAtsOperationsAndOnboardingWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1. CandidateDocuments
IF OBJECT_ID('dbo.CandidateDocuments', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CandidateDocuments', 'CandidateDocumentId') IS NULL AND COL_LENGTH('dbo.CandidateDocuments', 'DocumentId') IS NOT NULL
        EXEC sp_rename 'dbo.CandidateDocuments.DocumentId', 'CandidateDocumentId', 'COLUMN';
    IF COL_LENGTH('dbo.CandidateDocuments', 'DocType') IS NULL AND COL_LENGTH('dbo.CandidateDocuments', 'DocumentType') IS NOT NULL
        EXEC sp_rename 'dbo.CandidateDocuments.DocumentType', 'DocType', 'COLUMN';
    IF COL_LENGTH('dbo.CandidateDocuments', 'Title') IS NULL
        ALTER TABLE dbo.CandidateDocuments ADD Title NVARCHAR(250) NULL;
END;

-- 2. Files
IF OBJECT_ID('dbo.Files', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Files', 'Provider') IS NULL ALTER TABLE dbo.Files ADD Provider NVARCHAR(50) NOT NULL CONSTRAINT DF_Files_Provider DEFAULT ('LOCAL');
    IF COL_LENGTH('dbo.Files', 'OriginalFileName') IS NULL ALTER TABLE dbo.Files ADD OriginalFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_OriginalFileName DEFAULT ('cv.pdf');
    IF COL_LENGTH('dbo.Files', 'StoredFileName') IS NULL ALTER TABLE dbo.Files ADD StoredFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_StoredFileName DEFAULT ('cv.pdf');
    IF COL_LENGTH('dbo.Files', 'SizeBytes') IS NULL ALTER TABLE dbo.Files ADD SizeBytes BIGINT NOT NULL CONSTRAINT DF_Files_SizeBytes DEFAULT (0);
    IF COL_LENGTH('dbo.Files', 'Sha256') IS NULL ALTER TABLE dbo.Files ADD Sha256 CHAR(64) NULL;
    IF COL_LENGTH('dbo.Files', 'Url') IS NULL ALTER TABLE dbo.Files ADD Url NVARCHAR(1000) NOT NULL CONSTRAINT DF_Files_Url DEFAULT ('');
END;

-- 3. ApplicationStageHistory
IF OBJECT_ID('dbo.ApplicationStageHistory', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ApplicationStageHistory ALTER COLUMN ChangedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.ApplicationStageHistory', 'ChangedAt') IS NULL ALTER TABLE dbo.ApplicationStageHistory ADD ChangedAt DATETIME2 NOT NULL CONSTRAINT DF_AppStageHist_ChangedAt DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.ApplicationStageHistory', 'Note') IS NULL ALTER TABLE dbo.ApplicationStageHistory ADD Note NVARCHAR(1000) NULL;
END;

-- 4. InterviewBatches
IF OBJECT_ID('dbo.InterviewBatches', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewBatches', 'JobId') IS NULL ALTER TABLE dbo.InterviewBatches ADD JobId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.InterviewBatches', 'InterviewerUserId') IS NULL ALTER TABLE dbo.InterviewBatches ADD InterviewerUserId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.InterviewBatches', 'ShiftName') IS NOT NULL
        ALTER TABLE dbo.InterviewBatches ALTER COLUMN ShiftName NVARCHAR(100) NULL;
END;

-- 5. ExEmployees
IF OBJECT_ID('dbo.ExEmployees', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ExEmployees', 'CitizenId') IS NULL ALTER TABLE dbo.ExEmployees ADD CitizenId NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'PhoneNormalized') IS NULL ALTER TABLE dbo.ExEmployees ADD PhoneNormalized NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'DepartmentName') IS NULL ALTER TABLE dbo.ExEmployees ADD DepartmentName NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'ResignedDate') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignedDate DATE NULL;
    IF COL_LENGTH('dbo.ExEmployees', 'ResignationReason') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignationReason NVARCHAR(255) NULL;
END;

-- 6. Interviewers
IF OBJECT_ID('dbo.Interviewers', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Interviewers', 'Id') IS NULL ALTER TABLE dbo.Interviewers ADD Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Interviewers_Id DEFAULT (NEWID());
    IF COL_LENGTH('dbo.Interviewers', 'ResponseAt') IS NULL ALTER TABLE dbo.Interviewers ADD ResponseAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Interviewers', 'IsPrimary') IS NULL ALTER TABLE dbo.Interviewers ADD IsPrimary BIT NOT NULL CONSTRAINT DF_Interviewers_IsPrimary DEFAULT (0);
END;

-- 7. InterviewFeedback
IF OBJECT_ID('dbo.InterviewFeedback', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewFeedback', 'FeedbackId') IS NULL AND COL_LENGTH('dbo.InterviewFeedback', 'InterviewFeedbackId') IS NOT NULL
        EXEC sp_rename 'dbo.InterviewFeedback.InterviewFeedbackId', 'FeedbackId', 'COLUMN';
    IF COL_LENGTH('dbo.InterviewFeedback', 'OverallRating') IS NULL ALTER TABLE dbo.InterviewFeedback ADD OverallRating INT NOT NULL CONSTRAINT DF_IntFeedback_OverallRating DEFAULT (3);
    IF COL_LENGTH('dbo.InterviewFeedback', 'Recommendation') IS NULL ALTER TABLE dbo.InterviewFeedback ADD Recommendation NVARCHAR(50) NOT NULL CONSTRAINT DF_IntFeedback_Recommendation DEFAULT ('Hire');
    IF COL_LENGTH('dbo.InterviewFeedback', 'Summary') IS NULL ALTER TABLE dbo.InterviewFeedback ADD Summary NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewFeedback', 'RedFlags') IS NULL ALTER TABLE dbo.InterviewFeedback ADD RedFlags NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewFeedback', 'SubmittedAt') IS NULL ALTER TABLE dbo.InterviewFeedback ADD SubmittedAt DATETIME2 NOT NULL CONSTRAINT DF_IntFeedback_SubmittedAt DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.InterviewFeedback', 'UpdatedAt') IS NULL ALTER TABLE dbo.InterviewFeedback ADD UpdatedAt DATETIME2 NULL;
END;

-- 8. InterviewEvaluations
IF OBJECT_ID('dbo.InterviewEvaluations', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InterviewEvaluations', 'EvaluationId') IS NULL AND COL_LENGTH('dbo.InterviewEvaluations', 'InterviewEvaluationId') IS NOT NULL
        EXEC sp_rename 'dbo.InterviewEvaluations.InterviewEvaluationId', 'EvaluationId', 'COLUMN';
    IF COL_LENGTH('dbo.InterviewEvaluations', 'CriterionName') IS NOT NULL
        ALTER TABLE dbo.InterviewEvaluations ALTER COLUMN CriterionName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'InterviewerId') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD InterviewerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_IntEval_InterviewerId DEFAULT ('30000000-0000-0000-0000-000000000020');
    IF COL_LENGTH('dbo.InterviewEvaluations', 'SubmittedById') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD SubmittedById UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Comment') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Comment NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Result') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Result NVARCHAR(20) NOT NULL CONSTRAINT DF_IntEval_Result DEFAULT ('Passed');
    IF COL_LENGTH('dbo.InterviewEvaluations', 'Details') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD Details NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.InterviewEvaluations', 'CreatedAt') IS NULL ALTER TABLE dbo.InterviewEvaluations ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_IntEval_CreatedAt DEFAULT (SYSUTCDATETIME());
END;

-- 9. Notifications
IF OBJECT_ID('dbo.Notifications', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Notifications', 'Type') IS NULL ALTER TABLE dbo.Notifications ADD Type NVARCHAR(100) NOT NULL CONSTRAINT DF_Notifications_Type DEFAULT ('SYSTEM');
    IF COL_LENGTH('dbo.Notifications', 'ReferenceId') IS NULL ALTER TABLE dbo.Notifications ADD ReferenceId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Notifications', 'UpdatedAt') IS NULL ALTER TABLE dbo.Notifications ADD UpdatedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActionUrl') IS NULL ALTER TABLE dbo.Notifications ADD ActionUrl NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActorId') IS NULL ALTER TABLE dbo.Notifications ADD ActorId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.Notifications', 'ActorName') IS NULL ALTER TABLE dbo.Notifications ADD ActorName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Notifications', 'MetadataJson') IS NULL ALTER TABLE dbo.Notifications ADD MetadataJson NVARCHAR(MAX) NULL;
END;

-- 10. Skills, JobSkills, Tags, JobTags, RolePermissions, UserRoles
IF OBJECT_ID('dbo.Skills', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Skills', 'NormalizedName') IS NULL ALTER TABLE dbo.Skills ADD NormalizedName NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.Skills', 'CreatedAt') IS NULL ALTER TABLE dbo.Skills ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Skills_CreatedAt DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.Skills', 'UpdatedAt') IS NULL ALTER TABLE dbo.Skills ADD UpdatedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.Skills', 'IsDeleted') IS NULL ALTER TABLE dbo.Skills ADD IsDeleted BIT NOT NULL CONSTRAINT DF_Skills_IsDeleted DEFAULT (0);
END;

IF OBJECT_ID('dbo.JobSkills', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.JobSkills', 'IsRequired') IS NULL ALTER TABLE dbo.JobSkills ADD IsRequired BIT NOT NULL CONSTRAINT DF_JobSkills_IsRequired DEFAULT (1);
    IF COL_LENGTH('dbo.JobSkills', 'Weight') IS NULL ALTER TABLE dbo.JobSkills ADD Weight INT NULL;
    IF COL_LENGTH('dbo.JobSkills', 'CreatedAt') IS NULL ALTER TABLE dbo.JobSkills ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_JobSkills_CreatedAt DEFAULT (SYSUTCDATETIME());
END;

IF OBJECT_ID('dbo.Tags', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Tags', 'NormalizedName') IS NULL ALTER TABLE dbo.Tags ADD NormalizedName NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Tags', 'CreatedAt') IS NULL ALTER TABLE dbo.Tags ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Tags_CreatedAt DEFAULT (SYSUTCDATETIME());
END;

IF OBJECT_ID('dbo.JobTags', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.JobTags', 'CreatedAt') IS NULL ALTER TABLE dbo.JobTags ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_JobTags_CreatedAt DEFAULT (SYSUTCDATETIME());
END;

IF OBJECT_ID('dbo.RolePermissions', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.RolePermissions', 'CreatedAt') IS NULL ALTER TABLE dbo.RolePermissions ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_RolePermissions_CreatedAt DEFAULT (SYSUTCDATETIME());
END;

IF OBJECT_ID('dbo.UserRoles', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.UserRoles', 'AssignedAt') IS NULL ALTER TABLE dbo.UserRoles ADD AssignedAt DATETIME2 NOT NULL CONSTRAINT DF_UserRoles_AssignedAt DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('dbo.UserRoles', 'AssignedBy') IS NULL ALTER TABLE dbo.UserRoles ADD AssignedBy UNIQUEIDENTIFIER NULL;
END;

-- 11. Sync Jobs.HiredCount from real hired applications
IF COL_LENGTH('dbo.Jobs', 'HiredCount') IS NOT NULL
BEGIN
    EXEC('
        UPDATE j
        SET j.HiredCount = ISNULL((
            SELECT COUNT(1)
            FROM dbo.Applications a
            WHERE a.JobId = j.JobId
              AND (a.Status = ''Hired'' OR (COL_LENGTH(''dbo.Applications'', ''ConvertedEmployeeCode'') IS NOT NULL AND a.ConvertedEmployeeCode IS NOT NULL))
        ), 0)
        FROM dbo.Jobs j;
    ');
END;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

