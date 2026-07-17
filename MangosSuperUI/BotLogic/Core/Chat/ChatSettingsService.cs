using System.Text.Json;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.BotLogic.Chat.Core;

// ======================== §14.4 registry (authoritative defaults) ========================

/// <summary>
/// One tunable: key, UI group, control type, seeded default, meaning, slider range, and
/// long-form help.
///
/// Meaning is the terse one-liner shown inline on the Feel page. Help (added 2026-07-13)
/// is the plain-English "what does this actually do, and what does turning it up/down feel
/// like in-game" text behind the row's (?) — written for any operator running SuperUI on
/// their own box, not just the author. WoW and LLM terms are fair game (this is an admin
/// surface); doc-internal shorthand like "D17" or "§9.2" is not — that's exactly the
/// ancient-Egyptian problem the Help field exists to fix. Help travels with the knob in
/// code, so it can never drift from the DB the way a separate help table would.
/// </summary>
public sealed record ChatSettingDef(string Key, string Group, string Type, string Default,
                                    string Meaning, float Min = 0, float Max = 1, float Step = 0.01f,
                                    string Help = "");

/// <summary>
/// The CHAT_ARCHITECTURE §14.4 settings registry — the single source of truth for every
/// tunable's key, group, default, and UI shape. BotBrainDbInit seeds chat_settings from
/// this; the Feel page renders its controls from this; ChatSettingsService falls back to
/// this if a row ever goes missing. Locked defaults — change values in the DB, not here.
/// </summary>
public static class ChatSettingsRegistry
{
    public static readonly IReadOnlyList<ChatSettingDef> All = new List<ChatSettingDef>
    {
        // ── global (kill switches on Capacity; active_preset is display-only) ──
        new("global.chat_enabled",            "global", "bool",  "true",  "master kill switch",
            Help: "The big red switch for the entire chat system. OFF = bots go completely silent — no replies, " +
                  "no ambient chatter, nothing — but they keep playing normally. Takes effect within about 5 seconds. " +
                  "This is what you flip if the LLM endpoint dies or the bots start saying something you don't want."),
        new("global.ambient_enabled",         "global", "bool",  "true",  "ambient lane kill switch",
            Help: "Turns OFF the bot-to-bot background chatter (the scripted little exchanges that make a zone feel " +
                  "populated) while leaving REACTIVE replies alive — bots still answer real players who talk to them. " +
                  "Flip this off if the world feels too busy/noisy but you still want bots to respond when spoken to."),
        new("global.active_preset",           "global", "label", "2005 Authentic", "display only",
            Help: "Just shows which preset was last applied. Not a control — pick and apply presets from the bar at " +
                  "the top of this page."),

        // ── density: HOW MUCH ambient chatter happens, and where/when ──
        new("density.ambient_base_per_zone_hour",   "density", "int",   "12",  "exchanges/zone-hour pre-multipliers", 0, 60, 1,
            Help: "The master volume for ambient chatter: how many scripted bot-to-bot /say exchanges a zone gets per " +
                  "hour BEFORE the presence, empty-zone, and time-of-day multipliers below scale it. Everything else in " +
                  "this section multiplies this number. ~4 feels like a quiet realm, ~12 is lived-in, 30+ is a crowded " +
                  "server. Set to 0 to kill local ambient talk entirely (channel chatter is separate, below)."),
        new("density.presence_mult",                "density", "float", "1.5", "zone has ≥1 real player", 0, 3, 0.05f,
            Help: "Multiplies the ambient chatter rate in any zone that has at least one REAL player in it. Above 1.0 " +
                  "(default 1.5) bots get chattier where someone can actually see and enjoy it, and quieter in empty " +
                  "zones — spending your inference where it lands. Below 1.0 would make bots go shy around real players; " +
                  "there's rarely a reason for that."),
        new("density.empty_zone_mult",              "density", "float", "0.15","D17 trickle", 0, 1, 0.01f,
            Help: "Multiplies the ambient rate in zones with NO real players in them. Kept low on purpose — this is " +
                  "just a trickle so a player walking into an empty zone finds a little life instead of a ghost town, " +
                  "without wasting inference on chatter nobody's there to read. 0 = dead-silent empty zones."),
        new("density.diurnal_curve",                "density", "curve", "0.2,0.15,0.3,0.7,1.0,0.8", "6 points at 02/06/10/14/18/22h, lerp", 0, 2, 0.05f,
            Help: "Your server's day/night rhythm. Six multipliers on the ambient rate at 2am, 6am, 10am, 2pm, 6pm, " +
                  "and 10pm (server local time); the system smoothly blends between them for every hour in between. " +
                  "1.0 = full rate, 0.5 = half. The default dips low overnight (2–6am) and peaks in the evening (6pm) " +
                  "to mimic a real player population logging on after work/school. Flatten every point to ~1.0 if you " +
                  "want steady 24/7 chatter."),
        new("density.channel_msgs_per_hour",        "density", "int",   "30",  "ambient General/Trade lines per zone", 0, 120, 1,
            Help: "Separate from the local /say exchanges above: this is how many lines bots post to the global-ish " +
                  "channels (General, Trade) per zone per hour. This is the 'Trade chat scroll' feel. Turn it up for a " +
                  "busy, always-scrolling Trade; down (or 0) if the channels feel spammy or repetitive."),
        new("density.max_parallel_ambient_per_zone","density", "int",   "2",   "concurrent scripted exchanges", 0, 8, 1,
            Help: "How many separate bot-to-bot conversations can be happening AT THE SAME TIME in one zone. Keeps ten " +
                  "bots from all 'talking' at once and turning the Barrens into unreadable noise. 2–3 reads like a " +
                  "couple of little clusters chatting; higher gets chaotic fast."),

        // ── responsiveness: WHEN a bot decides to reply. These weights feed the 'urge' score;
        //    a bot speaks when urge ≥ urge_threshold. Raising a weight makes that factor matter more. ──
        new("responsiveness.urge_threshold",        "responsiveness", "float", "1.0", "§9.2 speak threshold", 0, 3, 0.05f,
            Help: "The bar a bot's 'urge to speak' has to clear before it actually replies. Every factor below adds to " +
                  "that urge; when the total clears this threshold, the bot talks. LOWER = chattier, bots pipe up more " +
                  "readily. HIGHER = bots only respond when something really pulls at them (they're addressed directly, " +
                  "mid-conversation with a friend, etc). The single biggest 'are my bots too talkative / too quiet' dial."),
        new("responsiveness.w_addr",                "responsiveness", "float", "2.0", "§9.2 weight: addressed", 0, 3, 0.05f,
            Help: "How much being spoken to directly (by name, or a reply aimed at them) pushes a bot toward replying. " +
                  "It's the strongest factor by default — you say something to a bot, it should almost always answer. " +
                  "Lower it and bots get aloof even when addressed; there's rarely a reason to."),
        new("responsiveness.w_thread",              "responsiveness", "float", "1.2", "§9.2 weight: live thread", 0, 3, 0.05f,
            Help: "How much being already IN a live back-and-forth pulls a bot to keep going. Higher = conversations " +
                  "have momentum and don't fizzle after one line; lower = bots drop threads quickly and chats feel choppy."),
        new("responsiveness.w_rel",                 "responsiveness", "float", "0.6", "§9.2 weight: relationship", 0, 3, 0.05f,
            Help: "How much a bot's built-up familiarity with whoever's talking nudges it to respond. Bots remember who " +
                  "they've talked to (see the Era & Memory section); crank this and they visibly favor 'friends' and " +
                  "regulars; zero it and everyone's a stranger every time."),
        new("responsiveness.w_pers",                "responsiveness", "float", "0.5", "§9.2 weight: personality", 0, 3, 0.05f,
            Help: "How much a bot's own personality (chatty vs terse, from its persona card) sways whether it speaks up " +
                  "unprompted. Higher = the loud personas dominate and the shy ones lurk, so the fleet feels more varied; " +
                  "lower = everyone's about equally likely to chime in regardless of character."),
        new("responsiveness.w_prox",                "responsiveness", "float", "0.4", "§9.2 weight: proximity", 0, 3, 0.05f,
            Help: "How much simply being near the person/bot who spoke adds to the urge to reply. This is the 'I'm " +
                  "standing right here so I'll join in' factor. Higher makes clusters of nearby bots more reactive to " +
                  "local chatter; lower makes distance irrelevant."),
        new("responsiveness.whisper_always_replies","responsiveness", "bool",  "true","whispers skip urge scoring",
            Help: "ON = a private whisper to a bot ALWAYS gets a reply, skipping all the urge math above. This is " +
                  "almost always what you want — whispering someone and getting silence feels broken. OFF makes bots " +
                  "weigh whispers like any other message, so they can ignore you even in a private message."),
        new("responsiveness.bot_cooldown_s",        "responsiveness", "int",   "8",   "per-bot line cooldown (seconds)", 0, 60, 1,
            Help: "Minimum seconds a single bot waits between its own lines. Stops one bot from machine-gunning message " +
                  "after message. Higher = more measured, human-paced individual bots; lower = a bot can fire rapidly in " +
                  "a heated exchange. Note this is per-bot; zone-wide flood limits live in the Budgets section."),

        // ── noise: the randomness and anti-spam guardrails ──
        new("noise.w_noise",                      "noise", "float", "0.35", "urge random term (D18)", 0, 1, 0.01f,
            Help: "A dose of pure randomness added to every bot's urge to speak, so replies aren't perfectly " +
                  "predictable. Higher = more spontaneous, occasionally-surprising chatter (and the odd non-sequitur); " +
                  "lower = bots behave more deterministically off the weighted factors alone. Small nudges go a long way."),
        new("noise.ignore_chance",                "noise", "float", "0.06", "post-threshold ignore roll", 0, 1, 0.01f,
            Help: "Even after a bot decides it WOULD reply, this is the chance it just... doesn't — the way a real " +
                  "person half-reads chat and lets one slide. Keeps things from feeling like every line demands an " +
                  "answer. 0.06 = ignores about 1 in 17. Raise for a more distracted, less clingy world."),
        new("noise.max_parallel_convos_per_spot", "noise", "int",   "2",    "crosstalk allowance", 0, 8, 1,
            Help: "How many overlapping conversations are allowed among bots crowded in the same spot before new ones " +
                  "get suppressed. This is the 'everyone talking over each other' limiter for reactive chat (the ambient " +
                  "equivalent is max_parallel_ambient_per_zone). Higher = busier, messier local chat."),
        new("noise.max_bot_chain_depth",          "noise", "int",   "2",    "D16 hard cap", 0, 5, 1,
            Help: "SAFETY RAIL. When a bot replies to another bot, that's a chain; this hard-caps how many bot→bot→bot " +
                  "hops can happen before the chain is forced to stop. Prevents two bots from talking to each other " +
                  "forever and burning your GPU. 2 means: player speaks, bot A answers, bot B chimes in on A, done. " +
                  "Leave this low unless you really know why you're raising it."),
        new("noise.chain_penalty",                "noise", "float", "0.8",  "urge penalty per chain depth", 0, 2, 0.05f,
            Help: "Before the hard cap above kicks in, each step deeper into a bot-to-bot chain subtracts this much from " +
                  "the urge to continue — so bot conversations naturally peter out instead of slamming into the cap. " +
                  "Higher = bot chatter dies down faster and more gracefully; lower = bots keep a bot-to-bot thread going " +
                  "right up until the hard limit stops them."),

        // ── voice: HOW the bots type (surface feel), plus the reply-timing envelope ──
        new("voice.wpm_mult",             "voice", "float", "1.0", "global typing speed scale", 0, 3, 0.05f,
            Help: "Fleet-wide multiplier on every persona's typing speed, which drives how long the '…is typing' delay " +
                  "lasts before a line appears. 1.0 = each bot uses its own persona's WPM; 0.5 = everyone types half as " +
                  "fast (longer, more deliberate delays); 2.0 = snappy. Adjust if replies feel too sluggish or too instant."),
        new("voice.typo_mult",            "voice", "float", "1.0", "global typo scale", 0, 3, 0.05f,
            Help: "Fleet-wide multiplier on how often bots make and 'leave in' typos. 1.0 = each persona's own typo rate; " +
                  "0 = everyone types cleanly; 2.0 = twice as sloppy. Turn down for an RP server that wants polish, up for " +
                  "gritty 2005-teenager authenticity. (RP-Heavy preset sets this to 0.5.)"),
        new("voice.split_aggressiveness", "voice", "float", "1.0", "scales split_threshold inverse", 0, 3, 0.05f,
            Help: "How eagerly a longer reply gets broken into two back-to-back messages (the way people fire off a " +
                  "thought, then a follow-up). Higher = splits more often and at shorter lengths, so chat feels more " +
                  "staccato and 'typing in bursts'; lower = bots send longer single lines. It scales the split threshold " +
                  "inversely, so 2.0 roughly halves the length needed to trigger a split."),
        new("voice.banter_intensity",     "voice", "float", "0.5", "0 wholesome → 1 edgy (above the floor)", 0, 1, 0.01f,
            Help: "How much attitude/profanity the bots put out, ON TOP of a hard content floor that always blocks slurs " +
                  "and sexual content no matter what. This scales BOTH the tone the model is asked for and the swearing " +
                  "post-pass. 0 = wholesome, bots barely curse even if their persona would. 0.5 = neutral (personas swear " +
                  "as written). 1.0 = everyone's dialed up and edgy. This does NOT unlock the floored stuff — that stays " +
                  "blocked at every setting."),
        new("voice.library_target",       "voice", "int",   "300", "§6.3 voice library size", 50, 1000, 10,
            Help: "How many distinct persona 'voice cards' to generate when you build the voice library (on the Capacity " +
                  "page). This is the pool every bot draws its personality from — bigger = more variety across the fleet " +
                  "and less chance two bots feel like twins, but a longer one-time build. 300 is plenty for a few hundred " +
                  "bots. Changing this only matters at the next library build."),
        new("voice.hold_min_ms",          "voice", "int",   "2000", "reply delay floor — lowest possible think+type hold", 500, 10000, 100,
            Help: "The FASTEST a bot can possibly reply, in milliseconds, even for a one-word 'lol'. Floors the combined " +
                  "think-time + type-time delay so nothing comes back instantly and robotic. 2000 = 2 seconds minimum. " +
                  "Raise if replies still feel too twitchy; this is the fast end of the timing envelope."),
        new("voice.hold_max_ms",          "voice", "int",   "45000", "reply delay ceiling before alt-tab tails", 5000, 120000, 1000,
            Help: "The SLOWEST a normal reply will be held before sending, in milliseconds — the ceiling on think+type " +
                  "delay. 45000 = 45 seconds. (Separately, a bot may occasionally tack on a much longer 'alt-tabbed away' " +
                  "pause for realism.) Lower this if bots sometimes take uncomfortably long to answer; it's the slow end " +
                  "of the timing envelope."),

        // ── topicality: WHAT the bots talk about ──
        new("topicality.ingame_ratio",             "topicality", "float",  "0.65", "in-game vs out-of-game talk", 0, 1, 0.01f,
            Help: "The mix of game talk vs real-life talk in ambient chatter. 1.0 = bots only ever discuss WoW (quests, " +
                  "loot, dungeons); 0.0 = they only chat about their fictional real lives (work, weekend, that show they " +
                  "watched); 0.65 = mostly game with real-life color. Push toward 1.0 for an immersive/RP realm, down for " +
                  "the goofy 2005 'my mom needs the phone line' texture. (RP-Heavy preset sets 0.75.)"),
        new("topicality.weights",                  "topicality", "string", "loot:3,quests:3,class:2,reallife:2,popculture:1,server:2", "ambient topic categories",
            Help: "Relative frequency of each ambient topic, as name:weight pairs. Higher weight = that topic comes up " +
                  "more. Bump 'server:5' to make bots gossip about the realm itself, 'popculture:0' to strip out " +
                  "2005-movie-and-music chatter, and so on. It's a ratio, not percentages — the numbers just need to be " +
                  "relative to each other. Keep the name:number,name:number format exactly."),
        new("topicality.lifesim_event_daily_chance","topicality","float",  "0.08", "§8 (alias lifesim.event_daily_chance)", 0, 1, 0.01f,
            Help: "Daily chance each bot has a little 'life event' happen (got a new job, bad day, visiting family) that " +
                  "then colors its chat and mood for a while. 0.08 = about one such beat every couple of weeks per bot. " +
                  "Higher = bots' fictional lives feel more eventful and their moods shift more often; 0 = static personas."),

        // ── memory: what bots REMEMBER, and how they forget ──
        new("memory.overhear_log_chance",     "memory", "float", "0.15", "§7.2 overheard Tier-1 sampling", 0, 1, 0.01f,
            Help: "Chance a bot bothers to remember a line it merely OVERHEARD (wasn't part of the conversation). Bots " +
                  "always remember chats they took part in; this is about ambient eavesdropping. Higher = bots pick up on " +
                  "more zone gossip they weren't in; lower = they only remember their own conversations. Also throttled " +
                  "by the per-hour valve below so it can't balloon the log."),
        new("memory.t1_lines_per_bot_hour",   "memory", "int",   "120",  "§7.2 valve", 0, 600, 10,
            Help: "Hard cap on how many chat lines each bot writes to its detailed (verbatim) memory per hour, so a busy " +
                  "zone can't flood the database. Lines the bot actually participated in always get through; overheard " +
                  "lines are the first to be dropped when the cap is hit. Raise only if you have DB headroom and want " +
                  "richer recall."),
        new("memory.t1_retention_days",       "memory", "int",   "14",   "§7.5 verbatim retention", 1, 90, 1,
            Help: "How many days a bot keeps the exact, word-for-word text of a remembered line before it's compacted " +
                  "into a summary and the verbatim copy is dropped. Longer = bots can quote you back precisely for weeks " +
                  "(more storage); shorter = detail fades faster into gist. The gist/relationship memory lives on past " +
                  "this via the settings below."),
        new("memory.compaction_cadence_hours","memory", "int",   "24",   "§7.5 batch cadence", 1, 168, 1,
            Help: "How often (in hours) the background job runs that boils old verbatim chat down into compact summaries " +
                  "and updates relationship strengths. 24 = nightly. This is a maintenance batch job; there's rarely a " +
                  "reason to change it. More frequent = smoother memory upkeep but more batch load."),
        new("memory.compaction_min_rows",     "memory", "int",   "60",   "§7.5 skip below this", 0, 500, 5,
            Help: "The compaction job skips any bot with fewer than this many new remembered lines, so it doesn't waste " +
                  "an LLM call summarizing a bot that barely talked. Higher = only chatty bots get their memories " +
                  "compacted (cheaper); lower = even quiet bots get tidied up."),
        new("memory.recency_halflife_days",   "memory", "int",   "21",   "§7.3 strength decay", 1, 90, 1,
            Help: "How fast a relationship 'cools off' when a bot stops interacting with someone. In this many days, an " +
                  "un-refreshed relationship's strength halves. Shorter = bots forget acquaintances quickly and you have " +
                  "to keep showing up to stay a 'regular'; longer = relationships are stickier and more forgiving of gaps."),
        new("memory.forget_floor",            "memory", "float", "0.15", "§7.5 forget below strength", 0, 1, 0.01f,
            Help: "The relationship-strength level below which a bot fully forgets someone (the memory gets pruned). " +
                  "Combined with the half-life decay above: weak, faded connections eventually drop off entirely rather " +
                  "than lingering forever. Higher = bots forget marginal acquaintances sooner; 0 = nothing is ever pruned " +
                  "by weakness alone."),
        new("memory.forget_after_days",       "memory", "int",   "45",   "§7.5 forget after silence", 1, 365, 1,
            Help: "A hard 'use it or lose it' timer: if a bot hasn't interacted with someone at all for this many days, " +
                  "the relationship is dropped regardless of how strong it once was. Longer = bots remember you after a " +
                  "long absence (that returning-player 'hey, you're back!' feel); shorter = clean slate after a break."),

        // ── era ──
        new("era.scrub_enabled", "era", "bool", "true", "§10.4 step 7 anachronism scrub",
            Help: "ON = a filter catches and removes modern references the model might slip in (smartphones, streaming, " +
                  "post-2005 games/memes) so the world stays period-authentic to 2005. Turn OFF only if you're running a " +
                  "non-era server and WANT bots referencing modern things. Requires an era pack to be built to do much."),

        // ── barks ──
        new("barks.ding_chance", "barks", "float", "0.35", "§9.6 level-up bark chance", 0, 1, 0.01f,
            Help: "Chance a bot says something when it dings (levels up) — a little 'woo ding!' in chat. 0.35 = about a " +
                  "third of level-ups get a reaction. Higher = more celebratory, more chat volume around leveling; 0 = " +
                  "bots level silently."),

        // ── budget: hard zone-wide flood limits (spam insurance on top of everything above) ──
        new("budget.bot_lines_per_min",          "budget", "int", "4",  "per-bot token bucket", 0, 60, 1,
            Help: "Hard ceiling on how many lines any single bot can send per minute, no matter what the urge math wants. " +
                  "This is a safety cap, not a feel dial — it just stops a runaway bot from spamming. The per-bot cooldown " +
                  "in Responsiveness shapes normal pacing; this catches the extremes."),
        new("budget.zone_say_lines_per_min",     "budget", "int", "20", "per-zone say bucket", 0, 120, 1,
            Help: "Hard ceiling on total local /say lines from ALL bots in one zone per minute. Zone-wide flood " +
                  "insurance: even if a hundred bots are packed in, local chat can't exceed this. Raise for a genuinely " +
                  "packed hub, lower to guarantee readable local chat."),
        new("budget.zone_channel_lines_per_min", "budget", "int", "10", "per-zone channel bucket", 0, 120, 1,
            Help: "Hard ceiling on bot lines to the General/Trade channels from one zone per minute. Keeps Trade chat " +
                  "from scrolling faster than a human could read. Pairs with density.channel_msgs_per_hour, which sets " +
                  "the target rate; this is the hard cap it can never blow past."),
        new("budget.zone_party_lines_per_min",   "budget", "int", "10", "per-zone party bucket", 0, 120, 1,
            Help: "Hard ceiling on bot party-chat lines in one zone per minute. Same flood-insurance idea as the others, " +
                  "scoped to party chat. Rarely needs touching unless you run a lot of bot-filled parties."),

        // ── lifesim ──
        new("lifesim.active_window_days", "lifesim", "int", "14", "§8 scope guard", 1, 60, 1,
            Help: "How many days a 'life event' (see topicality.lifesim_event_daily_chance) keeps coloring a bot's mood " +
                  "and chat before it fades and stops being brought up. 14 = a bad week at work echoes for about two " +
                  "weeks. Longer = life events have lasting emotional weight; shorter = bots bounce back fast."),

        // ── pairing: how bots choose ambient conversation partners ──
        new("pairing.rel_bias",   "pairing", "float", "3.0", "§9.5 D5 relationship bias", 0, 10, 0.1f,
            Help: "How strongly bots prefer to start ambient conversations with bots they already 'know' versus random " +
                  "strangers. Higher = friend groups form and persist, the same bots keep hanging out (feels social and " +
                  "organic); lower = everyone mixes randomly and no cliques emerge. 3.0 gives noticeable but not rigid " +
                  "clustering."),
        new("pairing.level_band", "pairing", "int",   "4",   "± levels for ambient pairing", 0, 10, 1,
            Help: "How far apart in character level two bots can be and still be paired for an ambient chat. 4 = a level " +
                  "10 and a level 14 can strike up a conversation, but not a 10 and a 40. Keeps pairings believable " +
                  "(people near the same content mingle). Wider = anyone talks to anyone; narrower = tight level-based " +
                  "cliques."),

        // ── tier0: the short-term 'what was just said' conversation window ──
        new("tier0.window_lines", "tier0", "int", "10", "§7.1 live window lines", 2, 30, 1,
            Help: "How many recent lines a bot keeps in immediate working memory for an active conversation — the context " +
                  "it 'sees' when composing its next reply. More = bots track longer exchanges and stay coherent over a " +
                  "bigger back-and-forth (costs more tokens per reply); fewer = they only react to the last couple of " +
                  "lines and lose the thread sooner."),
        new("tier0.ttl_min",      "tier0", "int", "10", "§7.1 live window TTL (minutes)", 1, 60, 1,
            Help: "How long (minutes) a conversation stays 'live' in that working memory after the last line before it's " +
                  "considered over and cleared. Within this window a bot treats new lines as continuing the same chat; " +
                  "after it, a fresh message starts clean. Longer = bots pick threads back up after a pause; shorter = " +
                  "conversations reset quickly."),
    };

    public static readonly IReadOnlyDictionary<string, ChatSettingDef> ByKey =
        All.ToDictionary(d => d.Key, d => d);
}

// ======================== §14.2 built-in presets ========================

/// <summary>
/// The five built-in presets (seeded builtin=1). Each is a name→value map bulk-written
/// into GLOBAL scope on apply (zone overrides untouched). "2005 Authentic" carries the
/// FULL default set so applying it is a complete reset (§14.2: "the defaults").
/// Derived multiplier values (Quiet ×0.3, Bustling ×2.5, …) were computed from the doc's
/// factors against §14.4 defaults — implementer-computed, flagged for operator review.
/// </summary>
public static class ChatPresets
{
    public static IReadOnlyDictionary<string, Dictionary<string, string>> BuiltIn { get; } = Build();

    private static Dictionary<string, Dictionary<string, string>> Build()
    {
        // 2005 Authentic = every §14.4 default except the display-only active_preset row.
        var authentic = ChatSettingsRegistry.All
            .Where(d => d.Key != "global.active_preset")
            .ToDictionary(d => d.Key, d => d.Default);

        return new Dictionary<string, Dictionary<string, string>>
        {
            ["2005 Authentic"] = authentic,

            ["Quiet Realm"] = new()   // density ×0.3, ignore 0.12, threshold 1.3
            {
                ["density.ambient_base_per_zone_hour"] = "4",   // 12 × 0.3
                ["density.channel_msgs_per_hour"] = "9",        // 30 × 0.3
                ["noise.ignore_chance"] = "0.12",
                ["responsiveness.urge_threshold"] = "1.3",
            },

            ["Bustling City"] = new() // density ×2.5, crosstalk 3, channel budgets ×2
            {
                ["density.ambient_base_per_zone_hour"] = "30",  // 12 × 2.5
                ["density.channel_msgs_per_hour"] = "75",       // 30 × 2.5
                ["noise.max_parallel_convos_per_spot"] = "3",
                ["budget.zone_channel_lines_per_min"] = "20",   // 10 × 2
            },

            ["RP-Heavy"] = new()      // in-game 0.75, banter low, typo ×0.5
            {
                ["topicality.ingame_ratio"] = "0.75",
                ["voice.banter_intensity"] = "0.2",
                ["voice.typo_mult"] = "0.5",
            },

            ["Minimal"] = new()       // ambient off, whisper_always on, everything else quiet
            {
                ["global.ambient_enabled"] = "false",
                ["responsiveness.whisper_always_replies"] = "true",
                ["responsiveness.urge_threshold"] = "1.5",
                ["density.ambient_base_per_zone_hour"] = "0",
                ["density.channel_msgs_per_hour"] = "0",
                ["budget.bot_lines_per_min"] = "2",
            },
        };
    }
}

// ======================== ChatSettingsService (§14.1) ========================

/// <summary>
/// Reads chat_settings into an immutable snapshot with a 5 s TTL (hot-apply, D10).
/// Resolution: `zone:&lt;id&gt;` overrides `global` for that zone. Writes go through the
/// controller (which audit-logs at [CHAT-SET]); this service performs the upsert and
/// invalidates the snapshot so the next read is fresh. Missing rows fall back to the
/// §14.4 registry default (belt-and-braces — the seed makes this unreachable normally).
/// </summary>
public class ChatSettingsService
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(5);

    private readonly ConnectionFactory _db;
    private readonly ILogger<ChatSettingsService> _logger;
    private readonly object _gate = new();

    private volatile Dictionary<(string Scope, string Name), string>? _snapshot;
    private DateTime _snapshotUtc = DateTime.MinValue;

    public ChatSettingsService(ConnectionFactory db, ILogger<ChatSettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---------- reads ----------

    /// <summary>Zone→global resolution: zone:&lt;id&gt; row wins, else global, else registry default.</summary>
    public string? Get(int zoneId, string key)
    {
        var snap = Snapshot();
        if (zoneId > 0 && snap.TryGetValue(($"zone:{zoneId}", key), out var zv)) return zv;
        if (snap.TryGetValue(("global", key), out var gv)) return gv;
        return ChatSettingsRegistry.ByKey.TryGetValue(key, out var def) ? def.Default : null;
    }

    public string? Get(string key) => Get(0, key);

    public float GetFloat(int zoneId, string key, float fallback = 0f) =>
        float.TryParse(Get(zoneId, key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public int GetInt(int zoneId, string key, int fallback = 0) =>
        int.TryParse(Get(zoneId, key), out var v) ? v : fallback;

    public bool GetBool(int zoneId, string key, bool fallback = false)
    {
        var s = Get(zoneId, key);
        if (string.IsNullOrEmpty(s)) return fallback;
        return s.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }

    /// <summary>Comma-separated float list (e.g. the diurnal curve's 6 points).</summary>
    public float[] GetCurve(int zoneId, string key)
    {
        var s = Get(zoneId, key) ?? "";
        return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => float.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f)
                .ToArray();
    }

    /// <summary>All rows of one scope (Feel page model, save-as-custom capture).</summary>
    public Dictionary<string, string> GetScope(string scope) =>
        Snapshot().Where(kv => kv.Key.Scope == scope)
                  .ToDictionary(kv => kv.Key.Name, kv => kv.Value);

    // ---------- writes (called by ChatSettingsController only) ----------

    /// <summary>Upsert one row. Returns the previous value (null = row did not exist).</summary>
    public async Task<string?> SetAsync(string scope, string key, string value)
    {
        using var conn = _db.Admin();
        var old = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM chat_settings WHERE scope=@scope AND name=@key",
            new { scope, key });
        await conn.ExecuteAsync(@"
            INSERT INTO chat_settings (scope, name, value) VALUES (@scope, @key, @value)
            ON DUPLICATE KEY UPDATE value=@value",
            new { scope, key, value });
        Invalidate();
        return old;
    }

    /// <summary>
    /// §14.1 preset apply: bulk-write the preset's pairs into GLOBAL scope (zone overrides
    /// untouched), set global/active_preset. Returns (key, old, new) per pair for [CHAT-SET].
    /// </summary>
    public async Task<List<(string Key, string? Old, string New)>?> ApplyPresetAsync(string name)
    {
        Dictionary<string, string>? pairs;
        using (var conn = _db.Admin())
        {
            var json = await conn.QuerySingleOrDefaultAsync<string?>(
                "SELECT settings_json FROM chat_preset WHERE name=@name", new { name });
            if (json == null) return null;
            pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        if (pairs == null) return null;

        var changes = new List<(string, string?, string)>();
        foreach (var (key, value) in pairs)
            changes.Add((key, await SetAsync("global", key, value), value));
        changes.Add(("global.active_preset",
            await SetAsync("global", "global.active_preset", name), name));
        return changes;
    }

    /// <summary>Save the current GLOBAL scope (minus active_preset) as a custom preset.</summary>
    public async Task<bool> SavePresetAsync(string name)
    {
        var current = GetScope("global");
        current.Remove("global.active_preset");
        var json = JsonSerializer.Serialize(current);

        using var conn = _db.Admin();
        var builtin = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT builtin FROM chat_preset WHERE name=@name", new { name });
        if (builtin == 1) return false;   // never overwrite a built-in

        await conn.ExecuteAsync(@"
            INSERT INTO chat_preset (name, settings_json, builtin) VALUES (@name, @json, 0)
            ON DUPLICATE KEY UPDATE settings_json=@json",
            new { name, json });
        return true;
    }

    // ---------- snapshot plumbing ----------

    public void Invalidate() { lock (_gate) { _snapshotUtc = DateTime.MinValue; } }

    private Dictionary<(string Scope, string Name), string> Snapshot()
    {
        var snap = _snapshot;
        if (snap != null && DateTime.UtcNow - _snapshotUtc < SnapshotTtl)
            return snap;

        lock (_gate)
        {
            if (_snapshot != null && DateTime.UtcNow - _snapshotUtc < SnapshotTtl)
                return _snapshot;
            try
            {
                using var conn = _db.Admin();
                var rows = conn.Query<(string Scope, string Name, string Value)>(
                    "SELECT scope, name, value FROM chat_settings");
                _snapshot = rows.ToDictionary(r => (r.Scope, r.Name), r => r.Value);
                _snapshotUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CHAT-SET] snapshot refresh failed — serving stale/registry defaults");
                _snapshot ??= new Dictionary<(string, string), string>();
                _snapshotUtc = DateTime.UtcNow;   // don't hammer a down DB; retry after TTL
            }
            return _snapshot;
        }
    }
}