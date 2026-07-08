# QuestShareInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestShareInfo

`QuestShareInfo` is a lightweight aggregate struct defined in `Player.h` within the `wowvmangos` codebase. It serves as a data carrier for tracking the context of a quest-sharing event between players. Specifically, it holds the identifier of the player initiating or involved in the share (`PlayerGuid`) and the specific quest being shared (`QuestId`).

This struct is not a standalone entity with behavior; it contains no methods other than its constructor. Its sole purpose is to encapsulate these two pieces of information so they can be stored as a single unit within the `Player` class, specifically inside the `nonstd::optional<QuestShareInfo>` member `m_questShareInfo`. This allows the game server to temporarily track whether a player is currently in the process of sharing a quest or has pending share-related state, without requiring a separate database table or complex object hierarchy.

## Member Reference

**QuestShareInfo**
The constructor for the `QuestShareInfo` struct. It takes two arguments: an `ObjectGuid` representing a player's unique identifier and a `uint32` representing a quest ID. It initializes the struct's two public members, `PlayerGuid` and `QuestId`, with these values. This constructor is marked `explicit` to prevent implicit conversions. It is called by `Player.SetQuestShareInfo` when a quest share operation is initiated.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestShareInfo

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestShareInfo | ctor | — | — | — |
