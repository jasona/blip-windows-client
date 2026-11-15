using Microsoft.Toolkit.Uwp.Notifications;

namespace Blip.Windows.Services;

/// <summary>Windows toast notifications for new inbound messages. Text is shown, never stored.</summary>
public static class Notifier
{
    public const string ChatArg = "chat";

    public static void Show(string chat, string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument(ChatArg, chat)
                .AddText(title)
                .AddText(body.Length > 200 ? body[..200] + "…" : body)
                .Show(t => t.Tag = TagFor(chat));   // one toast per conversation, replaced, not stacked
        }
        catch { /* notifications disabled or unavailable */ }
    }

    public static void Clear(string chat)
    {
        try { ToastNotificationManagerCompat.History.Remove(TagFor(chat)); } catch { }
    }

    private static string TagFor(string chat)
    {
        // Tags are limited to 64 chars; group ids are 32 hex or chat<digits>.
        var h = (uint)chat.GetHashCode(StringComparison.Ordinal);
        return chat.Length <= 60 ? chat : $"c{h:x8}";
    }
}
