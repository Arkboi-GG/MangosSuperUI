# Bot talent and rotation matrix: Warrior, Paladin, Shaman

Status: research/design baseline, validated against the live build-5875 DBC on 2026-08-25. This file does not change core, web-app, database, or deployment behavior.

## Contract used by this matrix

- Target: autonomous open-world leveling plus five-player PvE. The builds intentionally value uptime, survival, interrupts, dispels, and small-group utility over a raid-only damage or healing maximum.
- “Canonical” here means the recommended level-60 endpoint for this bot contract and a clear 31-point spec identity. It does not mean the same allocation is optimal for a geared raid parse, PvP, or a character that can respec at selected levels.
- Each purchase range is inclusive and supplies exactly one talent point per level from 10 through 60. Every build ends at exactly 51 points.
- All nine sequences were simulated one purchase at a time against the installed Linux `/home/wowvmangos/vmangos/run/data/5875/dbc/Talent.dbc`: `WDBC`, 432 records, 21 fields, 84-byte records. Rank fields 4–8, prerequisite talent field 13, prerequisite rank field 16, and required-spell field 20 produced zero maximum-rank, tier, prerequisite, gap, or total errors. `TalentTab.dbc` class mask field 12/order field 13 was also used.
- Names, rank counts, tiers, and prerequisites were independently compared with the build-5875-compatible [TInspect 1.12 talent data](https://github.com/SouD/Tinspect/blob/master/TI_talents.lua). The [Classic talent calculator data](https://github.com/maladr0it/classic-talent-calculator/tree/master/src/trees) is a second readable transcription; its obvious spelling errors are not copied here.
- Numeric spell IDs below identify rank 1 or the sole talent spell. Runtime code should resolve the highest spell rank actually known through `SpellChain`, except when the policy explicitly asks for rank 1 utility or a down-ranked heal.
- A bot must never attempt an unlearned rank or a talent ability before its purchase level. Each priority therefore degrades to trainer abilities and auto-attacks at low level.
- Preserve crowd control: no cleave, chain, shock DoT, totem pulse, or ground AoE may hit a controlled unit. Interrupt and emergency-heal decisions override damage.
- Resource reserves are policies rather than immutable percentages. Suggested thresholds are starting values to tune from telemetry; they prevent a damage loop from consuming the mana or rage needed for an interrupt, heal, taunt, or defensive.

## Warrior common combat contract

- Role comes from the current party assignment, not only the talent tree. Arms and Fury may rescue-tank with a shield, but a DPS bot must not Taunt unless an explicit peel/rescue rule asks it to.
- Maintain Battle Shout when it benefits the nearby group. Use Demoralizing Shout on dangerous physical packs, Disarm weapon users, and Hamstring/Piercing Howl on runners before they reach another pack.
- Interrupt dangerous heals/control first. Use Shield Bash with a shield in Battle/Defensive Stance or Pummel in Berserker Stance. Concussion Blow is a stun fallback, not a spell-school lock.
- Stance changes are resource decisions. Tactical Mastery retains 5/10/15/20/25 rage by rank; do not change stance for a low-value action if it starves the next core attack.
- Heroic Strike and Cleave replace the next white swing and therefore suppress that swing's rage gain. They are excess-rage dumps, not default fillers. Start telemetry tuning around more than 50 rage for DPS and 60 for tanks while still reserving the next core ability.
- Do not use untalented Slam until the AI is swing-timer aware: its cast can delay/reset auto-attacks. Bloodrage costs health and is suppressed at unsafe health or when deliberately avoiding combat.
- Suppress Thunder Clap, Whirlwind, Cleave, Sweeping Strikes, and aggro-producing shouts near crowd control. Intimidating Shout is a solo escape unless add pathing proves it safe.
- Equip shield plus Defensive Stance and use Shield Block for elites, packs, or low health. Last Stand addresses a recoverable health dip; Shield Wall handles predicted lethal damage. Retaliation, Shield Wall, and Recklessness share Vanilla's long 30-minute cooldown, so autonomous logic should reserve it for Shield Wall unless explicitly committing another ability.

Trainer-level fallback milestones: Charge at 4; Thunder Clap 6; Hamstring 8; the Defensive Stance/Taunt/Sunder quest around 10; Overpower and Shield Bash 12; Demoralizing Shout and Revenge 14; Mocking Blow and Shield Block 16; Disarm 18; Cleave and Retaliation 20; Intimidating Shout 22; Execute 24; Challenging Shout 26; Shield Wall 28; the Berserker Stance/Intercept quest around 30; Berserker Rage 32; Whirlwind 36; Pummel 38; tree capstones 40; Recklessness 50. Before a capstone, use auto-attack, Battle Shout, safe Bloodrage, reactive Overpower, Rend on a long-lived bleedable target, Sunder on a long/high-armor target, and Heroic Strike only with excess rage. The tank fallback is Defensive Stance, Demoralizing Shout, Revenge, tab-Sunder, then an excess-rage dump.

### Warrior — Arms

Role and assumptions: autonomous two-handed melee DPS that can tank ordinary leveling dungeons when assigned. Prefer a slow two-handed axe with trained weapon skill; carry a one-hander, shield, and ranged pulling weapon. If the best available weapon is not an axe, the five weapon-specialization points must be substituted as a configuration choice rather than left dead.

Level-60 allocation: **31 Arms / 20 Fury / 0 Protection**.

- Arms: Improved Rend 3, Deflection 3, Improved Charge 2, Tactical Mastery 5, Improved Overpower 2, Anger Management 1, Deep Wounds 3, Two-Handed Weapon Specialization 5, Axe Specialization 5, Sweeping Strikes 1, Mortal Strike 1.
- Fury: Cruelty 5, Unbridled Wrath 5, Piercing Howl 1, Improved Battle Shout 4, Enrage 5.

Purchase order:

- 10–12 Improved Rend; 13–15 Deflection; 16–17 Improved Charge; 18–19 Tactical Mastery ranks 1–2.
- 20–21 Improved Overpower; 22–24 Deep Wounds; 25–27 finish Tactical Mastery; 28 Anger Management.
- 29 Two-Handed Weapon Specialization rank 1; 30 Sweeping Strikes (12292); 31–35 Axe Specialization.
- 36–39 finish Two-Handed Weapon Specialization; 40 Mortal Strike (12294).
- 41–45 Cruelty; 46–50 Unbridled Wrath; 51 Piercing Howl (12323); 52–55 Improved Battle Shout; 56–60 Enrage.

Combat priority and state rules:

1. Emergency survival or interrupt overrides damage. Execute when it will finish the target or during a deliberate execute burn; do not drain rage into Execute when the target will survive and Mortal Strike/control is needed.
2. React to a dodge with Overpower during its roughly five-second window if reaching Battle Stance will not starve Mortal Strike. Use Mortal Strike on cooldown when threat is safe.
3. Use Whirlwind at two or more CC-safe targets; on one target, use it only with excess rage after Mortal Strike. Apply Rend only to a bleedable target expected to live for most of its duration, and Sunder Armor only to a long-lived/high-armor target.
4. At two or more healthy safe targets, pool 30 rage, use Sweeping Strikes in Battle Stance, then high-damage attacks/Whirlwind/Cleave. Suppress the combo if one target is dying, the tank lacks threat, or any controlled target is in reach.
5. Hamstring a runner early. With a slow weapon, the solo AI may kite between swings where pathing is safe, but it must not lose contact for Mortal Strike or pull nearby enemies.
6. In a group, assist the marked target, do not Charge through the tank, begin throttling around an 80–90% threat ratio, and avoid cleave near control. When assigned to tank, equip shield, enter Defensive Stance, use Demoralizing Shout, Revenge, tab-Sunder, Taunt losses, and save Shield Block for bosses/spikes.

Low-level fallbacks: the common trainer loop applies before Sweeping Strikes at 30 and Mortal Strike at 40. Overpower is available from the trainer at 12 but becomes materially better after its talent ranks at 20–21. A missing slow axe requires the best trained two-hander and an eventual specialization reassignment.

Known Vanilla limitations: weapon DPS and weapon skill dominate results; no Bladestorm, Victory Rush, or modern rage normalization; stance changes can discard rage; and bleed/stun/slow immunities are common. Sweeping Strikes behavior can differ among emulator implementations and must be integration-tested.

Sources and disagreement: [Icy Veins Arms leveling](https://www.icy-veins.com/wow-classic/arms-warrior-leveling-guide-1-60) and [Wowhead Arms leveling](https://www.wowhead.com/classic/guide/classes/warrior/arms/leveling-tips) support the core leveling pattern. Icy Veins' published route uses Improved Hamstring 3 and only 3/5 Two-Handed Weapon Specialization; this PvE bot favors stable weapon damage and retains Piercing Howl as a one-point escape/runner tool. Some level-60 builds respec Improved Charge into Impale. Weapon specialization remains the explicit unresolved gear-dependent choice.

### Warrior — Fury

Role and assumptions: high-uptime melee DPS using a slow two-hander while leveling, avoiding dual-wield miss and two simultaneous weapon-skill grinds. Carry a one-hander and shield for a rescue tank assignment. A later gear-aware profile may switch to conventional dual wield only with two good weapons, adequate skill/hit, and stable group threat.

Level-60 allocation: **20 Arms / 31 Fury / 0 Protection**.

- Arms: Improved Rend 3, Deflection 2, Tactical Mastery 5, Improved Overpower 1, Anger Management 1, Deep Wounds 3, Two-Handed Weapon Specialization 5.
- Fury: Cruelty 5, Unbridled Wrath 5, Piercing Howl 1, Blood Craze 3, Improved Battle Shout 5, Enrage 5, Death Wish 1, Flurry 5, Bloodthirst 1.

Purchase order:

- 10–14 Cruelty; 15–19 Unbridled Wrath; 20 Piercing Howl (12323).
- 21–23 Blood Craze; 24 Improved Battle Shout rank 1; 25–29 Enrage; 30 Death Wish (12328).
- 31–34 finish Improved Battle Shout; 35–39 Flurry; 40 Bloodthirst (23881).
- 41–43 Improved Rend; 44–45 Deflection; 46–50 Tactical Mastery.
- 51 Improved Overpower rank 1; 52 Anger Management; 53–55 Deep Wounds; 56–60 Two-Handed Weapon Specialization.

Combat priority and state rules:

1. Emergency/interrupt first, then a lethal Execute. Take Overpower after a target dodge only when the stance change is rage-positive and does not delay Bloodthirst.
2. On one target, use Bloodthirst on cooldown, then Whirlwind with sufficient reserve. At two or more clean targets, Whirlwind moves ahead of Bloodthirst. Rend/Sunder are reserved for a target that will live long enough; Heroic Strike/Cleave remain excess-rage dumps.
3. Use Berserker Stance for sustained DPS after it is unlocked, while recognizing that Charge, Rend, and Overpower require Battle Stance. Flurry consumes its next three charges on successful weapon swings, so maintain contact and avoid unnecessary queued Heroic Strikes that reduce rage income.
4. Enrage and Blood Craze react to receiving a critical strike and cannot be reliably activated on demand. Death Wish costs rage and reduces defenses: use it only when the target will live, health is safe, party threat is stable, and no incoming spike is expected.
5. Hold Berserker Rage for imminent/active Fear or Incapacitate unless deliberately using its rage behavior is demonstrably safe. Maintain Battle Shout for the melee party and throttle Whirlwind/Cleave for threat and crowd control.
6. Rescue-tank with shield, Defensive Stance, Revenge, Sunder, and Taunt, but do not volunteer Fury for a hard level-60 dungeon tank role: it lacks Defiance and Last Stand.

Low-level fallbacks: before Death Wish at 30, rely on auto-attacks, reactive trainer skills, Piercing Howl control, and excess-rage Heroic Strike. Before Bloodthirst at 40, prioritize contact and Whirlwind once trained at 36; the AI must omit unavailable capstones rather than substituting expansion abilities.

Known Vanilla limitations: two-handed Fury is chosen for autonomous leveling rather than the maximum geared raid parse; no Titan's Grip, Rampage, Victory Rush, or modern self-healing loop. Enrage/Flurry uptime is proc-dependent, Death Wish raises incoming risk, and dual-wield profiles need distinct miss/gear logic.

Sources and disagreement: [Icy Veins Fury leveling](https://www.icy-veins.com/wow-classic/fury-warrior-leveling-guide-1-60), [Wowhead Fury leveling](https://www.wowhead.com/classic/guide/classes/warrior/fury/leveling-tips), and [Wowhead endgame Fury builds](https://www.wowhead.com/classic/guide/classes/warrior/fury/dps-talent-builds-pve) disagree on Booming Voice versus Unbridled Wrath and early Battle Shout value. Arms is generally safer/easier while leveling, and geared level-60 raid Fury normally becomes dual-wield 17/34/0; this 20/31/0 two-handed route is the autonomous, no-respec spec contract.

### Warrior — Protection

Role and assumptions: tank-first five-player build using a one-handed weapon and shield full-time. Optimize survival, control, and predictable threat; accept deliberately slow solo leveling. Carry a ranged weapon for line-of-sight pulls.

Level-60 allocation: **11 Arms / 4 Fury / 36 Protection**.

- Arms: Deflection 5, Tactical Mastery 5, Anger Management 1.
- Fury: Cruelty 4.
- Protection: Shield Specialization 5, Anticipation 5, Improved Bloodrage 2, Toughness 5, Last Stand 1, Improved Shield Block 1, Improved Revenge 3, Defiance 5, Improved Taunt 2, Concussion Blow 1, One-Handed Weapon Specialization 5, Shield Slam 1.

Purchase order:

- 10–14 Shield Specialization; 15–19 Toughness; 20 Improved Shield Block rank 1.
- 21–22 Improved Bloodrage; 23 Last Stand (12975); 24 Improved Revenge rank 1; 25–29 Defiance.
- 30 Concussion Blow (12809); 31–32 finish Improved Revenge; 33–34 Improved Taunt.
- 35–39 One-Handed Weapon Specialization; 40 Shield Slam (23922).
- 41–45 Deflection; 46–50 Anticipation; 51–55 Tactical Mastery; 56 Anger Management; 57–60 Cruelty.

Combat priority and state rules:

1. Taunt or interrupt an immediate failure. Use Shield Block against elites/bosses, multiple attackers, or an expected spike; a block enables Revenge. Use Shield Slam on cooldown when threat is needed, Revenge whenever lit, then tab-Sunder with extra stacks on long-lived/boss targets.
2. Maintain Demoralizing Shout against dangerous physical damage or a pack. Use Concussion Blow for a heal/cast, runner, add, or damage-control window. Rend is only for a long-lived bleedable target with spare rage; Heroic Strike/Cleave come after the full Shield Slam/Revenge/Shield Block/interrupt reserve.
3. Mark skull and line-of-sight/ranged-pull casters when Charge would overpull. Otherwise Charge, enter Defensive Stance, use Bloodrage only if healthy, Demoralizing Shout once, Shield Slam skull, Revenge, then tab-Sunder and revisit skull. Battle Shout with nearby party members can add pack threat, but is not a substitute for direct threat.
4. Taunt copies the current highest threat; immediately follow it with Shield Slam, Revenge, or Sunder. Mocking Blow and Challenging Shout force attacks temporarily but do not equalize threat, so follow-up threat is mandatory.
5. At three or four safe mobs, a Battle-Stance Thunder Clap opener can mitigate damage, followed by Defensive Stance and tab threat. Thunder Clap and Whirlwind are capped at four targets in this era. Always face attackers and never expose the back.
6. One point in Improved Shield Block is intentional: rank 1 supplies the important additional block, while ranks 2–3 mainly extend duration. Use Last Stand for an acute dip and Shield Wall for predicted lethal damage.

Low-level fallbacks: before Last Stand at 23 and Concussion Blow at 30, tank with Shield Block, Revenge, Demoralizing Shout, tab-Sunder, Taunt, and an excess-rage dump. Before Shield Slam at 40, Sunder/Revenge are the principal direct threat. Deep Protection can solo, but must use conservative pulls and consumable/food recovery rather than pretending to have a DPS capstone.

Known Vanilla limitations: deep Protection has the lowest solo kill speed; there is no Devastate, Shockwave, Spell Reflection, Vitality, or Focused Rage. Taunt-immune targets and resisted control require proactive threat. Weapon swaps consume time, and emulator proc/timing differences—especially Improved Shield Block—need target-core tests.

Sources and disagreement: [Icy Veins Protection leveling](https://www.icy-veins.com/wow-classic/protection-warrior-leveling-guide-1-60), [Icy Veins tank rotation](https://www.icy-veins.com/wow-classic/warrior-tank-pve-rotation-cooldowns-abilities), and [Wowhead tank rotation](https://www.wowhead.com/classic/guide/classes/warrior/tank-rotation-cooldowns-abilities-pve) support the tank loop. Arms/Fury can tank most leveling dungeons faster, and 11/5/35 is a common alternative for Cruelty 5. This 11/4/36 route deliberately retains Improved Revenge 3 and Improved Taunt 2 for autonomous five-player control.

## Paladin common combat contract

- Maintain the role-appropriate aura and one blessing per party member. Refresh five-minute blessings out of combat when practical, not during an urgent global cooldown.
- Judgement consumes the active seal in Vanilla. Every branch that judges must explicitly reseal, and must not spend the last emergency-mana reserve merely to keep a damage seal active.
- `Cleanse` has higher priority than damage for dangerous poison, disease, or magic effects. `Hammer of Justice` is the primary cast stop, but it is a stun rather than a true interrupt and cannot control stun-immune enemies.
- Use `Blessing of Protection` on a non-tank threatened by physical attackers. Do not cast it on the active tank or a physical-damage ally unless the behavior deliberately accepts loss of attacks and threat.
- `Divine Shield` is a self-save or planned debuff clear. A tank must not bubble normally because enemies will leave it; an emergency tank cleanse requires immediate cancel-aura behavior and still risks threat loss.
- Prefer `Flash of Light` for small/urgent healing and `Holy Light` for a predicted large deficit. Avoid overheal, select a learned down-rank when efficiency matters, and reserve mana for at least one emergency heal.
- Against demons and undead, `Exorcism` is a useful ranged pull/damage action and `Holy Wrath` is situational AoE. Neither may be assumed usable against other creature types.

### Paladin — Holy

Role and assumptions: primary five-player healer who can contribute conservative melee/Holy damage between heals. Use healing/intellect gear with a one-handed weapon and shield. Prefer Blessing of Wisdom on self and mana users, Blessing of Kings when its broad stats are more valuable, Concentration Aura under spell pushback, and Devotion Aura otherwise.

Level-60 allocation: **35 Holy / 11 Protection / 5 Retribution**.

- Holy: Divine Intellect 5, Spiritual Focus 5, Healing Light 3, Consecration 1, Improved Lay on Hands 2, Unyielding Faith 2, Illumination 5, Improved Blessing of Wisdom 2, Divine Favor 1, Lasting Judgement 3, Holy Power 5, Holy Shock 1.
- Protection: Improved Devotion Aura 5, Precision 3, Guardian's Favor 2, Blessing of Kings 1.
- Retribution: Improved Blessing of Might 5.

Purchase order:

- 10–14 Divine Intellect; 15–19 Spiritual Focus; 20 Consecration (spell 26573).
- 21–23 Healing Light; 24 Improved Lay on Hands rank 1; 25–29 Illumination; 30 Divine Favor (20216).
- 31–32 Improved Blessing of Wisdom; 33–34 Lasting Judgement ranks 1–2; 35–39 Holy Power; 40 Holy Shock (20473).
- 41 Improved Lay on Hands rank 2; 42–43 Unyielding Faith; 44 Lasting Judgement rank 3.
- 45–49 Improved Devotion Aura; 50–52 Precision; 53–54 Guardian's Favor; 55 Blessing of Kings (20217); 56–60 Improved Blessing of Might.

Combat priority and state rules:

1. Cleanse a dangerous dispellable effect; keep the tank alive; then heal other party members. Do not chase minor dispels while the tank is entering lethal range.
2. At critical health while moving, use Holy Shock as the instant heal. If stationary and a large heal is needed, combine Divine Favor with the best appropriate Holy Light: its forced critical heal works with Illumination. Save Lay on Hands for a genuine death-prevention event because it empties the paladin's mana and has a very long cooldown.
3. Use Flash of Light for a fast, modest deficit and Holy Light for a large/predicted deficit. Down-rank only when the smaller rank safely covers the deficit; do not let an efficiency rule delay an emergency maximum-rank heal.
4. When nobody needs healing, judge Wisdom on a long-lived target (Light if party healing pressure makes it better), then melee safely with Seal of Righteousness. Use Consecration only for at least two safe targets and normally above roughly 70% mana. Holy Shock damage is allowed only with surplus mana and no plausible near-term healing need.
5. Hammer of Justice stops a dangerous stunnable caster. Use Freedom for movement roots/slows and Blessing of Protection to save a non-tank from physical focus.

Low-level fallbacks: before Consecration at 20, use Seal/Judgement of Righteousness plus melee only when healing is stable. Before Divine Favor at 30 and Holy Shock at 40, the heal loop is simply predicted Holy Light versus fast Flash of Light, Cleanse, and Lay on Hands emergency handling.

Known Vanilla limitations: no Beacon of Light, no true AoE heal, Holy Shock has a 30-second cooldown and high mana cost, blessings last five minutes, and healer versus solo-damage gear differs sharply. Paladin is Alliance-only in 1.12.

Sources and disagreement: [Wowhead Holy PvE talent builds](https://www.wowhead.com/classic/guide/classes/paladin/healer-talent-builds-pve) presents the standard 35/11/5 endgame shell; [Icy Veins Holy leveling](https://www.icy-veins.com/wow-classic/holy-paladin-leveling-talent-build-from-1-to-60) and the [general Paladin leveling guide](https://www.icy-veins.com/wow-classic/classic-paladin-leveling-guide) favor more Retribution while solo. This matrix keeps the canonical Holy identity because the bot is explicitly a Holy healer, but buys its healing-critical talents before the utility branches.

### Paladin — Protection

Role and assumptions: five-player tank and sturdy multi-mob leveler. Require a one-handed weapon and shield; spell damage, intellect, stamina, and defense are useful. Maintain Righteous Fury. Default to Devotion Aura, using another resistance/utility aura for a known encounter; use Blessing of Sanctuary against repeated physical hits and Kings or Wisdom when Sanctuary is not paying back.

Level-60 allocation: **11 Holy / 31 Protection / 9 Retribution**.

- Holy: Divine Strength 5, Improved Seal of Righteousness 5, Consecration 1.
- Protection: Redoubt 5, Precision 3, Guardian's Favor 2, Toughness 5, Blessing of Kings 1, Improved Righteous Fury 3, Shield Specialization 3, Improved Hammer of Justice 2, Blessing of Sanctuary 1, One-Handed Weapon Specialization 5, Holy Shield 1.
- Retribution: Benediction 5, Improved Judgement 2, Deflection 2.

Purchase order:

- 10–14 Divine Strength; 15–19 Improved Seal of Righteousness; 20 Consecration (26573).
- 21–25 Redoubt; 26–28 Precision; 29–30 Toughness ranks 1–2; 31 Blessing of Kings (20217).
- 32–34 Improved Righteous Fury; 35–37 Shield Specialization; 38–40 finish Toughness; 41 Blessing of Sanctuary (20911).
- 42–43 Guardian's Favor; 44–45 Improved Hammer of Justice; 46–50 One-Handed Weapon Specialization; 51 Holy Shield (20925).
- 52–56 Benediction; 57–58 Improved Judgement; 59–60 Deflection.

Combat priority and state rules:

1. Before the pull, verify shield, Righteous Fury, aura, and blessing. Vanilla has no Avenger's Shield: pull with Judgement at short range, Exorcism on an eligible creature, line-of-sight/body pulling, or a coordinated party ranged pull.
2. Cast Holy Shield just before contact and refresh it when charges/cooldown permit. Establish a Consecration area only after mobs are positioned and crowd control is safe. Judge Righteousness for snap Holy threat, reseal, and tab targets so melee and seal threat is distributed.
3. On a long fight, judge Wisdom when the threat lead is safe; otherwise keep Judgement of Righteousness as the snap-threat tool. Use Hammer of Justice on a dangerous caster or an uncontrolled add. There is no taunt, so prevent threat loss rather than expecting to recover it instantly.
4. Suppress or down-rank Consecration on a single target, short pull, or low mana. Preserve enough mana for Holy Shield, an emergency heal/Cleanse, and the next pull. Do not spend mana merely because a cooldown is available.
5. Use Lay on Hands as the final tank save. Freedom handles immobilization; Blessing of Protection protects a non-tank. Never use ordinary Divine Shield or self-Blessing of Protection while owning enemies.
6. For AoE pulls, count Holy Shield's four charges and avoid assuming it covers an unlimited pack. Reposition enemies inside Consecration without exposing the back; do not break Polymorph, Sap, or similar control.

Low-level fallbacks: before Consecration at 20, tank with Righteous Fury, Seal/Judgement of Righteousness, tabbed melee, and Hammer of Justice. Before Sanctuary at 41 and Holy Shield at 51, rely on Redoubt/block, armor, Consecration, and distributed seal threat; never attempt the missing spell.

Known Vanilla limitations: no taunt, Avenger's Shield, or Spiritual Attunement; no reliable long-range pull against ordinary creatures; Holy threat and mana are fragile; Holy Shield has only four charges; and tank itemization is awkward. It is a capable dungeon tank but not equivalent to a Warrior for every raid encounter.

Sources and disagreement: [Icy Veins Protection builds](https://www.icy-veins.com/wow-classic/protection-paladin-tank-pve-spec-builds-talents) documents the canonical 11/31/9 tank shell; [Icy Veins Protection leveling](https://www.icy-veins.com/wow-classic/protection-paladin-leveling-talent-build-from-1-to-60) and [Wowhead's Light's Bulwark](https://www.wowhead.com/classic/guide/lights-bulwark-protection-paladin-tanking) explain dungeon/AoE play. Some leveling guides stop short of Holy Shield or use a Holy/Protection hybrid because Holy Shield is late and mana-hungry; this matrix takes it to preserve explicit Protection identity and five-player mitigation.

### Paladin — Retribution

Role and assumptions: slow two-handed-weapon melee DPS with strong emergency support. Prefer the highest-DPS slow two-hander. Use Blessing of Might for ordinary melee damage, Wisdom when mana limits uptime, and Sanctity Aura unless a defensive/resistance aura is materially safer.

Level-60 allocation: **11 Holy / 8 Protection / 32 Retribution**.

- Holy: Divine Strength 5, Spiritual Focus 5, Consecration 1.
- Protection: Improved Devotion Aura 5, Precision 3.
- Retribution: Benediction 5, Improved Judgement 2, Improved Seal of the Crusader 3, Deflection 4, Conviction 5, Seal of Command 1, Pursuit of Justice 2, Two-Handed Weapon Specialization 3, Sanctity Aura 1, Vengeance 5, Repentance 1.

Purchase order:

- 10–14 Benediction; 15–16 Improved Judgement; 17–19 Improved Seal of the Crusader; 20 Seal of Command (20375).
- 21–25 Conviction; 26–27 Pursuit of Justice; 28–30 Deflection ranks 1–3; 31 Sanctity Aura (20218).
- 32–34 Two-Handed Weapon Specialization; 35–39 Vengeance; 40 Repentance (20066).
- 41–45 Divine Strength; 46–50 Spiritual Focus; 51 Consecration (26573); 52 Deflection rank 4.
- 53–57 Improved Devotion Aura; 58–60 Precision.

Combat priority and state rules:

1. Before Seal of Command is learned, use Seal/Judgement of Righteousness. At 20+, keep Seal of Command active for normal two-handed combat.
2. On a long-lived elite/boss, judge Crusader once if the target will live long enough to repay the seal/global/mana setup, then reseal Command. On ordinary targets, stay with Command and judge only when its damage will not compromise the healing/utility reserve.
3. Hammer of Justice can set up Judgement of Command's bonus against a stunnable target, or it can stop a dangerous cast; control wins over damage. Reseal immediately after every Judgement. Never auto-target a unit held by Repentance, and suppress all cleave/AoE near it.
4. Use Consecration at two or more stable targets only when mana is healthy. Use Exorcism/Holy Wrath only on eligible creature types and Hammer of Wrath as an execute after it is trained (level 44), with threat awareness in a party.
5. Below roughly 35% mana, stop optional Consecration and offensive Judgements, continue auto-attacking, and reserve mana for Cleanse, Freedom, or a heal. Heal with Flash/Holy Light, bubble-heal only in a genuine emergency, and protect a threatened caster with Blessing of Protection.
6. Repentance is a Humanoid-only control/pull tool. Do not treat it as a general interrupt or assume it works on beasts, undead, demons, elementals, or bosses.

Low-level fallbacks: Seal/Judgement of Righteousness and auto-attacks before Seal of Command at 20; no controlled pull from Repentance before 40; no Consecration until 51 in this exact path. The bot should still Cleanse, stun, heal, and use eligible trainer spells at their learned levels.

Known Vanilla limitations: no Crusader Strike, Command damage is random and weapon-dependent, there is no true interrupt, Judgement forces resealing, and sustained damage falls when mana is spent on support. Repentance affects only Humanoids and breaks on damage.

Sources and disagreement: [Wowhead Retribution PvE builds](https://www.wowhead.com/classic/guide/classes/paladin/dps-talent-builds-pve) gives the standard 11/8/32 structure; [Icy Veins Retribution leveling](https://www.icy-veins.com/wow-classic/retribution-paladin-leveling-talent-build-from-1-to-60) and [Warcraft Tavern's Retribution guide](https://www.warcrafttavern.com/wow-classic/guides/pve-retribution-paladin/) differ on early utility and raid debuff responsibilities. The order here reaches Command, Sanctity, Vengeance, and Repentance on their earliest legal milestone while retaining the canonical level-60 allocation.

## Shaman common combat contract

- Shaman is Horde-only in 1.12; profile assignment must enforce the character's available class/faction combination.
- Use a weapon imbue appropriate to role and threat. Never overwrite a useful temporary enchant accidentally. Enhancement uses Windfury Weapon after it is trained; a party damage dealer avoids Rockbiter when another unit tanks.
- Totems are local, stationary, mana-costing objects. Drop only the smallest encounter-relevant set, refresh after movement, and do not blindly use all four elements on every pull. Recall Totems does not exist in 1.12, so abandoned totems can pull respawns.
- Use Tremor proactively for expected fear/sleep/charm because it pulses rather than instantly dispelling on demand. Prefer Grounding against a dangerous hostile spell and poison/disease cleansing totems when repeated party effects justify them.
- Rank 1 Earth Shock is the default interrupt: it gets the school lockout without paying maximum-rank damage mana. A damage shock cannot consume the cooldown when a dangerous interrupt is expected.
- Purge high-impact enemy magic rather than every removable buff. Preserve enough mana for an emergency heal and, in a group, for required utility totems/interrupts.
- Choose one Air totem deliberately. Windfury Totem serves a physical melee group; Grace of Air may be preferable for agility users. Do not assume both effects coexist.

### Shaman — Elemental

Role and assumptions: ranged spell DPS with interrupts, purge, totem utility, and emergency off-healing. Prefer caster stats and either a one-handed weapon plus shield or a caster staff. Keep Lightning Shield active; use Mana Spring or Healing Stream according to group pressure.

Level-60 allocation: **31 Elemental / 0 Enhancement / 20 Restoration**.

- Elemental: Convection 5, Call of Flame 3, Concussion 5, Elemental Focus 1, Call of Thunder 5, Eye of the Storm 3, Elemental Fury 1, Storm Reach 2, Lightning Mastery 5, Elemental Mastery 1.
- Restoration: Improved Healing Wave 5, Improved Reincarnation 2, Totemic Focus 4, Nature's Guidance 3, Totemic Mastery 1, Tidal Mastery 5.

Purchase order:

- 10–14 Convection; 15–17 Call of Flame; 18–19 Concussion ranks 1–2; 20 Elemental Focus.
- 21–25 Call of Thunder; 26–28 Eye of the Storm; 29 Concussion rank 3; 30 Elemental Fury.
- 31–32 Storm Reach; 33–34 finish Concussion; 35–39 Lightning Mastery; 40 Elemental Mastery (16166).
- 41–45 Improved Healing Wave; 46–47 Improved Reincarnation; 48–50 Totemic Focus ranks 1–3.
- 51–53 Nature's Guidance; 54 Totemic Mastery; 55 Totemic Focus rank 4; 56–60 Tidal Mastery.

Combat priority and state rules:

1. For a normal solo pull, begin at maximum Lightning Bolt range, place Searing Totem only if the target will live long enough, and use Earthbind/kiting space before the enemy arrives. Flame Shock is worthwhile only if most of its duration will tick; Frost Shock handles a runner or creates kite distance.
2. Lightning Bolt is the single-target filler. Consume Elemental Focus/Clearcasting on the most useful expensive cast—Chain Lightning when two or three safe targets exist, otherwise the appropriate high-rank Lightning Bolt—without delaying an urgent interrupt or heal.
3. Use Elemental Mastery with Chain Lightning for two or three safe targets, otherwise Lightning Bolt or a lethal Earth Shock. Do not spend the guaranteed critical effect on a rank-1 interrupt.
4. Chain Lightning is the controlled two-to-three-target action. Magma/Fire Nova Totem is allowed only after the tank owns a stable, CC-safe pack. Do not use costly AoE on transient targets.
5. Reserve the shock cooldown for rank-1 Earth Shock when a dangerous cast is pending. Purge a high-value magic buff, and select Grounding, Tremor, Earthbind, or cleansing totems from encounter state rather than a fixed damage script.
6. Below roughly 35% mana, stop optional shocks, Chain Lightning, and fire totems; Lightning Bolt conservatively or melee with a safe imbue while allowing five-second-rule regeneration. Keep roughly 20–25% available for a heal/interrupt escape. Use Lesser Healing Wave for an urgent save and Improved Healing Wave for a predictable larger heal.

Low-level fallbacks: before Chain Lightning is trained at level 32, use Lightning Bolt, a duration-efficient shock, and melee/kiting. Before Elemental Mastery at 40, follow the same loop without a burst cooldown. Missing fire/totem ranks are simply omitted.

Known Vanilla limitations: severe mana pressure, cast pushback until Eye of the Storm procs, totems that become useless after movement, no Bloodlust/Heroism, and no modern instant-cast proc loop. Chain Lightning can break crowd control and Earth Shock damage competes with its interrupt cooldown.

Sources and disagreement: the [Icy Veins Elemental leveling guide](https://www.icy-veins.com/wow-classic/elemental-shaman-leveling-guide-1-60) supplies this legal 1–60 purchase order; [Wowhead Elemental leveling](https://www.wowhead.com/classic/guide/classes/shaman/elemental/leveling-tips) and [Elemental PvE talents](https://www.wowhead.com/classic/guide/classes/shaman/elemental/dps-talent-builds-pve) support the play pattern. Many guides recommend leveling Enhancement until about 40 and then respeccing Elemental, while a 30/0/21 Nature's Swiftness hybrid trades the Elemental capstone for flexibility. This immutable-spec matrix stays 31/0/20 so an Elemental bot develops and behaves as Elemental from level 10.

### Shaman — Enhancement

Role and assumptions: melee DPS/off-support with strong solo uptime. Use a one-handed weapon and shield through level 19, then the best slow two-handed axe or mace once the talent is purchased at 20. Use Rockbiter before Windfury Weapon is trained at 30; use Windfury thereafter unless threat requires Flametongue. Dual wield does not exist for Shaman in Vanilla.

Level-60 allocation: **0 Elemental / 31 Enhancement / 20 Restoration**.

- Enhancement: Shield Specialization 5, Thundering Strikes 5, Two-Handed Axes and Maces 1, Improved Lightning Shield 3, Anticipation 2, Flurry 5, Parry 1, Elemental Weapons 3, Weapon Mastery 5, Stormstrike 1.
- Restoration: Improved Healing Wave 5, Improved Reincarnation 2, Totemic Focus 3, Nature's Guidance 3, Totemic Mastery 1, Healing Focus 5, Healing Grace 1.

Purchase order:

- 10–14 Shield Specialization; 15–19 Thundering Strikes; 20 Two-Handed Axes and Maces.
- 21–23 Improved Lightning Shield; 24 Anticipation rank 1; 25–29 Flurry; 30 Parry.
- 31–33 Elemental Weapons; 34 Anticipation rank 2; 35–39 Weapon Mastery; 40 Stormstrike (17364).
- 41–45 Improved Healing Wave; 46–47 Improved Reincarnation; 48–50 Totemic Focus.
- 51–53 Nature's Guidance; 54 Totemic Mastery; 55–59 Healing Focus; 60 Healing Grace rank 1.

Combat priority and state rules:

1. Keep Lightning Shield and the correct weapon imbue active. A safe long pull may begin with Lightning Bolt; otherwise close without delaying melee. Drop Strength of Earth plus the chosen Air totem for a meaningful fight, not every trivial mob.
2. After level 40, use Stormstrike when in melee and then consume its Nature-damage vulnerability with Earth Shock if the shock is not reserved for an interrupt. Remember that Lightning Shield and other Nature damage can consume the two charges; do not assume both belong to the shaman's next attack.
3. Auto-attacks drive the build. Preserve contact, do not introduce movement that resets/delays the swing, and benefit from Flurry after a melee critical. Flame Shock is allowed only when its duration repays the mana; damage shocks are finishers or healthy-mana actions, not mandatory cooldown spam.
4. In a party, avoid Rockbiter when another unit tanks. Use Windfury Totem for a suitable melee group, Strength of Earth, and Mana Spring/Healing Stream as needed. Purge important buffs and keep rank-1 Earth Shock available for a dangerous cast.
5. Chain Lightning and Magma/Fire Nova Totem are optional AoE after stable tank threat and a crowd-control safety check. They are suppressed first when mana falls.
6. Heal an emergency with Lesser Healing Wave or a predicted deficit with Healing Wave. Use Stoneclaw to peel, Earthbind plus Ghost Wolf to escape, and Grounding/Tremor/cleansing totems for the encounter. Below roughly 30–35% mana, auto-attack and stop optional shocks/totems so the bot retains an interrupt and heal reserve.

Low-level fallbacks: use one-handed weapon plus shield and Rockbiter through 19, then switch to a slow two-hander if available. Windfury Weapon is a trainer ability at 30, not granted by talents. Before Stormstrike at 40, the loop is Lightning Shield, auto-attacks, duration-efficient Flame Shock, interrupt/finisher Earth Shock, and selective totems.

Known Vanilla limitations: no dual wield, Maelstrom Weapon, Feral Spirit, or Bloodlust/Heroism. Windfury is random and can spike threat; Stormstrike has a long cooldown and only two debuff charges; mana disappears quickly if shocks and totems are spammed. A shield must remain a supported fallback when survivability matters.

Sources and disagreement: [Wowhead Enhancement PvE talents](https://www.wowhead.com/classic/guide/classes/shaman/enhancement/dps-talent-builds-pve) identifies 0/31/20 as the mainstream Vanilla shell; [Icy Veins Enhancement leveling](https://www.icy-veins.com/wow-classic/enhancement-shaman-leveling-guide-1-60) supports the early shield, two-hander, Flurry, and weapon progression; [Wowhead Enhancement leveling](https://www.wowhead.com/classic/guide/classes/shaman/enhancement/leveling-tips) gives the broader rotation context. Guides disagree over early Ancestral Knowledge versus Shield Specialization/Anticipation and over how far to continue Enhancement after Stormstrike. This matrix favors mitigation and a full emergency-healing Restoration branch for autonomous and five-player use.

### Shaman — Restoration

Role and assumptions: primary five-player healer with dispel, interrupt, and encounter-specific totem duties. Use a one-handed healing/MP5/intellect weapon and shield. Keep enough distance to cast while remaining inside the party's useful totem radius.

Level-60 allocation: **0 Elemental / 5 Enhancement / 46 Restoration**.

- Enhancement: Ancestral Knowledge 5.
- Restoration: Improved Healing Wave 5, Tidal Focus 5, Ancestral Healing 3, Totemic Focus 5, Nature's Guidance 3, Healing Focus 5, Totemic Mastery 1, Healing Grace 3, Restorative Totems 5, Tidal Mastery 1, Healing Way 3, Nature's Swiftness 1, Purification 5, Mana Tide Totem 1.

Purchase order:

- 10–14 Improved Healing Wave; 15–19 Tidal Focus; 20–22 Ancestral Healing; 23–24 Totemic Focus ranks 1–2.
- 25–29 Restorative Totems; 30 Nature's Swiftness (16188); 31–33 Nature's Guidance; 34 Totemic Mastery.
- 35–39 Purification; 40 Mana Tide Totem (16190); 41–43 finish Totemic Focus; 44–48 Healing Focus.
- 49–51 Healing Grace; 52–54 Healing Way; 55 Tidal Mastery rank 1; 56–60 Ancestral Knowledge.

Combat priority and state rules:

1. Anticipate fear with Tremor and repeated poison/disease with the appropriate cleansing totem. Rank-1 Earth Shock a dangerous cast when healing permits; cleanse a lethal effect before routine topping, while respecting the tank's incoming damage.
2. Use Lesser Healing Wave for an urgent small/medium save and Healing Wave for a predicted larger deficit. Nature's Swiftness plus maximum appropriate Healing Wave is the instant critical save; do not consume Nature's Swiftness on damage while any party member is in danger.
3. Use Chain Heal only when at least two injured allies are in valid bounce range. Rank 1 can be an efficient light group heal; a high rank is for real multi-target pressure. Never spam it into one injured target merely because it is available.
4. Maintain Healing Way through Healing Wave on a tank during sustained pressure and exploit Ancestral Healing after a critical heal, but do not manufacture stacks through wasteful overheal. Choose a down-rank that safely covers the deficit and abandon efficiency immediately if lethal damage is imminent.
5. Use Mana Tide early enough to complete its pulses when several mana users are below roughly 60% and the fight will continue. It occupies the Water slot: restore Healing Stream, Mana Spring, or a cleansing totem afterward if still required, and protect Mana Tide from avoidable destruction.
6. During safe solo play, use Lightning Shield, Rockbiter/Flametongue, one or two Lightning Bolts, a duration-efficient Flame Shock, then melee. Earth Shock remains reserved for casts or a lethal finisher. Preserve most mana for healing and escape rather than trying to emulate Elemental DPS.

Low-level fallbacks: before Lesser Healing Wave at 20, Healing Wave is the direct heal. Before Chain Heal at 40, heal targets individually. Nature's Swiftness arrives at 30 and Mana Tide at 40 in this path; prior levels must not attempt them.

Known Vanilla limitations: no heal-over-time spell, Earth Shield, Riptide, or threat-drop; Chain Heal is unavailable until level 40; totem cleanses and Tremor work on pulses; and sustained healing is mana-sensitive. Reincarnation requires an ankh unless a reagent-removal effect applies, and must not be treated as ordinary in-combat resurrection.

Sources and disagreement: the long-form [Vanilla Shaman guide](https://github.com/agentmerlin/vanilla-shaman-guide) documents the canonical 0/5/46 Restoration allocation; [Icy Veins Restoration leveling](https://www.icy-veins.com/wow-classic/restoration-shaman-leveling-guide-1-60) and [Restoration rotation](https://www.icy-veins.com/wow-classic/restoration-shaman-healer-pve-rotation-cooldowns-abilities) cover the leveling/heal policy. Many guides advise leveling Enhancement or Elemental and healing dungeons off-spec because a deep-Restoration solo character kills slowly. This matrix deliberately remains Restoration and front-loads healing talents because spec fidelity and five-player competence are requirements.

## Key spell identifiers

These are build-5875 rank-1 or sole-spell identifiers checked against the installed 173-field `Spell.dbc` where marked as talent spells. Trainer spell IDs are provided as stable entry points; resolve later ranks through the core's spell-chain data.

| Class | Spell | ID | Policy relevance |
|---|---|---:|---|
| Warrior | Battle Stance | 2457 | required for Charge/Rend/Overpower |
| Warrior | Defensive Stance | 71 | tank stance |
| Warrior | Berserker Stance | 2458 | Fury DPS/Pummel stance |
| Warrior | Heroic Strike | 78 | next-swing excess-rage dump |
| Warrior | Charge | 100 | opener; unsafe through tank/packs |
| Warrior | Rend | 772 | long-lived bleedable target only |
| Warrior | Sunder Armor | 7386 | tank threat/long-target armor reduction |
| Warrior | Taunt | 355 | copies highest threat; follow immediately |
| Warrior | Overpower | 7384 | dodge reaction in Battle Stance |
| Warrior | Shield Bash | 72 | shield interrupt |
| Warrior | Revenge | 6572 | reactive tank threat |
| Warrior | Shield Block | 2565 | mitigation and Revenge enabler |
| Warrior | Execute | 5308 | lethal/committed execute phase |
| Warrior | Shield Wall | 871 | predicted-lethal defensive |
| Warrior | Whirlwind | 1680 | CC-safe cleave, four-target cap |
| Warrior | Pummel | 6552 | Berserker-Stance interrupt |
| Warrior | Sweeping Strikes | 12292 | verified Vanilla talent mapping |
| Warrior | Mortal Strike | 12294 | rank-1 talent spell |
| Warrior | Piercing Howl | 12323 | talent AoE slow/runner control |
| Warrior | Death Wish | 12328 | talent burst with defense penalty |
| Warrior | Bloodthirst | 23881 | rank-1 talent attack |
| Warrior | Last Stand | 12975 | talent emergency health increase |
| Warrior | Concussion Blow | 12809 | talent stun/cast stop |
| Warrior | Shield Slam | 23922 | rank-1 talent threat/dispelling attack |
| Paladin | Holy Light | 635 | large direct heal |
| Paladin | Flash of Light | 19750 | fast efficient heal |
| Paladin | Cleanse | 4987 | poison/disease/magic removal |
| Paladin | Judgement | 20271 | consumes current seal |
| Paladin | Righteous Fury | 25780 | Protection threat stance |
| Paladin | Hammer of Justice | 853 | stun/cast stop, not true interrupt |
| Paladin | Divine Shield | 642 | self immunity; tank threat warning |
| Paladin | Blessing of Protection | 1022 | physical immunity; attack/threat warning |
| Paladin | Lay on Hands | 633 | emergency heal, drains mana |
| Paladin | Consecration | 26573 | verified talent spell |
| Paladin | Divine Favor | 20216 | verified talent spell |
| Paladin | Holy Shock | 20473 | verified talent spell |
| Paladin | Blessing of Kings | 20217 | verified talent spell |
| Paladin | Blessing of Sanctuary | 20911 | verified talent spell |
| Paladin | Holy Shield | 20925 | verified talent spell |
| Paladin | Seal of Command | 20375 | verified talent spell |
| Paladin | Sanctity Aura | 20218 | verified talent spell |
| Paladin | Repentance | 20066 | verified talent spell |
| Shaman | Lightning Bolt | 403 | ranged filler/pull |
| Shaman | Earth Shock | 8042 | damage and rank-1 interrupt |
| Shaman | Chain Lightning | 421 | controlled cleave |
| Shaman | Healing Wave | 331 | efficient/predicted heal |
| Shaman | Lesser Healing Wave | 8004 | urgent heal |
| Shaman | Chain Heal | 1064 | multi-target heal |
| Shaman | Windfury Weapon | 8232 | Enhancement self imbue |
| Shaman | Ghost Wolf | 2645 | travel/escape |
| Shaman | Purge | 370 | enemy magic removal |
| Shaman | Grounding Totem | 8177 | hostile spell interception |
| Shaman | Tremor Totem | 8143 | pulsing fear/sleep/charm response |
| Shaman | Reincarnation | 20608 | reagent/cooldown-gated self resurrection |
| Shaman | Elemental Mastery | 16166 | verified talent spell |
| Shaman | Stormstrike | 17364 | verified rank-1 talent spell |
| Shaman | Nature's Swiftness | 16188 | verified talent spell |
| Shaman | Mana Tide Totem | 16190 | verified rank-1 talent spell |

## Implementation notes for the later core phase

- Store a stable spec identifier separately from the purchased talents. Detecting spec only from the deepest tree becomes ambiguous for partially leveled bots and for level-60 hybrids.
- Talent spending should be idempotent, run at creation/level-up/login, validate prerequisites before each purchase, and log the desired talent, rank, and failure reason. Never silently award the spell without the talent record.
- Rotation code should be a decision system over known spells, cooldowns, resource reserve, role, party threat, target type, target count, crowd-control map, and predicted time-to-live—not a fixed cast list.
- Add telemetry before tuning thresholds: attempted/cast spell, suppress reason, mana/rage before and after, threat owner, interrupt opportunity/result, overheal, time without melee/ranged contact, and deaths with unused defensives.
- Talent visibility in SuperUI should show class/spec, points by tree, each talent's current/max rank, next planned purchase and level, and validation/error state. The UI should read authoritative core state rather than infer purchases from known talent spells.
