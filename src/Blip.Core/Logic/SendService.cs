using Blip.Core.Protocol;

namespace Blip.Core.Logic;

public enum SendService { IMessage, SMS, RCS }

/// <summary>Which service a DM should send on. A port of collector.ts sendServiceForMessages; see the rules there.</summary>
public static class SendServiceSelector
{
    public static SendService Normalize(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "sms" => SendService.SMS,
        "rcs" => SendService.RCS,
        _ => SendService.IMessage,
    };

    /// <summary>
    /// Bubble colour: green only for a message the Mac recorded as RCS. iMessage and SMS
    /// (including unknown or missing service) draw blue. A deliberate departure from
    /// Messages, which greens SMS too. Display only; never changes which service a send uses.
    /// </summary>
    public static bool IsGreen(string? service) => Normalize(service) == SendService.RCS;

    public static string Wire(this SendService s) => s switch
    {
        SendService.SMS => "SMS",
        SendService.RCS => "RCS",
        _ => "iMessage",
    };

    /// <remarks>
    /// 1. Newest row is OUR failed iMessage to a phone → they are not on iMessage: SMS.
    /// 2. prefer_imessage: any successful iMessage in the window → iMessage.
    /// 3. Otherwise the last INBOUND service (a failed outbound never overrides it).
    /// 4. No inbound → the last outbound that succeeded.
    /// 5. Nothing → iMessage. Groups send by --chat-id and ignore all of this.
    /// </remarks>
    public static SendService For(IReadOnlyList<Message> msgs, bool preferIMessage = false)
    {
        if (msgs.Count == 0) return SendService.IMessage;
        var sorted = msgs.OrderBy(m => m.Ts, StringComparer.Ordinal).ToList();
        var newest = sorted[^1];
        var chat = newest.Chat.Length > 0 ? newest.Chat : newest.Handle ?? "";
        if (newest.Failed && Normalize(newest.Service) == SendService.IMessage && ChatIds.IsPhone(chat))
            return SendService.SMS;
        if (preferIMessage && sorted.Any(m => !m.Failed && Normalize(m.Service) == SendService.IMessage))
            return SendService.IMessage;
        for (var i = sorted.Count - 1; i >= 0; i--)
            if (!sorted[i].FromMe) return Normalize(sorted[i].Service);
        for (var i = sorted.Count - 1; i >= 0; i--)
            if (!sorted[i].Failed) return Normalize(sorted[i].Service);
        return SendService.IMessage;
    }
}
