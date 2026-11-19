using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using DATN.Entities;

namespace DATN.Data
{
    public static class DbInitializer
    {
        private static bool _isInitialized = false;
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        public static async Task EnsureInitializedAsync(DATNContext context)
        {
            if (_isInitialized && await context.Applications.AnyAsync())
            {
                return;
            }

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized && await context.Applications.AnyAsync())
                {
                    return;
                }

                await InitializeAsync(context);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] EnsureInitializedAsync warning: {ex.Message}");
            }
            finally
            {
                _initLock.Release();
            }
        }

        public static async Task InitializeAsync(DATNContext context)
        {
            // 1. Tự động kiểm tra và build / cập nhật toàn bộ database schema (tạo bảng, thêm cột còn thiếu)
            Console.WriteLine("[DbInitializer] Đang kiểm tra và tự động cập nhật Schema Database (Tables & Columns)...");
            await EnsureFullIndustrialSchemaAsync(context);

            try
            {
                // 2. Đồng bộ cấu hình __EFMigrationsHistory cho database legacy nếu có
                await EnsureMigrationsHistoryConfiguredAsync(context);

                // 3. Tự động áp dụng EF Core Migrations khi Restart Backend
                Console.WriteLine("[DbInitializer] Đang kiểm tra và tự động áp dụng EF Core Migrations...");
                await context.Database.MigrateAsync();
                Console.WriteLine("[DbInitializer] ✓ EF Core Migrations đã được áp dụng thành công!");
            }
            catch (Exception migEx)
            {
                Console.WriteLine($"[DbInitializer] MigrateAsync warning: {migEx.Message}");
            }

            // Đảm bảo lại một lần nữa sau migration để chắc chắn 100% không cột nào bị sót
            await EnsureFullIndustrialSchemaAsync(context);

            // 4. Seed dữ liệu mặc định chuẩn bằng EF Core C# (Departments, Jobs, Skills, Users...)
            await SeedDepartmentsAsync(context);
            await SeedJobTypesAsync(context);
            await SeedSkillsAsync(context);
            await SeedPipelineStagesAsync(context);
            await SeedRolesAsync(context);
            await SeedUsersAsync(context);
            await SeedChatbotFaqsAsync(context);

            _isInitialized = true;
        }

        /// <summary>
        /// Tự động kiểm tra và build / cập nhật toàn bộ cấu trúc Database (Tables, Columns, Constraints)
        /// Chạy an toàn độc lập từng khối, đảm bảo mọi bảng/cột cần thiết luôn sẵn sàng khi BE restart.
        /// </summary>
        private static async Task EnsureFullIndustrialSchemaAsync(DATNContext context)
        {
            var schemaScripts = new List<(string Name, string Sql)>
            {
                ("Drop old check constraints", @"
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
                        OBJECT_ID('dbo.ApplicationStageHistory'),
                        OBJECT_ID('dbo.InterviewBatches')
                    );
                    IF LEN(@dropCheckSql) > 0 EXEC sp_executesql @dropCheckSql;
                "),
                ("InterviewBatches table and columns", @"
                    IF OBJECT_ID('dbo.InterviewBatches', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.InterviewBatches
                        (
                            Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewBatches PRIMARY KEY,
                            JobId             UNIQUEIDENTIFIER  NULL,
                            BatchCode         NVARCHAR(50)      NOT NULL CONSTRAINT DF_InterviewBatches_BatchCode DEFAULT ('BATCH-INIT'),
                            ScheduledDate     DATE              NULL CONSTRAINT DF_InterviewBatches_ScheduledDate DEFAULT (CAST(GETDATE() AS DATE)),
                            ShiftSlot         NVARCHAR(30)      NULL CONSTRAINT DF_InterviewBatches_ShiftSlot DEFAULT ('MORNING_08_11'),
                            ShiftName         NVARCHAR(100)     NULL CONSTRAINT DF_InterviewBatches_ShiftName DEFAULT (N'Ca phỏng vấn tập trung'),
                            LocationRoom      NVARCHAR(200)     NULL CONSTRAINT DF_InterviewBatches_LocationRoom DEFAULT (N'Hội trường tuyển dụng A'),
                            MaxCapacity       INT               NULL CONSTRAINT DF_InterviewBatches_MaxCapacity DEFAULT (50),
                            CurrentCount      INT               NULL CONSTRAINT DF_InterviewBatches_CurrentCount DEFAULT (0),
                            InterviewerUserId UNIQUEIDENTIFIER  NULL,
                            Status            NVARCHAR(30)      NULL CONSTRAINT DF_InterviewBatches_Status DEFAULT ('Open'),
                            CreatedAt         DATETIME2         NULL CONSTRAINT DF_InterviewBatches_CreatedAt DEFAULT (SYSUTCDATETIME())
                        );
                    END
                    ELSE
                    BEGIN
                        -- Drop index phụ thuộc trước khi ALTER COLUMN để không bị lỗi Error 5074
                        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InterviewBatches_Interviewer_Slot' AND object_id = OBJECT_ID('dbo.InterviewBatches'))
                            DROP INDEX IX_InterviewBatches_Interviewer_Slot ON dbo.InterviewBatches;

                        IF COL_LENGTH('dbo.InterviewBatches', 'JobId') IS NULL ALTER TABLE dbo.InterviewBatches ADD JobId UNIQUEIDENTIFIER NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'BatchCode') IS NULL ALTER TABLE dbo.InterviewBatches ADD BatchCode NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'ScheduledDate') IS NULL ALTER TABLE dbo.InterviewBatches ADD ScheduledDate DATE NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'ShiftSlot') IS NULL ALTER TABLE dbo.InterviewBatches ADD ShiftSlot NVARCHAR(30) NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'ShiftName') IS NULL ALTER TABLE dbo.InterviewBatches ADD ShiftName NVARCHAR(100) NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'LocationRoom') IS NULL ALTER TABLE dbo.InterviewBatches ADD LocationRoom NVARCHAR(200) NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'MaxCapacity') IS NULL ALTER TABLE dbo.InterviewBatches ADD MaxCapacity INT NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'CurrentCount') IS NULL ALTER TABLE dbo.InterviewBatches ADD CurrentCount INT NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'InterviewerUserId') IS NULL ALTER TABLE dbo.InterviewBatches ADD InterviewerUserId UNIQUEIDENTIFIER NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'Status') IS NULL ALTER TABLE dbo.InterviewBatches ADD Status NVARCHAR(30) NULL;
                        IF COL_LENGTH('dbo.InterviewBatches', 'CreatedAt') IS NULL ALTER TABLE dbo.InterviewBatches ADD CreatedAt DATETIME2 NULL;

                        -- Đổi cột thành NULL để EF Core mapping không bị SqlNullValueException
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN ScheduledDate DATE NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN ShiftSlot NVARCHAR(30) NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN ShiftName NVARCHAR(100) NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN LocationRoom NVARCHAR(200) NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN MaxCapacity INT NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN CurrentCount INT NULL;
                        ALTER TABLE dbo.InterviewBatches ALTER COLUMN Status NVARCHAR(30) NULL;

                        IF COL_LENGTH('dbo.InterviewBatches', 'InterviewerId') IS NOT NULL
                            EXEC('UPDATE dbo.InterviewBatches SET InterviewerUserId = InterviewerId WHERE InterviewerUserId IS NULL');
                        IF COL_LENGTH('dbo.InterviewBatches', 'CurrentEnrolled') IS NOT NULL
                            EXEC('UPDATE dbo.InterviewBatches SET CurrentCount = CurrentEnrolled WHERE CurrentCount IS NULL OR CurrentCount = 0');

                        EXEC('UPDATE dbo.InterviewBatches SET ScheduledDate = CAST(GETDATE() AS DATE) WHERE ScheduledDate IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET ShiftSlot = ''MORNING_08_11'' WHERE ShiftSlot IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET ShiftName = N''Ca phỏng vấn tập trung'' WHERE ShiftName IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET LocationRoom = N''Hội trường tuyển dụng A'' WHERE LocationRoom IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET MaxCapacity = 50 WHERE MaxCapacity IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET CurrentCount = 0 WHERE CurrentCount IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET Status = ''Open'' WHERE Status IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET CreatedAt = SYSUTCDATETIME() WHERE CreatedAt IS NULL');
                        EXEC('UPDATE dbo.InterviewBatches SET BatchCode = ''BATCH-'' + CAST(Id AS VARCHAR) WHERE BatchCode IS NULL');

                        -- Tạo lại index an toàn sau khi đã chuẩn hóa cột
                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InterviewBatches_Interviewer_Slot' AND object_id = OBJECT_ID('dbo.InterviewBatches'))
                            CREATE INDEX IX_InterviewBatches_Interviewer_Slot ON dbo.InterviewBatches(InterviewerUserId, ScheduledDate, ShiftSlot) WHERE InterviewerUserId IS NOT NULL;
                    END;
                "),
                ("CandidateBlacklists table and columns", @"
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
                    END
                    ELSE
                    BEGIN
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'CitizenId') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD CitizenId NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'PhoneNormalized') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD PhoneNormalized NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'BanType') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD BanType NVARCHAR(50) NOT NULL CONSTRAINT DF_CandidateBlacklists_BanType_Sync DEFAULT ('PERMANENT');
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'BannedUntil') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD BannedUntil DATETIME2 NULL;
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'CreatedBy') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD CreatedBy NVARCHAR(100) NULL;
                        IF COL_LENGTH('dbo.CandidateBlacklists', 'CreatedAt') IS NULL ALTER TABLE dbo.CandidateBlacklists ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CandidateBlacklists_CreatedAt_Sync DEFAULT (SYSUTCDATETIME());
                    END;
                "),
                ("ExEmployees table and columns", @"
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
                    END
                    ELSE
                    BEGIN
                        IF COL_LENGTH('dbo.ExEmployees', 'CitizenId') IS NULL ALTER TABLE dbo.ExEmployees ADD CitizenId NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.ExEmployees', 'PhoneNormalized') IS NULL ALTER TABLE dbo.ExEmployees ADD PhoneNormalized NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.ExEmployees', 'DepartmentName') IS NULL ALTER TABLE dbo.ExEmployees ADD DepartmentName NVARCHAR(100) NULL;
                        IF COL_LENGTH('dbo.ExEmployees', 'ResignedDate') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignedDate DATE NULL;
                        IF COL_LENGTH('dbo.ExEmployees', 'ResignationReason') IS NULL ALTER TABLE dbo.ExEmployees ADD ResignationReason NVARCHAR(255) NULL;
                        IF COL_LENGTH('dbo.ExEmployees', 'IsEligibleForRehire') IS NULL ALTER TABLE dbo.ExEmployees ADD IsEligibleForRehire BIT NOT NULL CONSTRAINT DF_ExEmployees_IsEligible_Sync DEFAULT (1);
                        IF COL_LENGTH('dbo.ExEmployees', 'CreatedAt') IS NULL ALTER TABLE dbo.ExEmployees ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ExEmployees_CreatedAt_Sync DEFAULT (SYSUTCDATETIME());
                    END;
                "),
                ("Departments table", @"
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
                    END;
                "),
                ("Files table and columns", @"
                    IF OBJECT_ID('dbo.Files', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('dbo.Files', 'FileName') IS NULL
                        BEGIN
                            ALTER TABLE dbo.Files ADD FileName NVARCHAR(260) NULL;
                        END
                        ELSE
                        BEGIN
                            ALTER TABLE dbo.Files ALTER COLUMN FileName NVARCHAR(260) NULL;
                        END;
                        IF COL_LENGTH('dbo.Files', 'Provider') IS NULL ALTER TABLE dbo.Files ADD Provider NVARCHAR(50) NOT NULL CONSTRAINT DF_Files_Provider_Sync DEFAULT ('LOCAL');
                        IF COL_LENGTH('dbo.Files', 'OriginalFileName') IS NULL ALTER TABLE dbo.Files ADD OriginalFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_OriginalFileName_Sync DEFAULT ('cv.pdf');
                        IF COL_LENGTH('dbo.Files', 'StoredFileName') IS NULL ALTER TABLE dbo.Files ADD StoredFileName NVARCHAR(255) NOT NULL CONSTRAINT DF_Files_StoredFileName_Sync DEFAULT ('cv.pdf');
                        IF COL_LENGTH('dbo.Files', 'SizeBytes') IS NULL ALTER TABLE dbo.Files ADD SizeBytes BIGINT NOT NULL CONSTRAINT DF_Files_SizeBytes_Sync DEFAULT (0);
                        IF COL_LENGTH('dbo.Files', 'Sha256') IS NULL ALTER TABLE dbo.Files ADD Sha256 CHAR(64) NULL;
                        IF COL_LENGTH('dbo.Files', 'Url') IS NULL ALTER TABLE dbo.Files ADD Url NVARCHAR(1000) NOT NULL CONSTRAINT DF_Files_Url_Sync DEFAULT ('');
                        EXEC('UPDATE dbo.Files SET FileName = ISNULL(OriginalFileName, ISNULL(StoredFileName, ''file'')) WHERE FileName IS NULL');
                    END;
                "),
                ("Applications columns", @"
                    IF OBJECT_ID('dbo.Applications', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('dbo.Applications', 'InterviewBatchId') IS NULL ALTER TABLE dbo.Applications ADD InterviewBatchId INT NULL;
                        IF COL_LENGTH('dbo.Applications', 'CitizenId') IS NULL ALTER TABLE dbo.Applications ADD CitizenId NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.Applications', 'PhoneNormalized') IS NULL ALTER TABLE dbo.Applications ADD PhoneNormalized NVARCHAR(20) NULL;
                        IF COL_LENGTH('dbo.Applications', 'ConvertedEmployeeCode') IS NULL ALTER TABLE dbo.Applications ADD ConvertedEmployeeCode NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.Applications', 'OfferStatus') IS NULL ALTER TABLE dbo.Applications ADD OfferStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Apps_OfferStatus DEFAULT ('Not_Required');
                        IF COL_LENGTH('dbo.Applications', 'OfferedSalary') IS NULL ALTER TABLE dbo.Applications ADD OfferedSalary DECIMAL(18,2) NULL;
                        IF COL_LENGTH('dbo.Applications', 'OfferResponseNote') IS NULL ALTER TABLE dbo.Applications ADD OfferResponseNote NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Applications', 'OfferExpiresAt') IS NULL ALTER TABLE dbo.Applications ADD OfferExpiresAt DATETIME2 NULL;
                        IF COL_LENGTH('dbo.Applications', 'IsInTalentPool') IS NULL ALTER TABLE dbo.Applications ADD IsInTalentPool BIT NOT NULL CONSTRAINT DF_Apps_IsInTalentPool DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'IsExEmployeeRehire') IS NULL ALTER TABLE dbo.Applications ADD IsExEmployeeRehire BIT NOT NULL CONSTRAINT DF_Apps_IsExEmployeeRehire DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'PreviousEmployeeCode') IS NULL ALTER TABLE dbo.Applications ADD PreviousEmployeeCode NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.Applications', 'Probation30DayStatus') IS NULL ALTER TABLE dbo.Applications ADD Probation30DayStatus NVARCHAR(30) NULL DEFAULT ('Not_Started');
                        IF COL_LENGTH('dbo.Applications', 'MedicalCheckStatus') IS NULL ALTER TABLE dbo.Applications ADD MedicalCheckStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Apps_MedicalCheckStatus DEFAULT ('Pending');
                        IF COL_LENGTH('dbo.Applications', 'BankAccountNumber') IS NULL ALTER TABLE dbo.Applications ADD BankAccountNumber NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.Applications', 'BankName') IS NULL ALTER TABLE dbo.Applications ADD BankName NVARCHAR(100) NULL;
                        IF COL_LENGTH('dbo.Applications', 'OnboardingDocsCompleted') IS NULL ALTER TABLE dbo.Applications ADD OnboardingDocsCompleted BIT NOT NULL CONSTRAINT DF_Apps_OnboardingDocsCompleted DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'SlaDeadlineAt') IS NULL ALTER TABLE dbo.Applications ADD SlaDeadlineAt DATETIME2 NULL;
                        IF COL_LENGTH('dbo.Applications', 'IsSlaEscalated') IS NULL ALTER TABLE dbo.Applications ADD IsSlaEscalated BIT NOT NULL CONSTRAINT DF_Apps_IsSlaEscalated DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'SlaOverrideReason') IS NULL ALTER TABLE dbo.Applications ADD SlaOverrideReason NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Applications', 'ContactName') IS NULL ALTER TABLE dbo.Applications ADD ContactName NVARCHAR(200) NULL;
                        IF COL_LENGTH('dbo.Applications', 'ContactEmail') IS NULL ALTER TABLE dbo.Applications ADD ContactEmail NVARCHAR(320) NULL;
                        IF COL_LENGTH('dbo.Applications', 'ContactPhone') IS NULL ALTER TABLE dbo.Applications ADD ContactPhone NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.Applications', 'CvFileUrl') IS NULL ALTER TABLE dbo.Applications ADD CvFileUrl NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Applications', 'DateOfBirth') IS NULL ALTER TABLE dbo.Applications ADD DateOfBirth DATE NULL;
                        IF COL_LENGTH('dbo.Applications', 'ApplicationSource') IS NULL ALTER TABLE dbo.Applications ADD ApplicationSource NVARCHAR(30) NOT NULL CONSTRAINT DF_Apps_AppSource DEFAULT ('PDF_UPLOAD');
                        IF COL_LENGTH('dbo.Applications', 'ScoringMethod') IS NULL ALTER TABLE dbo.Applications ADD ScoringMethod NVARCHAR(30) NOT NULL CONSTRAINT DF_Apps_ScoringMethod DEFAULT ('GEMINI_AI');
                        IF COL_LENGTH('dbo.Applications', 'DuplicateCount') IS NULL ALTER TABLE dbo.Applications ADD DuplicateCount INT NOT NULL CONSTRAINT DF_Apps_DuplicateCount DEFAULT (1);
                        IF COL_LENGTH('dbo.Applications', 'PreviousApplicationId') IS NULL ALTER TABLE dbo.Applications ADD PreviousApplicationId INT NULL;
                        IF COL_LENGTH('dbo.Applications', 'IsBlacklisted') IS NULL ALTER TABLE dbo.Applications ADD IsBlacklisted BIT NOT NULL CONSTRAINT DF_Apps_IsBlacklisted DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'BlacklistWarningNote') IS NULL ALTER TABLE dbo.Applications ADD BlacklistWarningNote NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Applications', 'IsScannedOrUnparseablePdf') IS NULL ALTER TABLE dbo.Applications ADD IsScannedOrUnparseablePdf BIT NOT NULL CONSTRAINT DF_Apps_IsScanned DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'RawResumeText') IS NULL ALTER TABLE dbo.Applications ADD RawResumeText NVARCHAR(MAX) NULL;
                        IF COL_LENGTH('dbo.Applications', 'AiAnalysisSummary') IS NULL ALTER TABLE dbo.Applications ADD AiAnalysisSummary NVARCHAR(MAX) NULL;
                        IF COL_LENGTH('dbo.Applications', 'AppliedScoringVersion') IS NULL ALTER TABLE dbo.Applications ADD AppliedScoringVersion INT NULL DEFAULT (1);
                        IF COL_LENGTH('dbo.Applications', 'AppliedScoringConfigJson') IS NULL ALTER TABLE dbo.Applications ADD AppliedScoringConfigJson NVARCHAR(MAX) NULL;
                        IF COL_LENGTH('dbo.Applications', 'OralInterviewChecklistJson') IS NULL ALTER TABLE dbo.Applications ADD OralInterviewChecklistJson NVARCHAR(MAX) NULL;
                        IF COL_LENGTH('dbo.Applications', 'InterviewScore') IS NULL ALTER TABLE dbo.Applications ADD InterviewScore DECIMAL(5,2) NULL;
                        IF COL_LENGTH('dbo.Applications', 'IsAiOverriddenByHuman') IS NULL ALTER TABLE dbo.Applications ADD IsAiOverriddenByHuman BIT NOT NULL CONSTRAINT DF_Apps_IsAiOverridden DEFAULT (0);
                        IF COL_LENGTH('dbo.Applications', 'AiOverrideReason') IS NULL ALTER TABLE dbo.Applications ADD AiOverrideReason NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Applications', 'InterviewRescheduleCount') IS NULL ALTER TABLE dbo.Applications ADD InterviewRescheduleCount INT NOT NULL CONSTRAINT DF_Apps_RescheduleCount DEFAULT (0);
                    END;
                "),
                ("Jobs columns", @"
                    IF OBJECT_ID('dbo.Jobs', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('dbo.Jobs', 'DepartmentId') IS NULL ALTER TABLE dbo.Jobs ADD DepartmentId UNIQUEIDENTIFIER NULL;
                        IF COL_LENGTH('dbo.Jobs', 'CampaignCode') IS NULL ALTER TABLE dbo.Jobs ADD CampaignCode NVARCHAR(50) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleFormUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormUrl NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleFormPublicUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormPublicUrl NVARCHAR(1000) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleFormEditUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormEditUrl NVARCHAR(1000) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleFormId') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormId NVARCHAR(128) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleSheetUrl') IS NULL ALTER TABLE dbo.Jobs ADD GoogleSheetUrl NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'GoogleFormSyncCount') IS NULL ALTER TABLE dbo.Jobs ADD GoogleFormSyncCount INT NOT NULL CONSTRAINT DF_Jobs_SyncCount DEFAULT (0);
                        IF COL_LENGTH('dbo.Jobs', 'QrCodeImagePath') IS NULL ALTER TABLE dbo.Jobs ADD QrCodeImagePath NVARCHAR(500) NULL;
                        IF COL_LENGTH('dbo.Jobs', 'RecruitmentTrack') IS NULL ALTER TABLE dbo.Jobs ADD RecruitmentTrack NVARCHAR(30) NOT NULL CONSTRAINT DF_Jobs_RecruitTrack DEFAULT ('PROFESSIONAL');
                        IF COL_LENGTH('dbo.Jobs', 'TargetQuantity') IS NULL ALTER TABLE dbo.Jobs ADD TargetQuantity INT NOT NULL CONSTRAINT DF_Jobs_TargetQty DEFAULT (50);
                        IF COL_LENGTH('dbo.Jobs', 'HiredCount') IS NULL ALTER TABLE dbo.Jobs ADD HiredCount INT NOT NULL CONSTRAINT DF_Jobs_HiredCount DEFAULT (0);
                        IF COL_LENGTH('dbo.Jobs', 'WaitlistBufferPercent') IS NULL ALTER TABLE dbo.Jobs ADD WaitlistBufferPercent INT NOT NULL CONSTRAINT DF_Jobs_WaitlistBuffer DEFAULT (15);
                        IF COL_LENGTH('dbo.Jobs', 'CurrentScoringWeightVersion') IS NULL ALTER TABLE dbo.Jobs ADD CurrentScoringWeightVersion INT NOT NULL CONSTRAINT DF_Jobs_ScoringWeightVer DEFAULT (1);
                        IF COL_LENGTH('dbo.Jobs', 'RequisitionStatus') IS NULL ALTER TABLE dbo.Jobs ADD RequisitionStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Jobs_RequisitionStatus DEFAULT ('Approved');
                        IF COL_LENGTH('dbo.Jobs', 'RequireNightShift') IS NULL ALTER TABLE dbo.Jobs ADD RequireNightShift BIT NOT NULL CONSTRAINT DF_Jobs_RequireNightShift DEFAULT (0);
                    END;
                "),
                ("Users columns", @"
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
                        IF COL_LENGTH('dbo.Users', 'IsActive') IS NOT NULL
                            EXEC('UPDATE dbo.Users SET IsActive = 1 WHERE IsActive IS NULL');
                    END;
                "),
                ("PipelineStages and SlaStageConfiguration", @"
                    IF OBJECT_ID('dbo.PipelineStages', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('dbo.PipelineStages', 'IsSlaEnabled') IS NULL ALTER TABLE dbo.PipelineStages ADD IsSlaEnabled BIT NOT NULL CONSTRAINT DF_PipelineStages_IsSlaEnabled DEFAULT (1);
                        IF COL_LENGTH('dbo.PipelineStages', 'SlaMaxDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaMaxDays INT NULL DEFAULT (3);
                        IF COL_LENGTH('dbo.PipelineStages', 'SlaWarnBeforeDays') IS NULL ALTER TABLE dbo.PipelineStages ADD SlaWarnBeforeDays INT NULL DEFAULT (1);
                    END;

                    IF OBJECT_ID('dbo.SlaStageConfiguration', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.SlaStageConfiguration
                        (
                            SlaStageConfigId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SlaStageConfiguration PRIMARY KEY DEFAULT NEWID(),
                            StageCode         NVARCHAR(100)    NOT NULL CONSTRAINT UQ_SlaStageConfiguration_StageCode UNIQUE,
                            StageName         NVARCHAR(200)    NOT NULL,
                            SlaMaxDays        INT              NOT NULL CONSTRAINT DF_SlaStageConfig_SlaMaxDays DEFAULT 7,
                            SlaWarnBeforeDays INT              NOT NULL CONSTRAINT DF_SlaStageConfig_SlaWarnBeforeDays DEFAULT 2,
                            IsActive          BIT              NOT NULL CONSTRAINT DF_SlaStageConfig_IsActive DEFAULT 1,
                            OrderSequence     INT              NOT NULL CONSTRAINT DF_SlaStageConfig_OrderSequence DEFAULT 0,
                            CreatedAt         DATETIME2        NOT NULL CONSTRAINT DF_SlaStageConfig_CreatedAt DEFAULT SYSUTCDATETIME(),
                            UpdatedAt         DATETIME2        NULL,
                            CreatedBy         UNIQUEIDENTIFIER NULL
                        );
                    END;
                ")
            };

            foreach (var (name, sql) in schemaScripts)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DbInitializer] Schema sync warning on [{name}]: {ex.Message}");
                }
            }
            Console.WriteLine("[DbInitializer] ✓ Kiểm tra và đồng bộ Schema Database hoàn tất!");
        }

        private static async Task EnsureMigrationsHistoryConfiguredAsync(DATNContext context)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__EFMigrationsHistory
    (
        MigrationId    NVARCHAR(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
        ProductVersion NVARCHAR(32)  NOT NULL
    );
END;

IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
BEGIN
    MERGE dbo.__EFMigrationsHistory AS target
    USING (VALUES
        (N'20260210091803_AddNotificationSettingsV2', N'8.0.10'),
        (N'20260311041636_AddAuthProvider', N'8.0.10'),
        (N'20260425000000_AddBreakdownJsonToAiScore', N'8.0.10'),
        (N'20260521082932_Phase1_ChatbotInfrastructure_Clean', N'8.0.10'),
        (N'20260522020912_Make_ChatSessionId_Nullable', N'8.0.10'),
        (N'20261010042838_AddMassRecruitmentAndAiScreening', N'8.0.10'),
        (N'20261010063838_AddDepartmentAndMatrixOrg', N'8.0.10'),
        (N'20261102135027_SyncModelWithDbSchema', N'8.0.10'),
        (N'20261102155424_AddJobGoogleFormQr', N'8.0.10')
    ) AS source (MigrationId, ProductVersion)
    ON target.MigrationId = source.MigrationId
    WHEN NOT MATCHED THEN
        INSERT (MigrationId, ProductVersion) VALUES (source.MigrationId, source.ProductVersion);
END;
");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] EnsureMigrationsHistoryConfiguredAsync warning: {ex.Message}");
            }
        }

        private static async Task SeedJobTypesAsync(DATNContext context)
        {
            var defaults = new List<JobType>
            {
                new JobType
                {
                    JobTypeId = new Guid("40000000-0000-0000-0000-000000000001"),
                    Name = "Toàn thời gian",
                    Description = "Làm việc toàn thời gian theo ca hoặc hành chính"
                },
                new JobType
                {
                    JobTypeId = new Guid("40000000-0000-0000-0000-000000000002"),
                    Name = "Theo ca",
                    Description = "Làm việc theo ca sáng/chiều/đêm hoặc xoay ca"
                },
                new JobType
                {
                    JobTypeId = new Guid("40000000-0000-0000-0000-000000000003"),
                    Name = "Thời vụ",
                    Description = "Tuyển theo đợt cao điểm sản xuất"
                },
                new JobType
                {
                    JobTypeId = new Guid("40000000-0000-0000-0000-000000000004"),
                    Name = "Hợp đồng ngắn hạn",
                    Description = "Hợp đồng có thời hạn cho chiến dịch tuyển nhanh"
                }
            };

            var existingNames = await context.JobTypes
                .Select(x => x.Name.Trim().ToLower())
                .ToListAsync();

            var missing = defaults
                .Where(x => !existingNames.Contains(x.Name.Trim().ToLower()))
                .ToList();

            if (!missing.Any()) return;

            await context.JobTypes.AddRangeAsync(missing);
            await context.SaveChangesAsync();
            Console.WriteLine($"✓ Seeded {missing.Count} JobTypes mặc định");
        }

        private static async Task SeedSkillsAsync(DATNContext context)
        {
            var defaultSkillNames = new[]
            {
                "Lắp ráp điện tử",
                "Đứng chuyền sản xuất",
                "Vận hành máy",
                "Đóng gói thành phẩm",
                "Kiểm tra chất lượng",
                "Kho vận nội bộ",
                "An toàn lao động",
                "5S công nghiệp",
                "Sẵn sàng tăng ca",
                "Làm việc xoay ca",
                "Hàn cơ bản",
                "May công nghiệp"
            };

            var existing = await context.Skills
                .Select(x => x.NormalizedName)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var missing = new List<Skill>();

            foreach (var name in defaultSkillNames)
            {
                var normalized = name.Trim().ToUpperInvariant();
                if (existing.Contains(normalized))
                {
                    continue;
                }

                missing.Add(new Skill
                {
                    SkillId = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalized,
                    CreatedAt = now,
                    IsDeleted = false
                });
            }

            if (!missing.Any()) return;

            await context.Skills.AddRangeAsync(missing);
            await context.SaveChangesAsync();
            Console.WriteLine($"✓ Seeded {missing.Count} Skills mặc định");
        }

        private static async Task SeedPipelineStagesAsync(DATNContext context)
        {
            var defaults = new List<PipelineStage>
            {
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000001"),
                    Code = "PENDING_REVIEW",
                    Name = "Tiếp nhận hồ sơ",
                    SortOrder = 1,
                    IsTerminal = false,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = true,
                    SlaMaxDays = 2,
                    SlaWarnBeforeDays = 1
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000002"),
                    Code = "SCREENING",
                    Name = "Sàng lọc",
                    SortOrder = 2,
                    IsTerminal = false,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = true,
                    SlaMaxDays = 3,
                    SlaWarnBeforeDays = 1
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000003"),
                    Code = "INTERVIEW",
                    Name = "Phỏng vấn",
                    SortOrder = 3,
                    IsTerminal = false,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = true,
                    SlaMaxDays = 3,
                    SlaWarnBeforeDays = 1
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000004"),
                    Code = "OFFER",
                    Name = "Đề nghị nhận việc",
                    SortOrder = 4,
                    IsTerminal = false,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = true,
                    SlaMaxDays = 2,
                    SlaWarnBeforeDays = 1
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000005"),
                    Code = "HIRED",
                    Name = "Đã nhận việc",
                    SortOrder = 5,
                    IsTerminal = true,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = false
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000006"),
                    Code = "PASSED",
                    Name = "Đạt phỏng vấn - Chờ tiếp nhận",
                    SortOrder = 4,
                    IsTerminal = false,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = true,
                    SlaMaxDays = 3,
                    SlaWarnBeforeDays = 1
                },
                new PipelineStage
                {
                    StageId = new Guid("50000000-0000-0000-0000-000000000007"),
                    Code = "REJECTED",
                    Name = "Từ chối / Không đạt",
                    SortOrder = 6,
                    IsTerminal = true,
                    CreatedAt = DateTime.UtcNow,
                    IsSlaEnabled = false
                }
            };

            var existingCodes = await context.PipelineStages
                .Select(x => x.Code.Trim().ToUpper())
                .ToListAsync();

            var missing = defaults
                .Where(x => !existingCodes.Contains(x.Code.Trim().ToUpper()))
                .ToList();

            if (!missing.Any()) return;

            await context.PipelineStages.AddRangeAsync(missing);
            await context.SaveChangesAsync();
            Console.WriteLine($"✓ Seeded {missing.Count} PipelineStages mặc định");
        }

        // ════════════════════════════════════════════════════════════════════════
        // SEED: 7 Phòng ban / Khu vực chuẩn Nhà máy Công nghiệp
        // ════════════════════════════════════════════════════════════════════════
        private static async Task SeedDepartmentsAsync(DATNContext context)
        {
            var departments = new List<Department>
            {
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000001"),
                    DepartmentCode = "ADMIN_SYS",
                    DepartmentName = "Ban Giám Đốc",
                    Description    = "Cấp quản lý cao nhất – Ban Giám đốc và Quản trị hệ thống ATS.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000002"),
                    DepartmentCode = "HR_TEAM",
                    DepartmentName = "Phòng Nhân Sự (HR)",
                    Description    = "Tuyển dụng, sàng lọc hồ sơ, quản lý pipeline ứng viên toàn nhà máy.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000003"),
                    DepartmentCode = "INT_TEAM",
                    DepartmentName = "Hội Đồng Phỏng Vấn",
                    Description    = "Thực hiện phỏng vấn chuyên môn, chấm điểm và phản hồi ứng viên.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000004"),
                    DepartmentCode = "PROD_LINE",
                    DepartmentName = "Xưởng Sản Xuất & Lắp Ráp",
                    Description    = "Trực tiếp sản xuất, lắp ráp sản phẩm theo dây chuyền công nghiệp.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000005"),
                    DepartmentCode = "QA_QC",
                    DepartmentName = "Phòng Quản Lý Chất Lượng (QA/QC)",
                    Description    = "Kiểm tra, kiểm soát chất lượng sản phẩm đầu vào và đầu ra.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000006"),
                    DepartmentCode = "WH_LOGS",
                    DepartmentName = "Khu Vực Kho & Logistics",
                    Description    = "Quản lý kho hàng, vận chuyển, xuất-nhập kho tại khu công nghiệp.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                },
                new Department
                {
                    DepartmentId   = new Guid("10000000-0000-0000-0000-000000000007"),
                    DepartmentCode = "TECH_MNT",
                    DepartmentName = "Phòng Kỹ Thuật & Bảo Trì",
                    Description    = "Vận hành, bảo dưỡng máy móc, hệ thống điện-cơ trong khu sản xuất.",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                }
            };

            var existingCodes = await context.Departments
                .Select(x => x.DepartmentCode.Trim().ToUpper())
                .ToListAsync();

            var missing = departments
                .Where(x => !existingCodes.Contains(x.DepartmentCode.Trim().ToUpper()))
                .ToList();

            if (missing.Any())
            {
                await context.Departments.AddRangeAsync(missing);
                await context.SaveChangesAsync();
                Console.WriteLine($"✓ Seeded {missing.Count} Departments (nhà máy công nghiệp)");
            }
        }

        private static async Task SeedRolesAsync(DATNContext context)
        {
            if (await context.Roles.AnyAsync()) return;

            var roles = new List<Role>
            {
                new Role { RoleId = new Guid("20000000-0000-0000-0000-000000000001"), Code = "ADMIN",       Name = "Ban Giám Đốc / Administrator", CreatedAt = DateTime.UtcNow },
                new Role { RoleId = new Guid("20000000-0000-0000-0000-000000000002"), Code = "MANAGER",     Name = "Trưởng Phòng / Quản Lý",       CreatedAt = DateTime.UtcNow },
                new Role { RoleId = new Guid("20000000-0000-0000-0000-000000000003"), Code = "HR",          Name = "Chuyên Viên Nhân Sự",           CreatedAt = DateTime.UtcNow },
                new Role { RoleId = new Guid("20000000-0000-0000-0000-000000000004"), Code = "INTERVIEWER", Name = "Hội Đồng Phỏng Vấn",            CreatedAt = DateTime.UtcNow }
            };

            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();
            Console.WriteLine("✓ Seeded 4 Roles: ADMIN, MANAGER, HR, INTERVIEWER");
        }

        // ════════════════════════════════════════════════════════════════════════
        // SEED: Full matrix – Nhân sự đầy đủ cấu trúc phân cấp nhà máy
        // ════════════════════════════════════════════════════════════════════════
        private static async Task SeedUsersAsync(DATNContext context)
        {
            if (await context.Users.AnyAsync()) return;

            // ─── Định nghĩa các Guid cố định để tham chiếu ManagerId ───────────
            // Ban Giám Đốc
            var uGiamDoc          = new Guid("30000000-0000-0000-0000-000000000001");

            // Trưởng Phòng Nhân Sự
            var uTruongHR         = new Guid("30000000-0000-0000-0000-000000000010");
            // Trưởng Hội Đồng Phỏng Vấn
            var uTruongPV         = new Guid("30000000-0000-0000-0000-000000000020");
            // Trưởng Xưởng Sản Xuất
            var uTruongSX         = new Guid("30000000-0000-0000-0000-000000000030");
            // Trưởng Phòng QA/QC
            var uTruongQA         = new Guid("30000000-0000-0000-0000-000000000040");
            // Trưởng Kho & Logistics
            var uTruongKho        = new Guid("30000000-0000-0000-0000-000000000050");
            // Trưởng Phòng Kỹ Thuật
            var uTruongKT         = new Guid("30000000-0000-0000-0000-000000000060");

            var pw = BCrypt.Net.BCrypt.HashPassword("Password123!");

            // ─── DEPT IDs ─────────────────────────────────────────────────────
            var dAdmin  = new Guid("10000000-0000-0000-0000-000000000001");
            var dHR     = new Guid("10000000-0000-0000-0000-000000000002");
            var dPV     = new Guid("10000000-0000-0000-0000-000000000003");
            var dSX     = new Guid("10000000-0000-0000-0000-000000000004");
            var dQA     = new Guid("10000000-0000-0000-0000-000000000005");
            var dKho    = new Guid("10000000-0000-0000-0000-000000000006");
            var dKT     = new Guid("10000000-0000-0000-0000-000000000007");

            // ─── ROLE IDs ─────────────────────────────────────────────────────
            var rAdmin  = new Guid("20000000-0000-0000-0000-000000000001");
            var rMgr    = new Guid("20000000-0000-0000-0000-000000000002");
            var rHR     = new Guid("20000000-0000-0000-0000-000000000003");
            var rPV     = new Guid("20000000-0000-0000-0000-000000000004");

            // ─── Danh sách Users ──────────────────────────────────────────────
            var users = new List<(User user, Guid roleId)>
            {
                // ===== BAN GIÁM ĐỐC =====
                (new User
                {
                    UserId = uGiamDoc,
                    Email = "giamdoc@factory.com", NormalizedEmail = "GIAMDOC@FACTORY.COM",
                    PasswordHash = pw, FullName = "Nguyễn Văn Giám Đốc", Phone = "0901000001",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dAdmin, ManagerId = null
                }, rAdmin),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000002"),
                    Email = "pho.giamdoc@factory.com", NormalizedEmail = "PHO.GIAMDOC@FACTORY.COM",
                    PasswordHash = pw, FullName = "Trần Thị Phó Giám Đốc", Phone = "0901000002",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dAdmin, ManagerId = uGiamDoc
                }, rAdmin),

                // ===== PHÒNG NHÂN SỰ (HR) =====
                (new User
                {
                    UserId = uTruongHR,
                    Email = "truong.hr@factory.com", NormalizedEmail = "TRUONG.HR@FACTORY.COM",
                    PasswordHash = pw, FullName = "Lê Thị Trưởng HR", Phone = "0901000010",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dHR, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000011"),
                    Email = "hr1@factory.com", NormalizedEmail = "HR1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Phạm Văn Nhân Sự 1", Phone = "0901000011",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dHR, ManagerId = uTruongHR
                }, rHR),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000012"),
                    Email = "hr2@factory.com", NormalizedEmail = "HR2@FACTORY.COM",
                    PasswordHash = pw, FullName = "Hoàng Thị Nhân Sự 2", Phone = "0901000012",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dHR, ManagerId = uTruongHR
                }, rHR),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000013"),
                    Email = "hr3@factory.com", NormalizedEmail = "HR3@FACTORY.COM",
                    PasswordHash = pw, FullName = "Đỗ Văn Nhân Sự 3", Phone = "0901000013",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dHR, ManagerId = uTruongHR
                }, rHR),

                // ===== HỘI ĐỒNG PHỎNG VẤN =====
                (new User
                {
                    UserId = uTruongPV,
                    Email = "truong.pv@factory.com", NormalizedEmail = "TRUONG.PV@FACTORY.COM",
                    PasswordHash = pw, FullName = "Ngô Văn Trưởng Phỏng Vấn", Phone = "0901000020",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dPV, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000021"),
                    Email = "pv1@factory.com", NormalizedEmail = "PV1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Vũ Thị Phỏng Vấn Viên 1", Phone = "0901000021",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dPV, ManagerId = uTruongPV
                }, rPV),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000022"),
                    Email = "pv2@factory.com", NormalizedEmail = "PV2@FACTORY.COM",
                    PasswordHash = pw, FullName = "Bùi Văn Phỏng Vấn Viên 2", Phone = "0901000022",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dPV, ManagerId = uTruongPV
                }, rPV),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000023"),
                    Email = "pv3@factory.com", NormalizedEmail = "PV3@FACTORY.COM",
                    PasswordHash = pw, FullName = "Đinh Thị Phỏng Vấn Viên 3", Phone = "0901000023",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dPV, ManagerId = uTruongPV
                }, rPV),

                // ===== XƯỞNG SẢN XUẤT & LẮP RÁP =====
                (new User
                {
                    UserId = uTruongSX,
                    Email = "truong.sx@factory.com", NormalizedEmail = "TRUONG.SX@FACTORY.COM",
                    PasswordHash = pw, FullName = "Trịnh Văn Trưởng Xưởng", Phone = "0901000030",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dSX, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000031"),
                    Email = "sx.hr1@factory.com", NormalizedEmail = "SX.HR1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Lý Thị HR Xưởng 1", Phone = "0901000031",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dSX, ManagerId = uTruongSX
                }, rHR),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000032"),
                    Email = "sx.pv1@factory.com", NormalizedEmail = "SX.PV1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Mai Văn PV Xưởng 1", Phone = "0901000032",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dSX, ManagerId = uTruongSX
                }, rPV),

                // ===== PHÒNG QUẢN LÝ CHẤT LƯỢNG (QA/QC) =====
                (new User
                {
                    UserId = uTruongQA,
                    Email = "truong.qa@factory.com", NormalizedEmail = "TRUONG.QA@FACTORY.COM",
                    PasswordHash = pw, FullName = "Phan Thị Trưởng QA", Phone = "0901000040",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dQA, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000041"),
                    Email = "qa.hr1@factory.com", NormalizedEmail = "QA.HR1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Tô Văn HR Chất Lượng 1", Phone = "0901000041",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dQA, ManagerId = uTruongQA
                }, rHR),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000042"),
                    Email = "qa.pv1@factory.com", NormalizedEmail = "QA.PV1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Cao Thị PV Chất Lượng 1", Phone = "0901000042",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dQA, ManagerId = uTruongQA
                }, rPV),

                // ===== KHU VỰC KHO & LOGISTICS =====
                (new User
                {
                    UserId = uTruongKho,
                    Email = "truong.kho@factory.com", NormalizedEmail = "TRUONG.KHO@FACTORY.COM",
                    PasswordHash = pw, FullName = "Lương Văn Trưởng Kho", Phone = "0901000050",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dKho, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000051"),
                    Email = "kho.hr1@factory.com", NormalizedEmail = "KHO.HR1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Đặng Thị HR Kho 1", Phone = "0901000051",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dKho, ManagerId = uTruongKho
                }, rHR),

                // ===== PHÒNG KỸ THUẬT & BẢO TRÌ =====
                (new User
                {
                    UserId = uTruongKT,
                    Email = "truong.kt@factory.com", NormalizedEmail = "TRUONG.KT@FACTORY.COM",
                    PasswordHash = pw, FullName = "Hứa Văn Trưởng Kỹ Thuật", Phone = "0901000060",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dKT, ManagerId = uGiamDoc
                }, rMgr),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000061"),
                    Email = "kt.pv1@factory.com", NormalizedEmail = "KT.PV1@FACTORY.COM",
                    PasswordHash = pw, FullName = "Dương Thị PV Kỹ Thuật 1", Phone = "0901000061",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dKT, ManagerId = uTruongKT
                }, rPV),

                (new User
                {
                    UserId = new Guid("30000000-0000-0000-0000-000000000062"),
                    Email = "kt.pv2@factory.com", NormalizedEmail = "KT.PV2@FACTORY.COM",
                    PasswordHash = pw, FullName = "Thái Văn PV Kỹ Thuật 2", Phone = "0901000062",
                    IsActive = true, CreatedAt = DateTime.UtcNow, DepartmentId = dKT, ManagerId = uTruongKT
                }, rPV),
            };

            // Thêm tất cả users vào context
            await context.Users.AddRangeAsync(users.Select(u => u.user));
            await context.SaveChangesAsync();

            // Gán roles
            var userRoles = users.Select(u => new UserRole
            {
                UserId    = u.user.UserId,
                RoleId    = u.roleId,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await context.UserRoles.AddRangeAsync(userRoles);
            await context.SaveChangesAsync();

            Console.WriteLine($"✓ Seeded {users.Count} Users với đầy đủ cấu trúc phân cấp nhà máy.");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            Console.WriteLine("  📧 TÀI KHOẢN ĐĂNG NHẬP (mật khẩu: Password123!)");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            Console.WriteLine("  [ADMIN]    giamdoc@factory.com        → Ban Giám Đốc");
            Console.WriteLine("  [ADMIN]    pho.giamdoc@factory.com    → Ban Giám Đốc");
            Console.WriteLine("  [MANAGER]  truong.hr@factory.com      → Phòng Nhân Sự (HR)");
            Console.WriteLine("  [MANAGER]  truong.pv@factory.com      → Hội Đồng Phỏng Vấn");
            Console.WriteLine("  [MANAGER]  truong.sx@factory.com      → Xưởng Sản Xuất");
            Console.WriteLine("  [MANAGER]  truong.qa@factory.com      → Phòng QA/QC");
            Console.WriteLine("  [MANAGER]  truong.kho@factory.com     → Khu Vực Kho");
            Console.WriteLine("  [MANAGER]  truong.kt@factory.com      → Phòng Kỹ Thuật");
            Console.WriteLine("  [HR]       hr1@factory.com            → Phòng Nhân Sự");
            Console.WriteLine("  [HR]       hr2@factory.com            → Phòng Nhân Sự");
            Console.WriteLine("  [PV]       pv1@factory.com            → Hội Đồng Phỏng Vấn");
            Console.WriteLine("  [PV]       pv2@factory.com            → Hội Đồng Phỏng Vấn");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
        }

        private static async Task SeedChatbotFaqsAsync(DATNContext context)
        {
            try
            {
                // 1. Dọn dẹp dữ liệu FAQ cũ liên quan đến V9 Tech nếu có
                var legacyFaqs = await context.ChatbotFaqs
                    .Where(f => f.Question.Contains("V9") || f.Answer.Contains("V9 TECH") || f.Answer.Contains("V9 Tech"))
                    .ToListAsync();
                if (legacyFaqs.Any())
                {
                    context.ChatbotFaqs.RemoveRange(legacyFaqs);
                    await context.SaveChangesAsync();
                }

                // 2. Danh mục câu hỏi chuẩn và trả lời chuyên sâu cho ATS Industrial
                var industrialFaqs = new List<ChatbotFaq>
                {
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000001"),
                        Question = "Hệ thống ATS Industrial là gì?",
                        Answer = "ATS Industrial là Nền tảng Quản trị & Tuyển dụng Nhân sự Khu công nghiệp và Nhà máy Sản xuất thông minh, ứng dụng AI để tự động hóa toàn bộ quy trình: từ tiếp nhận hồ sơ qua mã QR / Google Form, sàng lọc CV, xếp lịch phỏng vấn, tiếp nhận nhân sự, cấp mã nhân viên và theo dõi 30 ngày thử việc.",
                        Category = "Giới thiệu & Tổng quan",
                        Keywords = "ats industrial, hệ thống, giới thiệu, công ty, nhà máy, kcn, là gì, phần mềm, ats",
                        Priority = 100,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000002"),
                        Question = "Các vị trí công việc đang tuyển dụng tại nhà máy KCN?",
                        Answer = "Hiện tại ATS Industrial đang mở các đợt tuyển dụng liên tục cho các vị trí chủ lực:\n• Công nhân vận hành dây chuyền SMT & Lắp ráp linh kiện (Số lượng lớn, không yêu cầu kinh nghiệm, đào tạo từ đầu)\n• Kỹ sư Tự động hóa PLC & Kỹ thuật Bảo trì Dây chuyền\n• Chuyên viên Quản lý Chất lượng IQC / OQC / QA (ISO 9001, 5S)\n• Nhân viên Kho vận, Thủ kho & Lái xe nâng Reach Truck\n• Công nhân thời vụ, đóng gói và kiểm định sản phẩm.",
                        Category = "Vị trí Tuyển dụng",
                        Keywords = "vị trí, tuyển dụng, việc làm, công việc, có việc gì, smt, bảo trì, qa, qc, kho vận, công nhân, lao động",
                        Priority = 95,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000003"),
                        Question = "Mức lương, phụ cấp và các khoản thưởng tại nhà máy như thế nào?",
                        Answer = "Chế độ thu nhập tại các nhà máy KCN rất cạnh tranh và minh bạch:\n• Thu nhập bình quân: từ 8.500.000 – 14.000.000 VNĐ/tháng (với công nhân) và 14.000.000 – 25.000.000 VNĐ/tháng (với kỹ thuật viên/kỹ sư).\n• Phụ cấp chuyên cần: 500.000 – 800.000 VNĐ/tháng.\n• Hỗ trợ tiền ăn ca miễn phí tại nhà ăn công ty (đủ dinh dưỡng, đảm bảo VSATTP).\n• Phụ cấp nhà ở hoặc xăng xe: 500.000 – 1.000.000 VNĐ/tháng.\n• Tiền tăng ca (OT): Nhân hệ số 150% ngày thường, 200% ngày nghỉ và 300% ngày lễ tết theo quy định Luật Lao Động.\n• Thưởng tháng lương thứ 13, thưởng sản lượng và quà các dịp lễ Tết.",
                        Category = "Chế độ & Phúc lợi",
                        Keywords = "lương, thu nhập, phụ cấp, thưởng, tăng ca, ot, chuyên cần, tiền ăn, bao nhiêu, tiền lương, đãi ngộ",
                        Priority = 90,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000004"),
                        Question = "Ca kíp làm việc và xe đưa đón công nhân viên?",
                        Answer = "• Ca làm việc: Áp dụng linh hoạt theo vị trí:\n  - Ca hành chính: 8h00 – 17h00 (Thứ 2 đến Thứ 7).\n  - Ca sản xuất 12h (Xoay ca ngày/đêm theo tuần hoặc 2 tuần/lần): Ca ngày (7h30 – 19h30), Ca đêm (19h30 – 7h30), nghỉ giữa ca 60 phút và giải lao 15 phút.\n• Xe đưa đón: Công ty bố trí hệ thống xe bus đưa đón miễn phí theo các tuyến đường chính từ trung tâm các huyện, tỉnh lân cận về thẳng nhà máy.",
                        Category = "Quy định Làm việc",
                        Keywords = "ca làm, ca kíp, ca 8h, ca 12h, xoay ca, giờ làm việc, xe đưa đón, xe bus, đưa đón",
                        Priority = 85,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000005"),
                        Question = "Hồ sơ ứng tuyển và thủ tục tiếp nhận cần chuẩn bị những gì?",
                        Answer = "Khi đến phỏng vấn và làm thủ tục tiếp nhận, ứng viên vui lòng chuẩn bị:\n1. Bản gốc hoặc bản photo Căn cước công dân (CCCD) gắn chip hợp lệ.\n2. Giấy khám sức khỏe (KSK) khổ A3 có dán ảnh, đóng dấu giáp lai (còn hạn trong 06 tháng, đủ điều kiện làm việc KCN).\n3. Sơ yếu lý lịch tự thuật có xác nhận của địa phương.\n4. Bằng tốt nghiệp THPT, Trung cấp, Cao đẳng hoặc Đại học (nếu ứng tuyển vị trí kỹ thuật/văn phòng).\n5. Ảnh thẻ 3x4 hoặc 4x6 (2-4 ảnh) để làm thẻ nhân viên và hồ sơ tiếp nhận.",
                        Category = "Hồ sơ & Thủ tục",
                        Keywords = "hồ sơ, giấy tờ, chuẩn bị, cccd, giấy khám sức khỏe, ksk, sơ yếu lý lịch, bằng cấp, thủ tục, cần gì",
                        Priority = 85,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000006"),
                        Question = "Quy trình thử việc 30 ngày và ký hợp đồng lao động chính thức?",
                        Answer = "Quy trình tiếp nhận và Onboarding tại ATS Industrial gồm các bước:\n1. Tiếp nhận & Cấp mã nhân viên: Sau khi trúng tuyển và đạt điều kiện KSK, ứng viên được cấp mã nhân viên chính thức và hỗ trợ mở tài khoản ngân hàng liên kết nhận lương.\n2. Thử việc 30 ngày: Được đào tạo an toàn lao động, nội quy nhà máy, quy trình thao tác chuẩn (SOP) và hướng dẫn trực tiếp từ tổ trưởng/quản lý xưởng.\n3. Đánh giá thử việc: Sau đủ 30 ngày, hệ thống ghi nhận đánh giá thử việc đạt chuẩn.\n4. Ký HĐLĐ & Đóng BHXH: Ký hợp đồng lao động chính thức dài hạn, hưởng 100% lương chính thức và tham gia đầy đủ bảo hiểm xã hội (BHXH), bảo hiểm y tế (BHYT), bảo hiểm thất nghiệp (BHTN).",
                        Category = "Onboarding & Hợp đồng",
                        Keywords = "thử việc, 30 ngày, đánh giá thử việc, hợp đồng, chính thức, ký hợp đồng, bảo hiểm, bhxh, mã nhân viên, mã nv",
                        Priority = 80,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000007"),
                        Question = "Nhà máy có chỗ ở ký túc xá cho người ở xa không?",
                        Answer = "Có. Công ty có khu Ký túc xá hiện đại dành riêng cho cán bộ công nhân viên ở xa:\n• Phòng ở khép kín, sạch sẽ, trang bị đầy đủ điều hòa, quạt mát, bình nóng lạnh, wifi miễn phí.\n• Có khu căng tin, phòng sinh hoạt chung, sân thể thao.\n• Chi phí lưu trú được công ty hỗ trợ hoàn toàn hoặc chỉ thu mức phí tượng trưng cho dịch vụ vệ sinh/an ninh 24/7.",
                        Category = "Chế độ & Phúc lợi",
                        Keywords = "ký túc xá, ktx, nhà ở, chỗ ở, ở xa, trọ, phòng ở, ký túc",
                        Priority = 75,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ChatbotFaq
                    {
                        FaqId = new Guid("50000000-0000-0000-0000-000000000008"),
                        Question = "Làm sao để nộp hồ sơ ứng tuyển nhanh nhất?",
                        Answer = "Bạn có thể ứng tuyển cực kỳ nhanh chóng qua các cách sau:\n1. Quét mã QR của chiến dịch tuyển dụng trên bảng thông báo nhà máy hoặc website để điền form online trong 1 phút.\n2. Nộp CV trực tiếp tại cổng thông tin việc làm của hệ thống ATS Industrial.\n3. Liên hệ trực tiếp Bộ phận Tuyển dụng Nhân sự tại văn phòng nhà máy để được phỏng vấn nhanh trong ngày.",
                        Category = "Ứng Tuyển",
                        Keywords = "nộp hồ sơ, cách nộp, ứng tuyển, đăng ký, quét mã qr, form, link, nộp cv, làm sao",
                        Priority = 90,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                foreach (var item in industrialFaqs)
                {
                    var existing = await context.ChatbotFaqs.FirstOrDefaultAsync(x => x.Question == item.Question);
                    if (existing == null)
                    {
                        context.ChatbotFaqs.Add(item);
                    }
                    else
                    {
                        existing.Answer = item.Answer;
                        existing.Category = item.Category;
                        existing.Keywords = item.Keywords;
                        existing.Priority = item.Priority;
                        existing.IsActive = true;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await context.SaveChangesAsync();
                Console.WriteLine("✓ Đã khởi tạo và đồng bộ 8 FAQ chuyên sâu cho ATS Industrial.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] SeedChatbotFaqsAsync error: {ex.Message}");
            }
        }
    }
}
