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
            try
            {
                // 1. Đồng bộ cấu hình __EFMigrationsHistory cho database legacy nếu có
                await EnsureMigrationsHistoryConfiguredAsync(context);

                // 2. Tự động áp dụng EF Core Migrations khi Restart Backend
                Console.WriteLine("[DbInitializer] Đang kiểm tra và tự động áp dụng EF Core Migrations...");
                await context.Database.MigrateAsync();
                Console.WriteLine("[DbInitializer] ✓ EF Core Migrations đã được áp dụng thành công!");
            }
            catch (Exception migEx)
            {
                Console.WriteLine($"[DbInitializer] MigrateAsync warning: {migEx.Message}");
            }

            // 3. Seed dữ liệu mặc định chuẩn bằng EF Core C# (Departments, Jobs, Skills, Users...)
            await SeedDepartmentsAsync(context);
            await SeedJobTypesAsync(context);
            await SeedSkillsAsync(context);
            await SeedPipelineStagesAsync(context);
            await SeedRolesAsync(context);
            await SeedUsersAsync(context);
            await SeedChatbotFaqsAsync(context);

            _isInitialized = true;
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
