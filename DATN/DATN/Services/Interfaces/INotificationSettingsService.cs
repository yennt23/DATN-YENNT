using System;
using System.Threading.Tasks;
using DATN.DTOs;

namespace DATN.Services.Interfaces
{
    public interface INotificationSettingsService
    {
        Task<NotificationSettingDto> GetSettingsAsync(Guid userId);
        Task<NotificationSettingDto> UpdateSettingsAsync(Guid userId, NotificationSettingDto settings);
    }
}
