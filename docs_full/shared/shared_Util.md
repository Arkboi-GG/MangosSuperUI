# shared_Util

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# shared_Util

## Purpose & Responsibilities

`shared_Util` (implemented in `Util.cpp` and declared in `Util.h`) serves as the foundational utility library for the WoWVMaNGOS server. It provides low-level, domain-neutral functions required across the entire codebase, including:

1.  **Random Number Generation:** A suite of functions (`irand`, `urand`, `frand`, etc.) wrapping a thread-local Mersenne Twister generator (`MTRand`). These are heavily used by AI logic, combat calculations, loot generation, and movement systems.
2.  **Time Management:** Utilities for high-resolution timing (`WorldTimer`), converting between seconds and human-readable strings (`secsToTimeString`), and parsing time strings (`TimeStringToSecs`).
3.  **String & Character Processing:** Functions for splitting strings (`StrSplit`, `Tokenizer`), validating character sets (Latin, Cyrillic, East Asian), case conversion (`wstrToUpper`, `wstrToLower`), and handling invisible characters.
4.  **Encoding Conversion:** Robust conversion between UTF-8, Wide Strings (`std::wstring`), and Console/OEM encodings, critical for cross-platform compatibility (Windows vs. Linux/macOS) and database interaction.
5.  **Data Serialization Helpers:** Bit-packing utilities (`secsToTimeBitFields`), byte manipulation (`SetByteValue`, `SetUInt16Value`), and hex encoding/decoding (`hexEncodeByteArray`, `ByteArrayToHexStr`).
6.  **Mathematical Helpers:** Rounding with specific tie-breaking rules (`round_float`, `round_float_chance`), linear interpolation (`InterpolateValueAtIndex`), and "dithering" (adding small random noise to float values for damage/healing calculations).

This unit contains **no database table interactions**. All data operations are in-memory transformations.

## Member-by-Member Behavior

### Random Number Generation
The core RNG engine is a `thread_local MTRand mtRand` instance. This ensures that concurrent threads (e.g., different map updates or player sessions) do not race on a global RNG state, providing deterministic sequences per thread.

*   **`irand`**: Returns a signed 32-bit integer in `[min, max]`. Uses `mtRand.randInt`.
*   **`urand`**: Returns an unsigned 32-bit integer in `[min, max]`. Heavily used by AI for target selection, spell cooldowns, and movement waypoints.
*   **`frand`**: Returns a float in `[min, max)`. Used for spatial randomness (e.g., spawn positions).
*   **`rand32`**: Returns a raw 32-bit integer. Used primarily for cryptographic-like challenges (Warden scans, AuthSocket).
*   **`rand_norm` / `rand_norm_f`**: Returns a normalized random value in `[0.0, 1.0)`. Used for probability checks and Gaussian-like distributions in some AI logic.
*   **`rand_chance` / `rand_chance_f`**: Returns a random value in `[0.0, 100.0)`. Used by `roll_chance_*` functions.
*   **`randtime`**: Generates a random duration (`Milliseconds`) between `min` and `max`. Used by boss AI timers (e.g., Heigan the Unclean).
*   **`roll_chance_f` / `roll_chance_i` / `roll_chance_u`**: Inline helpers that return `true` if a random roll succeeds against a given percentage chance (0-100). Critical for hit/crit/resist/loot rolls.

### Time Utilities
*   **`WorldTimer` Class**:
    *   **`tick`**: Advances the global world time. It saves the previous time, fetches the current steady clock time via `getMSTime`, and returns the delta in milliseconds. This is the heartbeat of the server loop.
    *   **`getMSTime`**: Calculates milliseconds elapsed since the application started, using `std::chrono::steady_clock`. It subtracts `GetApplicationStartTime()` (from `Timer` unit) to ensure monotonicity.
    *   **`tickTime` / `tickPrevTime`**: Accessors for the current and previous tick timestamps.
*   **`secsToTimeBitFields`**: Packs a `time_t` into a 32-bit integer bitfield (Year, Month, Day, Weekday, Hour, Minute). Used by `Player.Main` to send initial packets efficiently.
*   **`secsToTimeString`**: Converts seconds to a human-readable string (e.g., "1 Day 2 Hours"). Supports short text ("1d 2h") and hours-only modes.
*   **`TimeStringToSecs`**: Parses strings like "1d2h3m4s" back into total seconds.
*   **`TimeToTimestampStr`**: Formats a `time_t` into a strict `YYYY-MM-DD_HH-MM-SS` string for logging or filenames.

### String & Character Validation
*   **`Tokenizer`**: A lightweight string splitter. It allocates a mutable copy of the input string, replaces separators with null terminators, and stores pointers to the substrings in a `std::vector`. This avoids copying substring data, making it fast for DB row parsing.
*   **`StrSplit`**: Similar to `Tokenizer` but returns a `std::vector<std::string>` (owned copies). Safer for general use but higher memory overhead.
*   **`stripLineInvisibleChars`**: Removes tabs, newlines, and bells (`\7`) from strings, collapsing multiple whitespace into single spaces. Used for chat sanitization.
*   **`isBasicLatinCharacter` / `isExtendedLatinCharacter` / `isCyrillicCharacter` / `isEastAsianCharacter`**: Inline predicates checking Unicode code points. Used for name validation and chat filtering.
*   **`isNumeric`**: Overloaded for `char`, `wchar_t`, `std::string`, and `std::wstring`. Checks if a string contains only digits.
*   **`isWhiteSpace`**: Wrapper around `std::isspace`.
*   **`strToUpper` / `strToLower`**: Case conversion for `std::string`.
*   **`wcharToUpper` / `wcharToLower`**: Case conversion for wide characters, handling extended Latin and Cyrillic ranges manually.
*   **`wstrToUpper` / `wstrToLower`**: Case conversion for `std::wstring`.

### Encoding Conversion
*   **`Utf8toWStr`**: Converts UTF-8 to `std::wstring` (UTF-16). Handles invalid UTF-8 gracefully by returning `false` and clearing the output. Respects `max_len`.
*   **`WStrToUtf8`**: Converts `std::wstring` back to UTF-8.
*   **`utf8ToConsole` / `consoleToUtf8`**: Platform-specific conversions. On Windows, uses `CharToOemBuffW`/`OemToCharBuffW` to handle legacy console encoding issues. On Linux/macOS, assumes UTF-8 is native.
*   **`Utf8FitTo`**: Checks if a UTF-8 string contains a wide-string substring (case-insensitive). Used for lookup commands.
*   **`utf8printf` / `vutf8printf`**: Printf-style formatting that ensures output is valid for the console/log system, handling Windows OEM conversion internally.

### Data Manipulation & Math
*   **`dither` / `ditheru`**: Adds a small random float (`frand(0,1)`) to the absolute value of the input, then floors it. This introduces slight variance to damage/healing numbers to avoid "flat" values, mimicking client-side behavior.
*   **`round_float`**: Rounds to nearest integer. Ties (0.5) are broken randomly (`urand(0,1)`).
*   **`round_float_chance`**: Probabilistic rounding. If the fractional part is 0.5, it rounds up with 50% chance. Used for mana regeneration.
*   **`ApplyModUInt32Var` / `ApplyModFloatVar` / `ApplyPercentModFloatVar`**: Helper functions to apply modifiers (buffs/debuffs) to variables. `ApplyPercentModFloatVar` prevents division by zero if the modifier is -100%.
*   **`GetUInt32ValueFromArray` / `GetFloatValueFromArray`**: Extracts values from a `Tokens` vector. Note: `GetFloatValueFromArray` performs a bitwise cast from `uint32` to `float`, implying the token stores the IEEE 754 binary representation of the float, not its decimal string.
*   **`SetByteValue` / `SetUInt16Value`**: Sets specific bytes or 16-bit words within a 32-bit integer at a given offset. Used for constructing packet fields.
*   **`FlagsToString`**: Converts a bitmask into a comma-separated string of names, using a callback function.
*   **`SplitStringByDelimiter`**: Splits a string by a single character delimiter.
*   **`hexEncodeByteArray` / `ByteArrayToHexStr` / `HexStrToByteArray`**: Bidirectional conversion between binary data and hexadecimal strings. `ByteArrayToHexStr` supports reversing the byte order.
*   **`IsIPAddress`**: Validates an IP address string using `IO::Networking::IpAddress::TryParseFromString`.
*   **`CreatePIDFile`**: Writes the current process ID to a file. Uses `IO::Utils::GetCurrentProcessId`.
*   **`BatchifyTimer`**: Rounds a timer up to the next multiple of an interval. Used for batching spell effects.
*   **`GetLambda` / `InterpolateValueAtIndex`**: Linear interpolation helpers. Used for calculating creature stats based on level.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   `WorldTimer::tick` calls `WorldTimer::getMSTimeDiff` (internal to `WorldTimer` class, but `getMSTime` calls `Timer::GetApplicationStartTime` from the `Timer` unit).
    *   `CreatePIDFile` calls `IO::Utils::GetCurrentProcessId` from the `shared_IO_Utils` unit.
    *   `IsIPAddress` calls `IO::Networking::IpAddress::TryParseFromString` from the `IpAddress` unit.
    *   `Utf8toWStr` / `WStrToUtf8` rely on the `utf8cpp` library (external dependency, included via headers).
    *   `MTRand` relies on the `mersennetwister` library (external dependency).

*   **Called By:**
    *   **AI & Combat:** Almost every AI module (`boss_*`, `CreatureAI`, `Unit.Main`) calls `urand`, `frand`, `roll_chance_*`, and `dither` for decision-making and damage calculation.
    *   **World Loop:** `Map.Main::Update`, `World::Update`, and `Creature.Main::Update` call `WorldTimer::tick` and `getMSTime` to drive game time.
    *   **Database Loading:** `Tokenizer` is used by `game_Objects_Item::LoadFromDB` and `WorldObject.Object::_LoadIntoDataField` to parse raw DB rows.
    *   **Chat & Commands:** `ChatHandler` modules extensively use `StrSplit`, `isNumeric`, `secsToTimeString`, and `Utf8FitTo` for command parsing and output formatting.
    *   **Logging:** `Log.Main` uses `utf8printf` and `vutf8printf` for safe log output.
    *   **Warden (Anti-Cheat):** `Log.Warden` and `WardenScan` use `rand32` for challenge generation and `ByteArrayToHexStr` for reporting scan results.

## Data Model

This unit does **not** interact directly with any database tables. It operates purely on in-memory data structures (strings, integers, floats). The `Tokenizer` class is designed to consume data *already fetched* from the database by other units (e.g., `Player.Main`, `Creature.Main`), but it performs no SQL queries itself.

## Notable Implementation Details

1.  **Thread-Local RNG:** The use of `thread_local MTRand mtRand` is crucial. In a multi-threaded server, sharing a single RNG instance would cause race conditions and non-deterministic behavior. Each thread gets its own sequence.
2.  **Tokenizer Memory Management:** `Tokenizer` allocates a `char*` buffer (`m_str`) in its constructor and deletes it in the destructor. It stores `char const*` pointers into this buffer in `m_storage`. This is efficient but requires careful lifetime management: the `Tokenizer` object must outlive any usage of the tokens it produces. If a token pointer is stored elsewhere after the `Tokenizer` goes out of scope, it becomes a dangling pointer.
3.  **Float Casting in `GetFloatValueFromArray`:** The function `GetFloatValueFromArray` casts a `uint32` (obtained from `atoi` on the token string) to a `float` via `memcpy`. This implies that the database or configuration files store floats as their raw 32-bit integer representation (IEEE 754), not as decimal strings. This is an unusual and fragile design choice; if the input string is a standard decimal representation (e.g., "1.5"), `atoi` will truncate it to "1", and the resulting float will be incorrect. However, given the callers (`Corpse::LoadFromDB`, `Player.Main::BuildEnumData`), it likely processes pre-formatted binary dumps or specific DB columns where this format is enforced.
4.  **Dithering Logic:** `dither` and `ditheru` add random noise before flooring. This is a common technique in MMOs to simulate client-side floating-point precision differences and prevent "perfect" damage numbers, adding a layer of realism and unpredictability.
5.  **Platform-Specific Console Handling:** `utf8ToConsole` and `consoleToUtf8` use Windows API functions (`CharToOemBuffW`, `OemToCharBuffW`) on Windows to handle the discrepancy between UTF-16 wide strings and the legacy OEM code page used by the Windows console. On Unix-like systems, they assume UTF-8 is the native encoding.
6.  **Case Conversion Limitations:** The `wcharToUpper` and `wcharToLower` functions handle Basic Latin, Extended Latin, and Cyrillic manually. They do not use ICU or standard locale-aware functions. This means they may fail for complex Unicode cases (e.g., Turkish 'i', Greek sigma, or certain ligatures). This is acceptable for a game server where player names and chat are often restricted to simpler character sets, but it is a known limitation.
7.  **`secsToTimeBitFields` Packing:** The bitfield packing order is: Year (bits 24-31), Month (20-23), Day (14-19), Weekday (11-13), Hour (6-10), Minute (0-5). This compact representation is used for network transmission efficiency.

## Member Reference

*   **Tokenizer**: Constructor. Allocates a mutable copy of the input string, splits it by `sep`, and stores pointers to substrings in `m_storage`.
*   **~Tokenizer**: Destructor. Frees the allocated `m_str` buffer.
*   **begin**: Returns iterator to the start of `m_storage`.
*   **end**: Returns iterator to the end of `m_storage`.
*   **size**: Returns the number of tokens found.
*   **operator[]**: Non-const access to a token by index.
*   **operator[]#2**: Const access to a token by index.
*   **secsToTimeBitFields**: Packs `time_t` into a 32-bit bitfield for network transmission.
*   **tickTime**: Returns the current world tick timestamp.
*   **tickPrevTime**: Returns the previous world tick timestamp.
*   **tick**: Advances world time, returns delta in ms. Calls `WorldTimer::getMSTimeDiff` (internal) and `Timer::GetApplicationStartTime` (cross-unit).
*   **getMSTime**: Returns milliseconds since application start. Calls `Timer::GetApplicationStartTime` (cross-unit).
*   **irand**: Returns random `int32` in `[min, max]`.
*   **urand**: Returns random `uint32` in `[min, max]`.
*   **frand**: Returns random `float` in `[min, max)`.
*   **roll_chance_f**: Returns `true` if `chance` > random 0-100.
*   **roll_chance_i**: Returns `true` if `chance` > random 0-99.
*   **rand32**: Returns raw random `int32`.
*   **roll_chance_u**: Returns `true` if `chance` > random 0-99.
*   **rand_norm**: Returns random `double` in `[0.0, 1.0)`.
*   **rand_norm_f**: Returns random `float` in `[0.0, 1.0)`.
*   **rand_chance**: Returns random `double` in `[0.0, 100.0)`.
*   **rand_chance_f**: Returns random `float` in `[0.0, 100.0)`.
*   **round_float**: Rounds float to nearest int, breaking ties randomly.
*   **randtime**: Returns random `Milliseconds` in `[min, max]`.
*   **StrSplit**: Splits string by separator characters, returning `vector<string>`.
*   **round_float_chance**: Probabilistically rounds float based on fractional part.
*   **ApplyModUInt32Var**: Applies additive modifier to `uint32`.
*   **GetUInt32ValueFromArray**: Extracts `uint32` from token vector.
*   **ApplyModFloatVar**: Applies additive modifier to `float`.
*   **GetFloatValueFromArray**: Extracts `float` from token vector via bitwise cast.
*   **ApplyPercentModFloatVar**: Applies percentage modifier to `float`.
*   **stripLineInvisibleChars**: Removes invisible chars from `std::string`.
*   **isBasicLatinCharacter**: Checks if wchar is Basic Latin.
*   **isExtendedLatinCharacter**: Checks if wchar is Extended Latin.
*   **stripLineInvisibleChars#2**: Removes invisible chars from `char*`.
*   **isCyrillicCharacter**: Checks if wchar is Cyrillic.
*   **isEastAsianCharacter**: Checks if wchar is East Asian.
*   **secsToTimeString**: Converts seconds to human-readable string.
*   **isWhiteSpace**: Checks if char is whitespace.
*   **isNumeric#5**: Checks if `wchar_t` is numeric.
*   **isNumeric#4**: Checks if `std::wstring` is numeric.
*   **isNumericOrSpace**: Checks if `wchar_t` is numeric or space.
*   **isNumeric#3**: Checks if `std::string` is numeric.
*   **isNumeric**: Checks if `char` is numeric.
*   **isNumeric#2**: Checks if `char const*` is numeric.
*   **isBasicLatinString**: Checks if `wstring` contains only Basic Latin (and optionally numeric/space).
*   **TimeStringToSecs**: Parses time string to seconds.
*   **isExtendedLatinString**: Checks if `wstring` contains only Extended Latin.
*   **isCyrillicString**: Checks if `wstring` contains only Cyrillic.
*   **isEastAsianString**: Checks if `wstring` contains only East Asian.
*   **isLeapYear**: Checks if year is a leap year.
*   **TimeToTimestampStr**: Formats time to `YYYY-MM-DD_HH-MM-SS`.
*   **strToUpper**: Converts `std::string` to uppercase.
*   **strToLower**: Converts `std::string` to lowercase.
*   **IsIPAddress**: Validates IP address string. Calls `IO::Networking::IpAddress::TryParseFromString` (cross-unit).
*   **wcharToUpper**: Converts `wchar_t` to uppercase.
*   **CreatePIDFile**: Writes PID to file. Calls `IO::Utils::GetCurrentProcessId` (cross-unit).
*   **utf8length**: Returns length of UTF-8 string in characters.
*   **wcharToUpperOnlyLatin**: Converts `wchar_t` to uppercase if Basic Latin.
*   **wcharToLower**: Converts `wchar_t` to lowercase.
*   **Utf8toWStr**: Converts UTF-8 to `std::wstring`.
*   **wstrToUpper**: Converts `std::wstring` to uppercase.
*   **wstrToLower**: Converts `std::wstring` to lowercase.
*   **WStrToUtf8**: Converts `std::wstring` to UTF-8.
*   **BatchifyTimer**: Rounds timer up to next interval multiple.
*   **GetLambda**: Calculates interpolation lambda.
*   **utf8ToConsole**: Converts UTF-8 to console encoding.
*   **InterpolateValueAtIndex**: Linearly interpolates value.
*   **consoleToUtf8**: Converts console encoding to UTF-8.
*   **Utf8FitTo**: Checks if UTF-8 string contains wide-string substring.
*   **utf8printf**: Printf-style output with UTF-8/console handling.
*   **vutf8printf**: Variadic printf-style output with UTF-8/console handling.
*   **hexEncodeByteArray**: Encodes byte array to hex string.
*   **ByteArrayToHexStr**: Converts byte array to hex string.
*   **HexStrToByteArray**: Converts hex string to byte array.
*   **dither**: Adds random noise to float and floors.
*   **ditheru**: Adds random noise to float and floors (unsigned).
*   **SetByteValue**: Sets byte at offset in `uint32`.
*   **SetUInt16Value**: Sets 16-bit word at offset in `uint32`.
*   **FlagsToString**: Converts bitmask to string of names.
*   **SplitStringByDelimiter**: Splits string by single delimiter.

---

<!-- machine-true, projected from graph.json -->

## Map — shared_Util

*Source:* Util.cpp, Util.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Tokenizer | ctor | — | game_Objects_Item/LoadFromDB, WorldObject.Object/_LoadIntoDataField | — |
| ~Tokenizer | dtor | — | — | — |
| begin | method | — | — | — |
| end | method | — | — | — |
| size | method | — | game_Objects_Item/LoadFromDB, WorldObject.Object/_LoadIntoDataField | — |
| operator[] | method | — | game_Objects_Item/LoadFromDB, WorldObject.Object/_LoadIntoDataField | — |
| operator[]#2 | method | — | — | — |
| secsToTimeBitFields | function | — | Player.Main/SendInitialPacketsBeforeAddToMap | — |
| tickTime | method | — | Creature.Main/Update | — |
| tickPrevTime | method | — | — | — |
| tick | method | WorldTimer/getMSTimeDiff | — | — |
| getMSTime | method | Timer/GetApplicationStartTime | BattleGroundMgr/AddGroup, BattleGroundMgr/CheckPremadeMatch, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerInvitedToBgUpdateAverageWaitTime, BattleGroundMgr/PlayerLoggedIn, BattleGroundMgr/PlayerLoggedOut#2, ChatHandler.DebugCommands/HandleMmapTestArea, CreatureAI/TriggerAlertDirect, CreatureGroups/Load, DatabaseMysql/Execute, DatabaseMysql/_Query, GameObject/AddToWorld, GMTicketMgr/LoadSurveys, GMTicketMgr/LoadTickets, Log.Warden/BeginScanClock, Log.Warden/BeginTimeoutClock, Log.Warden/Update, Map.Main/DoUpdate, Map.Main/Map, Map.Main/SendObjectUpdates, Map.Main/Update#3, Map.Main/UpdateCells, Map.Main/UpdatePlayers, Map.Main/UpdateSessionsMovementAndSpellsIfNeeded, Map.Main/UpdateVisibilityForRelocations, MapManager/Update, Master/freezeDetector, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnDeath, MovementAnticheat/ResetBottingStats, MovementBroadcaster/UpdateConfiguration, MovementBroadcaster/Work, PointMovementGenerator/ComputePath, Spell.Effects/EffectSanctuary, Spell.Main/DoAllEffectOnTarget#3, SpellMgr/LoadSpells, SqlOperations/Update, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, Unit.Main/AddToWorld, Unit.Main/ApplyDiminishingAura, Unit.Main/CheckPendingMovementChanges, Unit.Main/GetDiminishing, Unit.Main/IncrDiminishing, Unit.Main/PlayerMovementPendingChange, Unit.Main/SetInCombatState, WardenWin/LoadScriptedScans, World/GetDelayUntilNextSpellBatchingInterval, World/RemoveQueuedSession, World/SetInitialWorldSettings, World/Update, World/UpdateSessions, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/FillFrom, WorldObject.Object/Read, WorldObject.Object/Relocate#2, WorldObject.Object/WorldObject, WorldRunnable/operator(), WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer, WorldSession.Main/ProcessPackets, WorldSession.Main/Update, WorldSession.Main/WorldSession, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/RejectMovementPacketsFor, WorldSocket/_HandleCompleteReceivedPacket | — |
| irand | function | — | GameObject/Use, instance_razorfen_downs/SetData, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_temple_of_ahnqiraj/UpdateCThunWhisper, LootMgr/Roll#2, Player.Main/UpdateSkillPro, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Main/CheckCast, SpellCaster/CalculateSpellEffectValue, SpellCaster/MagicSpellHitResult, spell_item/OnEffectExecute#5, Unit.SpellAuras/HandleAuraModShapeshift, world_event_wareffort/UpdateAI#2 | — |
| urand | function | — | AiBotAI.Grind/DoGrindPatrol, AiBotAI.Main/UpdateAI, AiBotAI.Movement/DoRandomWander, AiBotAI.Movement/MoveToDestination, arathi_highlands/Reset, arathi_highlands/UpdateEscortAI, arena_challenge_ai/UpdateAI, arena_challenge_ai/UpdateAI#2, arena_challenge_ai/UpdateAI#3, arena_challenge_ai/UpdateAI#4, arena_challenge_ai/UpdateAI#5, arena_challenge_ai/UpdateAI#6, arena_challenge_ai/UpdateAI#7, ashenvale/EnragedFoulwealdJustDied, AuthSocket/_HandleLogonProof__PostRecv, azshara/Reset, azshara/UpdateAI, azshara/UpdateAI#2, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceGraveyard, BattleBotAI.BattleBotWaypoints/WSG_AtHordeGraveyard, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/UpdateAI, BattleGroundAB/StartingEventOpenDoors, BattleGroundAV/initializeChallengeInvocationGoals, BattleGroundAV/Update, blackrock_depths/Aggro, blackrock_depths/Aggro#3, blackrock_depths/AttackThief, blackrock_depths/GOHello_go_dark_keeper_portrait, blackrock_depths/npc_grimstoneAI, blackrock_depths/UpdateAI, blackrock_depths/UpdateAI#4, blackrock_depths/WarnThief, boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/IMPALE_CD, boss_anubrekhan/KilledUnit, boss_anubrekhan/LOCUST_SWARM_CD, boss_anubrekhan/Reset, boss_anubrekhan/UpdateAI, boss_arcanist_doan/UpdateAI, boss_archaedas/Reset#2, boss_arlokk/Reset, boss_arlokk/Reset#2, boss_arlokk/UpdateAI, boss_arlokk/UpdateAI#2, boss_ayamiss/UpdateAI, boss_baroness_anastari/UpdateAI, boss_baron_geddon/Reset, boss_baron_geddon/UpdateAI, boss_broodlord_lashlayer/Reset, boss_broodlord_lashlayer/UpdateAI, boss_bug_trio/Reset#2, boss_bug_trio/Reset#3, boss_bug_trio/Reset#4, boss_bug_trio/UpdateBugAI#2, boss_bug_trio/UpdateBugAI#3, boss_bug_trio/UpdateBugAI#4, boss_buru/UpdateAI, boss_cannon_master_willey/Reset, boss_cannon_master_willey/UpdateAI, boss_chromaggus/UpdateAI, boss_cthun/groundTremorResetCooldownFunc, boss_cthun/trashResetCooldownFunc, boss_dathrohan_balnazzar/JustDied, boss_dathrohan_balnazzar/UpdateAI, boss_dragon_of_nightmare/ChangeTarget, boss_dragon_of_nightmare/Reset, boss_dragon_of_nightmare/UpdateAI, boss_dragon_of_nightmare/UpdatePetAI, boss_ebonroc/UpdateAI, boss_emeriss/Reset, boss_emeriss/UpdateDragonAI, boss_emperor_dagran_thaurissan/Reset, boss_emperor_dagran_thaurissan/UpdateAI, boss_faerlina/KilledUnit, boss_faerlina/POSIONBOLT_VOLLEY_CD, boss_faerlina/RAINOFFIRE_CD, boss_faerlina/UpdateAI, boss_fankriss/GetHatchlingSpawnAmount, boss_fankriss/ReinitializeWebTimers, boss_fankriss/Reset, boss_fankriss/UpdateAI, boss_firemaw/UpdateAI, boss_flamegor/UpdateAI, boss_four_horsemen/KilledUnit, boss_four_horsemen/UpdateAI#2, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_gahzranka/UpdateAI, boss_garr/UpdateEvents, boss_gehennas/Reset, boss_gehennas/UpdateAI, boss_general_angerforge/Reset, boss_general_angerforge/UpdateAI, boss_gluth/SummonAdd, boss_gordok_king/Reset, boss_gordok_king/Reset#2, boss_gordok_king/UpdateAI#2, boss_gordok_king/UpdateAIMage, boss_gordok_king/UpdateAIPrist, boss_gordok_king/UpdateAIShaman, boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/UpdateAI, boss_grobbulus/DoCastMutagenInjection, boss_grobbulus/INJECTION_CD, boss_grobbulus/SLIMESPRAY_CD, boss_hakkar/UpdateAI, boss_heigan/Aggro, boss_heigan/EventPortPlayer, boss_herod/mob_scarlet_traineeAI, boss_herod/Reset, boss_herod/UpdateAI, boss_huhuran/Reset, boss_huhuran/UpdateAI, boss_immol_thar/Reset, boss_immol_thar/UpdateAI, boss_interrogator_vishas/UpdateAI, boss_jandice_barov/Reset#2, boss_jandice_barov/UpdateAI#2, boss_jeklik/DoAttack, boss_jeklik/UpdateAI, boss_jeklik/UpdateAI#3, boss_jindo/Reset, boss_jindo/UpdateAI, boss_kurinnaxx/UpdateAI, boss_loatheb/boss_loathebAI, boss_loatheb/SummonedCreatureDespawn, boss_loatheb/SummonedCreatureJustDied, boss_loatheb/WhackAStalk, boss_maexxna/DoCastWebWrap, boss_maexxna/NecroticPoisonCooldown, boss_maexxna/PoisonShockCooldown, boss_majordomo_executus/UpdateAI, boss_maleki_the_pallid/UpdateAI, boss_mandokir/KilledUnit, boss_mandokir/Reset#3, boss_mandokir/UpdateAI, boss_mandokir/UpdateAI#3, boss_marli/UpdateAI, boss_mr_smite/UpdateAI, boss_nefarian/KilledUnit, boss_nefarian/npc_corrupted_totemAI, boss_nefarian/OnAfterApply, boss_nefarian/OnPeriodicTickEnd, boss_nefarian/Reset, boss_nefarian/UpdateAI, boss_nerubenkan/RaiseUndeadScarab, boss_nerubenkan/UpdateAI, boss_noth/Aggro, boss_noth/BlinkAndRepeatEvent, boss_noth/CurseAndRepeatEvent, boss_noth/KilledUnit, boss_noth/OnRemoveVulnerability, boss_noth/Summon2Guardians, boss_noth/Summon4Champions, boss_noth/TeleportToBalc, boss_onyxia/DoMovement, boss_onyxia/MovementInform, boss_onyxia/PhaseOne, boss_onyxia/PhaseThree, boss_onyxia/PhaseTransition, boss_onyxia/PhaseTwo, boss_onyxia/Reset#2, boss_ossirian/OnUse, boss_ossirian/UpdateAI#2, boss_ouro/MoveInLineOfSight#2, boss_ouro/Reset, boss_ouro/UpdateAI, boss_ouro/UpdateAI#2, boss_patchwerk/Aggro, boss_patchwerk/KilledUnit, boss_razorgore/PopAdd, boss_razorgore/UpdateAI, boss_razuvious/Aggro, boss_razuvious/KilledUnit, boss_razuvious/Reset#2, boss_razuvious/UpdateAI, boss_razuvious/UpdateAI#2, boss_renataki/Reset, boss_renataki/UpdateAI, boss_sapphiron/DoIceBolt, boss_sapphiron/OnSetTargetMap, boss_sapphiron/PickNewTarget, boss_sapphiron/UpdateAI, boss_sapphiron/UpdateAI#2, boss_sartura/AssignRandomThreat, boss_sartura/AssignRandomThreat#2, boss_sartura/Reset, boss_sartura/Reset#2, boss_sartura/UpdateAI, boss_sartura/UpdateAI#2, boss_shazzrah/Reset, boss_shazzrah/UpdateAI, boss_skeram/CastBlink#2, boss_skeram/KilledUnit, boss_skeram/Reset, boss_skeram/UpdateAI, boss_sulfuron_harbinger/UpdateAI, boss_taerar/Reset, boss_taerar/Reset#2, boss_taerar/UpdateDragonAI, boss_taerar/UpdatePetAI, boss_tendris_warpwood/Reset, boss_tendris_warpwood/UpdateAI, boss_thaddius/ChainLightningTimer, boss_thaddius/PowerSurgeTimer, boss_thaddius/WarstompTimer, boss_thermaplugg/EffectDummyCreature_spell_boss_thermaplugg, boss_thermaplugg/Reset, boss_thermaplugg/UpdateAI, boss_the_beast/Reset, boss_the_beast/UpdateAI, boss_twinemperors/GetBugSpellCooldown, boss_twinemperors/GetBugSpellCooldown#2, boss_twinemperors/HandleBugSpell, boss_twinemperors/KilledUnit, boss_twinemperors/KilledUnit#2, boss_twinemperors/Reset, boss_twinemperors/Reset#2, boss_twinemperors/UpdateAI#2, boss_twinemperors/updateArcaneBurst, boss_twinemperors/UpdateBlizzard, boss_twinemperors/UpdateEmperor, boss_twinemperors/UpdateEmperor#2, boss_twinemperors/UpdateTeleportToMyBrother#2, boss_urok/UrokUnderlingDied, boss_vaelastrasz/KilledUnit, boss_vaelastrasz/Reset#2, boss_vaelastrasz/Reset#3, boss_vaelastrasz/UpdateAI, boss_vaelastrasz/UpdateAI#2, boss_vaelastrasz/UpdateAI#3, boss_venoxis/UpdateAI, boss_victor_nefarius/Reset, boss_victor_nefarius/UpdateAI, boss_viscidus/Reset, boss_viscidus/UpdateAI, boss_ysondre/Reset, boss_ysondre/Reset#2, boss_ysondre/UpdateAI, boss_ysondre/UpdateDragonAI, boss_zevrim/Reset, boss_zevrim/UpdateAI, ChatHandler.AuctionHouseBotMgr/AddItem, ChatHandler.AuctionHouseBotMgr/Update, ChatHandler.DebugCommands/HandleDebugPvPCreditCommand, ChatHandler.HardcodedEvents/HandleActiveZone, ChatHandler.HardcodedEvents/StartNewCityAttackIfTime, ChatHandler.HardcodedEvents/Update#2, ChatHandler.HardcodedEvents/Update#3, ChatHandler.PlayerBotMgr/AddOrRemoveBot, ChatHandler.PlayerBotMgr/AddRandomBot, ChatHandler.PlayerBotMgr/DeleteRandomBot, ChatHandler.PlayerBotMgr/Update, CombatBotBaseAI/EquipRandomGearInEmptySlots, Creature.Main/ChooseDisplayId, Creature.Main/SelectAttackingTarget, Creature.Main/SelectLevel, Creature.Main/SetDeathState, Creature.Main/UpdateVendorItemCurrentCount, CreatureAI/DoSpellsListCasts, CreatureEventAI/ProcessEvent, CreatureEventAI/UpdateRepeatTimer, CreatureGroups/ChooseCreatureId, darkshore/Aggro, darkshore/Aggro#2, darkshore/DoAttack, darkshore/Reset, darkshore/Reset#2, darkshore/SetSleeping, darkshore/UpdateFollowerAI, desolace/JustSummoned, desolace/Reset#3, desolace/UpdateAI, desolace/UpdateAI#2, DNS/ResolveDomainSingle, dreadsteed_ritual/BreakNode, dreadsteed_ritual/reset#3, dreadsteed_ritual/SummonGuard, dreadsteed_ritual/SummonImp, dreadsteed_ritual/UpdateAI, dreadsteed_ritual/UpdateAI#2, duskwood/Aggro, duskwood/Reset#4, duskwood/UpdateAI#3, dustwallow_marsh/Aggro, dustwallow_marsh/Reset#2, dustwallow_marsh/UpdateAI#4, dustwallow_marsh/UpdateAI#5, eastern_plaguelands/DamageTaken#2, eastern_plaguelands/GenerateWaveNumber, eastern_plaguelands/NewWave, eastern_plaguelands/Reset#5, eastern_plaguelands/SetAttackOnPeasantOrPlayer, eastern_plaguelands/SpellHit, eastern_plaguelands/UpdateAI, eastern_plaguelands/UpdateAI#2, eastern_plaguelands/UpdateAI#3, eastern_plaguelands/UpdateAI#4, elemental_invasions/Reset, elemental_invasions/UpdateAI#2, FearMovementGenerator/TimedFearMovementGenerator, FearMovementGenerator/_setTargetLocation, felwood/Reset, felwood/Reset#2, felwood/UpdateAI#2, felwood/UpdateEscortAI, feralas/EnterCombat, feralas/JustDied, feralas/MoveInLineOfSight, feralas/Reset#2, feralas/Reset#4, feralas/UpdateAI, feralas/UpdateAI#2, feralas/UpdateAI#3, feralas/UpdateAI#4, feralas/UpdateFollowerAI, fireworks_show/CheerPicker, FleeingMovementGenerator/_setTargetLocation, GameObject/ComputeRespawnDelay#2, GameObject/FinishRitual, game_Battlegrounds_BattleGround/HandleTriggerBuff, game_Group_Group/CountTheRoll, gnomeregan/AttackedBy, gnomeregan/OnInit, go_scripts/go_lunar_festival_firecracker, go_scripts/OnUse, GridMap/TerrainInfo, hinterlands/Aggro, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_depths/OnCreatureDeath, instance_blackrock_depths/Update, instance_blackrock_spire/OnCreatureCreate, instance_blackrock_spire/OnCreatureDeath, instance_blackrock_spire/Update, instance_blackwing_lair/go_engin_suppressionAI, instance_blackwing_lair/Initialize, instance_blackwing_lair/OnCreatureCreate, instance_blackwing_lair/OnCreatureRespawn, instance_blackwing_lair/OnUse, instance_blackwing_lair/OnUse#2, instance_blackwing_lair/Reset#2, instance_blackwing_lair/Reset#3, instance_blackwing_lair/UpdateAI#2, instance_blackwing_lair/UpdateAI#3, instance_blackwing_lair/UpdateAI#4, instance_dire_maul/Aggro, instance_dire_maul/GetChoRushEquipment, instance_dire_maul/MovementInform, instance_dire_maul/Reset#12, instance_dire_maul/Reset#13, instance_dire_maul/Reset#2, instance_dire_maul/Reset#3, instance_dire_maul/Reset#4, instance_dire_maul/Reset#5, instance_dire_maul/Reset#6, instance_dire_maul/Reset#7, instance_dire_maul/Reset#9, instance_dire_maul/UpdateAI, instance_dire_maul/UpdateAI#10, instance_dire_maul/UpdateAI#2, instance_dire_maul/UpdateAI#3, instance_dire_maul/UpdateAI#4, instance_dire_maul/UpdateAI#5, instance_dire_maul/UpdateAI#6, instance_dire_maul/UpdateAI#7, instance_dire_maul/UpdateAI#9, instance_molten_core/OnCreatureCreate, instance_molten_core/OnCreatureRespawn, instance_naxxramas.boss_kelthuzad/DoChains, instance_naxxramas.boss_kelthuzad/KilledUnit, instance_naxxramas.boss_kelthuzad/SpawnAndSendP1Creature, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/ChangeColor, instance_naxxramas.Main/Initialize, instance_naxxramas.Main/Reset#2, instance_naxxramas.Main/Update, instance_naxxramas.Main/UpdateAI#3, instance_ruins_of_ahnqiraj/GetData64, instance_stratholme/DoSpawnPlaguedCritters, instance_stratholme/MoveAbomnationMob, instance_stratholme/Update, instance_temple_of_ahnqiraj/OnCreatureCreate, instance_temple_of_ahnqiraj/Reset, instance_temple_of_ahnqiraj/UpdateAI, instance_temple_of_ahnqiraj/UpdateCThunWhisper, instance_zulfarrak/SendAddsUpStairs, Log.Warden/RequestChallenge, LootMgr/GenerateMoneyLoot, LootMgr/LootItem, LootMgr/Roll#2, Map.ScriptCommands/ChooseScriptIdToStart, Map.ScriptCommands/ScriptCommand_CreatureSpells, Map.ScriptCommands/ScriptCommand_Emote, Map.ScriptCommands/ScriptCommand_SetPhaseRange, molten_core/Reset#2, molten_core/Reset#3, molten_core/Reset#4, molten_core/Reset#5, molten_core/UpdateAI#2, molten_core/UpdateAI#3, molten_core/UpdateAI#4, molten_core/UpdateAI#5, moonglade/UpdateEscortAI, npcs_special/npc_gnomish_battle_chickenAI, npcs_special/ReceiveEmote, npcs_special/Reset, npcs_special/Reset#7, npcs_special/SpellHit, npcs_special/UpdateAI#3, npcs_special/UpdateAI#5, npcs_special/UpdatePetAI, npcs_special/UpdatePetAI#4, npc_sandstalker/UpdateAI, ObjectMgr/GeneratePetName, ObjectMgr/GeneratePlayerName, ObjectMgr/GetCreatureDisplayInfoRandomGender, ObjectMgr/GetRandomMountForRace, PartyBotAI/UpdateAI, Pet.Main/CheckLearning, PetAI/UpdateAI, Player.Main/GetNextRandomRaidMember, Player.Main/OnMirrorTimerExpirationPulse, Player.Main/Player#5, Player.Main/SendLoot, PlayerAI/UpdateAI#2, PlayerBotAI/OnPlayerLogin#3, PlayerBotAI/SpawnNewPlayer, PlayerBotAI/UpdateAI, PoolManager/RollOne, quest_stormwind_rendezvous/GetRandomGuardText, quest_stormwind_rendezvous/UpdateAI, RandomMovementGenerator/_setRandomLocation, razorfen_downs/AttackedBy, razorfen_downs/DoSummonRandom, razorfen_downs/UpdateEscortAI, razorfen_kraul/Aggro, ruins_of_ahnqiraj/Reset#11, ruins_of_ahnqiraj/Reset#7, ruins_of_ahnqiraj/Reset#9, ruins_of_ahnqiraj/UpdateAI, ruins_of_ahnqiraj/UpdateAI#5, ruins_of_ahnqiraj/UpdateAI#6, scourge_invasion/GossipSelect_npc_argent_emissary, scourge_invasion/JustDied#3, scourge_invasion/MouthAI, scourge_invasion/PallidHorrorAI, scourge_invasion/SelectRandomFlameshockerSpawnTarget, scourge_invasion/UncommonMinionspawner, scourge_invasion/UpdateAI#2, scourge_invasion/UpdateAI#7, scourge_invasion/UpdateAI#8, scourge_invasion/UpdateAI#9, silithus/OnActivateBySpell, silithus/Reset, silithus/Reset#10, silithus/Reset#2, silithus/UpdateAI, silithus/UpdateAI#2, silithus/UpdateAI#9, silverpine_forest/Aggro, Spell.Effects/EffectDispel, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectSummonGuardian, Spell.Main/SetTargetMap, SpellCaster/MeleeSpellHitResult, spell_item/OnAuraValueCalculate, spell_item/OnCast, spell_item/OnEffectExecute#11, spell_item/OnEffectExecute#12, spell_item/OnEffectExecute#2, spell_item/OnEffectExecute#9, stranglethorn_vale/UpdateAI, stratholme/AI_mobs_rat_pestifere, stratholme/JustDied#3, stratholme/OnPeriodicDummy, stratholme/OnUse, swamp_of_sorrows/Aggro, tanaris/GOHello_go_inconspicuous_landmark, tanaris/Reset, tanaris/UpdateFollowerAI, TargetedMovementGenerator/Update, the_barrens/Aggro, the_barrens/Reset#4, the_barrens/UpdateAI, the_barrens/UpdateAI#2, ThreatListCopier.battleground_alterac/Reset#17, ThreatListCopier.battleground_alterac/Reset#18, ThreatListCopier.battleground_alterac/Reset#19, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#13, ThreatListCopier.battleground_alterac/UpdateAI#14, ThreatListCopier.battleground_alterac/UpdateAI#15, ThreatListCopier.battleground_alterac/UpdateAI#16, ThreatListCopier.battleground_alterac/UpdateAI#6, ThreatListCopier.battleground_alterac/UpdateAI#7, ThreatListCopier.battleground_alterac/UpdateAI#9, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, ThreatListCopier.battleground_alterac/UpdateRenferalAI, ThreatListCopier.battleground_alterac/UpdateThurlogaAI, ThreatListCopier.boss_ragnaros/DoLavaBurst, ThreatListCopier.boss_ragnaros/Reset, ThreatListCopier.boss_ragnaros/UpdateAI, ThreatListCopier.boss_ragnaros/UpdateLavaBurstAI, ubrs_trash/UpdateAI, uldaman/Reset#3, uldaman/UpdateAI#3, ungoro_crater/Aggro, ungoro_crater/Reset#4, ungoro_crater/Reset#5, ungoro_crater/Reset#6, ungoro_crater/UpdateAI#3, ungoro_crater/UpdateAI#4, ungoro_crater/UpdateFollowerAI, Unit.Main/DealDamage, Unit.Main/HandlePetCommand, Unit.Main/IsEffectResist, Unit.Main/RollMagicResistanceMultiplierOutcomeAgainst, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/SelectRandomFriendlyTarget, Unit.Main/SelectRandomUnfriendlyTarget, Unit.SpellAuras/PeriodicDummyTick, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/Update#4, WardenModuleMgr/GetMacModule, WardenModuleMgr/GetWindowsModule, WardenScan/GetBuilder, Weather/ReGenerate, western_plaguelands/UpdateAI, wetlands/Reset, wetlands/Reset#2, wetlands/UpdateCombatAI, wetlands/UpdateEscortAI, winterspring/Reset, winterspring/UpdateAI, WorldObject.Object/FindRandomCreature, WorldObject.Object/FindRandomGameObject, WorldSession.GroupHandler/HandleRandomRollOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.PetHandler/HandlePetCastSpellOpcode, world_event_wareffort/Reset#2, world_event_wareffort/UpdateAI#2, world_event_wareffort/UpdateAI#3, zulfarrak/OnGossipHello_go_shallow_grave, zulgurub_trash/UpdateAI, zulgurub_trash/UpdateAI#2, zulgurub_trash/UpdateAI#4, zulgurub_trash/UpdateAI#5 | — |
| frand | function | — | AiBotAI.Bridge/BridgeHandleMoveTo, BattleBotAI.BattleBotWaypoints/MoveToNextPoint, BattleBotAI.BattleBotWaypoints/MoveToNextPointSpecial, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/UpdateAI, boss_gluth/SummonAdd, boss_herod/SpawnMyrmidons, boss_jeklik/UpdateAI, boss_nefarian/OnEffectExecute, boss_sapphiron/UpdateAI, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/HandlePartyBotCloneCommand, ChatHandler.PlayerBotMgr/HandlePartyBotLoadCommand, ConfusedMovementGenerator/Update, elemental_invasions/DoSpawn, FearMovementGenerator/Update#2, FearMovementGenerator/_getPoint, feralas/UpdateFollowerAI, fireworks_show/UpdateAI, FleeingMovementGenerator/_getPoint, gnomeregan/JustSummoned, instance_blackrock_spire/DoSendNextStadiumWave, instance_dire_maul/Reset#8, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_stratholme/SetData, instance_temple_of_ahnqiraj/TeleportPlayerToCThun, Map.Main/GetWalkRandomPosition, Map.ScriptCommands/ScriptCommand_MoveTo, Map.ScriptCommands/ScriptCommand_SetMovementType, PartyBotAI/UpdateAI, PlayerBotAI/UpdateAI, stranglethorn_vale/UpdateAI#4, TargetedMovementGenerator/DoSpreadIfNeeded, ThreatListCopier.boss_ragnaros/DoLavaBurst, Unit.Main/CalculateDamage, Unit.Main/CalculateMeleeDamage | — |
| roll_chance_f | function | — | LootMgr/Roll, Player.Main/CastItemCombatSpell, Player.Main/UpdateCombatSkills, Unit.AuraProcHandler/HandleAddTargetTriggerAuraProc, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.AuraProcHandler/HandleRemoveByDamageChanceProc, Unit.AuraProcHandler/HandleRemoveFearByDamageChanceProc, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/DealDamage, Unit.Main/DealMeleeDamage, Unit.Main/IsSpellCrit, Unit.Main/RollSpellBlockChanceOutcome, WorldSession.LootHandler/DoLootRelease | — |
| roll_chance_i | function | — | arathi_highlands/Aggro, boss_onyxia/KilledUnit, felwood/Aggro#2, felwood/OnPeriodicDummy, instance_dire_maul/ChangeForm, instance_shadowfang_keep/OnPeriodicDummy, Player.Main/SelectResurrectionSpellId, Spell.Effects/EffectDispel, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Main/Delayed, Spell.Main/DelayedChannel, Spell.Main/HandleAddTargetTriggerAuras, SpellCaster/SpellHitResult, spell_item/OnEffectExecute#13, spell_item/OnEffectExecute#4, spell_item/OnEffectExecute#7, spell_item/OnEffectExecute#8, stratholme/OnPeriodicDummy, Unit.AuraProcHandler/HandleOverrideClassScriptAuraProc, Unit.Main/RollMeleeOutcomeAgainst#2 | — |
| rand32 | function | — | AuthSocket/_HandleLogonChallenge, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsHookScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans, WorldSocket/WorldSocket | — |
| roll_chance_u | function | — | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, PetAI/DoAttack, Spell.Main/prepare#2, Spell.Main/ShouldRemoveStealthAuras, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, world_event_wareffort/KilledUnit | — |
| rand_norm | function | — | instance_naxxramas.boss_kelthuzad/StartEncounter | — |
| rand_norm_f | function | — | Map.Main/GetSwimRandomPosition, Map.Main/GetWalkRandomPosition, razorfen_downs/SpawnerSummon, Spell.Effects/EffectTransmitted, Unit.Main/GetRandomAttackPoint, Unit.SpellAuras/CalculateHeartBeat, Weather/ReGenerate, WorldObject.Object/GetNearRandomPositions, WorldObject.Object/GetRandomPoint | — |
| rand_chance | function | — | PoolManager/RollOne | — |
| rand_chance_f | function | — | ItemEnchantmentMgr/GetItemEnchantMod, LootMgr/Roll#2 | — |
| round_float | function | — | Player.Main/CalculateReputationGain | — |
| randtime | function | Errors/PrintStacktraceAndThrow | boss_heigan/Aggro, boss_heigan/EventTaunt, boss_heigan/UpdateAI | — |
| StrSplit | function | — | ChatHandler.CharacterCommands/HandleServiceDeleteCharacters, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, Corpse/LoadFromDB, Database/Initialize#2, MapPersistentStateMgr/_DelHelper, Pet.Main/LoadPetFromDB, Player.Main/BuildEnumData, Player.Main/_LoadIntoDataField, PlayerTaxi/LoadTaxiDestinationsFromString, PlayerTaxi/LoadTaxiMask, RealmList/UpdateRealm, Unit.Main/LoadPetActionBar, WorldObject.Object/LoadValues | — |
| round_float_chance | function | — | Creature.Main/RegenerateMana | — |
| ApplyModUInt32Var | function | — | — | — |
| GetUInt32ValueFromArray | function | — | Corpse/LoadFromDB, Player.Main/BuildEnumData | — |
| ApplyModFloatVar | function | — | — | — |
| GetFloatValueFromArray | function | — | — | — |
| ApplyPercentModFloatVar | function | — | Player.Main/HandleBaseModValue, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/HandleAttackPowerModifier, Unit.Main/HandleStatModifier, Unit.SpellAuras/HandleModThreat | — |
| stripLineInvisibleChars | function | — | WorldSession.ChatHandler/SanitizeChatMessage | — |
| isBasicLatinCharacter | function | — | — | — |
| isExtendedLatinCharacter | function | — | — | — |
| stripLineInvisibleChars#2 | function | — | — | — |
| isCyrillicCharacter | function | — | — | — |
| isEastAsianCharacter | function | — | — | — |
| secsToTimeString | function | — | AsyncCommandHandlers/HandleResponse, ChatHandler.AccountCommands/HandleBanInfoHelper, ChatHandler.AccountCommands/HandleBanInfoIPCommand, ChatHandler.AccountCommands/SendBanResult, ChatHandler.CreatureCommands/HandleNpcInfoCommand, ChatHandler.CreatureCommands/HandleNpcSpawnInfoCommand, ChatHandler.DebugCommands/HandleDebugGetPrevPlayTimeCommand, ChatHandler.DebugCommands/HandleDebugSetPrevPlayTimeCommand, ChatHandler.HardcodedEvents/HandleWarEffortInfoCommand, ChatHandler.MiscCommands/HandleBGStatusCommand, ChatHandler.MiscCommands/HandleGuildShowLogCommand, ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstanceUnbindHelper, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, ChatHandler.ServerCommands/HandleEventInfoCommand, ChatHandler.ServerCommands/HandleListMapsCommand, ChatHandler.ServerCommands/HandleServerInfoCommand, game_Battlegrounds_BattleGround/~BattleGround, GMTicketMgr/FormatMessageString#2, GMTicketMgr/SetChatLog, World/ShutdownMsg, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode | — |
| isWhiteSpace | function | — | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractLinkArg, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/ExtractUInt32Base, ChatHandler.Chat/SkipWhiteSpaces | — |
| isNumeric#5 | function | — | — | — |
| isNumeric#4 | function | — | ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ChatHandler.MiscCommands/HandleInstanceUnbindCommand | — |
| isNumericOrSpace | function | — | — | — |
| isNumeric#3 | function | — | — | — |
| isNumeric | function | — | ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand | — |
| isNumeric#2 | function | — | — | — |
| isBasicLatinString | function | — | ObjectMgr/isValidString, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| TimeStringToSecs | function | — | ChatHandler.AccountCommands/HandleBanHelper | — |
| isExtendedLatinString | function | — | ObjectMgr/isValidString | — |
| isCyrillicString | function | — | ObjectMgr/isValidString | — |
| isEastAsianString | function | — | ObjectMgr/isValidString | — |
| isLeapYear | function | — | GameEventMgr.Main/LoadFromDB | — |
| TimeToTimestampStr | function | — | ChatHandler.CharacterCommands/HandleCharacterDeletedListHelper, ChatHandler.HardcodedEvents/HandleWarEffortInfoCommand, ChatHandler.HardcodedEvents/HandleWarEffortSetGongTimeCommand, ChatHandler.ServerCommands/HandleEventInfoCommand | — |
| strToUpper | function | — | — | — |
| strToLower | function | — | ChatHandler.LookupCommands/HandleLookupPoolCommand, ChatHandler.LookupCommands/HandleLookupSoundCommand | — |
| IsIPAddress | function | IpAddress/TryParseFromString | ChatHandler.AccountCommands/HandleBanHelper, ChatHandler.AccountCommands/HandleBanInfoIPCommand, ChatHandler.AccountCommands/HandleUnBanHelper | — |
| wcharToUpper | function | — | ObjectMgr/normalizePlayerName | — |
| CreatePIDFile | function | shared_IO_Utils/GetCurrentProcessId | Master/Run, realmd_Main/main | — |
| utf8length | function | — | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | — |
| wcharToUpperOnlyLatin | function | — | — | — |
| wcharToLower | function | — | ObjectMgr/normalizePlayerName | — |
| Utf8toWStr | function | — | AccountMgr/normalizeString, ChannelMgr/GetChannel, ChannelMgr/GetJoinChannel, ChannelMgr/LeftChannel, ChatHandler.CharacterCommands/FindSkillLineEntryFromProfessionName, ChatHandler.CharacterCommands/HandleModifyRepCommand, ChatHandler.LookupCommands/HandleLookupAreaCommand, ChatHandler.LookupCommands/HandleLookupCreatureCommand, ChatHandler.LookupCommands/HandleLookupCreatureModelCommand, ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.LookupCommands/HandleLookupFactionCommand, ChatHandler.LookupCommands/HandleLookupItemCommand, ChatHandler.LookupCommands/HandleLookupItemSetCommand, ChatHandler.LookupCommands/HandleLookupObjectCommand, ChatHandler.LookupCommands/HandleLookupQuestCommand, ChatHandler.LookupCommands/HandleLookupSkillCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.LookupCommands/HandleLookupTaxiNodeCommand, ChatHandler.LookupCommands/HandleLookupTeleCommand, DBCStores/LoadDBCStores, ObjectMgr/AddGameTele, ObjectMgr/CheckPetName, ObjectMgr/CheckPlayerName, ObjectMgr/DeleteGameTele, ObjectMgr/GetGameTele, ObjectMgr/IsReservedName, ObjectMgr/IsValidCharterName, ObjectMgr/LoadGameTele, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/normalizePlayerName, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MiscHandler/HandleWhoOpcode, WorldSession.MiscHandler/operator() | — |
| wstrToUpper | function | — | — | — |
| wstrToLower | function | — | ChannelMgr/GetChannel, ChannelMgr/GetJoinChannel, ChannelMgr/LeftChannel, ChatHandler.CharacterCommands/FindSkillLineEntryFromProfessionName, ChatHandler.CharacterCommands/HandleModifyRepCommand, ChatHandler.LookupCommands/HandleLookupAreaCommand, ChatHandler.LookupCommands/HandleLookupCreatureCommand, ChatHandler.LookupCommands/HandleLookupCreatureModelCommand, ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.LookupCommands/HandleLookupFactionCommand, ChatHandler.LookupCommands/HandleLookupItemCommand, ChatHandler.LookupCommands/HandleLookupItemSetCommand, ChatHandler.LookupCommands/HandleLookupObjectCommand, ChatHandler.LookupCommands/HandleLookupQuestCommand, ChatHandler.LookupCommands/HandleLookupSkillCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.LookupCommands/HandleLookupTaxiNodeCommand, ChatHandler.LookupCommands/HandleLookupTeleCommand, ObjectMgr/AddGameTele, ObjectMgr/DeleteGameTele, ObjectMgr/GetGameTele, ObjectMgr/IsReservedName, ObjectMgr/LoadGameTele, ObjectMgr/LoadReservedPlayersNames, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.MiscHandler/HandleWhoOpcode, WorldSession.MiscHandler/operator() | — |
| WStrToUtf8 | function | — | AccountMgr/normalizeString, ObjectMgr/normalizePlayerName | — |
| BatchifyTimer | function | — | Unit.Main/SetInCombatState | — |
| GetLambda | function | — | — | — |
| utf8ToConsole | function | — | — | — |
| InterpolateValueAtIndex | function | — | ObjectMgr/LoadCreatureClassLevelStats | — |
| consoleToUtf8 | function | — | CliRunnable/operator() | — |
| Utf8FitTo | function | — | AuctionHouseMgr/BuildListAuctionItems, ChatHandler.CharacterCommands/FindSkillLineEntryFromProfessionName, ChatHandler.LookupCommands/HandleLookupAreaCommand, ChatHandler.LookupCommands/HandleLookupCreatureCommand, ChatHandler.LookupCommands/HandleLookupCreatureModelCommand, ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.LookupCommands/HandleLookupFactionCommand, ChatHandler.LookupCommands/HandleLookupItemCommand, ChatHandler.LookupCommands/HandleLookupItemSetCommand, ChatHandler.LookupCommands/HandleLookupObjectCommand, ChatHandler.LookupCommands/HandleLookupQuestCommand, ChatHandler.LookupCommands/HandleLookupSkillCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.LookupCommands/HandleLookupTaxiNodeCommand, WorldSession.MiscHandler/operator() | — |
| utf8printf | function | — | Log.Main/OutConsole | — |
| vutf8printf | function | — | Log.Main/Out, Log.Warden/OutWarden | — |
| hexEncodeByteArray | function | — | AccountMgr/CalculateShaPassHash | — |
| ByteArrayToHexStr | function | — | Log.Warden/RequestChallenge, Log.Warden/SendModuleUse, Log.Warden/Warden | — |
| HexStrToByteArray | function | — | RealmList/LoadAllowedClients | — |
| dither | function | — | Spell.Effects/EffectDummy, Spell.Effects/EffectEnvironmentalDMG, Spell.Effects/EffectPowerDrain, Spell.Effects/EffectScriptEffect, Spell.Main/CheckItems, spell_shaman/OnEffectExecute, spell_shaman/OnPeriodicTrigger, spell_warlock/OnEffectExecute#5, spell_warrior/OnCast, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/CalculateMeleeDamage, Unit.SpellAuras/HandleAuraModIncreaseHealth, Unit.SpellAuras/HandleSchoolAbsorb, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/TriggerSpell | — |
| ditheru | function | — | ChatHandler.UnitCommands/HandleDamageCommand, Spell.Effects/EffectHealthLeech, Spell.Effects/EffectResurrect, Spell.Effects/EffectSelfResurrect, Spell.Main/DoAllEffectOnTarget#3, SpellCaster/CalculateSpellDamage, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.Main/CalculateMeleeDamage, Unit.Main/TriggerDamageShields, Unit.SpellAuras/PeriodicTick | — |
| SetByteValue | function | Log.Main/Out | — | — |
| SetUInt16Value | function | Log.Main/Out | — | — |
| FlagsToString | function | — | ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, ChatHandler.UnitCommands/HandleUnitFactionInfoCommand, ChatHandler.UnitCommands/HandleUnitMoveInfoCommand, ChatHandler.UnitCommands/HandleUnitShowMiscFlagsCommand, ChatHandler.UnitCommands/HandleUnitShowMoveFlagsCommand, ChatHandler.UnitCommands/HandleUnitShowNPCFlagsCommand, ChatHandler.UnitCommands/HandleUnitShowUnitFlagsCommand, ChatHandler.UnitCommands/HandleUnitShowUnitStateCommand, ChatHandler.UnitCommands/HandleUnitShowVisFlagsCommand | — |
| SplitStringByDelimiter | function | — | Master/Run, realmd_Main/main | — |
