namespace MangosSuperUI.BotLogic.Chat.Core;

// ======================== ChatEnums (CHAT_ARCHITECTURE C0, §5, §9.2, §12) ========================

/// <summary>The chat kinds the social layer arbitrates. Yell is output-only (§5.4).</summary>
public enum ChatKind
{
    Say,
    Whisper,
    Channel,
    Party
}

/// <summary>Inference traffic classes for the broker (§12). Declared in C0; broker lands in C5.</summary>
public enum TrafficClass
{
    Reactive,
    Ambient,
    Batch
}

/// <summary>
/// The chat wire maps — every int on the SAY_TEXT wire and every core enum value,
/// VERIFIED against the deployed source (2026-07-07). Never hardcode these elsewhere.
/// </summary>
public static class ChatWire
{
    // ── VERIFIED: ~/vmangos SharedDefines.h `enum ChatMsg` (deployed 1.12.1 build) ──
    //   CHAT_MSG_SAY     = 0x00
    //   CHAT_MSG_PARTY   = 0x01
    //   CHAT_MSG_YELL    = 0x05
    //   CHAT_MSG_WHISPER = 0x06
    //   CHAT_MSG_CHANNEL = 0x0E
    public const int CoreSay = 0x00;
    public const int CoreParty = 0x01;
    public const int CoreYell = 0x05;
    public const int CoreWhisper = 0x06;
    public const int CoreChannel = 0x0E;

    // ── The C#→C++ SAY_TEXT `chatType` ints — the HISTORICAL wire convention implemented
    // by AiBotAIBridge.cpp BridgeHandleSayText (VERIFIED). It does NOT equal the core enum
    // for whisper/yell; do not "fix" it — both ends must agree, and this is what's deployed.
    // Party (added C0, §5.3) uses the core CHAT_MSG_PARTY value 1 per the design doc.
    public const int WireSay = 0;        // default branch
    public const int WireParty = 1;      // == CHAT_MSG_PARTY (C0)
    public const int WireYell = 6;
    public const int WireWhisper = 7;    // requires target (player name)
    public const int WireChannel = 14;   // requires channel name

    /// <summary>Reply routing (§9.3): whisper→whisper, say→say, channel→same channel, party→party.</summary>
    public static int WireTypeFor(ChatKind kind) => kind switch
    {
        ChatKind.Whisper => WireWhisper,   // cb:fold pure wire mapping, no guid in reach
        ChatKind.Channel => WireChannel,   // cb:fold pure wire mapping, no guid in reach
        ChatKind.Party => WireParty,   // cb:fold pure wire mapping, no guid in reach
        _ => WireSay   // cb:fold pure wire mapping, no guid in reach
    };

    /// <summary>Parse the CHAT_RECV `chat_type` wire string. Unknown → Say (safest kind).</summary>
    public static ChatKind ParseKind(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "whisper" => ChatKind.Whisper,   // cb:fold parse detail, kind consumed by coordinator probes
        "channel" => ChatKind.Channel,   // cb:fold parse detail, kind consumed by coordinator probes
        "party" => ChatKind.Party,   // cb:fold parse detail, kind consumed by coordinator probes
        _ => ChatKind.Say   // cb:fold parse detail, kind consumed by coordinator probes
    };
}

/// <summary>
/// Chattiness mapping (§9.2): derives a 0..1 chattiness from bot_personality.chat_style.
/// Value set VERIFIED from PersonalityRoller.ChatStyles (BotPersonality.cs):
///   terse, chatty, leetspeak, rp, newbie, veteran, casual.
/// Feeds the urge personality term: (spontaneity*0.6 + chattiness*0.4).
/// </summary>
public static class Chattiness
{
    public static float FromChatStyle(string? chatStyle) => (chatStyle ?? "").Trim().ToLowerInvariant() switch
    {
        "terse" => 0.15f,     // says little, replies short   // cb:fold content table lookup, urge term probed at scorer
        "veteran" => 0.35f,   // seen it all, talks when it matters   // cb:fold content table lookup, urge term probed at scorer
        "casual" => 0.50f,    // the baseline player   // cb:fold content table lookup, urge term probed at scorer
        "rp" => 0.55f,        // talkative in flavor, not spammy   // cb:fold content table lookup, urge term probed at scorer
        "newbie" => 0.65f,    // asks questions, reacts to everything   // cb:fold content table lookup, urge term probed at scorer
        "leetspeak" => 0.70f, // loud presence   // cb:fold content table lookup, urge term probed at scorer
        "chatty" => 0.90f,    // never shuts up   // cb:fold content table lookup, urge term probed at scorer
        _ => 0.50f   // cb:fold content table lookup, urge term probed at scorer
    };
}
