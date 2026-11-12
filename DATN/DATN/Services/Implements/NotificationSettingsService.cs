using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DATN.Data;
using DATN.DTOs;
using DATN.Entities;
using DATN.Services.Interfaces;

namespace DATN.Services.Implements
{
    public class NotificationSettingsService : INotificationSettingsService
    {
        private readonly DATNContext _context;

        public NotificationSettingsService(DATNContext context)
        {
            _context = context;
        }

        public async Task<NotificationSettingDto> GetSettingsAsync(Guid userId)
        {
            var settings = await _context.NotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                // Create default settings
                settings = new NotificationSetting
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotifyJobOpportunities = true,
                    NotifyApplicationUpdates = true,
                    NotifySecurityAlerts = true,
                    NotifyMarketing = false,
                    ChannelEmail = true,
                    ChannelPush = true,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.NotificationSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return new NotificationSettingDto
            {
                NotifyJobOpportunities = settings.NotifyJobOpportunities,
                NotifyApplicationUpdates = settings.NotifyApplicationUpdates,
                NotifySecurityAlerts = settings.NotifySecurityAlerts,
                NotifyMarketing = settings.NotifyMarketing,
                ChannelEmail = settings.ChannelEmail,
                ChannelPush = settings.ChannelPush
            };
        }

        public async Task<NotificationSettingDto> UpdateSettingsAsync(Guid userId, NotificationSettingDto dto)
        {
            var settings = await _context.NotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                settings = new NotificationSetting
                {
                    Id = Guid.NewGuid(),
                    UserId = userId
                };
                _context.NotificationSettings.Add(settings);
            }

            // Update properties
            settings.NotifyJobOpportunities = dto.NotifyJobOpportunities;
            settings.NotifyApplicationUpdates = dto.NotifyApplicationUpdates;
            settings.NotifySecurityAlerts = dto.NotifySecurityAlerts;
            settings.NotifyMarketing = dto.NotifyMarketing;
            settings.ChannelEmail = dto.ChannelEmail;
            settings.ChannelPush = dto.ChannelPush;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return dto;
        }
    }
}
