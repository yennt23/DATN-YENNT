using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DATN.Data;

#nullable disable

namespace DATN.Migrations
{
    [DbContext(typeof(DATNContext))]
    [Migration("20261117221000_MakeFilesFileNameNullable")]
    public partial class MakeFilesFileNameNullable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.Files', 'U') IS NOT NULL
BEGIN
    -- 1. Nếu bảng Files đã có cột FileName, cho phép NULL và bổ sung Default Constraint
    IF COL_LENGTH('dbo.Files', 'FileName') IS NOT NULL
    BEGIN
        -- Cập nhật dữ liệu cũ nếu FileName bị NULL
        EXEC('UPDATE dbo.Files SET FileName = ISNULL(OriginalFileName, ISNULL(StoredFileName, ''file'')) WHERE FileName IS NULL');

        -- Đổi cột FileName cho phép NULL
        ALTER TABLE dbo.Files ALTER COLUMN FileName NVARCHAR(260) NULL;

        -- Thêm Default Value rỗng nếu chưa có ràng buộc Default
        IF NOT EXISTS (
            SELECT 1 FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID('dbo.Files') AND c.name = 'FileName'
        )
        BEGIN
            ALTER TABLE dbo.Files ADD CONSTRAINT DF_Files_FileName DEFAULT ('') FOR FileName;
        END;
    END
    ELSE
    BEGIN
        -- 2. Nếu chưa có cột FileName thì tạo cột FileName cho phép NULL
        ALTER TABLE dbo.Files ADD FileName NVARCHAR(260) NULL;
    END;
END;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
