# Vanilla 1.12 caster bot talent and combat research

Research date: 2026-08-25

Target: World of Warcraft 1.12.1, client build 5875, level 60, 51 talent points.

Classes covered: Mage, Warlock, and Priest, all three talent trees.

This is a design artifact. It does not change the core, bot AI, database, or SuperUI.

## What “canonical” means here

There is no single uncontested Vanilla leveling build for every nominal specialization. Raid builds, PvP builds, speed-leveling builds, Hardcore builds, and dungeon-spam builds make different choices. In this document, canonical means the one baseline profile recommended for an autonomous bot that must:

- level without buying a talent reset;
- survive ordinary open-world pulls;
- contribute sensibly in a five-player group;
- work with imperfect threat, crowd-control, pet, and proc reasoning;
- retain the named tree’s defining level-60 identity; and
- spend exactly one point at every level from 10 through 60.

These are bot baselines, not claims that every point is mathematically best for a raid-geared human player. Each section calls out the important community disagreement.

## Live DBC validation

The tree layouts were validated read-only against the running server’s extracted client data:

- Talent.dbc: /home/wowvmangos/vmangos/run/data/5875/dbc/Talent.dbc
- TalentTab.dbc: /home/wowvmangos/vmangos/run/data/5875/dbc/TalentTab.dbc
- Spell.dbc: /home/wowvmangos/vmangos/run/data/5875/dbc/Spell.dbc

Talent.dbc has the build-5875 WDBC header, 432 records, 21 fields, and 84-byte records. The validation used TalentTab, row, column, the five RankID fields, DependsOn TalentID, and prerequisite-rank fields. Talent and tab names were resolved through the live Spell.dbc and TalentTab.dbc string blocks. A simulator then bought every point below in level order and rejected an entry if its tier, maximum rank, or prerequisite was not yet legal.

All nine final sequences pass that live simulation, cover every level 10–60 exactly once, and total 51 points. The live DBC also resolves an external-data discrepancy: Shatter has five nonzero rank spell IDs, 11170, 12982, 12983, 12984, and 12985. It is therefore 5/5 in build 5875. One secondary calculator data file incorrectly declares maxRank 1 while carrying five rank descriptions; that declaration must not be copied into code.

Primary structural references:

- [VMaNGOS build-5875 extraction guidance](https://github.com/vmangos/wiki/blob/master/docs/Getting-it-working-Linux.md)
- [VMaNGOS DBC structures](https://github.com/vmangos/core/blob/development/src/game/Database/DBCStructure.h)
- [CMaNGOS Classic DBC structures](https://github.com/cmangos/mangos-classic/blob/master/src/game/Server/DBCStructure.h)
- [CMaNGOS Talent.dbc loading and talent-rank map](https://github.com/cmangos/mangos-classic/blob/master/src/game/Server/DBCStores.cpp)

Secondary full-tree cross-checks:

- [Mage talent tree data](https://github.com/maladr0it/classic-talent-calculator/blob/master/src/trees/Mage/data.ts)
- [Warlock talent tree data](https://github.com/maladr0it/classic-talent-calculator/blob/master/src/trees/Warlock/data.ts)
- [Priest talent tree data](https://github.com/maladr0it/classic-talent-calculator/blob/master/src/trees/Priest/data.ts)

## Shared AI conventions

- Availability always wins. If the bot has not trained a named spell or rank yet, skip it and continue down the priority. Never stall waiting for an unavailable spell.
- Use the highest trained damage rank by default. Permit explicitly configured rank 1 utility casts where the effect is what matters, such as Frostbolt for a cheap slow, Frost Nova, or a cheap emergency spell.
- Estimate target time-to-live before applying damage over time. Do not apply a long DoT to a target likely to die before it produces useful ticks.
- Never damage a crowd-controlled target unless the group has explicitly selected it as the kill target.
- Give a player or designated bot tank time to establish threat. Stop or downgrade burst if the caster is about to pull threat.
- Channels are action-locked. Do not restart Arcane Missiles, Blizzard, Drain Life, Drain Soul, Hellfire, Rain of Fire, Mind Flay, or Evocation every AI tick.
- A wand is a repeating ranged attack, not an instant spell. Start it once, let a shot complete, and stop it before beginning a cast. Wand clipping destroys much of its leveling value.
- Out of combat, drink before the next pull when mana is below the configured pull threshold. Do not begin a normal pull below roughly 45% mana unless the group leader is already engaged.
- AoE requires a safety gate: at least three valid non-CC targets, no fragile neutral pack in the area, tank control or a deliberate kite plan, and enough mana to complete the chosen sequence.
- Interrupt and dispel policy should use a dangerous-spell allowlist. Do not waste a long cooldown on harmless filler.
- The numeric health, mana, and time-to-live thresholds below are starting values for tuning, not facts encoded by Vanilla.

## Mage

### Mage spell-ID reference

The following are player spell IDs from the live build-5875 Spell.dbc. Base damage entries are rank 1 unless noted.

- Core: Fireball 133, Frostbolt 116, Arcane Missiles 5143, Arcane Explosion 1449, Fire Blast 2136, Scorch 2948, Flamestrike 2120, Blizzard 10, and Cone of Cold 120.
- Control and survival: Frost Nova 122, Polymorph 118, Counterspell 2139, Blink 1953, Mana Shield 1463, Evocation 12051, and Remove Lesser Curse 475.
- Buffs: Arcane Intellect 1459, Frost Armor 168, Ice Armor 7302, and Mage Armor 6117.
- Talent spells: Presence of Mind 12043, Arcane Power 12042, Pyroblast rank 1 11366, Blast Wave rank 1 11113, Combustion 11129, Cold Snap 12472, Ice Block 11958, and Ice Barrier rank 1 11426.

The DBC contains NPC spells with some identical names. The IDs above are the player versions; code should still resolve the player’s trained spell chain rather than selecting the lowest same-name Spell.dbc row.

### Arcane Mage — 31 Arcane / 0 Fire / 20 Frost

Role and equipment assumptions:

- Ranged damage and utility; reliable secondary interrupter and Polymorph controller.
- Solo damage transitions from Arcane Missiles and wand finishing into an Arcane Power plus Frostbolt hybrid at higher level.
- Equip the best-stat staff or dagger/sword plus off-hand available, favoring Intellect, Spirit, spell damage, and Stamina. Keep the highest-DPS usable wand equipped; Wand Specialization is deliberately taken for the long early leveling period.

Level-60 allocation:

- Arcane, 31: Improved Arcane Missiles 5/5; Wand Specialization 2/2; Arcane Concentration 5/5; Improved Arcane Explosion 3/3; Arcane Resilience 1/1; Improved Counterspell 2/2; Arcane Meditation 3/3; Presence of Mind 1/1; Arcane Mind 5/5; Arcane Instability 3/3; Arcane Power 1/1.
- Fire, 0.
- Frost, 20: Improved Frostbolt 5/5; Elemental Precision 3/3; Ice Shards 5/5; Piercing Ice 3/3; Cold Snap 1/1; Frost Channeling 3/3.
- Total: 31 + 0 + 20 = 51.

Exact purchase order:

- Levels 10–14: Improved Arcane Missiles ranks 1–5.
- Levels 15–16: Wand Specialization ranks 1–2.
- Levels 17–21: Arcane Concentration ranks 1–5.
- Levels 22–24: Improved Arcane Explosion ranks 1–3.
- Level 25: Arcane Resilience rank 1.
- Levels 26–27: Improved Counterspell ranks 1–2.
- Levels 28–29: Arcane Meditation ranks 1–2.
- Level 30: Presence of Mind rank 1.
- Levels 31–35: Arcane Mind ranks 1–5.
- Levels 36–38: Arcane Instability ranks 1–3.
- Level 39: Arcane Meditation rank 3.
- Level 40: Arcane Power rank 1.
- Levels 41–45: Improved Frostbolt ranks 1–5.
- Levels 46–48: Elemental Precision ranks 1–3.
- Levels 49–53: Ice Shards ranks 1–5.
- Levels 54–56: Piercing Ice ranks 1–3.
- Level 57: Cold Snap rank 1.
- Levels 58–60: Frost Channeling ranks 1–3.

Combat policy:

- Preparation: maintain Arcane Intellect. Use Mage Armor when mana regeneration matters and Ice or Frost Armor when melee contact is likely. Conjure food and water out of combat.
- Solo priority: Polymorph an unsafe second target; open with the best recently trained Fireball or Frostbolt; use Arcane Missiles when Clearcasting is active, when the target is already in melee and 5/5 Improved Arcane Missiles prevents pushback, or when it is currently the best trained nuke; use Fire Blast only while moving or to finish; switch to the wand when the enemy is safely controlled and either mana is below about 35% or one to two wand shots should kill.
- Level 41 and later: Frostbolt becomes the normal efficient filler because all 20 off-tree points improve it. Arcane Missiles remains the Clearcasting and pushback-resistant channel.
- Burst: use Arcane Power only above roughly 55% mana and only when the target will live long enough for several casts. Pair it with Presence of Mind for an instant highest-rank Fireball, Frostbolt, or emergency Polymorph. Do not use Arcane Power when already close to the tank’s threat.
- Proc handling: consume Clearcasting on the most expensive safe action that will complete. Prefer full Arcane Missiles on one target or Blizzard/Arcane Explosion on a valid pack; never consume it on a target that will die before the cast or channel completes.
- Interrupts and control: Counterspell dangerous heals, summons, crowd control, and high-damage casts. With 2/2 Improved Counterspell it also silences the school-independent follow-up window, so it is valuable early rather than only at the last cast millisecond. Refresh Polymorph before it expires only when the group still needs the target controlled.
- AoE: with a stable tank, use Flamestrike from range and then Arcane Explosion only while the pack remains controlled. Without a tank, Frost Nova then move or Blink away; do not treat Improved Arcane Explosion as permission for autonomous mass pulling.
- Defensive order: Frost Nova and create distance; Blink out of a root or melee collapse; Polymorph a spare humanoid/beast; Mana Shield only when health risk outweighs its severe mana cost; Cold Snap only to restore an urgently needed Frost Nova.
- Party utility: buff Arcane Intellect, provide water, remove curses, own an assigned Polymorph target, and interrupt from range.
- Low-level fallback: before Arcane Missiles is trained, use Fireball/Frostbolt and wand. Before Counterspell, use Polymorph or line of sight. Before Evocation, conserve with the wand and drink.

Known Vanilla limitations:

- Arcane has no Arcane Blast in 1.12. Its capstone amplifies other schools, so the end-state legitimately plays as Arcane/Frost rather than a modern pure-Arcane rotation.
- Arcane Power sharply raises mana cost and threat. A raid-style burn policy is unsafe for an autonomous dungeon bot.
- Arcane Missiles can waste most of a channel when the target dies or moves out of range.
- The build has no Ice Block or Ice Barrier; Cold Snap is primarily a Frost Nova reset.

Sources and disagreement:

- [Icy Veins Arcane Mage leveling](https://www.icy-veins.com/wow-classic/arcane-mage-leveling-talent-build-from-1-to-60)
- [Warcraft Tavern Arcane Mage talents and builds](https://www.warcrafttavern.com/wow-classic/guides/pve-arcane-mage-talents-builds/)
- [Icy Veins general Mage leveling](https://www.icy-veins.com/wow-classic/classic-mage-leveling-guide)

The sources agree that deep Arcane is versatile but realizes much of its power late and commonly finishes with Frostbolt talents. Human guides often spend the two utility points on Arcane Focus or threat reduction. This bot baseline keeps Wand Specialization for leveling and 2/2 Improved Counterspell for five-player utility. A raid-only 31/0/20 allocation may move those points because another player handles interrupts and wanding is no longer important.

### Fire Mage — 17 Arcane / 31 Fire / 3 Frost

Role and equipment assumptions:

- Highest single-target leveling damage of the three Mage profiles, with burst AoE and less safety than Frost.
- Ranged damage in groups; not the preferred autonomous mass-pull controller.
- Use a spell-stat staff or one-hand plus off-hand and keep a good wand for low-mana finishing. Fire damage and general spell damage become more valuable as gear appears.

Level-60 allocation:

- Arcane, 17: Arcane Subtlety 2/2; Arcane Focus 5/5; Arcane Concentration 5/5; Improved Arcane Explosion 3/3; Arcane Resilience 1/1; Arcane Meditation 1/3.
- Fire, 31: Improved Fireball 5/5; Impact 3/5; Ignite 5/5; Flame Throwing 2/2; Pyroblast 1/1; Burning Soul 2/2; Master of Elements 3/3; Critical Mass 3/3; Blast Wave 1/1; Fire Power 5/5; Combustion 1/1.
- Frost, 3: Elemental Precision 3/3.
- Total: 17 + 31 + 3 = 51.

Exact purchase order:

- Levels 10–12: Elemental Precision ranks 1–3.
- Levels 13–17: Improved Fireball ranks 1–5.
- Levels 18–20: Impact ranks 1–3.
- Levels 21–22: Ignite ranks 1–2.
- Level 23: Pyroblast rank 1.
- Levels 24–25: Burning Soul ranks 1–2.
- Levels 26–28: Ignite ranks 3–5.
- Levels 29–31: Master of Elements ranks 1–3.
- Level 32: Flame Throwing rank 1.
- Levels 33–35: Critical Mass ranks 1–3.
- Level 36: Blast Wave rank 1.
- Level 37: Flame Throwing rank 2.
- Levels 38–42: Fire Power ranks 1–5.
- Level 43: Combustion rank 1.
- Levels 44–45: Arcane Subtlety ranks 1–2.
- Levels 46–50: Arcane Focus ranks 1–5.
- Levels 51–55: Arcane Concentration ranks 1–5.
- Levels 56–58: Improved Arcane Explosion ranks 1–3.
- Level 59: Arcane Resilience rank 1.
- Level 60: Arcane Meditation rank 1.

Combat policy:

- Preparation: maintain Arcane Intellect. Prefer Mage Armor for sustained casting and Ice Armor when solo mobs regularly reach melee.
- Solo priority: Pyroblast from maximum range only on a fresh, unengaged target; Fireball as the main filler; Scorch when movement or remaining target life is too short for Fireball; Fire Blast to finish or while moving; wand when the next full cast would be wasteful or mana falls below about 30%.
- Group priority: let the tank establish threat, then Fireball. Pyroblast is a pre-pull spell only when the tank or leader has authorized it. Ignite continues generating damage and threat after a critical hit, so stop earlier than the threat meter’s apparent edge.
- Burst and procs: use Combustion on an elite or boss expected to survive multiple Fireballs. Master of Elements refunds mana after critical hits; the AI should accept the refund, not cast an inefficient spell merely to fish for a crit. Consume Clearcasting on Fireball, Flamestrike, or a safe AoE action.
- Fire resistance fallback: switch to Frostbolt or Arcane Missiles after repeated fire resists or on a known fire-immune target. Do not loop forever on an immune school.
- Interrupts and control: Counterspell dangerous casts; Polymorph assigned humanoids or beasts. Impact stuns are opportunistic and must never be assumed by the planner.
- AoE: Flamestrike from range, then Blast Wave when at least three enemies are in range and no CC will break. Continue with Arcane Explosion only behind stable tank control. Frost Nova and Blink are exits, not routine damage buttons.
- Defensive order: Frost Nova and move; Blast Wave to slow a collapsed pack; Blink; Polymorph a spare target; Mana Shield only for imminent damage. Fire has no Ice Barrier or Ice Block.
- Party utility: Arcane Intellect, water, curse removal, Polymorph, and Counterspell remain higher priority than a small personal DPS gain.
- Low-level fallback: before Pyroblast use Fireball. Before Scorch or Fire Blast, finish with wand. Before Blast Wave, never enter melee just to AoE.

Known Vanilla limitations:

- Fire performs poorly against fire-immune and high-fire-resistance enemies, which are common in some Vanilla endgame areas.
- Ignite is a debuff and adds delayed threat. The 1.12 debuff limit and shared Ignite behavior can make raid results differ from solo testing.
- Fire lacks Frost’s deterministic barrier and reset package. Random Impact is not a survivability guarantee.
- Combustion is not a modern fixed-duration cooldown; model its charge/critical behavior from the actual aura, not assumptions from later expansions.

Sources and disagreement:

- [Icy Veins Fire Mage leveling](https://www.icy-veins.com/wow-classic/fire-mage-leveling-talent-build-from-1-to-60)
- [Warcraft Tavern Mage leveling](https://www.warcrafttavern.com/wow-classic/guides/mage-leveling-guide/)
- [Icy Veins general Mage leveling](https://www.icy-veins.com/wow-classic/classic-mage-leveling-guide)

The sources agree that Fire offers very high single-target speed and weaker pack control than Frost. Some human paths maximize Impact and Incinerate earlier; raid paths commonly emphasize Improved Scorch and threat reduction. This baseline instead preserves Master of Elements, Blast Wave, and the 17-point Arcane sustain package because the target is mixed solo and five-player automation, not a fire-raid debuff assignment.

### Frost Mage — 18 Arcane / 0 Fire / 33 Frost

Role and equipment assumptions:

- Safest Mage leveler, ranged control damage, emergency peel, and reliable group AoE.
- This is controlled single-target Frost with useful AoE, not an autonomous AoE-grinding profile.
- Use spell-stat weapons and a strong wand. Stamina is somewhat more valuable for this bot because Ice Barrier scales its ability to survive imperfect positioning.

Level-60 allocation:

- Arcane, 18: Arcane Subtlety 2/2; Arcane Focus 4/5; Arcane Concentration 5/5; Improved Arcane Explosion 3/3; Arcane Resilience 1/1; Improved Counterspell 2/2; Arcane Meditation 1/3.
- Fire, 0.
- Frost, 33: Improved Frostbolt 5/5; Elemental Precision 3/3; Ice Shards 5/5; Frostbite 3/3; Improved Frost Nova 2/2; Piercing Ice 3/3; Cold Snap 1/1; Arctic Reach 1/2; Frost Channeling 3/3; Shatter 5/5; Ice Block 1/1; Ice Barrier 1/1.
- Total: 18 + 0 + 33 = 51.

Exact purchase order:

- Levels 10–14: Improved Frostbolt ranks 1–5.
- Levels 15–17: Frostbite ranks 1–3.
- Levels 18–19: Improved Frost Nova ranks 1–2.
- Levels 20–24: Ice Shards ranks 1–5.
- Levels 25–29: Shatter ranks 1–5.
- Level 30: Ice Block rank 1.
- Levels 31–33: Piercing Ice ranks 1–3.
- Level 34: Cold Snap rank 1.
- Levels 35–37: Frost Channeling ranks 1–3.
- Levels 38–39: Elemental Precision ranks 1–2.
- Level 40: Ice Barrier rank 1.
- Level 41: Elemental Precision rank 3.
- Level 42: Arctic Reach rank 1.
- Levels 43–44: Arcane Subtlety ranks 1–2.
- Levels 45–48: Arcane Focus ranks 1–4.
- Levels 49–53: Arcane Concentration ranks 1–5.
- Levels 54–56: Improved Arcane Explosion ranks 1–3.
- Level 57: Arcane Resilience rank 1.
- Levels 58–59: Improved Counterspell ranks 1–2.
- Level 60: Arcane Meditation rank 1.

Combat policy:

- Preparation: maintain Arcane Intellect and Ice Barrier. Use Ice Armor for solo control and Mage Armor when a tank is reliably holding every target.
- Solo priority: Frostbolt from maximum range; continue Frostbolt while the slow gives safe cast time; when the target becomes frozen, prefer a spell that can land before the freeze breaks and benefits from Shatter; Cone of Cold or Fire Blast to finish while moving; wand only when the target is controlled and mana conservation matters.
- Shatter handling: Frost Nova at close range, immediately create distance, then cast the highest-value spell that will land during the freeze. Do not stand in melee trying to force a combo. Frostbite is random; react to its aura rather than predicting it.
- Group priority: Frostbolt the kill target. Keep assigned Polymorph active. Counterspell high-value casts. Use Ice Barrier before expected damage rather than after pushback has already ruined a cast.
- Clearcasting: after Arcane Concentration is learned, spend it on a full Blizzard against a valid pack or on a highest-rank Frostbolt. Do not clip Blizzard because a proc appeared mid-channel.
- AoE: with stable tank control, use Blizzard from maximum range and Cone of Cold only when positioning is safe. Frost Nova is primarily an escape or planned Shatter tool. Arcane Explosion is a finisher after the pack is low, not an opener.
- Defensive order: Ice Barrier; Frost Nova and move; Blink; Ice Block for lethal incoming damage or to wait for the tank to recover threat; Cold Snap only when restoring Barrier, Nova, or Ice Block materially prevents death. Cancel Ice Block once the rescue condition is satisfied.
- Immunity fallback: use Fireball or Arcane Missiles against frost-immune targets.
- Party utility: Polymorph, Counterspell, Arcane Intellect, water, Remove Lesser Curse, and peeling a loose melee enemy from the healer.
- Low-level fallback: before Ice Barrier, behave as a ranged kiter. Before Shatter, Frost Nova is escape control. Before Blizzard, never attempt an AoE pull.

Known Vanilla limitations:

- Frostbite can split a moving pack, which is bad for classic AoE kiting. This profile is for ordinary autonomous combat, not mass grinding.
- Ice Block makes the bot unable to act and usually transfers attention to another party member. It needs an exit condition, not a fixed full-duration wait.
- Ice Barrier rank upgrades are trainer spells after the talent unlock. Always resolve the highest trained rank.
- A secondary calculator’s Shatter max-rank declaration is wrong; live build 5875 has five ranks and requires 2/2 Improved Frost Nova.

Sources and disagreement:

- [Icy Veins single-target Frost Mage leveling](https://www.icy-veins.com/wow-classic/single-target-frost-mage-leveling-talent-build-from-1-to-60)
- [Icy Veins Frost AoE Mage leveling](https://www.icy-veins.com/wow-classic/aoe-grinding-frost-mage-leveling-talent-build-from-1-to-60)
- [Warcraft Tavern Frost Mage PvE](https://www.warcrafttavern.com/wow-classic/guides/frost-mage-pve-dps/)

Single-target guides favor Frostbite, Shatter, Ice Block, and Ice Barrier. Dedicated AoE-grinding guides favor Improved Blizzard and Permafrost and often avoid Frostbite because random roots scatter packs. The bot baseline deliberately follows the safer single-target branch and gates baseline Blizzard behind tank control. Winter’s Chill is omitted because its largest value is coordinated long-target group or raid damage.

## Warlock

### Warlock spell-ID reference

The following player IDs were checked against the live build-5875 Spell.dbc:

- Core damage and sustain: Shadow Bolt 686, Immolate 348, Corruption 172, Curse of Agony 980, Curse of Weakness 702, Curse of Tongues 1714, Life Tap 1454, Drain Life 689, and Drain Soul 1120.
- Control and survival: Fear 5782, Howl of Terror 5484, Death Coil rank 1 6789, Health Funnel rank 1 755, Rain of Fire rank 1 5740, and Hellfire rank 1 1949.
- Talent spells: Amplify Curse 18288, Siphon Life rank 1 18265, Curse of Exhaustion 18223, Dark Pact rank 1 18220, Fel Domination 18708, Demonic Sacrifice 18788, Soul Link 19028, Shadowburn rank 1 17877, and Conflagrate rank 1 17962.
- Pet abilities: Felhunter Spell Lock rank 1 19244, Felhunter Devour Magic rank 1 19505, Succubus Seduction 6358, and Voidwalker Sacrifice rank 1 7812.

Create-item and pet spells have multiple same-name NPC and player rows. For Healthstone, Soulstone, Firestone, and Spellstone creation, resolve the trained player spell chain rather than hard-coding the lowest same-name ID.

Shared pet and resource rules:

- Maintain Demon Armor or the best appropriate self armor. Carry a Healthstone and reserve at least one Soul Shard for a Soulstone or emergency summon when bag space permits.
- Send the pet before applying high threat. Wait for the Voidwalker’s first threat action when it is assigned to tank.
- Default solo pet choice is conditional: Voidwalker when the pet can actually hold threat; Succubus for faster drain-tanking against ordinary melee; Felhunter against dangerous casters; Imp in a group for ranged uptime and Blood Pact.
- Never Fear into unknown space or through a dungeon pack. Fear and Howl of Terror require a no-add safety check.
- Curse selection is exclusive. Solo default is Curse of Agony on a long-lived target. Use Curse of Tongues on a dangerous caster when Spell Lock is insufficient, Curse of Weakness on a hard-hitting physical elite when survival matters, and Curse of Exhaustion only when the kite/runner policy needs it.
- Corruption is worthwhile when expected target life is at least about 9 seconds. Immolate needs roughly 8 seconds. Curse of Agony needs a substantially longer target, roughly 18 seconds, because its damage ramps late. These thresholds must be tuned from logs.
- Use Life Tap only when mana is meaningfully missing, health is normally above 65%, no lethal damage is incoming, and the healer is not under pressure. Do not convert the healer’s mana into the Warlock’s mana by tapping recklessly.
- Drain Soul only when the target is expected to die during the channel and the shard inventory is below its configured cap. Stop creating shards before they consume all free bag slots.

### Affliction Warlock — 31 Affliction / 20 Demonology / 0 Destruction

Role and equipment assumptions:

- Most sustainable solo Warlock, multidot damage, drain-tanking, and party curse support.
- Use a wand early; later favor Shadow damage, general spell damage, Stamina, Intellect, and Spirit on a staff or one-hand plus off-hand.
- Voidwalker is the safe default on hard pulls. Succubus is preferred for ordinary drain-tanking if threat logic proves reliable. Imp is the normal five-player pet; Felhunter replaces it for interrupt or dispel duty.

Level-60 allocation:

- Affliction, 31: Improved Corruption 5/5; Suppression 3/5; Improved Life Tap 2/2; Improved Drain Soul 2/2; Improved Drain Life 3/5; Fel Concentration 5/5; Nightfall 2/2; Grim Reach 2/2; Siphon Life 1/1; Shadow Mastery 5/5; Dark Pact 1/1.
- Demonology, 20: Demonic Embrace 5/5; Improved Voidwalker 3/3; Fel Intellect 5/5; Fel Domination 1/1; Fel Stamina 4/5; Master Summoner 2/2.
- Destruction, 0.
- Total: 31 + 20 + 0 = 51.

Exact purchase order:

- Levels 10–14: Improved Corruption ranks 1–5.
- Levels 15–17: Suppression ranks 1–3.
- Levels 18–19: Improved Life Tap ranks 1–2.
- Levels 20–21: Improved Drain Soul ranks 1–2.
- Levels 22–24: Improved Drain Life ranks 1–3.
- Levels 25–29: Fel Concentration ranks 1–5.
- Level 30: Siphon Life rank 1.
- Levels 31–32: Nightfall ranks 1–2.
- Levels 33–34: Grim Reach ranks 1–2.
- Levels 35–39: Shadow Mastery ranks 1–5.
- Level 40: Dark Pact rank 1.
- Levels 41–45: Demonic Embrace ranks 1–5.
- Levels 46–48: Improved Voidwalker ranks 1–3.
- Levels 49–53: Fel Intellect ranks 1–5.
- Level 54: Fel Domination rank 1.
- Levels 55–58: Fel Stamina ranks 1–4.
- Levels 59–60: Master Summoner ranks 1–2.

Combat policy:

- Solo priority: send pet; apply Corruption; apply Curse of Agony only to a target that will live for its late ticks; add Siphon Life on a long target; use Drain Life while health is missing and Fel Concentration can protect the channel; otherwise wand or Shadow Bolt according to mana and target life.
- Nightfall: cast the instant Shadow Bolt promptly unless an interrupt, defensive, pet rescue, or healer duty is more urgent. Do not overwrite the proc by waiting through several Drain Life ticks.
- Resource loop: Life Tap while safe, recover health through Siphon Life and Drain Life, and use Dark Pact when pet mana is above roughly 50% after reserving enough for its taunt, interrupt, or primary attack. Never drain the Felhunter below Spell Lock reserve against a caster.
- Group priority: pick the group’s support curse; apply Corruption only if the mob will live long enough; Siphon Life on elites or bosses; Shadow Bolt or Drain Life filler. Many normal dungeon mobs die too quickly for a three-DoT setup.
- Multidot and AoE: spread Corruption to at most a small configured number of non-CC targets when the tank owns them. Use Rain of Fire only after control is stable. Affliction should almost never use autonomous Hellfire.
- Interrupt and control: Felhunter Spell Lock first when assigned; Curse of Tongues as prevention; Death Coil for an immediate peel; Fear only with a safe flee path. Banish is valuable against demons and elementals when trained and assigned.
- Pet survival: Health Funnel only when the pet is holding a needed target and the Warlock is not taking dangerous damage. Fel Domination plus Master Summoner is the recovery path after a pet dies.
- Defensive order: Healthstone; Death Coil; pet Sacrifice when a Voidwalker shield can prevent death; controlled Fear; then escape. Do not consume the only shard casually.
- Party utility: create Healthstones, Soulstone the healer or recovery target, provide Blood Pact with the Imp, Spell Lock and Devour Magic with the Felhunter, and maintain the chosen curse.
- Low-level fallback: before Corruption becomes instant, cast it only when the pet has threat or the opening time is safe. Before Drain Life, use DoTs plus wand. Before Siphon Life and Dark Pact, use the Life Tap/Drain Life loop.

Known Vanilla limitations:

- Vanilla has a 16-debuff target limit. Do not indiscriminately place every DoT and curse on shared raid-style targets.
- DoTs are poor on short-lived dungeon trash and can break crowd control if target selection is wrong.
- Improved Drain Soul’s regeneration requires the drain to participate correctly in the kill; a bot must not assume the buff merely because it began channeling.
- Dark Pact can disable the pet’s next taunt or Spell Lock if it consumes the reserve mana.

Sources and disagreement:

- [Icy Veins Affliction Warlock leveling](https://www.icy-veins.com/wow-classic/affliction-warlock-leveling-talent-build-from-1-to-60)
- [Icy Veins general Warlock leveling](https://www.icy-veins.com/wow-classic/classic-warlock-leveling-guide)
- [Warcraft Tavern Warlock leveling](https://www.warcrafttavern.com/wow-classic/guides/warlock-leveling-guide/)

The sources broadly agree on instant Corruption, Life Tap/Drain Life sustain, and an Affliction/Demonology finish. They differ on Voidwalker versus Succubus drain-tanking and on how many points to put into Suppression and Improved Drain Life. This profile uses adaptive pet selection and spends some pure Drain Life points on pet recovery because an autonomous bot needs resilience after an imperfect pull.

### Demonology Warlock — 20 Affliction / 31 Demonology / 0 Destruction

Role and equipment assumptions:

- Safest pet-centered Warlock; solo durability, emergency pet recovery, and useful pet selection in a group.
- Equip Stamina, Shadow/general spell damage, Intellect, and Spirit. Keep a wand for low-cost finishing.
- Voidwalker is the default solo tank, Imp the default physical-party support pet, Felhunter the anti-caster pet, and Succubus the high-damage option when threat is safe.

Level-60 allocation:

- Affliction, 20: Improved Corruption 5/5; Suppression 3/5; Improved Life Tap 2/2; Improved Drain Soul 2/2; Improved Drain Life 3/5; Fel Concentration 5/5.
- Demonology, 31: Demonic Embrace 5/5; Improved Voidwalker 3/3; Fel Intellect 3/5; Fel Domination 1/1; Fel Stamina 5/5; Master Summoner 2/2; Unholy Power 5/5; Demonic Sacrifice 1/1; Master Demonologist 5/5; Soul Link 1/1.
- Destruction, 0.
- Total: 20 + 31 + 0 = 51.

Exact purchase order:

- Levels 10–14: Improved Corruption ranks 1–5.
- Levels 15–16: Suppression ranks 1–2.
- Levels 17–21: Demonic Embrace ranks 1–5.
- Levels 22–24: Improved Voidwalker ranks 1–3.
- Levels 25–27: Fel Intellect ranks 1–3.
- Level 28: Fel Domination rank 1.
- Levels 29–33: Fel Stamina ranks 1–5.
- Levels 34–35: Master Summoner ranks 1–2.
- Levels 36–40: Unholy Power ranks 1–5.
- Level 41: Demonic Sacrifice rank 1.
- Levels 42–46: Master Demonologist ranks 1–5.
- Level 47: Soul Link rank 1.
- Level 48: Suppression rank 3.
- Levels 49–50: Improved Life Tap ranks 1–2.
- Levels 51–52: Improved Drain Soul ranks 1–2.
- Levels 53–55: Improved Drain Life ranks 1–3.
- Levels 56–60: Fel Concentration ranks 1–5.

Combat policy:

- Solo priority: select and summon the pet for the encounter; enable Soul Link whenever a pet is present; send pet and wait for initial threat; Corruption; a long-target curse; Immolate only if the target will live long enough; Drain Life when health is missing, otherwise wand or Shadow Bolt.
- Pet threat: with a Voidwalker, reduce or pause damage when the player is about to overtake pet threat. If the Voidwalker repeatedly cannot hold the current gear level’s damage, switch ordinary pulls to Succubus drain-tanking rather than oscillating threat every global cooldown.
- Master Demonologist: consume the actual aura associated with the summoned demon. Do not hard-code later-expansion bonuses. Pet choice remains encounter-driven.
- Pet recovery: Health Funnel when the pet is tanking and player health is safe. If the pet dies in combat, use Fel Domination plus the fastest legal summon only when movement/control creates a safe cast window.
- Demonic Sacrifice: disabled in the default Soul Link profile. Sacrificing removes the active pet and defeats the build’s survival model. It may be an explicit no-pet mode for a special encounter, never a routine cooldown.
- Group priority: Imp for Blood Pact and ranged damage unless Felhunter utility is required. Corruption on long targets, the assigned curse, then Shadow Bolt/Drain Life. Avoid sending a melee pet through an uncleared pack.
- AoE: Rain of Fire behind stable tank control. Hellfire only if the tank controls the pack, no CC is nearby, healer mana is healthy, player health is high, and the expected channel is worth the self-damage.
- Interrupt and control: Felhunter Spell Lock, Devour Magic, Succubus Seduction only on a safely assigned humanoid, Death Coil as a peel, Fear only in controlled space.
- Defensive order: Healthstone, Soul Link damage sharing, Voidwalker Sacrifice, Death Coil, controlled Fear, then Fel Domination recovery.
- Party utility: Healthstone, Soulstone, Blood Pact, Felhunter dispel/interrupt, and curse assignment.
- Low-level fallback: levels 10–16 intentionally buy instant Corruption and hit before committing to Demonology. Before Soul Link, play as a sturdier pet Affliction Warlock; there is no paid level-40 respec requirement.

Known Vanilla limitations:

- The five Master Demonologist ranks are talent spells 23785, 23822, 23823, 23824, and 23825 in the live DBC, and their Spell.dbc rank labels are blank. Resolve them by Talent.dbc RankID, not rank text.
- Soul Link requires a living active demon; Demonic Sacrifice and Soul Link are not simultaneous default modes.
- Pet pathing, autocast state, and line of sight are more important than theoretical talent DPS. A pet stuck behind geometry nullifies much of this spec.
- Demonic Embrace trades Spirit for Stamina in Vanilla, so its durability gain has a regeneration cost.

Sources and disagreement:

- [Icy Veins Demonology Warlock leveling](https://www.icy-veins.com/wow-classic/demonology-warlock-leveling-talent-build-from-1-to-60)
- [Icy Veins general Warlock leveling](https://www.icy-veins.com/wow-classic/classic-warlock-leveling-guide)
- [Warcraft Tavern Warlock leveling](https://www.warcrafttavern.com/wow-classic/guides/warlock-leveling-guide/)

Guides commonly take Improved Corruption before entering Demonology, which delays Soul Link but makes the whole pre-40 path much better. Some advise a level-40 respec to get the capstone immediately. This no-respec bot order accepts Soul Link at 47, avoiding a special reset workflow. Pet advice also disagrees: Voidwalker is safer when threat works, while Succubus drain-tanking is faster when the player can manage incoming damage. The AI should select rather than globally hard-code one.

### Destruction Warlock — 17 Affliction / 0 Demonology / 34 Destruction

Role and equipment assumptions:

- Direct and fire damage with stronger burst and dungeon AoE, but materially worse solo sustain than Affliction or Demonology.
- Imp is the normal group pet and receives Improved Firebolt. Voidwalker remains the safe solo pet until it can no longer hold threat; Felhunter replaces either against dangerous casters.
- Favor spell damage, fire/shadow damage appropriate to the rotation, Intellect, Stamina, and a good wand.

Level-60 allocation:

- Affliction, 17: Improved Corruption 5/5; Suppression 3/5; Improved Life Tap 2/2; Improved Drain Soul 2/2; Improved Curse of Agony 3/3; Nightfall 2/2.
- Demonology, 0.
- Destruction, 34: Cataclysm 5/5; Bane 5/5; Improved Firebolt 2/2; Devastation 5/5; Shadowburn 1/1; Intensity 2/2; Destructive Reach 2/2; Improved Immolate 5/5; Ruin 1/1; Emberstorm 5/5; Conflagrate 1/1.
- Total: 17 + 0 + 34 = 51.

Exact purchase order:

- Levels 10–14: Cataclysm ranks 1–5.
- Levels 15–19: Bane ranks 1–5.
- Levels 20–24: Devastation ranks 1–5.
- Level 25: Shadowburn rank 1.
- Levels 26–27: Intensity ranks 1–2.
- Levels 28–29: Destructive Reach ranks 1–2.
- Level 30: Ruin rank 1.
- Levels 31–35: Improved Immolate ranks 1–5.
- Levels 36–39: Emberstorm ranks 1–4.
- Level 40: Conflagrate rank 1.
- Level 41: Emberstorm rank 5.
- Levels 42–43: Improved Firebolt ranks 1–2.
- Levels 44–48: Improved Corruption ranks 1–5.
- Levels 49–51: Suppression ranks 1–3.
- Levels 52–53: Improved Life Tap ranks 1–2.
- Levels 54–55: Improved Drain Soul ranks 1–2.
- Levels 56–58: Improved Curse of Agony ranks 1–3.
- Levels 59–60: Nightfall ranks 1–2.

Combat policy:

- Solo priority: send pet; Immolate if the target should live at least about 8 seconds; Corruption only once instant at level 48 or when earlier cast time is safe; long-target curse; Shadow Bolt as the direct filler; Conflagrate near the end of Immolate or when immediate burst will secure the kill.
- Do not consume a fresh Immolate with Conflagrate unless burst is required to prevent damage, stop a runner, or beat a dangerous cast. Prefer harvesting most of the DoT first.
- Shadowburn: use only as an execute when the target should die within five seconds and the shard reserve is above its minimum. Do not spend the Soul Shard on ordinary filler.
- Searing Pain is not a normal group filler because its extra threat is dangerous. It is acceptable only when the Warlock intentionally owns the target.
- Nightfall after level 59: consume with an instant Shadow Bolt unless a defensive or interrupt is more urgent.
- Mana: Life Tap under the shared safety gate; use a wand on low-value targets. Cataclysm reduces Destruction costs but does not make unlimited burst sustainable.
- Group priority: assigned support curse if needed; Immolate on long-lived targets; Shadow Bolt; Conflagrate late; Shadowburn execute. Let the tank establish threat before Ruin-boosted criticals.
- AoE: Rain of Fire is the safe default. Hellfire requires at least three controlled targets, no nearby CC, high health, healer capacity, and Intensity-protected channel value. Stop immediately if tank control or health safety fails.
- Interrupt and control: Felhunter Spell Lock when selected; Curse of Tongues as prevention; Death Coil for a peel; Fear only with a safe path.
- Defensive order: Healthstone, Death Coil, pet control/Sacrifice when available, controlled Fear, and escape.
- Party utility: Imp Blood Pact, Healthstones, Soulstone, curses, Felhunter utility when required.
- Low-level fallback: before Bane, DoTs plus wand are often more efficient than repeated Shadow Bolt. Before Conflagrate, Immolate remains a normal DoT. Before instant Corruption at 48, skip it on short fights.

Known Vanilla limitations:

- Destruction leveling is substantially weaker solo than Affliction or Demonology because direct casting causes pushback, pet threat problems, and drinking.
- Hellfire damages the caster. A rotation copied from a coordinated spell-cleave guide can kill an autonomous bot or exhaust its healer.
- Ruin and large direct criticals can spike threat. Threat checks must happen before starting the cast, not only after damage lands.
- Improved Firebolt helps the Imp’s cast speed, but the Imp can become mana-limited; its value varies with fight length.

Sources and disagreement:

- [Icy Veins Destruction Warlock leveling](https://www.icy-veins.com/wow-classic/destruction-warlock-leveling-talent-build-from-1-to-60)
- [Icy Veins general Warlock leveling](https://www.icy-veins.com/wow-classic/classic-warlock-leveling-guide)
- [Warcraft Tavern Warlock leveling](https://www.warcrafttavern.com/wow-classic/guides/warlock-leveling-guide/)

The dedicated Icy Veins Destruction path explicitly targets dungeon spell cleave and warns that it is much worse for solo questing. It takes Aftermath and Pyroclasm to improve Hellfire control. This mixed-use bot baseline instead takes Bane and Improved Firebolt, uses direct single-target spells more effectively, and heavily gates Hellfire. If the deployment has a dedicated dungeon-only queue with trusted tank/healer bots, a separate Aftermath/Pyroclasm profile is justified.

## Priest

### Priest spell-ID reference

The following player IDs were checked against the live build-5875 Spell.dbc:

- Damage and control: Smite 585, Holy Fire rank 1 14914, Mind Blast rank 1 8092, Shadow Word: Pain rank 1 589, Psychic Scream rank 1 8122, and Fade rank 1 586.
- Healing: Lesser Heal rank 1 2050, Heal rank 1 2054, Greater Heal rank 1 2060, Flash Heal rank 1 2061, Renew rank 1 139, and Prayer of Healing rank 1 596.
- Protection and utility: Power Word: Shield rank 1 17, Power Word: Fortitude rank 1 1243, Inner Fire rank 1 588, Shadow Protection rank 1 976, Dispel Magic rank 1 527, Abolish Disease 552, and Resurrection rank 1 2006.
- Discipline/Holy talents: Inner Focus 14751, Divine Spirit rank 1 14752, Power Infusion 10060, Holy Nova rank 1 15237, and Lightwell rank 1 724.
- Shadow talents: Mind Flay rank 1 15407, Vampiric Embrace 15286, Silence 15487, and Shadowform 15473.

As with the other classes, Spell.dbc contains same-name NPC versions. Resolve the player’s spell chain and highest trained rank.

Shared healing and utility rules:

- Maintain Power Word: Fortitude and Inner Fire. Maintain Divine Spirit when the build has it and Shadow Protection when the encounter warrants it.
- Select the smallest heal rank predicted to land the target in the safe health band. Vanilla has no later-expansion downranking penalty, so blindly using the maximum rank creates avoidable overheal and downtime.
- Use Heal or Greater Heal for efficient planned recovery and Flash Heal for a target whose time-to-death is shorter than the efficient cast. Renew is for continuing damage, not a healthy target.
- Prayer of Healing is party-only and expensive. Use it when at least three party members will receive meaningful healing and the cast is safe.
- Power Word: Shield applies Weakened Soul. Do not spam it on a target that cannot receive another shield. In Vanilla, absorbed damage does not provide normal damage-taken rage, so avoid routine pre-shields on Warrior and bear tanks; shield them for emergencies or predictable lethal bursts.
- Dispel dangerous magic effects and disease by priority, not merely the first dispellable aura. Preserve mana when an effect is harmless or nearly expired.
- Fade after healing aggro or when a loose enemy targets the Priest. It is not permanent threat deletion, so combine it with movement toward the tank.
- Psychic Scream is an emergency tool only when feared targets cannot pull additional packs.
- Equip and train a strong wand. Spirit Tap requires the Priest to get the killing blow; coordinate wand finishing rather than assuming every group kill will grant it.

### Discipline Priest — 31 Discipline / 15 Holy / 5 Shadow

Role and equipment assumptions:

- Primary five-player healer, durable solo leveler, dispeller, and caster support through Divine Spirit and Power Infusion.
- Equip a high-DPS wand throughout leveling. Main-hand/off-hand or staff should favor Spirit, Intellect, healing/spell power, and enough Stamina to survive heal aggro.
- The five early Shadow points intentionally improve no-respec solo sustain; Power Infusion arrives at level 45 instead of 40.

Level-60 allocation:

- Discipline, 31: Wand Specialization 5/5; Silent Resolve 5/5; Improved Power Word: Fortitude 2/2; Improved Power Word: Shield 3/3; Inner Focus 1/1; Meditation 3/3; Mental Agility 5/5; Mental Strength 5/5; Divine Spirit 1/1; Power Infusion 1/1.
- Holy, 15: Healing Focus 2/2; Improved Renew 3/3; Holy Specialization 5/5; Divine Fury 5/5.
- Shadow, 5: Spirit Tap 5/5.
- Total: 31 + 15 + 5 = 51.

Exact purchase order:

- Levels 10–14: Spirit Tap ranks 1–5.
- Levels 15–19: Wand Specialization ranks 1–5.
- Levels 20–22: Improved Power Word: Shield ranks 1–3.
- Levels 23–24: Improved Power Word: Fortitude ranks 1–2.
- Levels 25–29: Silent Resolve ranks 1–5.
- Level 30: Inner Focus rank 1.
- Levels 31–33: Meditation ranks 1–3.
- Levels 34–38: Mental Agility ranks 1–5.
- Levels 39–43: Mental Strength ranks 1–5.
- Level 44: Divine Spirit rank 1.
- Level 45: Power Infusion rank 1.
- Levels 46–47: Healing Focus ranks 1–2.
- Levels 48–50: Improved Renew ranks 1–3.
- Levels 51–55: Holy Specialization ranks 1–5.
- Levels 56–60: Divine Fury ranks 1–5.

Combat policy:

- Healer priority: prevent a predicted death first; dispel a lethal control or damage effect; shield an emergency target without Weakened Soul; use the smallest efficient Heal/Greater Heal that reaches the safety band; Renew continuing damage; Flash Heal only when the slower cast would land too late.
- Inner Focus: pair with Prayer of Healing when at least three allies need it, or with the largest necessary Greater Heal. Do not burn it on a trivial Smite.
- Power Infusion: give it to a high-value caster during a long elite/boss window when healing is stable. Use it on self for a forecast healing emergency. Do not give it to a low-mana target that cannot exploit the duration.
- Solo priority: Holy Fire from range if trained; Shadow Word: Pain when the target should live at least about 12 seconds; Mind Blast or Smite while safe; wand to finish and secure Spirit Tap. Shield before a dangerous cast only when the mana cost and Weakened Soul are justified.
- Mana: enter the five-second-rule regeneration window by finishing with the wand. Spirit Tap after a killing blow is a signal to avoid unnecessary spell casts while its regeneration matters.
- Group damage: only Smite or wand when all allies are stable, healer mana is safe, and no dispel or incoming-damage event is imminent.
- Interrupt/control: Psychic Scream only under the shared safety gate. Priests have no normal spell interrupt in this build.
- Defensive order: Power Word: Shield, Fade and move toward tank, Psychic Scream if safe, Healthstone or potion if available, self-heal.
- Party utility: Fortitude, Divine Spirit, dispels, disease removal, Resurrection out of combat, and Power Infusion.
- Low-level fallback: before Heal, use Lesser Heal. Before Flash Heal, start healing earlier. Before Divine Spirit/Power Infusion, the role remains the same; skip unavailable buffs cleanly.

Known Vanilla limitations:

- Power Infusion is a single-target support cooldown and has opportunity cost; modern Priest assumptions do not apply.
- Shielding a rage tank can reduce rage generation, so a simplistic “shield tank on cooldown” policy harms the group.
- Spirit Tap is unreliable in a fast group unless the Priest is permitted to land a wand or damage-spell killing blow.
- Vanilla healing ranks and coefficients differ; the downrank selector must use live Spell data rather than modern values.

Sources and disagreement:

- [Icy Veins Discipline Priest leveling](https://www.icy-veins.com/wow-classic/discipline-priest-leveling-talent-build-from-1-to-60)
- [Icy Veins general Priest leveling](https://www.icy-veins.com/wow-classic/classic-priest-leveling-guide)
- [Warcraft Tavern Priest leveling](https://www.warcrafttavern.com/wow-classic/guides/priest-leveling-guide/)

Leveling guides strongly value Spirit Tap and Wand Specialization even for healing Priests, while dungeon-only healers may spend those points directly in healing throughput and reach Power Infusion at 40. This profile chooses a no-respec hybrid and accepts Power Infusion at 45. At level 60, a raid-only Discipline build may remove Spirit Tap and wand points; that is outside this bot’s mixed workload.

### Holy Priest — 16 Discipline / 30 Holy / 5 Shadow

Role and equipment assumptions:

- Primary five-player healer with better direct-heal efficiency and a workable Smite/wand solo loop.
- Equip a high-DPS wand and favor Spirit, Intellect, healing/spell power, and Stamina.
- This is the classic 30-point Holy shape rather than a Lightwell capstone build. An autonomous party cannot be assumed to click a placed Lightwell.

Level-60 allocation:

- Discipline, 16: Wand Specialization 5/5; Silent Resolve 2/5; Improved Power Word: Fortitude 2/2; Improved Power Word: Shield 3/3; Inner Focus 1/1; Meditation 3/3.
- Holy, 30: Healing Focus 2/2; Improved Renew 3/3; Holy Specialization 5/5; Divine Fury 5/5; Holy Nova 1/1; Inspiration 3/3; Improved Healing 3/3; Searing Light 2/2; Spiritual Guidance 5/5; Spiritual Healing 1/5.
- Shadow, 5: Spirit Tap 5/5.
- Total: 16 + 30 + 5 = 51.

Exact purchase order:

- Levels 10–14: Spirit Tap ranks 1–5.
- Levels 15–19: Wand Specialization ranks 1–5.
- Levels 20–21: Healing Focus ranks 1–2.
- Levels 22–24: Improved Renew ranks 1–3.
- Levels 25–29: Holy Specialization ranks 1–5.
- Levels 30–34: Divine Fury ranks 1–5.
- Level 35: Holy Nova rank 1.
- Levels 36–38: Inspiration ranks 1–3.
- Level 39: Improved Healing rank 1.
- Levels 40–41: Searing Light ranks 1–2.
- Levels 42–43: Improved Healing ranks 2–3.
- Levels 44–48: Spiritual Guidance ranks 1–5.
- Level 49: Spiritual Healing rank 1.
- Levels 50–52: Improved Power Word: Shield ranks 1–3.
- Levels 53–54: Improved Power Word: Fortitude ranks 1–2.
- Level 55: Inner Focus rank 1.
- Levels 56–58: Meditation ranks 1–3.
- Levels 59–60: Silent Resolve ranks 1–2.

Combat policy:

- Healing priority: same shared triage, with Heal/Greater Heal as the efficient default, Renew for continuing damage, Flash Heal for imminent death, and Prayer of Healing for three or more meaningfully injured party members.
- Inspiration: welcome it after a healing critical, but do not spam inefficient Flash Heals merely to fish for armor. Give naturally critical heals to the tank when several physical enemies are active.
- Inner Focus: pair with Prayer of Healing or the required large Greater Heal.
- Holy Nova: use as a no-threat emergency heal when multiple nearby allies need healing, or as finishing AoE when mana and aggro are safe. Its efficiency is poor; never spam it as the normal rotation.
- Solo priority: Holy Fire from range; Shadow Word: Pain on a long-enough target; Smite; wand to finish and trigger Spirit Tap. Divine Fury and Searing Light make this credible without pretending Holy equals Shadow kill speed.
- Mana: downrank, avoid overheal, use wand finishing, and let Spirit Tap/five-second-rule regeneration work. Do not cast Renew on trivial missing health just because it is instant.
- Aggro: Fade immediately after heal aggro and move toward the tank. Holy Nova’s no-threat healing is situationally useful but not a substitute for positioning.
- Defensive order: shield self, Fade, safe Psychic Scream, self-heal, consumable.
- Party utility: Fortitude, dispel, disease removal, Resurrection, and optional damage only during stable healing windows.
- Low-level fallback: Lesser Heal before Heal; Smite and wand before Holy Fire; no AoE until Holy Nova; skip talents’ spell actions until trained.

Known Vanilla limitations:

- Lightwell requires other players to notice and click it and is a poor default for bots without explicit click/interaction AI. The profile intentionally stops at 30 Holy.
- Holy Nova has a small radius and high mana cost. Walking into danger to reach an enemy invalidates its no-threat advantage.
- Spirit of Redemption is omitted because planning around the healer’s death is inferior to preventing it; consequently Lightwell’s prerequisite is also absent.
- This hybrid has only 1/5 Spiritual Healing. A raid-only throughput build commonly trades solo talents for more Spiritual Healing and deeper Discipline support.

Sources and disagreement:

- [Icy Veins Holy Priest leveling](https://www.icy-veins.com/wow-classic/holy-priest-leveling-talent-build-from-1-to-60)
- [Warcraft Tavern Holy Priest guide](https://www.warcrafttavern.com/wow-classic/guides/holy-priest/)
- [Warcraft Tavern Priest leveling](https://www.warcrafttavern.com/wow-classic/guides/priest-leveling-guide/)

Leveling sources still recommend Spirit Tap and wand damage because a pure healing path is slow outside continuous dungeon groups. Endgame healing guides commonly use a 21 Discipline / 30 Holy shape and skip Lightwell. This baseline is 16/30/5 to retain solo sustain without a reset. If every Holy bot is guaranteed a dungeon group from level 10, a separate pure-healer profile should move the five Shadow points into Discipline.

### Shadow Priest — 20 Discipline / 0 Holy / 31 Shadow

Role and equipment assumptions:

- Solo ranged damage, party shadow damage and passive healing, emergency off-healer, dispeller, and caster interrupter.
- Equip the best available wand through leveling, then favor Shadow/general spell damage, Spirit, Intellect, and Stamina.
- The order goes directly down Shadow so Shadowform is learned at level 40 without a respec. Discipline quality-of-life arrives afterward.

Level-60 allocation:

- Discipline, 20: Wand Specialization 5/5; Silent Resolve 1/5; Improved Power Word: Fortitude 2/2; Improved Power Word: Shield 3/3; Inner Focus 1/1; Meditation 3/3; Mental Agility 5/5.
- Holy, 0.
- Shadow, 31: Spirit Tap 5/5; Blackout 2/5; Improved Shadow Word: Pain 2/2; Shadow Focus 3/5; Improved Psychic Scream 2/2; Mind Flay 1/1; Shadow Reach 3/3; Shadow Weaving 5/5; Silence 1/1; Vampiric Embrace 1/1; Darkness 5/5; Shadowform 1/1.
- Total: 20 + 0 + 31 = 51.

Exact purchase order:

- Levels 10–14: Spirit Tap ranks 1–5.
- Levels 15–16: Improved Shadow Word: Pain ranks 1–2.
- Levels 17–19: Shadow Focus ranks 1–3.
- Level 20: Mind Flay rank 1.
- Levels 21–22: Blackout ranks 1–2.
- Levels 23–24: Improved Psychic Scream ranks 1–2.
- Levels 25–27: Shadow Reach ranks 1–3.
- Levels 28–32: Shadow Weaving ranks 1–5.
- Level 33: Silence rank 1.
- Level 34: Vampiric Embrace rank 1.
- Levels 35–39: Darkness ranks 1–5.
- Level 40: Shadowform rank 1.
- Levels 41–45: Wand Specialization ranks 1–5.
- Levels 46–48: Improved Power Word: Shield ranks 1–3.
- Levels 49–50: Improved Power Word: Fortitude ranks 1–2.
- Level 51: Inner Focus rank 1.
- Levels 52–54: Meditation ranks 1–3.
- Level 55: Silent Resolve rank 1.
- Levels 56–60: Mental Agility ranks 1–5.

Combat policy:

- Preparation: maintain Fortitude, Inner Fire, and Shadowform after level 40. Do not repeatedly leave and re-enter Shadowform for small heals.
- Solo priority: Vampiric Embrace on an elite or a target expected to live long enough to return useful healing; Shadow Word: Pain when expected life is roughly 12 seconds or more; Mind Blast when mana and threat permit; Mind Flay while the target remains at useful range; wand to finish and trigger Spirit Tap.
- Group priority: allow tank threat; Vampiric Embrace on the long-lived kill target when the party is missing health; Shadow Word: Pain; Mind Blast under threat cap; Mind Flay filler. Maintain Shadow Weaving naturally rather than attacking a CC target to preserve stacks.
- Mana: below roughly 30%, stop refreshing marginal DoTs, prefer wand, and exploit Spirit Tap after a secured kill. Mental Agility reduces instant-spell cost after level 56 but does not remove the need for conservation.
- Blackout: react to a proc as free control. Never plan survival around its random stun.
- Interrupts: Silence dangerous caster spells and use Psychic Scream only when its fear paths are safe. Dispel Magic remains important even while dealing damage.
- Off-healing: Power Word: Shield and utility remain available as permitted by the live spell rules. To cast Holy healing spells, leave Shadowform, perform a real emergency healing sequence, and re-enter only after the party stabilizes. Do not stance-dance for a trivial Renew.
- Defensive order: shield self if available and no Weakened Soul; Silence a caster; safe Psychic Scream; Fade and move; drop Shadowform and heal if survival requires it.
- AoE: multidot only a small number of tank-controlled, non-CC targets with sufficient time-to-live. Psychic Scream is not an AoE damage setup. Shadow Priest has no efficient normal damage AoE in Vanilla.
- Party utility: Fortitude, Shadow Protection where useful, Dispel Magic, Abolish Disease, Vampiric Embrace healing, Silence, and emergency Resurrection after combat.
- Low-level fallback: before Mind Flay, Shadow Word: Pain, Smite/Mind Blast, then wand. Before Shadowform, the damage rotation is the same without the form bonus. If no wand is equipped, use Smite sparingly rather than waiting idle.

Known Vanilla limitations:

- Shadowform prevents Holy healing casts. Form cancellation and recast consume time and mana, so off-healing must have a real urgency threshold.
- Vampiric Embrace healing creates additional threat and is party-oriented in Vanilla; do not assume modern raid-wide behavior.
- Shadow Word: Pain, Vampiric Embrace, and Shadow Weaving consume limited debuff slots on shared targets.
- Mind Flay is a channel and is sensitive to range, pushback, and target death. Lock the channel rather than issuing it every update.

Sources and disagreement:

- [Icy Veins Shadow Priest leveling](https://www.icy-veins.com/wow-classic/shadow-priest-leveling-talent-build-from-1-to-60)
- [Warcraft Tavern Priest leveling](https://www.warcrafttavern.com/wow-classic/guides/priest-leveling-guide/)
- [Warcraft Tavern Shadow Priest PvE](https://www.warcrafttavern.com/wow-classic/guides/shadow-priest-pve/)

Many human leveling guides buy Wand Specialization early and pay for a level-40 respec into Shadowform. That is faster in some level bands but adds state and cost that the current bot flow does not need. This no-respec order gets Shadowform exactly at 40 and buys wand power afterward. Endgame Shadow builds also disagree between Improved Mind Blast/Shadow Affinity and Silence/control points; this profile chooses Silence because a five-player autonomous interrupter is more valuable than a small theoretical throughput increase.

## Implementation-facing notes for later work

- Store talent profiles as ordered TalentID or, preferably, verified Talent.dbc RankID sequences. Names are documentation labels and are unsafe runtime keys.
- Resolve the next RankID from Talent.dbc and verify class, tree, current rank, tier points, DependsOn, and free talent points before learning it.
- Make talent spending idempotent. On login and every level-up, compute all missing legal ranks up to the character’s available point count. This repairs existing level-20-plus bots instead of helping only fresh spawns.
- Detect an existing non-template build. Do not silently overwrite or reset it. A migration policy needs an explicit choice between filling compatible missing ranks, performing a controlled reset, or leaving custom builds alone.
- Trainer spells and talent spells are different acquisition paths. The one-point talent unlocks the first talent-spell rank; later spell ranks may come from trainers. Combat AI must select the highest spell rank the character actually knows.
- Use aura and spell-family data for procs such as Clearcasting, Nightfall, Spirit Tap, Shadowform, Soul Link, and Weakened Soul. Do not infer them solely from localized names.
- Keep rotation selection separate from action execution. A cast, wand shot, or channel needs a completion/cancel policy so the high-frequency AI update cannot clip it.
- Log the reason for skipped high-priority actions: unavailable spell, low mana, unsafe threat, bad time-to-live, CC collision, pet reserve, or cooldown. Those reasons are essential for tuning.

## Explicit uncertainties and follow-up tests

- The talent structures, ranks, tiers, prerequisite links, capstone spell IDs, and all nine point orders are validated against the live build-5875 DBC and are not uncertain.
- The combat thresholds are design starting points. Target time-to-live, threat headroom, healer-pressure, Life Tap, Dark Pact pet reserve, and AoE safety thresholds require telemetry and in-game tuning.
- Same-name NPC spells in Spell.dbc mean name-only ID lookup is unsafe. The listed player IDs were selected from the live data, but implementation should resolve known player spell chains.
- Pet autocast behavior, Voidwalker threat, Fear pathing, Ice Block cancellation, wand clipping, and Shadowform heal restrictions must be integration-tested on this exact core because emulator behavior and copied partybot logic can differ from Blizzard Classic Era.
- The 16-debuff limit is the Vanilla design constraint, but the realm’s current configuration and any core customizations should be checked before implementing raid-scale debuff arbitration.
- “Optimal” varies with guaranteed group composition. Dedicated dungeon-only variants may justifiably replace the mixed-use Destruction, Holy, or Frost profiles; they should be separate named profiles rather than hidden changes to these baselines.
