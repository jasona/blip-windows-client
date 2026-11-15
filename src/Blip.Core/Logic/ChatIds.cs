using System.Text.RegularExpressions;

namespace Blip.Core.Logic;

/// <summary>Chat identifier rules, ported from collector.ts so both clients agree.</summary>
public static partial class ChatIds
{
    [GeneratedRegex(@"^\+?[0-9]{3,15}$")] private static partial Regex PhoneRe();
    [GeneratedRegex(@"^[0-9a-f]{32}$", RegexOptions.IgnoreCase)] private static partial Regex HexRe();
    [GeneratedRegex(@"^chat[0-9]+$", RegexOptions.IgnoreCase)] private static partial Regex ChatNumRe();
    [GeneratedRegex(@"^[^@\s\x00-\x1f\x7f]+@[^@\s\x00-\x1f\x7f]+$")] private static partial Regex EmailRe();
    [GeneratedRegex(@"^(?:chat[0-9]{1,40}|[a-fA-F0-9]{32})$")] private static partial Regex GroupIdRe();

    public static bool IsPhone(string id) => PhoneRe().IsMatch(id);

    /// <summary>collector.ts isGroupChat: anything that is not a phone or an email is a group.</summary>
    public static bool IsGroupChat(string chat)
    {
        if (PhoneRe().IsMatch(chat) || chat.IndexOf('@') > 0) return false;
        return HexRe().IsMatch(chat) || ChatNumRe().IsMatch(chat) || chat != "";
    }

    /// <summary>An id safe to hand to the bridge as a chat argument (collector.ts validChatId).</summary>
    public static bool IsValid(string id) =>
        id.Length is > 0 and <= 512 && (PhoneRe().IsMatch(id) || EmailRe().IsMatch(id) || GroupIdRe().IsMatch(id));

    /// <summary>"+15551234567" → "(555) 123-4567" for NANP numbers; anything else unchanged.</summary>
    public static string Pretty(string handle)
    {
        var digits = new string(handle.Where(char.IsAsciiDigit).ToArray());
        if (handle.StartsWith("+1") && digits.Length == 11)
            return $"({digits[1..4]}) {digits[4..7]}-{digits[7..]}";
        return handle;
    }
}
