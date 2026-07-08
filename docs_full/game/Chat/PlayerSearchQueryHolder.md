# PlayerSearchQueryHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerSearchQueryHolder

**Purpose & Responsibilities**

`PlayerSearchQueryHolder` is a lightweight data aggregation container used during asynchronous player search operations. It inherits from `SqlQueryHolder`, indicating its role in managing the lifecycle of database queries executed off the main thread. Specifically, it holds the context required to correlate results from multiple database lookups—namely, the account ID being searched and the maximum number of results (`limit`) requested by the user.

Its primary responsibility is to store intermediate results (account IDs and names) gathered during the search process, allowing the system to assemble a complete list of matching accounts before displaying them to the Game Master (GM) or administrator who initiated the command. It acts as a bridge between the asynchronous database execution layer and the synchronous command handler that ultimately presents the data to the user.

**Member-by-Member Behavior**

The unit consists of three members: a constructor and two accessor methods.

*   **Construction (`PlayerSearchQueryHolder`)**: The constructor initializes the holder with the specific `accountId` of the requester (likely the GM issuing the search command) and the `limit` (maximum number of results to return). It delegates initialization to the base class `SqlQueryHolder`. This object is instantiated by `ChatHandler.LookupCommands/LookupPlayerSearchCommand` when a player search command is issued.
*   **`GetLimit`**: Returns the maximum number of results allowed for this search operation. This value is consumed by `AsyncCommandHandlers/operator()#2` to enforce result truncation or pagination logic when processing the final output.
*   **`GetAccountId`**: Returns the account ID associated with this search request. This is used by `AsyncCommandHandlers/operator()#2` to identify the context of the search, likely for logging, permission verification, or correlating the final response with the original session.

**Cross-Unit Boundaries**

*   **Called by `ChatHandler.LookupCommands/LookupPlayerSearchCommand`**: The `ChatHandler` unit creates an instance of `PlayerSearchQueryHolder` when parsing a player search command. It passes the necessary parameters (account ID and limit) to the constructor. This establishes the holder as the central state object for that specific command invocation.
*   **Called by `AsyncCommandHandlers/operator()#2`**: After the asynchronous database queries complete, the `AsyncCommandHandlers` unit (specifically the second overload of its `operator()` function) accesses the holder via `GetLimit` and `GetAccountId`. This suggests that `AsyncCommandHandlers` is responsible for interpreting the raw query results stored within or associated with the holder and formatting them for display. The holder provides the metadata needed to correctly interpret those results.

**Data Model**

This unit does not directly interact with database tables. It operates entirely in memory, holding transient state (`m_accountId`, `m_limit`, and `m_accounts`) derived from command arguments and populated by other units (presumably `PlayerSearchHandler` or similar, though not explicitly shown in the map as calling into this unit's methods like `AddAccountInfo`). No SQL queries are embedded in this unit's source code.

**Notable Implementation Details**

*   **Inheritance from `SqlQueryHolder`**: By inheriting from `SqlQueryHolder`, `PlayerSearchQueryHolder` integrates into the server's asynchronous database framework. This implies that the lifetime of this object is tied to the execution of a specific SQL query chain. The base class likely manages the cleanup of resources once the query chain completes.
*   **Thread Safety Considerations**: While the holder itself stores simple integers and a map, it is created in the main thread (by `ChatHandler`) and potentially accessed after asynchronous operations. The design relies on the `SqlQueryHolder` mechanism to ensure that the holder is only accessed safely, typically within the callback context of the completed query. The `AddAccountInfo` and `GetAccountInfo` methods (declared in the header but not detailed in the map's "Calls out/Called by" for this specific unit's perspective, implying they are internal or called by units not listed in the cross-boundary map for this specific partial) manage the `m_accounts` map. Since `m_accounts` is a `std::map`, concurrent access would require synchronization, but the async framework likely serializes access to these callbacks.
*   **Memory Management**: The holder is passed by pointer to `PlayerAccountSearchDisplayTask` (as seen in the header), suggesting that the `SqlQueryHolder` base class manages its own deletion or that the async framework handles the lifecycle. The `PlayerAccountSearchDisplayTask` holds a raw pointer to the holder, relying on the holder's lifetime extending beyond the task's creation.

## Member Reference

**PlayerSearchQueryHolder**
Constructor that initializes the holder with the requesting account ID and the result limit. It delegates to the `SqlQueryHolder` base class. Instantiated by `ChatHandler.LookupCommands/LookupPlayerSearchCommand`.

**GetLimit**
Returns the `m_limit` value, specifying the maximum number of search results to retrieve. Used by `AsyncCommandHandlers/operator()#2` to control output volume.

**GetAccountId**
Returns the `m_accountId` value, identifying the account that initiated the search. Used by `AsyncCommandHandlers/operator()#2` for context identification.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerSearchQueryHolder

*Source:* AsyncCommandHandlers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerSearchQueryHolder | ctor | — | ChatHandler.LookupCommands/LookupPlayerSearchCommand | — |
| GetLimit | method | — | AsyncCommandHandlers/operator()#2 | — |
| GetAccountId | method | — | AsyncCommandHandlers/operator()#2 | — |
