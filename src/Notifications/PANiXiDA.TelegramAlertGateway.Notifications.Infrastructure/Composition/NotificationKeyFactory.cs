using System.Security.Cryptography;
using System.Text;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

internal static class NotificationKeyFactory
{
    public static string Create(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
