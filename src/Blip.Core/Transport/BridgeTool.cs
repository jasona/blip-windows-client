namespace Blip.Core.Transport;

/// <summary>The tools blip-dispatch will run for the dedicated key, and nothing else.</summary>
public enum BridgeTool
{
    Imsg,
    ImsgSend,
    ImsgRead,
    ImsgReact,
    ImsgReply,
    Contacts,
    ContactSave,
    BlipCheck,
}

public static class BridgeToolNames
{
    public static string Name(this BridgeTool tool) => tool switch
    {
        BridgeTool.Imsg => "imsg",
        BridgeTool.ImsgSend => "imsg-send",
        BridgeTool.ImsgRead => "imsg-read",
        BridgeTool.ImsgReact => "imsg-react",
        BridgeTool.ImsgReply => "imsg-reply",
        BridgeTool.Contacts => "contacts",
        BridgeTool.ContactSave => "contact-save",
        BridgeTool.BlipCheck => "blip-check",
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };
}

/// <summary>Exit codes the bridge uses as a contract (sysexits.h), shared with the Linux client.</summary>
public static class BridgeExit
{
    public const int Ok = 0;
    public const int Usage = 64;        // dispatch refused / bad request
    public const int Unavailable = 69;  // Mac unreachable: grey out, retry later
    public const int TempFail = 75;     // try again (serve fallback, Messages busy)
    public const int NoPerm = 77;       // missing Accessibility / TCC grant on the Mac
    public const int Config = 78;       // bridge.conf refused, or a feature is switched off
}
