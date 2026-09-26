using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace OneToOneMessenger_Client.Services;

public sealed class NotificationService
{
    public bool TryShow(string title, string message, out string? error)
    {
        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = xml.GetElementsByTagName("text");
            textNodes[0].AppendChild(xml.CreateTextNode(title));
            textNodes[1].AppendChild(xml.CreateTextNode(message));

            ToastNotificationManager.CreateToastNotifier("OneToOneMessenger.Client")
                .Show(new ToastNotification(xml));
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool TryShowMessage(string sender, string? body, out string? error)
    {
        return TryShow(
            sender,
            string.IsNullOrWhiteSpace(body) ? "새 파일 메시지가 도착했습니다." : body,
            out error);
    }
}
