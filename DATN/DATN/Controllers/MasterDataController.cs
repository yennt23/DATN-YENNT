using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DATN.Services.Interfaces;

namespace DATN.Controllers
{
    [Route("api/master-data")] // Đổi từ [controller] sang master-data
    [ApiController]
    [AllowAnonymous] // Cho phép truy cập không cần authentication
    public class MasterDataController : ControllerBase
    {
        private readonly IMasterDataService _masterDataService;

        public MasterDataController(IMasterDataService masterDataService)
        {
            _masterDataService = masterDataService;
        }

        /// <summary>
        /// Lấy danh sách tất cả Skills để hiển thị trên dropdown
        /// </summary>
        /// <returns>Danh sách Skills</returns>
        [HttpGet("skills")]
        public async Task<IActionResult> GetSkills()
        {
            try
            {
                var skills = await _masterDataService.GetAllSkillsAsync();
                return Ok(skills);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách Skills", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách tất cả JobTypes để hiển thị trên dropdown
        /// </summary>
        /// <returns>Danh sách JobTypes</returns>
        [HttpGet("job-types")]
        public async Task<IActionResult> GetJobTypes()
        {
            try
            {
                var jobTypes = await _masterDataService.GetAllJobTypesAsync();
                return Ok(jobTypes);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách JobTypes", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách tất cả Tỉnh/Thành phố từ API công khai
        /// </summary>
        /// <returns>Danh sách Tỉnh/Thành phố</returns>
        [HttpGet("provinces")]
        public async Task<IActionResult> GetProvinces()
        {
            try
            {
                var provinces = await _masterDataService.GetProvincesAsync();
                return Ok(provinces);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách tỉnh/thành phố", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách Phường/Xã theo mã tỉnh (V2 API - bỏ cấp huyện)
        /// </summary>
        /// <param name="provinceCode">Mã tỉnh</param>
        /// <returns>Danh sách Phường/Xã</returns>
        [HttpGet("provinces/{provinceCode}/wards")]
        public async Task<IActionResult> GetWardsByProvince(int provinceCode)
        {
            try
            {
                var wards = await _masterDataService.GetWardsAsync(provinceCode);
                return Ok(wards);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách phường/xã", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách các Phân xưởng / Phòng ban sản xuất trong hệ thống
        /// </summary>
        /// <returns>Danh sách phòng ban/phân xưởng</returns>
        [HttpGet("departments")]
        public async Task<IActionResult> GetDepartments()
        {
            try
            {
                var departments = await _masterDataService.GetDepartmentsAsync();
                return Ok(departments);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách phòng ban / phân xưởng", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách ngày lễ Việt Nam theo năm
        /// </summary>
        [HttpGet("vietnam-holidays/{year}")]
        public IActionResult GetVietnamHolidays(int year)
        {
            // Danh sách ngày lễ chính thức của Việt Nam theo định dạng DD-MM
            string[] holidays;
            switch (year)
            {
                case 2025:
                    holidays = new[]
                    {
                        "01-01", // Tết Dương Lịch
                        "25-01", "26-01", "27-01", "28-01", "29-01", "30-01", "31-01", "01-02", "02-02", // Tết Nguyên Đán Ất Tỵ
                        "07-04", // Giỗ Tổ Hùng Vương (10/3 Âm lịch)
                        "30-04", // Ngày Giải phóng miền Nam
                        "01-05", "02-05", // Quốc tế Lao động và nghỉ bù
                        "01-09", "02-09", "03-09" // Quốc khánh
                    };
                    break;
                case 2026:
                    holidays = new[]
                    {
                        "01-01", "02-01", // Tết Dương Lịch
                        "15-02", "16-02", "17-02", "18-02", "19-02", "20-02", "21-02", "22-02", "23-02", // Tết Bính Ngọ
                        "26-04", // Giỗ Tổ Hùng Vương (10/3 Âm lịch)
                        "30-04", // Ngày Giải phóng
                        "01-05", // Quốc tế Lao động
                        "01-09", "02-09", "03-09" // Quốc khánh
                    };
                    break;
                case 2027:
                    holidays = new[]
                    {
                        "01-01", // Tết Dương Lịch
                        "05-02", "06-02", "07-02", "08-02", "09-02", "10-02", "11-02", "12-02", // Tết Đinh Mùi
                        "15-04", // Giỗ Tổ Hùng Vương
                        "30-04", // Ngày Giải phóng
                        "01-05", // Quốc tế Lao động
                        "01-09", "02-09", "03-09" // Quốc khánh
                    };
                    break;
                default:
                    holidays = new[]
                    {
                        "01-01", // Tết Dương Lịch
                        "17-02", "18-02", "19-02", "20-02", "21-02", "22-02", "23-02", // Tết Nguyên Đán
                        "30-04", // Ngày Giải phóng
                        "01-05", // Quốc tế Lao động
                        "02-09", "03-09" // Quốc khánh
                    };
                    break;
            }

            return Ok(new { year, count = holidays.Length, holidays });
        }
    }
}
