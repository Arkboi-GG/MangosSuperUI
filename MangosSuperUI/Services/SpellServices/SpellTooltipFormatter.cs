using System.Globalization;
using System.Text.RegularExpressions;

namespace MangosSuperUI.Services.SpellServices;

/// <summary>
/// Resolves the variable tokens embedded in a spell's Spell.dbc description /
/// aura text (e.g. "Increases Strength by $s1 for $d.") into concrete numbers
/// using the spell's effect fields from spell_template, mirroring how the game
/// client builds the tooltip string.
///
/// Supported tokens (case-insensitive letter, optional 1-3 effect index):
///   $s / $m  effect value          $d   duration
///   $o       periodic total        $t   tick interval (sec)
///   $x       chain targets         $n   proc charges
///   $u       stack amount          $i   max affected targets
///   $h       proc chance (%)
/// Plus the conditional/format constructs:
///   $lsingular:plural;  $gmale:female;  $/divisor;token
/// A leading spell-id prefix ($12345s1) is resolved only when it matches the
/// current spell; otherwise the raw token is left untouched. Anything else the
/// formatter doesn't recognise is left as written, so output degrades to the
/// original text rather than to nonsense.
/// </summary>
public static class SpellTooltipFormatter
{
    /// <summary>
    /// Effect data needed to resolve tooltip tokens for one spell. Pull these
    /// straight from a spell_template row (values are stored as in Spell.dbc,
    /// i.e. base points are the in-game value minus one).
    /// </summary>
    public sealed class SpellNumbers
    {
        public uint Entry;
        public int[] BasePoints = new int[3];
        public int[] DieSides = new int[3];
        public int[] BaseDice = new int[3];
        public int[] Amplitude = new int[3];   // ms per tick
        public int[] ChainTargets = new int[3];
        public int DurationMs;
        public int ProcChance;
        public int ProcCharges;
        public int StackAmount;
        public int MaxAffectedTargets;
    }

    private static readonly Regex TokenRegex = new(
        @"\$(?<spell>\d+)?(?:(?<div>/\d+;))?(?<letter>[a-zA-Z])(?<idx>[123])?",
        RegexOptions.Compiled);

    // $lsingular:plural;  and  $gmale:female;
    private static readonly Regex ConditionalRegex = new(
        @"\$(?<kind>[gGlL])(?<a>[^:;]*):(?<b>[^;]*);",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns the description with all resolvable tokens substituted. If
    /// <paramref name="description"/> is null/empty the input is returned as-is.
    /// </summary>
    public static string Format(string? description, SpellNumbers n)
    {
        if (string.IsNullOrEmpty(description)) return description ?? string.Empty;

        // Conditional forms first (they can wrap around plain tokens).
        // Plural: pick singular when the leading number is exactly 1, else plural.
        // Gender: no character context, so pick the first (male) form.
        string text = ConditionalRegex.Replace(description, m =>
        {
            char kind = char.ToLowerInvariant(m.Groups["kind"].Value[0]);
            if (kind == 'g') return m.Groups["a"].Value;
            // plural — default to the plural form; singular is the rarer case
            return m.Groups["b"].Value;
        });

        text = TokenRegex.Replace(text, m =>
        {
            // A spell-id prefix that isn't this spell → leave untouched.
            if (m.Groups["spell"].Success &&
                uint.TryParse(m.Groups["spell"].Value, out var refId) && refId != n.Entry)
                return m.Value;

            int idx = m.Groups["idx"].Success ? int.Parse(m.Groups["idx"].Value) - 1 : 0;
            if (idx < 0 || idx > 2) idx = 0;

            char letter = m.Groups["letter"].Value[0];
            string? resolved = Resolve(char.ToLowerInvariant(letter), idx, n);
            if (resolved == null) return m.Value; // unknown → keep raw

            // Optional $/divisor; prefix applies to the resolved numeric value.
            if (m.Groups["div"].Success &&
                int.TryParse(m.Groups["div"].Value.Trim('/', ';'), out var div) && div != 0 &&
                double.TryParse(resolved, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return Trim(num / div);

            return resolved;
        });

        return text;
    }

    private static string? Resolve(char letter, int idx, SpellNumbers n)
    {
        switch (letter)
        {
            case 's':
            case 'm':
            {
                var (lo, hi) = EffectValue(idx, n);
                return lo == hi ? lo.ToString() : $"{lo} to {hi}";
            }
            case 'o': // periodic total over the whole duration
            {
                var (lo, hi) = EffectValue(idx, n);
                int amp = n.Amplitude[idx];
                if (amp <= 0 || n.DurationMs <= 0) return hi.ToString();
                int ticks = n.DurationMs / amp;
                if (ticks <= 0) ticks = 1;
                int loT = lo * ticks, hiT = hi * ticks;
                return loT == hiT ? loT.ToString() : $"{loT} to {hiT}";
            }
            case 'd': // duration
                return n.DurationMs > 0 ? FormatDuration(n.DurationMs) : null;
            case 't': // tick interval in seconds
                return n.Amplitude[idx] > 0 ? Trim(n.Amplitude[idx] / 1000.0) : null;
            case 'x': // chain / affected targets
                return n.ChainTargets[idx] > 0 ? n.ChainTargets[idx].ToString() : null;
            case 'i': // max affected targets
                return n.MaxAffectedTargets > 0 ? n.MaxAffectedTargets.ToString() : null;
            case 'n': // proc charges
                return n.ProcCharges > 0 ? n.ProcCharges.ToString() : null;
            case 'u': // stack amount
                return n.StackAmount > 0 ? n.StackAmount.ToString() : null;
            case 'h': // proc chance (%)
                return n.ProcChance > 0 ? n.ProcChance.ToString() : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Computes the min/max absolute effect value the way the game does: base
    /// points are the DBC value (in-game value minus one), so a fixed effect
    /// with DieSides==1 resolves to |BasePoints + 1|.
    /// </summary>
    private static (int Lo, int Hi) EffectValue(int idx, SpellNumbers n)
    {
        int bp = n.BasePoints[idx];
        int ds = n.DieSides[idx];
        int bd = n.BaseDice[idx];

        int lo, hi;
        if (ds == 0) { lo = hi = bp; }
        else if (ds == 1) { lo = hi = bp + 1; }
        else { lo = bp + (bd >= 1 ? bd : 1); hi = bp + ds; }

        return (Math.Abs(lo), Math.Abs(hi));
    }

    /// <summary>Formats a millisecond duration the way tooltips read: "30 sec", "5 min", "1 hour".</summary>
    private static string FormatDuration(int ms)
    {
        if (ms >= 3600000 && ms % 3600000 == 0)
        {
            int h = ms / 3600000;
            return h == 1 ? "1 hour" : $"{h} hours";
        }
        if (ms >= 60000 && ms % 60000 == 0)
            return $"{ms / 60000} min";
        return $"{Trim(ms / 1000.0)} sec";
    }

    private static string Trim(double v) =>
        v == Math.Floor(v)
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}
