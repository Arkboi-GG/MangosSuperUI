<!-- provenance: verbose -->
# WardenModuleMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WardenModuleMgr

## Purpose & Responsibilities

`WardenModuleMgr` is a singleton responsible for loading and managing Warden anti-cheat modules from the filesystem. It scans a configured directory for `.bin` files, pairs them with corresponding `.key` and `.cr` files, and categorizes them into Windows or macOS vectors based on their content. At runtime, it provides random access to these modules for client verification. The unit is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_5_1`.

## Member-by-Member Behavior

### Initialization

**`WardenModuleMgr()`**
The constructor performs module discovery and loading:
1.  Retrieves the module directory via `World::GetWardenModuleDirectory`.
2.  Calls `GetModuleNames` to obtain a list of `.bin` files.
3.  Iterates through each file, deriving paths for associated `.key` and `.cr` files by replacing the `.bin` extension.
4.  Attempts to construct a `WardenModule`. On success, it uses `WardenModule::Windows()` to place the module in `m_winModules` or `m_macModules`.
5.  On `std::runtime_error`, it logs the failure via `Log::Out` and continues.
6.  Validates that modules exist for any enabled OS (via `World::getConfig`). If modules are missing for an enabled platform, it logs an error and calls `Log::WaitBeforeContinueIfNeed` to potentially pause startup.

### Module Retrieval

**`GetWindowsModule()`**
Returns a pointer to a randomly selected `WardenModule` from `m_winModules` using `shared_Util::urand`. Returns `nullptr` if empty. Called by `WardenWin::WardenWin`.

**`GetMacModule()`**
Returns a pointer to a randomly selected `WardenModule` from `m_macModules` using `shared_Util::urand`. Returns `nullptr` if empty. Called by `WardenMac::WardenMac`.

### Helper

**`GetModuleNames()`**
Free function that scans a directory for valid module binaries. It calls `FileSystem::GetAllFilesInFolder` to retrieve all files, then filters the result to keep only those ending in `.bin`.

## Cross-Unit Boundaries

*   **`FileSystem/GetAllFilesInFolder`**: Called by `GetModuleNames` for directory scanning.
*   **`WardenModule/WardenModule#2` & `WardenModule/Windows`**: Called by the constructor to instantiate modules and determine OS target.
*   **`World/getConfig` & `World/GetWardenModuleDirectory`**: Called by the constructor for configuration and path retrieval.
*   **`Log.Main/Out` & `Log.Main/WaitBeforeContinueIfNeed`**: Called by the constructor for error logging and startup gating.
*   **`shared_Util/urand`**: Called by `GetWindowsModule` and `GetMacModule` for random selection.
*   **`WardenWin/WardenWin` & `WardenMac/WardenMac`**: Call `GetWindowsModule` and `GetMacModule` respectively.

## Data Model

This unit does not interact with any database tables. It operates entirely on filesystem resources (`.bin`, `.key`, `.cr`) and in-memory configuration.

## Notable Implementation Details

*   **Singleton**: Instantiated via `INSTANTIATE_SINGLETON_1`, accessed via `sWardenModuleMgr`.
*   **Conditional Compilation**: Wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`.
*   **Naming Convention**: Strictly assumes `module.bin` requires `module.key` and `module.cr`.
*   **Error Handling**: Individual load failures are non-fatal; missing modules for an enabled OS trigger a startup warning/pause.
*   **Randomization**: Uses `urand` to prevent predictable check sequences.

## Member Reference

**GetModuleNames**
Free function that scans a directory for `.bin` files. It calls `FileSystem::GetAllFilesInFolder` to get all files, then filters out any file not ending in `.bin` using `std::remove_if`. Returns a `std::vector<std::string>` of full paths.

**WardenModuleMgr**
Constructor that initializes the singleton. It retrieves the module directory from `World::GetWardenModuleDirectory`, calls `GetModuleNames` to find `.bin` files, and iterates through them. For each file, it attempts to construct a `WardenModule` using derived `.key` and `.cr` paths. Successful modules are sorted into `m_winModules` or `m_macModules` based on `WardenModule::Windows()`. Failures are logged via `Log::Out`. If modules are missing for an enabled OS, it logs an error and calls `Log::WaitBeforeContinueIfNeed`.

**GetWindowsModule**
Method that returns a pointer to a randomly selected `WardenModule` from `m_winModules`. Returns `nullptr` if the vector is empty. Uses `shared_Util::urand` for selection. Called by `WardenWin::WardenWin`.

**GetMacModule**
Method that returns a pointer to a randomly selected `WardenModule` from `m_macModules`. Returns `nullptr` if the vector is empty. Uses `shared_Util::urand` for selection. Called by `WardenMac::WardenMac`.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenModuleMgr

*Source:* WardenModuleMgr.cpp, WardenModuleMgr.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetModuleNames | function | FileSystem/GetAllFilesInFolder | — | — |
| WardenModuleMgr | ctor | Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, WardenModule/WardenModule#2, WardenModule/Windows, World/getConfig, World/GetWardenModuleDirectory | — | — |
| GetWindowsModule | method | shared_Util/urand | WardenWin/WardenWin | — |
| GetMacModule | method | shared_Util/urand | WardenMac/WardenMac | — |
