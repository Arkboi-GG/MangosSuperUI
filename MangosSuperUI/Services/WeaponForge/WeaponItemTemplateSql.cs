using System.Security.Cryptography;
using System.Text;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Compiles the owner-applied <c>item_template.sql</c> for a custom weapon from the literal,
/// hash-verified donor-2131 fixture (WEAPON_GEN.md §2.1, §4.2, §13.3). The gameplay row is cloned
/// with an explicit 130-column list, changing only deliberate fields (entry, name, display_id, plus
/// any caller overrides). It never emits <c>INSERT … SELECT</c> off the live table — whose donor
/// could drift between build and apply — and it fails closed:
///   • (entry, patch) is the item_template PRIMARY KEY, so a plain INSERT ERRORS on a colliding
///     entry rather than overwriting an unrelated live row (never ON DUPLICATE KEY UPDATE);
///   • the fixture hash is re-verified before anything is generated;
///   • identity/range are validated (custom entry ≥ 900000 and ≤ MEDIUMINT ceiling; display ≥ 60000).
/// </summary>
public static class WeaponItemTemplateSql
{
    private const int Col_Entry = 0;
    private const int Col_Name = 4;
    private const int Col_Description = 5;
    private const int Col_DisplayId = 6;

    public static GeneratedSql Build(long entry, string name, long displayId,
        string buildId, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (!DonorItemTemplateFixture.Verify())
            throw new InvalidOperationException("Donor item_template fixture failed hash verification; refusing to generate SQL.");

        if (entry < WeaponIdReservationService.ItemEntryFloor || entry > WeaponIdReservationService.MediumIntUnsignedMax)
            throw new ArgumentOutOfRangeException(nameof(entry), $"Entry {entry} outside the custom item range [{WeaponIdReservationService.ItemEntryFloor}, {WeaponIdReservationService.MediumIntUnsignedMax}].");
        if (displayId < WeaponIdReservationService.ItemDisplayFloor || displayId > WeaponIdReservationService.MediumIntUnsignedMax)
            throw new ArgumentOutOfRangeException(nameof(displayId), $"Display id {displayId} outside the custom display range.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            throw new ArgumentException("Name must be 1..255 characters and contain no control characters.", nameof(name));

        var cols = DonorItemTemplateFixture.Columns;
        var vals = (string[])DonorItemTemplateFixture.DonorValues.Clone();

        // Deliberate identity changes.
        vals[Col_Entry] = entry.ToString();
        vals[Col_Name] = name;
        vals[Col_DisplayId] = displayId.ToString();

        // Caller overrides by column name (validated against the fixture columns).
        if (overrides is not null)
        {
            foreach (var (col, value) in overrides)
            {
                int idx = Array.IndexOf(cols, col);
                if (idx < 0) throw new ArgumentException($"Unknown item_template column '{col}'.", nameof(overrides));
                vals[idx] = value;
            }
        }

        // Only `name` and `description` are string columns; everything else is a numeric literal
        // (ints, floats, and negative cooldowns straight from the donor row).
        var literals = new string[vals.Length];
        for (int i = 0; i < vals.Length; i++)
            literals[i] = (i == Col_Name || i == Col_Description) ? SqlString(vals[i]) : vals[i];

        var colList = string.Join(", ", cols.Select(c => "`" + c + "`"));
        var valList = string.Join(", ", literals);

        var sql = new StringBuilder();
        sql.Append("-- Weapon Forge — item_template row for a custom weapon\n");
        sql.Append($"-- Build: {buildId}\n");
        // Never interpolate user-authored text into SQL comments. A newline in a name would end
        // the comment and turn the remainder into executable SQL before the quoted VALUES row.
        sql.Append($"-- Entry: {entry}   Display: {displayId}\n");
        sql.Append("-- Donor 2131 (Shortsword) supplies the base row; validated requested gameplay overrides are included.\n");
        sql.Append("-- FAIL-CLOSED: (entry,patch) is the PRIMARY KEY, so a colliding entry ERRORS instead of\n");
        sql.Append("-- overwriting a live row. Verify the entry is free before applying:\n");
        sql.Append($"--   SELECT entry, name, display_id FROM item_template WHERE entry = {entry};\n");
        sql.Append("INSERT INTO item_template\n  (");
        sql.Append(colList);
        sql.Append(")\nVALUES\n  (");
        sql.Append(valList);
        sql.Append(");\n");

        var text = sql.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new GeneratedSql(text, hash);
    }

    /// <summary>Encode user-authored text as a UTF-8 hex expression. This is unambiguous under
    /// both MySQL string modes (with or without NO_BACKSLASH_ESCAPES) and cannot terminate the
    /// VALUES expression.</summary>
    private static string SqlString(string s)
    {
        if (s.Length == 0) return "''";
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(s));
        return $"CONVERT(0x{hex} USING utf8mb4)";
    }
}

/// <summary>Generated SQL text plus its SHA-256, recorded in the build manifest.</summary>
public sealed record GeneratedSql(string Text, string Sha256);
