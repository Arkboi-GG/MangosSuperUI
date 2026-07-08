<!-- provenance: verbose -->
# TradeData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`TradeData` manages one participant’s state in a peer-to-peer trade session. It tracks the items (`m_items`), gold (`m_money`), and optional enchantment spell (`m_spell`) offered by the owning `Player` (`m_player`). It maintains a direct pointer to the trading partner (`m_trader`) to enable bidirectional consistency checks.

Key responsibilities:
1.  **State Storage**: Holds trade offers and acceptance flags (`m_accepted`, `m_acceptProccess`).
2.  **Consistency Enforcement**: Mutations (`SetItem`, `SetSpell`, `SetMoney`) automatically invalidate acceptance for **both** parties, preventing post-acceptance tampering.
3.  **Synchronization**: Triggers network updates via `WorldSession.TradeHandler` to reflect state changes to both clients.

It does not perform final transfers or database persistence; those are handled by `WorldSession.TradeHandler` and `Player`.

## Member-by-Member Behavior

### Initialization & Accessors

**`TradeData`**
Constructor initializes `m_player`, `m_trader`, and resets state flags/values to defaults.

**`GetTrader`**
Returns the `Player*` pointer to the trading partner.

**`GetTraderData`**
Delegates to `Player.Main/GetTradeData` on `m_trader` to retrieve the partner’s `TradeData` instance.

**`GetItem`**
Resolves the `ObjectGuid` in `m_items[slot]` to an `Item*` via `Player.Main/GetItemByGuid`. Returns `nullptr` if empty.

**`HasItem`**
Iterates `m_items` to check if a given `ObjectGuid` is present.

**`GetTradeSlotForItem`**
Returns the `TradeSlots` index for a given `ObjectGuid`, or `TRADE_SLOT_INVALID` if not found.

**`GetSpell`**, **`GetMoney`**, **`IsAccepted`**, **`IsInAcceptProcess`**
Simple accessors for `m_spell`, `m_money`, `m_accepted`, and `m_acceptProccess`. `IsInAcceptProcess` is checked by `Spell.Main/CheckCast` to block spells during finalization.

**`GetSpellCastItem`**
Resolves `m_spellCastItem` GUID to an `Item*` via `Player.Main/GetItemByGuid`.

**`HasSpellCastItem`**
Checks if `m_spellCastItem` is non-empty.

**`GetLastModificationTime`**, **`SetLastModificationTime`**
Accessors for `m_lastModificationTime`, used for scam-prevention timing checks.

**`GetScamPreventionDelay`**, **`SetScamPreventionDelay`**
Accessors for `m_scamPreventionDelay`, the mandatory wait time before acceptance after a modification.

### State Mutation & Synchronization

**`SetItem`**
Updates `m_items[slot]` with the item’s GUID.
1.  Exits early if the GUID is unchanged.
2.  Calls `SetAccepted(false)` on **both** local and trader `TradeData` to invalidate prior acceptance.
3.  Calls `Update()` to sync clients.
4.  If `slot == TRADE_SLOT_NONTRADED`, it clears the trader’s spell (`GetTraderData()->SetSpell(0)`) and the local spell (`SetSpell(0)`), as the enchantment target has changed.

**`SetSpell`**
Records `spellId` and `castItem` GUID.
1.  Exits early if unchanged.
2.  Invalidates acceptance for both players.
3.  Calls `Update(true)` (to trader) and `Update(false)` (to local player) to sync spell visuals.

**`SetMoney`**
Updates `m_money`.
1.  Exits early if unchanged.
2.  Invalidates acceptance for both players.
3.  Calls `Update()` to sync clients.

**`SetAccepted`**
Sets `m_accepted`. If setting to `false`, sends `TRADE_STATUS_BACK_TO_TRADE` to the local player’s session, or to the trader’s session if `crosssend` is true.

**`SetInAcceptProcess`**
Sets `m_acceptProccess` flag, used to block spell casts during final acceptance.

**`Update`**
Internal helper. If `for_trader` is true, calls `m_trader->GetSession()->SendUpdateTrade(true)`; otherwise, calls `m_player->GetSession()->SendUpdateTrade(false)`.

**`FillTransactionLog`**
Populates a `TransactionPart` struct with `m_money`, player GUID, `m_spell`, and item details (count, entry, GUID) for all occupied slots. Used by `WorldSession.TradeHandler/HandleAcceptTradeOpcode` for audit logging.

## Cross-Unit Boundaries

*   **`Player`**: `TradeData` relies on `Player.Main/GetItemByGuid` to resolve item pointers and `Player.Main/GetTradeData` to access the partner’s state. It uses `Player.Main/GetSession` to send network packets.
*   **`WorldSession.TradeHandler`**: The primary driver. Handlers like `HandleSetTradeItemOpcode` call `SetItem`; `HandleAcceptTradeOpcode` reads state via `IsAccepted`, `GetMoney`, and `FillTransactionLog`. `TradeData` calls `SendUpdateTrade` and `SendTradeStatus` to update clients.
*   **`Spell` System**: `Spell.Main/CheckCast` calls `IsInAcceptProcess` to prevent casting during trade finalization and `SetSpell` to apply enchantments.

## Data Model

`TradeData` operates entirely in memory. It does not query or update any database tables. `FillTransactionLog` prepares data for external logging, but persistence is handled elsewhere.

## Notable Implementation Details

*   **Bidirectional Invalidation**: `SetItem`, `SetSpell`, and `SetMoney` all call `GetTraderData()->SetAccepted(false)`. This ensures that if one player modifies the trade, the other player’s acceptance is voided, requiring re-acceptance.
*   **Non-Traded Slot Logic**: Slot 6 (`TRADE_SLOT_NONTRADED`) holds the item being enchanted. Changing this slot clears spells for both players, as the enchantment target is gone.
*   **Early Exits**: Mutators check for value equality before proceeding, avoiding unnecessary network traffic and state resets.

## Member Reference

**`TradeData`**
Constructor initializing player/trader pointers and resetting state to defaults.

**`GetTraderData`**
Retrieves the partner’s `TradeData` via `Player.Main/GetTradeData`.

**`GetItem`**
Resolves slot GUID to `Item*` via `Player.Main/GetItemByGuid`.

**`HasItem`**
Checks if a GUID exists in `m_items`.

**`GetTradeSlotForItem`**
Returns slot index for a GUID, or `TRADE_SLOT_INVALID`.

**`GetTrader`**
Returns the `Player*` pointer to the trading partner.

**`FillTransactionLog`**
Populates `TransactionPart` with money, spell, and item details for logging.

**`GetSpell`**
Returns `m_spell` ID.

**`HasSpellCastItem`**
Checks if `m_spellCastItem` is non-empty.

**`GetMoney`**
Returns `m_money`.

**`IsAccepted`**
Returns `m_accepted`.

**`IsInAcceptProcess`**
Returns `m_acceptProccess`.

**`GetLastModificationTime`**
Returns `m_lastModificationTime`.

**`GetSpellCastItem`**
Resolves `m_spellCastItem` GUID to `Item*` via `Player.Main/GetItemByGuid`.

**`SetLastModificationTime`**
Sets `m_lastModificationTime`.

**`GetScamPreventionDelay`**
Returns `m_scamPreventionDelay`.

**`SetScamPreventionDelay`**
Sets `m_scamPreventionDelay`.

**`SetItem`**
Updates slot GUID, invalidates acceptance for both players, syncs clients, and clears spells if the non-traded slot is modified.

**`SetInAcceptProcess`**
Sets `m_acceptProccess` flag.

**`SetSpell`**
Records spell/cast item, invalidates acceptance for both players, and syncs clients.

**`SetMoney`**
Updates `m_money`, invalidates acceptance for both players, and syncs clients.

**`Update`**
Sends `SendUpdateTrade` to the trader or local player session.

**`SetAccepted`**
Sets `m_accepted`; sends `TRADE_STATUS_BACK_TO_TRADE` if unaccepting.

---

<!-- machine-true, projected from graph.json -->

## Map — TradeData

*Source:* TradeData.cpp, TradeData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetTraderData | method | Player.Main/GetTradeData | SpellCastTargetsInfo/Update, SpellEntry/GetCastTime, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetItem | method | Player.Main/GetItemByGuid | SpellCastTargetsInfo/Update, SpellEntry/GetCastTime, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/SendUpdateTrade, WorldSession.TradeHandler/setAcceptTradeMode | — |
| HasItem | method | ObjectGuid/operator== | WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| GetTradeSlotForItem | method | ObjectGuid/operator== | Player.Main/SplitItem | — |
| TradeData | ctor | — | WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| GetTrader | method | — | Player.Main/GetTrader, Player.Main/TradeCancel, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleBeginTradeOpcode, WorldSession.TradeHandler/HandleClearTradeItemOpcode, WorldSession.TradeHandler/HandleSetTradeGoldOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| FillTransactionLog | method | game_Objects_Item/GetCount, Object/GetEntry, Object/GetGUIDLow | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetSpell | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/SendUpdateTrade | — |
| HasSpellCastItem | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetMoney | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/SendUpdateTrade | — |
| IsAccepted | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| IsInAcceptProcess | method | — | Spell.Main/CheckCast | — |
| GetLastModificationTime | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetSpellCastItem | method | Player.Main/GetItemByGuid | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| SetLastModificationTime | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleClearTradeItemOpcode, WorldSession.TradeHandler/HandleSetTradeGoldOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| GetScamPreventionDelay | method | — | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| SetScamPreventionDelay | method | — | WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| SetItem | method | Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator== | WorldSession.TradeHandler/HandleClearTradeItemOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| SetInAcceptProcess | method | — | WorldSession.TradeHandler/clearAcceptTradeMode, WorldSession.TradeHandler/setAcceptTradeMode | — |
| SetSpell | method | Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator== | Spell.Main/CheckCast, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| SetMoney | method | — | WorldSession.TradeHandler/HandleSetTradeGoldOpcode | — |
| Update | method | Player.Main/GetSession, WorldSession.TradeHandler/SendUpdateTrade | — | — |
| SetAccepted | method | Player.Main/GetSession, WorldSession.TradeHandler/SendTradeStatus | WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleClearTradeItemOpcode, WorldSession.TradeHandler/HandleSetTradeGoldOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode, WorldSession.TradeHandler/HandleUnacceptTradeOpcode | — |
