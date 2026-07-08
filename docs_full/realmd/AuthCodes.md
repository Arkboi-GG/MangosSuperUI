<!-- provenance: verbose, failed-members -->
# AuthCodes

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuthCodes

**AuthCodes** (`AuthCodes.h`) is a header-only unit providing the static protocol constants, result codes, and account flag bitmasks for the `realmd` authentication service. It defines no executable logic, classes, or database interactions. Its sole responsibility is to supply a shared vocabulary of integer constants that other units use to interpret network packets, determine authentication outcomes, and evaluate account privileges.

The unit defines four categories of constants:
1.  **Client-to-Server Command IDs** (`eAuthCmd`): Opcodes for login, reconnection, realm listing, and character transfer messages.
2.  **Server-to-Client Result Codes** (`AuthResult`): Status codes indicating success or specific failure reasons (e.g., banned, incorrect password).
3.  **Internal Server Commands** (`eAuthSrvCmd`): Legacy Blizzard internal commands, explicitly marked as unused.
4.  **Account Flags** (`AccountFlags`): Bitmask values representing account properties like GM status, trial subscriptions, and bans.

## Member-by-Member Behavior

### Authentication Protocol Commands (`eAuthCmd`)
This enumeration defines opcodes for the handshake and connection management between the client and the realm server. Other units (e.g., network handlers) use these values to dispatch incoming packets.

*   **`CMD_AUTH_LOGON_CHALLENGE` (0x00)**: Initiates the login sequence.
*   **`CMD_AUTH_LOGON_PROOF` (0x01)**: Client’s cryptographic response to the server’s challenge.
*   **`CMD_AUTH_RECONNECT_CHALLENGE` (0x02)**: Initiates reconnection to an existing session.
*   **`CMD_AUTH_RECONNECT_PROOF` (0x03)**: Cryptographic proof for reconnection.
*   **`CMD_REALM_LIST` (0x10)**: Requests the list of available game realms.
*   **`CMD_XFER_INITIATE` (0x30)** through **`CMD_XFER_CANCEL` (0x34)**: Manages character realm transfers (initiate, data, accept, resume, cancel).

### Authentication Results (`AuthResult`)
This enumeration maps numeric return codes to specific client-side behaviors. Other units (e.g., `AuthSession.cpp`) select these codes to send in response packets after validating credentials.

*   **Success**: `WOW_SUCCESS` (0x00) for standard login; `WOW_SUCCESS_SURVEY` (0x0E) for login requiring a survey.
*   **Credential Errors**: `WOW_FAIL_UNKNOWN_ACCOUNT` (0x04) and `WOW_FAIL_INCORRECT_PASSWORD` (0x05). Note: The client rejects subsequent attempts after `INCORRECT_PASSWORD`, so servers often use `UNKNOWN_ACCOUNT` for both to avoid client-side lockouts.
*   **Account Status**: Includes `WOW_FAIL_BANNED` (0x03), `WOW_FAIL_SUSPENDED` (0x0C), `WOW_FAIL_ALREADY_ONLINE` (0x06), `WOW_FAIL_NO_TIME` (0x07), `WOW_FAIL_TRIAL_ENDED` (0x11), `WOW_FAIL_PARENTCONTROL` (0x0F), `WOW_FAIL_LOCKED_ENFORCED` (0x10), `WOW_FAIL_GAME_ACCOUNT_LOCKED` (0x18), `WOW_FAIL_UNLOCKABLE_LOCK` (0x19), and `WOW_FAIL_CHARGEBACK` (0x16).
*   **Version/Compatibility**: `WOW_FAIL_VERSION_INVALID` (0x09) for mismatch/corruption; `WOW_FAIL_VERSION_UPDATE` (0x0A) for required updates.
*   **Battle.net Integration**: `WOW_FAIL_USE_BATTLENET` (0x12), `WOW_FAIL_IGR_WITHOUT_BNET` (0x17), `WOW_FAIL_CONVERSION_REQUIRED` (0x20).
*   **System/Network**: `WOW_FAIL_DB_BUSY` (0x08), `WOW_FAIL_DISCONNECTED` (0xFF), and various generic failures (`WOW_FAIL_UNKNOWN0`, `WOW_FAIL_UNKNOWN1`, `WOW_FAIL_INVALID_SERVER`, `WOW_FAIL_FAIL_NOACCESS`, `WOW_FAIL_ANTI_INDULGENCE`, `WOW_FAIL_EXPIRED`, `WOW_FAIL_NO_GAME_ACCOUNT`).

### Internal Server Commands (`eAuthSrvCmd`)
This enumeration lists legacy Blizzard internal commands (e.g., `CMD_GRUNT_AUTH_CHALLENGE`, `CMD_GRUNT_HELLO`). The source explicitly marks these as **"not used by us currently."** They are preserved for historical completeness and should not be used in new logic.

### Account Flags (`AccountFlags`)
These bitmask constants interpret account properties stored in the database (typically the `flags` column of the `account` table). Other units (e.g., `AccountMgr.cpp`) use these to enforce permissions.

*   **Privileges**: `ACCOUNT_FLAG_GM`, `ACCOUNT_FLAG_NOKICK`, `ACCOUNT_FLAG_PRIVILEGED`, `ACCOUNT_FLAG_BLIZZARD`.
*   **Subscription/Billing**: `ACCOUNT_FLAG_WOW_TRIAL`, `ACCOUNT_FLAG_RECURRING_BILLING`, `ACCOUNT_FLAG_CANCELLED`, `ACCOUNT_FLAG_IGR`, `ACCOUNT_FLAG_PROPASS`, `ACCOUNT_FLAG_PROPASS_LOCK`.
*   **Restrictions**: `ACCOUNT_FLAG_WOW_RESTRICTED`, `ACCOUNT_FLAG_DISABLE_VOICE`, `ACCOUNT_FLAG_DISABLE_VOICE_SPEAK`, `ACCOUNT_FLAG_EU_FORBID_ELV`, `ACCOUNT_FLAG_EU_FORBID_BILLING`, `ACCOUNT_FLAG_EU_FORBID_CC`.
*   **Entitlements/Misc**: `ACCOUNT_FLAG_DEATH_KNIGHT_OK`, `ACCOUNT_FLAG_EXPANSION_COLLECTOR`, `ACCOUNT_FLAG_EXPANSION2_COLLECTOR`, `ACCOUNT_FLAG_COLLECTOR`, `ACCOUNT_FLAG_WHOLESALER`, `ACCOUNT_FLAG_REFERRAL`, `ACCOUNT_FLAG_REFERRAL_RESURRECT`, `ACCOUNT_FLAG_OVERMIND_LINKED`, `ACCOUNT_FLAG_OPENBETA_DELL`, `ACCOUNT_FLAG_PENDING_UPGRADE`, `ACCOUNT_FLAG_RETAIL_FROM_TRIAL`, `ACCOUNT_FLAG_DEMOS`, `ACCOUNT_FLAG_NOELECTUP`, `ACCOUNT_FLAG_KR_CERTIFICATE`.

## Cross-Unit Boundaries

`AuthCodes.h` is a passive definition unit. It does not call out to other units. It is included by numerous units in the `realmd` module:
*   **Network Handlers**: Use `eAuthCmd` to route incoming packets.
*   **Authentication Logic**: Use `AuthResult` to construct response packets.
*   **Account Management**: Use `AccountFlags` to interpret database records.

## Data Model

This unit does not interact with the database directly. It defines constants that correspond to data stored in tables such as `account` (specifically the `flags` column). Interpretation of this data occurs in other units that include this header.

## Notable Implementation Details

1.  **Client Lockout Mitigation**: The comment for `WOW_FAIL_INCORRECT_PASSWORD` warns that the client rejects subsequent login attempts after this error. Servers should use `WOW_FAIL_UNKNOWN_ACCOUNT` for both unknown accounts and incorrect passwords to prevent accidental client-side lockouts.
2.  **Unused Legacy Code**: `eAuthSrvCmd` is explicitly unused. Maintainers should ignore these values unless re-enabling legacy Blizzard protocols.
3.  **Bitmask Scope**: `AccountFlags` covers many historical/regional features (EU billing, Dell promotions). Many may be obsolete; maintainers should verify which flags are populated in their database.
4.  **Type Safety**: `eAuthCmd` and `AuthResult` are `uint8` to fit single-byte packet fields. `AccountFlags` defaults to `int` for 32-bit bitwise operations.

## Member Reference

*   **eAuthCmd**: Enumeration defining client-to-server authentication and realm transfer command opcodes.
*   **CMD_AUTH_LOGON_CHALLENGE**: Opcode for initiating login.
*   **CMD_AUTH_LOGON_PROOF**: Opcode for sending login credentials.
*   **CMD_AUTH_RECONNECT_CHALLENGE**: Opcode for initiating reconnection.
*   **CMD_AUTH_RECONNECT_PROOF**: Opcode for sending reconnection credentials.
*   **CMD_REALM_LIST**: Opcode for requesting realm list.
*   **CMD_XFER_INITIATE**: Opcode for starting realm transfer.
*   **CMD_XFER_DATA**: Opcode for sending transfer data.
*   **CMD_XFER_ACCEPT**: Opcode for accepting transfer.
*   **CMD_XFER_RESUME**: Opcode for resuming transfer.
*   **CMD_XFER_CANCEL**: Opcode for canceling transfer.
*   **eAuthSrvCmd**: Enumeration of unused internal server commands.
*   **CMD_GRUNT_AUTH_CHALLENGE**: Unused internal command.
*   **CMD_GRUNT_AUTH_VERIFY**: Unused internal command.
*   **CMD_GRUNT_CONN_PING**: Unused internal command.
*   **CMD_GRUNT_CONN_PONG**: Unused internal command.
*   **CMD_GRUNT_HELLO**: Unused internal command.
*   **CMD_GRUNT_PROVESESSION**: Unused internal command.
*   **CMD_GRUNT_KICK**: Unused internal command.
*   **CMD_GRUNT_PCWARNING**: Unused internal command.
*   **CMD_GRUNT_STRINGS**: Unused internal command.
*   **CMD_GRUNT_SUNKENUPDATE**: Unused internal command.
*   **CMD_GRUNT_SUNKEN_ONLINE**: Unused internal command.
*   **AuthResult**: Enumeration defining authentication success/failure codes.
*   **WOW_SUCCESS**: Login successful.
*   **WOW_FAIL_UNKNOWN0**: Generic connection failure.
*   **WOW_FAIL_UNKNOWN1**: Generic connection failure.
*   **WOW_FAIL_BANNED**: Account permanently banned.
*   **WOW_FAIL_UNKNOWN_ACCOUNT**: Invalid username/password (used to avoid client lockout).
*   **WOW_FAIL_INCORRECT_PASSWORD**: Incorrect password (client may lock out).
*   **WOW_FAIL_ALREADY_ONLINE**: Account already logged in.
*   **WOW_FAIL_NO_TIME**: Prepaid time expired.
*   **WOW_FAIL_DB_BUSY**: Database unavailable.
*   **WOW_FAIL_VERSION_INVALID**: Client version invalid/corrupt.
*   **WOW_FAIL_VERSION_UPDATE**: Client needs update.
*   **WOW_FAIL_INVALID_SERVER**: Invalid server connection.
*   **WOW_FAIL_SUSPENDED**: Account temporarily suspended.
*   **WOW_FAIL_FAIL_NOACCESS**: No access.
*   **WOW_SUCCESS_SURVEY**: Login successful with survey requirement.
*   **WOW_FAIL_PARENTCONTROL**: Blocked by parental controls.
*   **WOW_FAIL_LOCKED_ENFORCED**: Account locked.
*   **WOW_FAIL_TRIAL_ENDED**: Trial expired.
*   **WOW_FAIL_USE_BATTLENET**: Requires Battle.net login.
*   **WOW_FAIL_ANTI_INDULGENCE**: Anti-indulgence restriction.
*   **WOW_FAIL_EXPIRED**: Account expired.
*   **WOW_FAIL_NO_GAME_ACCOUNT**: No game account found.
*   **WOW_FAIL_CHARGEBACK**: Closed due to chargeback.
*   **WOW_FAIL_IGR_WITHOUT_BNET**: IGR time requires Battle.net merge.
*   **WOW_FAIL_GAME_ACCOUNT_LOCKED**: Account temporarily disabled.
*   **WOW_FAIL_UNLOCKABLE_LOCK**: Account locked but unlockable.
*   **WOW_FAIL_CONVERSION_REQUIRED**: Requires Battle.net conversion.
*   **WOW_FAIL_DISCONNECTED**: Connection disconnected.
*   **AccountFlags**: Bitmask enumeration for account properties.
*   **ACCOUNT_FLAG_GM**: Game Master privilege.
*   **ACCOUNT_FLAG_NOKICK**: Cannot be kicked.
*   **ACCOUNT_FLAG_COLLECTOR**: Collector status.
*   **ACCOUNT_FLAG_WOW_TRIAL**: Trial subscription.
*   **ACCOUNT_FLAG_CANCELLED**: Subscription cancelled.
*   **ACCOUNT_FLAG_IGR**: In-Game Rewards active.
*   **ACCOUNT_FLAG_WHOLESALER**: Wholesaler status.
*   **ACCOUNT_FLAG_PRIVILEGED**: Privileged account.
*   **ACCOUNT_FLAG_EU_FORBID_ELV**: EU billing restriction.
*   **ACCOUNT_FLAG_EU_FORBID_BILLING**: EU billing restriction.
*   **ACCOUNT_FLAG_WOW_RESTRICTED**: Account restricted.
*   **ACCOUNT_FLAG_REFERRAL**: Referral program.
*   **ACCOUNT_FLAG_BLIZZARD**: Blizzard employee.
*   **ACCOUNT_FLAG_RECURRING_BILLING**: Recurring billing active.
*   **ACCOUNT_FLAG_NOELECTUP**: No electronic upgrade.
*   **ACCOUNT_FLAG_KR_CERTIFICATE**: Korea certificate.
*   **ACCOUNT_FLAG_EXPANSION_COLLECTOR**: Expansion collector.
*   **ACCOUNT_FLAG_DISABLE_VOICE**: Voice chat disabled.
*   **ACCOUNT_FLAG_DISABLE_VOICE_SPEAK**: Voice speak disabled.
*   **ACCOUNT_FLAG_REFERRAL_RESURRECT**: Referral resurrect.
*   **ACCOUNT_FLAG_EU_FORBID_CC**: EU credit card restriction.
*   **ACCOUNT_FLAG_OPENBETA_DELL**: Dell open beta.
*   **ACCOUNT_FLAG_PROPASS**: Promotional pass.
*   **ACCOUNT_FLAG_PROPASS_LOCK**: Promotional pass locked.
*   **ACCOUNT_FLAG_PENDING_UPGRADE**: Pending upgrade.
*   **ACCOUNT_FLAG_RETAIL_FROM_TRIAL**: Converted from trial.
*   **ACCOUNT_FLAG_EXPANSION2_COLLECTOR**: Expansion 2 collector.
*   **ACCOUNT_FLAG_OVERMIND_LINKED**: Linked to Overmind.
*   **ACCOUNT_FLAG_DEMOS**: Demo account.
*   **ACCOUNT_FLAG_DEATH_KNIGHT_OK**: Death Knight creation allowed.

---

<!-- machine-true, projected from graph.json -->

## Map — AuthCodes

*Source:* AuthCodes.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: AccountFlags, ACCOUNT_FLAG_BLIZZARD, ACCOUNT_FLAG_CANCELLED, ACCOUNT_FLAG_COLLECTOR, ACCOUNT_FLAG_DEATH_KNIGHT_OK, ACCOUNT_FLAG_DEMOS, ACCOUNT_FLAG_DISABLE_VOICE, ACCOUNT_FLAG_DISABLE_VOICE_SPEAK, ACCOUNT_FLAG_EU_FORBID_BILLING, ACCOUNT_FLAG_EU_FORBID_CC, ACCOUNT_FLAG_EU_FORBID_ELV, ACCOUNT_FLAG_EXPANSION2_COLLECTOR, ACCOUNT_FLAG_EXPANSION_COLLECTOR, ACCOUNT_FLAG_GM, ACCOUNT_FLAG_IGR, ACCOUNT_FLAG_KR_CERTIFICATE, ACCOUNT_FLAG_NOELECTUP, ACCOUNT_FLAG_NOKICK, ACCOUNT_FLAG_OPENBETA_DELL, ACCOUNT_FLAG_OVERMIND_LINKED, ACCOUNT_FLAG_PENDING_UPGRADE, ACCOUNT_FLAG_PRIVILEGED, ACCOUNT_FLAG_PROPASS, ACCOUNT_FLAG_PROPASS_LOCK, ACCOUNT_FLAG_RECURRING_BILLING, ACCOUNT_FLAG_REFERRAL, ACCOUNT_FLAG_REFERRAL_RESURRECT, ACCOUNT_FLAG_RETAIL_FROM_TRIAL, ACCOUNT_FLAG_WHOLESALER, ACCOUNT_FLAG_WOW_RESTRICTED, ACCOUNT_FLAG_WOW_TRIAL, AuthResult, CMD_AUTH_LOGON_CHALLENGE, CMD_AUTH_LOGON_PROOF, CMD_AUTH_RECONNECT_CHALLENGE, CMD_AUTH_RECONNECT_PROOF, CMD_GRUNT_AUTH_CHALLENGE, CMD_GRUNT_AUTH_VERIFY, CMD_GRUNT_CONN_PING, CMD_GRUNT_CONN_PONG, CMD_GRUNT_HELLO, CMD_GRUNT_KICK, CMD_GRUNT_PCWARNING, CMD_GRUNT_PROVESESSION, CMD_GRUNT_STRINGS, CMD_GRUNT_SUNKENUPDATE, CMD_GRUNT_SUNKEN_ONLINE, CMD_REALM_LIST, CMD_XFER_ACCEPT, CMD_XFER_CANCEL, CMD_XFER_DATA, CMD_XFER_INITIATE, CMD_XFER_RESUME, eAuthCmd, eAuthSrvCmd, WOW_FAIL_ALREADY_ONLINE, WOW_FAIL_ANTI_INDULGENCE, WOW_FAIL_BANNED, WOW_FAIL_CHARGEBACK, WOW_FAIL_CONVERSION_REQUIRED, WOW_FAIL_DB_BUSY, WOW_FAIL_DISCONNECTED, WOW_FAIL_EXPIRED, WOW_FAIL_FAIL_NOACCESS, WOW_FAIL_GAME_ACCOUNT_LOCKED, WOW_FAIL_IGR_WITHOUT_BNET, WOW_FAIL_INCORRECT_PASSWORD, WOW_FAIL_INVALID_SERVER, WOW_FAIL_LOCKED_ENFORCED, WOW_FAIL_NO_GAME_ACCOUNT, WOW_FAIL_NO_TIME, WOW_FAIL_PARENTCONTROL, WOW_FAIL_SUSPENDED, WOW_FAIL_TRIAL_ENDED, WOW_FAIL_UNKNOWN0, WOW_FAIL_UNKNOWN1, WOW_FAIL_UNKNOWN_ACCOUNT, WOW_FAIL_UNLOCKABLE_LOCK, WOW_FAIL_USE_BATTLENET, WOW_FAIL_VERSION_INVALID, WOW_FAIL_VERSION_UPDATE, WOW_SUCCESS, WOW_SUCCESS_SURVEY -->
