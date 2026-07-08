# SystemConfig

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SystemConfig

`SystemConfig.h` is a header-only unit providing compile-time constants, platform detection macros, and default configuration values for the `wowvmangos` server binaries. It contains no classes, functions, or variables; its entire behavior is resolved by the C preprocessor.

The unit serves three static roles:
1.  **Platform Identification:** Detects OS, CPU architecture, and endianness via compiler macros, exposing them as string literals (`_ENDIAN_PLATFORM`, `ARCHITECTURE`) for logging.
2.  **Configuration Paths:** Defines the default filenames for the world server (`mangosd.conf`) and realm server (`realmd.conf`) configuration files.
3.  **Operational Defaults:** Establishes fallback values for network ports and player limits.

## Member-by-Member Behavior

This unit has no executable members. Its definitions are grouped logically below.

### Versioning
*   `_MANGOSDCONFVERSION` (`2025040601`) and `_REALMDCONFVERSION` (`2024091701`) track the expected configuration file schema versions using a `YYYYMMDDRR` format. They are guarded by `#ifndef` to allow override via compiler flags.

### Platform Detection
*   **Endianness:** `_ENDIAN_STRING` resolves to `"big-endian"` or `"little-endian"` based on `MANGOS_ENDIAN` (from `Platform/Define.h`).
*   **Architecture:** `ARCHITECTURE` resolves to a string like `"x64"`, `"AArch64"`, or `"ARM32"` based on standard compiler macros. If unrecognized, it defaults to `"x32"`.
*   **Platform String:** `_ENDIAN_PLATFORM` concatenates the OS name, architecture, and endianness (e.g., `"Linux_x64 (little-endian)"`).
*   **Full Version:** `_FULLVERSION` combines `REVISION_HASH` and `REVISION_DATE` (from `revision.h`) with `_ENDIAN_PLATFORM`.

### Configuration Paths
*   `_MANGOSD_CONFIG` and `_REALMD_CONFIG` resolve to `SYSCONFDIR "mangosd.conf"` and `SYSCONFDIR "realmd.conf"`, respectively. `SYSCONFDIR` defaults to `""` if undefined, placing configs in the current working directory.

### Operational Defaults
*   `DEFAULT_PLAYER_LIMIT`: `100`.
*   `DEFAULT_WORLDSERVER_PORT`: `8085`.
*   `DEFAULT_REALMSERVER_PORT`: `3724`.

## Cross-Unit Boundaries

As a header-only file, `SystemConfig.h` does not call other units. It depends on:
1.  **`Platform/Define.h`**: Provides `MANGOS_ENDIAN`, `MANGOS_BIGENDIAN`, and `PLATFORM`.
2.  **`revision.h`**: Provides `REVISION_HASH` and `REVISION_DATE`.

It is included by core server units (e.g., `mangosd` and `realmd` entry points) to access configuration paths, default ports, and version strings.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Fallback Architecture:** If no standard architecture macro is defined, `ARCHITECTURE` defaults to `"x32"`, which may misidentify exotic platforms.
2.  **Config Path Flexibility:** `SYSCONFDIR` allows deployment flexibility (e.g., `/etc/mangos/` in production vs. current directory in dev).
3.  **Port Ambiguity:** The default world server port is `8085`, with a comment noting `8129` as an alternative. Engineers must ensure the config file overrides this if needed.

## Member Reference

This unit contains no executable members listed in the MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — SystemConfig

*Source:* SystemConfig.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
