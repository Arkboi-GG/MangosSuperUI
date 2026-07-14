using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>
/// CHAT_ARCHITECTURE §10.4 — the deterministic per-persona fingerprint, applied to EVERY
/// outgoing line. Steps 1–6, 8, 9 shipped in C2; step 7 (anachronism scrub) lands in C10
/// and slots in between 6b and 8 without touching the others.
///
/// AMENDED 2026-07-13:
///   • step 6b — REGISTER PASS (SwearTables): de-censor the model's self-bowdlerization
///     and apply the persona's swear register. Runs BEFORE the content floor, which still
///     gets the last word on slurs and sexual content.
///   • step 10 — SELF-REPEAT LEDGER: the last 12 lines this bot actually emitted are
///     remembered, and a near-identical new line is DISCARDED. Tier 0 only shows the model
///     its own lines within ONE (bot, counterpart, kind) thread — a bot answering five
///     people in the Barrens could, and did, emit the same line five times. Silence beats
///     parroting. Trigram overlap (>2 shared) for real sentences; exact match for short
///     lines like "lol yeah", which legitimately recur but not back-to-back.
///
/// Returns null when the line must NOT be sent (floor / self-repeat / emptied) with the
/// reason for the coordinator's [CHAT-ENGINE] discard log.
/// </summary>
public class StylePostPass
{
    private readonly ChatSettingsService _settings;

    // Step 10 — per-bot emission ledger. Keyed by bot NAME (identity-blind, same
    // convention as ChatMemoryStore). This instance is captured by the coordinator for
    // the life of the process, so instance state is fine regardless of DI lifetime.
    private const int LedgerDepth = 12;
    private readonly ConcurrentDictionary<string, Queue<string>> _recent =
        new(StringComparer.OrdinalIgnoreCase);

    public StylePostPass(ChatSettingsService settings)
    {
        _settings = settings;
    }

    /// <param name="moodValence">bot_persona.mood_valence — sours the register pass. 0 until C9.</param>
    public (string? Line, string? DiscardReason) Apply(PersonaCard card, string botName, string raw,
                                                       float moodValence = 0f)
    {
        // ── Step 1: CleanResponse (VERBATIM port from OllamaChatService — proven code)
        //           + trailing/leading "As {name}," / "Name:" artifact strip (§10.4.1) ──
        var line = CleanResponse(raw);
        line = StripSpeakerArtifacts(line, card.GivenName, botName);
        if (string.IsNullOrWhiteSpace(line)) return (null, "empty-after-clean");

        var t = card.Typing;
        var rng = Random.Shared;

        // ── Step 2: caps transform per typing.caps ──
        line = t.Caps switch
        {
            "lower" => line.ToLowerInvariant(),                                   // just lowercase everything, it's chat
            "proper" => SentenceCase(line),
            "mixed" => rng.NextDouble() < 0.5 ? line.ToLowerInvariant() : SentenceCase(line), // 50/50 per message
            "CRUISE" => rng.NextDouble() < 0.05 ? line.ToUpperInvariant() : line, // 5% whole-message burst
            _ => line
        };

        // ── Step 3: punctuation per level ──
        if (t.Punctuation == "minimal")
        {
            line = line.TrimEnd();
            while (line.EndsWith('.') && !line.EndsWith("...")) line = line[..^1].TrimEnd();  // strip terminal periods, keep ?/!
        }
        else if (t.Punctuation == "heavy")
        {
            if (line.Contains('!') && rng.NextDouble() < 0.30) line = line.Replace("!", "!!");
            if (rng.NextDouble() < 0.25)
            {
                int comma = line.IndexOf(", ");
                line = comma > 0 ? line[..comma] + "... " + line[(comma + 2)..] : line.TrimEnd('.') + "...";
            }
        }

        // ── Step 4: abbreviation dictionary by abbrev_level (cumulative tiers, 60% per candidate) ──
        int levels = Math.Clamp(t.AbbrevLevel, 0, StyleTables.AbbrevTiers.Length);
        for (int tier = 0; tier < levels; tier++)
        {
            foreach (var (from, to) in StyleTables.AbbrevTiers[tier])
            {
                if (rng.NextDouble() >= 0.60) continue;   // tendency, not cipher
                line = Regex.Replace(line, $@"\b{Regex.Escape(from)}\b", to, RegexOptions.IgnoreCase);
            }
        }

        // ── Step 5: typo injection at typo_rate per word (× voice.typo_mult) ──
        float typoRate = t.TypoRate * _settings.GetFloat(0, "voice.typo_mult", 1.0f);
        if (typoRate > 0)
        {
            var words = line.Split(' ');
            for (int i = 1; i < words.Length; i++)   // never the first word
            {
                var w = words[i];
                if (w.Length < 4) continue;
                if (char.IsUpper(w[0])) continue;    // never on names
                if (rng.NextDouble() >= typoRate) continue;
                words[i] = InjectTypo(w, rng);
            }
            line = string.Join(' ', words);
        }

        // ── Step 6: tics — append/prepend one at 15% chance ──
        if (t.Tics.Count > 0 && rng.NextDouble() < 0.15)
        {
            var tic = t.Tics[rng.Next(t.Tics.Count)];
            line = rng.NextDouble() < 0.5 && tic.Length > 1
                ? $"{tic} {line}"
                : $"{line} {tic}";
        }

        // ── Step 6b: REGISTER PASS (§10.4 amendment) — de-censor + persona swear register.
        //    Strength = typing.swear_level × voice.banter_intensity. Runs before the floor. ──
        float banter = _settings.GetFloat(0, "voice.banter_intensity", 0.5f);
        line = SwearTables.Apply(card, line, banter, moodValence, rng);

        // ── Step 7: anachronism scrub — C10 (era pack), slots in here ──

        // ── Step 8: content floor (non-configurable) ──
        if (ContentFloor.IsBlocked(line, out var stem))
            return (null, $"floor:{stem}");

        // ── Step 9: length cap 200 chars with sentence-boundary cut (client-safe) ──
        if (line.Length > MaxResponseLength)
        {
            var cutPoint = line.LastIndexOf('.', MaxResponseLength);
            if (cutPoint < 20) cutPoint = line.LastIndexOf(' ', MaxResponseLength);
            if (cutPoint < 20) cutPoint = MaxResponseLength;
            line = line[..cutPoint].TrimEnd();
        }

        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line)) return (null, "empty-after-style");

        // ── Step 10: self-repeat ledger (§10.4 amendment) ──
        if (IsSelfRepeat(botName, line)) return (null, "self-repeat");
        Remember(botName, line);

        return (line, null);
    }

    // ==================== Step 10 internals ====================

    private bool IsSelfRepeat(string botName, string line)
    {
        if (!_recent.TryGetValue(botName, out var q)) return false;

        var norm = Normalize(line);
        if (norm.Length == 0) return false;
        var words = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        lock (q)
        {
            foreach (var prev in q)
            {
                if (prev == norm) return true;                       // exact — always a repeat

                // Short lines ("lol yeah", "brb") legitimately recur; only exact matches
                // above catch those. Longer lines get the trigram test.
                if (words.Length < 5) continue;

                var prevWords = prev.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (prevWords.Length < 5) continue;
                if (SharedTrigrams(words, prevWords) > 2) return true;
            }
        }
        return false;
    }

    private void Remember(string botName, string line)
    {
        var q = _recent.GetOrAdd(botName, _ => new Queue<string>());
        lock (q)
        {
            q.Enqueue(Normalize(line));
            while (q.Count > LedgerDepth) q.Dequeue();
        }
    }

    /// <summary>Same trigram mechanic as VoiceLibraryBuilder's library dedup (§6.3 step 3).</summary>
    private static int SharedTrigrams(string[] a, string[] b)
    {
        var set = new HashSet<string>();
        for (int i = 0; i + 2 < b.Length; i++) set.Add($"{b[i]} {b[i + 1]} {b[i + 2]}");

        int shared = 0;
        for (int i = 0; i + 2 < a.Length; i++)
            if (set.Contains($"{a[i]} {a[i + 1]} {a[i + 2]}")) shared++;
        return shared;
    }

    /// <summary>Compare on content, not on typos/punctuation — step 5 must not defeat step 10.</summary>
    private static string Normalize(string line)
    {
        var sb = new StringBuilder(line.Length);
        foreach (var c in line.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    // ==================== Step 1 internals ====================

    private const int MaxResponseLength = 200;

    /// <summary>VERBATIM port of OllamaChatService.CleanResponse (§10.4 step 1 — proven, reuse as-is).</summary>
    private static string CleanResponse(string raw)
    {
        var cleaned = raw.Trim();

        // Strip <think>...</think> blocks if the model outputs them despite /no_think
        var thinkStart = cleaned.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (thinkStart >= 0)
        {
            var thinkEnd = cleaned.IndexOf("</think>", thinkStart, StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0)
                cleaned = cleaned[(thinkEnd + 8)..].Trim();
            else
                cleaned = cleaned[..thinkStart].Trim();
        }

        // Cap length — don't let the bot write essays
        if (cleaned.Length > MaxResponseLength)
        {
            // Cut at last sentence boundary before limit
            var cutPoint = cleaned.LastIndexOf('.', MaxResponseLength);
            if (cutPoint < 20) cutPoint = cleaned.LastIndexOf(' ', MaxResponseLength);
            if (cutPoint < 20) cutPoint = MaxResponseLength;
            cleaned = cleaned[..cutPoint].TrimEnd();
        }

        // Strip leading/trailing quotes if model wraps response
        if (cleaned.StartsWith('"') && cleaned.EndsWith('"'))
            cleaned = cleaned[1..^1].Trim();


        // Replace common unicode escapes that break in-game display
        cleaned = cleaned.Replace("\u2014", "-");   // em dash
        cleaned = cleaned.Replace("\u2013", "-");   // en dash  
        cleaned = cleaned.Replace("\u2018", "'");   // left single quote
        cleaned = cleaned.Replace("\u2019", "'");   // right single quote
        cleaned = cleaned.Replace("\u201C", "\"");  // left double quote
        cleaned = cleaned.Replace("\u201D", "\"");  // right double quote
        cleaned = cleaned.Replace("\u2026", "...");  // ellipsis
        cleaned = cleaned.Replace("\n", " ");        // newline to space

        return cleaned;
    }

    /// <summary>§10.4.1: strip "As {name}," lead-ins and "{Name}:" speaker prefixes the model may emit.</summary>
    private static string StripSpeakerArtifacts(string line, string givenName, string botName)
    {
        line = line.Trim();
        foreach (var name in new[] { givenName, botName })
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (line.StartsWith($"As {name},", StringComparison.OrdinalIgnoreCase))
                line = line[$"As {name},".Length..].Trim();
            if (line.StartsWith($"{name}:", StringComparison.OrdinalIgnoreCase))
                line = line[(name.Length + 1)..].Trim();
        }
        return line;
    }

    private static string SentenceCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var chars = s.ToCharArray();
        bool atSentenceStart = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (atSentenceStart && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                atSentenceStart = false;
            }
            else if (chars[i] is '.' or '!' or '?')
                atSentenceStart = true;
        }
        // The pronoun I (a proper-caps typist writes I, I'm, I'll, I've, I'd)
        var result = new string(chars);
        result = Regex.Replace(result, @"\bi\b", "I");
        result = Regex.Replace(result, @"\bi('m|'ll|'ve|'d)\b", m => "I" + m.Groups[1].Value);
        return result;
    }

    private static string InjectTypo(string word, Random rng)
    {
        int idx = rng.Next(1, word.Length);   // never position 0 — keeps words recognizable
        char c = char.ToLowerInvariant(word[idx]);

        // 50/50: adjacent-key swap or dropped letter
        if (rng.NextDouble() < 0.5 && StyleTables.QwertyAdjacent.TryGetValue(c, out var adj))
        {
            var sb = new StringBuilder(word);
            sb[idx] = adj[rng.Next(adj.Length)];
            return sb.ToString();
        }
        return word.Remove(idx, 1);
    }
}