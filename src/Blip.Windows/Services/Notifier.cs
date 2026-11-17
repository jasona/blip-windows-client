#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace Blip.Windows.Services;

/// <summary>Windows toast notifications for new inbound messages. Text is shown, never stored. No-op elsewhere.</summary>
public static class Notifier
{
    public const string ChatArg = "chat";

    /// <summary>Raised (on a background thread) when a toast is clicked, with its chat id.</summary>
#pragma warning disable CS0067   // raised only on Windows
    public static event Action<string>? Activated;
#pragma warning restore CS0067

    public static void Init()
    {
#if WINDOWS
        ToastNotificationManagerCompat.OnActivated += args =>
        {
            var parsed = ToastArguments.Parse(args.Argument);
            if (parsed.TryGetValue(ChatArg, out var chat)) Activated?.Invoke(chat);
        };
#endif
    }

    public static void Show(string chat, string title, string body)
    {
#if WINDOWS
        try
        {
            new ToastContentBuilder()
                .AddArgument(ChatArg, chat)
                .AddText(title)
                .AddText(body.Length > 200 ? body[..200] + "…" : body)
                .Show(t => t.Tag = TagFor(chat));   // one toast per conversation, replaced, not stacked
        }
        catch { /* notifications disabled or unavailable */ }
#endif
    }

    public static void Clear(string chat)
    {
#if WINDOWS
        try { ToastNotificationManagerCompat.History.Remove(TagFor(chat)); } catch { }
#endif
    }

    private static string TagFor(string chat)
    {
        // Tags are limited to 64 chars; group ids are 32 hex or chat<digits>.
        var h = (uint)chat.GetHashCode(StringComparison.Ordinal);
        return chat.Length <= 60 ? chat : $"c{h:x8}";
    }
}
