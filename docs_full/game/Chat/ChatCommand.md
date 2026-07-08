# ChatCommand

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatCommand (`Chat.h`)

## Purpose & Responsibilities

The `ChatCommand` structure, defined in `Chat.h`, serves as the fundamental metadata descriptor for administrative and debugging commands within the WoWVMaNGOS server. It is not a standalone executable entity but a configuration object used to build a hierarchical tree of available commands.

Its primary responsibilities are:
1.  **Command Identification:** Storing the textual name of a command (e.g., "go", "npc", "help").
2.  **Access Control:** Defining the minimum security level (`SecurityLevel`) required to execute the command and whether it is accessible via the in-game chat interface or the server console (`AllowConsole`).
3.  **Execution Binding:** Holding a function pointer (`Handler`) that points to the specific implementation logic (typically a method within `ChatHandler`) responsible for executing the command.
4.  **Hierarchy Management:** Linking to an array of child `ChatCommand` structures (`ChildCommands`), enabling nested command syntax (e.g., `.go xyz` where `xyz` is a child of `go`).
5.  **Help System Integration:** Storing the help text string displayed when users request assistance for a specific command.
6.  **RBAC Support:** Maintaining a `PermissionMask` for Role-Based Access Control systems, allowing for more granular permission checks beyond simple security levels.

This structure is typically instantiated in static arrays (often in `Chat.cpp` or similar command registration files) to define the entire command tree loaded by the server at startup.

## Member-by-Member Behavior

The `ChatCommand` structure contains only data members and a constructor. There are no behavioral methods other than construction.

### Constructor
**`ChatCommand`**

This constructor initializes all core fields of the command definition.
*   **Parameters:**
    *   `name`: The string identifier for the command.
    *   `securityLevel`: The integer representing the required GM/Admin level.
    *   `allowConsole`: Boolean flag indicating if the command can be run from the server CLI.
    *   `handler`: A pointer to a member function of `ChatHandler` (signature: `bool (ChatHandler::*)(char* args)`). This is the function executed when the command is invoked.
    *   `help`: The description string shown in the help menu.
    *   `childCommands`: A pointer to an array of `ChatCommand` structs representing subcommands. If `nullptr`, the command has no subcommands.
*   **Initialization:**
    *   Assigns the passed arguments to their respective members.
    *   Initializes `PermissionMask` to `0`.
    *   Leaves `Flags` and `FullName` uninitialized in the constructor body (though `FullName` is likely populated later by `ChatHandler::FillFullCommandsName`).

### Data Members

*   **`Name`**: The raw string name of the command. Used for matching user input against the command tree.
*   **`SecurityLevel`**: The minimum access level required. The comment notes that `uint8` is used for alignment purposes related to the function pointer, despite the logical size being small.
*   **`AllowConsole`**: Determines visibility/accessibility from the server's command-line interface.
*   **`Handler`**: The function pointer to the execution logic. Defined as `typedef bool (ChatHandler::*ChatCommandHandler)(char* args)`.
*   **`Help`**: The help text associated with the command.
*   **`ChildCommands`**: Pointer to an array of subcommands. Enables recursive parsing of command strings.
*   **`Flags`**: Reserved for future or specific command flags (defined in `CommandFlags` enum, though currently unused in the struct initialization).
*   **`FullName`**: Likely stores the full path of the command (e.g., "go xyz") for display purposes, generated post-construction.
*   **`PermissionMask`**: Bitmask for RBAC permissions. Initialized to 0.

## Cross-Unit Boundaries

The `ChatCommand` structure itself is passive data. However, its members interact closely with the `ChatHandler` class (defined in the same header, `Chat.h`) and potentially other units during the command parsing and execution lifecycle.

*   **Called By:**
    *   **`ChatHandler` (specifically `ChatHandler::FindCommand` and `ChatHandler::ExecuteCommand`):** The `ChatHandler` traverses the `ChatCommand` tree using `Name` and `ChildCommands`. It checks `SecurityLevel` and `AllowConsole` against the current user's session. Finally, it invokes the `Handler` function pointer, passing the parsed arguments.
    *   **`ChatHandler::FillFullCommandsName`:** This static method iterates through the `ChatCommand` tree to populate the `FullName` member for each node, facilitating better error messages and help displays.
    *   **`ChatHandler::SetPermissionMaskForCommandInTable`:** Updates the `PermissionMask` member based on RBAC configurations.

*   **Calls Out:**
    *   The `ChatCommand` structure does not actively call other units. It is a data container. The *execution* of the command happens via the `Handler` pointer, which resides in `ChatHandler` or its subclasses (`CliHandler`, `NullChatHandler`).

## Data Model

The `ChatCommand` structure does not directly interact with database tables. It is an in-memory representation of the command system. While the *permissions* or *custom commands* might be sourced from a database (e.g., `rbac_permissions` or custom command tables), the `ChatCommand` struct itself holds no SQL queries or table references. The `PermissionMask` and `SecurityLevel` may reflect data loaded from such tables, but the struct is purely a runtime C++ object.

## Notable Implementation Details

1.  **Function Pointer Alignment:** The comment next to `SecurityLevel` states: `// function pointer required correct align (use uint32)`. This suggests that the order of members in the struct was chosen to ensure proper memory alignment for the `Handler` function pointer, which is critical for performance and correctness on certain architectures. `SecurityLevel` is placed immediately before `Handler` in the initialization list, but in the member declaration, `SecurityLevel` is before `Handler`. Wait, looking at the declaration:
    ```cpp
    char const*        Name = nullptr;
    uint8              SecurityLevel = 0;               // function pointer required correct align (use uint32)
    bool               AllowConsole = false;
    ChatCommandHandler Handler = nullptr;
    ```
    The comment implies that `SecurityLevel` (and possibly `AllowConsole`) might be padded or sized to align `Handler`. In practice, `uint8` and `bool` are small, so padding is inserted by the compiler. The comment might be a legacy note or referring to a previous layout. Regardless, the `Handler` is a member function pointer, which can be larger than a standard function pointer depending on the compiler and class inheritance.

2.  **Static Initialization:** `ChatCommand` instances are typically created as static arrays. This means the command tree is fixed at compile time (mostly), with dynamic aspects handled by the `Handler` logic and RBAC masks applied at runtime.

3.  **Null Terminators:** The `ChildCommands` array is usually terminated by a `ChatCommand` with a `nullptr` name or a specific sentinel value, allowing `ChatHandler` to iterate safely. The struct itself doesn't enforce this; it relies on the consumer (`ChatHandler`) to handle the array bounds correctly.

4.  **RBAC Integration:** The presence of `PermissionMask` indicates support for a more flexible permission system than the flat `SecurityLevel`. This allows for fine-grained control (e.g., allowing a user to use `.go` but not `.gm`) without changing the core security level.

## Member Reference

**ChatCommand**
Constructor that initializes the command name, security level, console availability, handler function pointer, help text, and child command array. It sets `PermissionMask` to 0.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatCommand

*Source:* Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChatCommand | ctor | — | — | — |
