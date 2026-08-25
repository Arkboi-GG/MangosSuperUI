using MangosSuperUI.Services.ArmorForge;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services;

/// <summary>
/// Re-registers every piece of custom content into <see cref="DbcService"/>'s in-memory caches
/// (<c>ItemDisplayIcons</c>, <c>ItemModelInfos</c>).
///
/// === Why this exists as its own type ===
///
/// Those caches are the ONLY way the web UI resolves a custom display: the Items page reads them for
/// the inventory icon, for the model/texture panel, for the GLB it builds, and for the enchant glow.
/// Nothing about a forged item lives in the statically-loaded DBC files, so a display that is not
/// registered renders as the red "?" and cannot be dressed onto the 3D character.
///
/// The registrations are NOT durable. Two separate events discard them:
///   1. An app restart — the caches are rebuilt from the extracted DBC directory, which has never
///      contained forged rows (forged ItemDisplayInfo rows ship inside the patch MPQ).
///   2. <c>DbcService.Reload()</c> — reassigns both dictionaries wholesale. This is reachable from
///      the Settings page's "Reload DBC" button, and it used to return success while silently
///      un-registering every forged weapon, every forged armour piece and every retexture for the
///      rest of the process lifetime.
///
/// Both call sites therefore need the identical sequence, in the identical order, which is what this
/// centralises. Order matters: retextures first (they clone real display rows), then weapons, then
/// armour — the same order the boot path has always used.
///
/// Every step is individually guarded. A failure here must never take the panel down, and one lane
/// being unavailable must not stop the other two from registering.
/// </summary>
public sealed class CustomDisplayRegistrar
{
    private readonly ItemRetextureService _retexture;
    private readonly CustomWeaponBuildService _weapons;
    private readonly CustomArmorBuildService _armor;
    private readonly ILogger<CustomDisplayRegistrar> _logger;

    public CustomDisplayRegistrar(ItemRetextureService retexture, CustomWeaponBuildService weapons,
        CustomArmorBuildService armor, ILogger<CustomDisplayRegistrar> logger)
    {
        _retexture = retexture;
        _weapons = weapons;
        _armor = armor;
        _logger = logger;
    }

    /// <summary>Re-register all three lanes. Never throws.</summary>
    /// <param name="reason">What triggered this, for the log — "startup" or "dbc reload".</param>
    public async Task RegisterAllAsync(string reason)
    {
        await SafeAsync("retexture", _retexture.LoadExistingRetexturesAsync);
        await SafeAsync("forged weapon", _weapons.LoadExistingWeaponsAsync);
        await SafeAsync("forged armor", _armor.LoadExistingArmorAsync);
        _logger.LogInformation("CustomDisplayRegistrar: custom display registration completed ({Reason})", reason);
    }

    private async Task SafeAsync(string lane, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            // The admin DB being down at boot is the expected case here. Everything else on the
            // panel still works; the affected lane's displays render as the red "?" until the next
            // successful registration.
            _logger.LogError(ex, "CustomDisplayRegistrar: {Lane} registration skipped (DB unavailable?)", lane);
        }
    }
}
