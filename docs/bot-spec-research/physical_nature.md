# Bot talent and rotation matrix: Hunter, Rogue, Druid

Status: research baseline, validated against the live build-5875 DBC on 2026-08-25. This file does not change runtime behavior.

## Contract used by this matrix

- Target: autonomous open-world leveling plus five-player PvE, with useful solo survivability and party utility. These are not raid-only or PvP-only builds.
- Each purchase range is inclusive and supplies exactly one point per level from 10 through 60.
- The listed level-60 distribution is a primary-tree identity, not a claim that every effective Vanilla build must take the 31-point capstone.
- Every sequence below was simulated, one purchase at a time, against the installed Linux Talent.dbc: 432 records, 21 fields, 84-byte records. All nine finish at 51 points with zero tier, maximum-rank, or prerequisite errors.
- Trained spell ranks should be resolved through CombatBotBaseAI's highest-known-rank helpers. Active talent spell IDs are called out where they change the rotation.
- Gear assumptions are part of the spec contract. A rotation must fall back safely if the preferred weapon, pet, or form is unavailable.

## Hunter common combat contract

- Keep the best learned Aspect of the Hawk active for combat; use Cheetah only for travel and cancel it before expected incoming damage.
- Ensure a living, fed pet. Enable Growl while solo; disable it when a player or bot tank owns threat. Pet attack precedes hunter burst so the pet can establish threat.
- Apply Hunter's Mark when the target is expected to live long enough to repay the global. Apply Serpent Sting only to targets that will live for most of its duration and when mana is healthy.
- Never clip Auto Shot. Aimed Shot and Multi-Shot are scheduled immediately after an Auto Shot. Arcane Shot is a movement/low-level fallback and is suppressed once Aimed Shot is learned unless its shared cooldown and mana policy make it the only usable shot.
- Multi-Shot is allowed on one target when mana is high and on two or three targets only when it will not break crowd control. Volley is reserved for at least three stable targets.
- If the hunter gains melee aggro: command Growl/Intimidation where available, use Concussive Shot before the target enters the dead zone, Wing Clip in melee, create range, and Feign Death when another unit can retain the target. Raptor Strike and Mongoose Bite are fallbacks, not the intended sustained rotation.
- Rapid Fire is used only when the hunter can remain stationary. Mend Pet is used between pulls or during low incoming pressure; revive logic takes precedence over damage when the pet is essential to the current role.

### Hunter — Beast Mastery

Role and assumptions: pet-centered ranged DPS and strongest solo hunter. Prefer a durable threat pet with Growl and a focus dump; an owl/carrion bird with Screech is ideal when available.

Level-60 allocation: 31 Beast Mastery / 20 Marksmanship / 0 Survival.

- Beast Mastery: Improved Aspect of the Hawk 5, Thick Hide 3, Improved Revive Pet 2, Pathfinding 2, Bestial Swiftness 1, Unleashed Fury 5, Ferocity 5, Intimidation 1, Spirit Bond 1, Frenzy 5, Bestial Wrath 1.
- Marksmanship: Efficiency 5, Lethal Shots 5, Aimed Shot 1, Hawk Eye 3, Improved Concussive Shot 1, Mortal Shots 5.

Purchase order:

- 10–14 Improved Aspect of the Hawk; 15–17 Thick Hide; 18–19 Improved Revive Pet.
- 20–21 Pathfinding; 22 Bestial Swiftness; 23–27 Unleashed Fury; 28–29 Ferocity.
- 30 Intimidation (spell 19577); 31–33 finish Ferocity; 34 Spirit Bond; 35–39 Frenzy; 40 Bestial Wrath (19574).
- 41–45 Efficiency; 46–50 Lethal Shots; 51 Aimed Shot (19434); 52–54 Hawk Eye; 55 Improved Concussive Shot; 56–60 Mortal Shots.

Spec priority additions:

1. Intimidation to stop a dangerous cast, peel the hunter/healer, or immediately reinforce pet threat; do not waste it on an already controlled target.
2. Bestial Wrath when the pet is alive, in melee, and the target will survive the burst window.
3. Aimed Shot after Auto Shot when threat is safe; Multi-Shot under the common AoE policy; Arcane Shot only as fallback.
4. Mend/cleanse pet sooner than other specs because losing the pet removes most of the build's advantage.

Sources: [Wowhead BM leveling](https://www.wowhead.com/classic/guide/classes/hunter/beast-mastery/leveling-tips), [Icy Veins BM leveling](https://www.icy-veins.com/wow-classic/beast-mastery-hunter-leveling-guide-1-60), [Bouk's Vanilla hunter leveling guide](https://boukx.github.io/guide/leveling).

### Hunter — Marksmanship

Role and assumptions: party-oriented ranged DPS. Prefer the highest-DPS bow or gun and a pet chosen for reliable uptime rather than tanking the entire fight.

Level-60 allocation: 20 Beast Mastery / 31 Marksmanship / 0 Survival.

- Marksmanship: Efficiency 5, Lethal Shots 5, Aimed Shot 1, Hawk Eye 3, Improved Hunter's Mark 2, Mortal Shots 5, Scatter Shot 1, Barrage 3, Ranged Weapon Specialization 5, Trueshot Aura 1.
- Beast Mastery: Improved Aspect of the Hawk 5, Improved Revive Pet 2, Thick Hide 3, Bestial Swiftness 1, Unleashed Fury 5, Ferocity 4.

Purchase order:

- 10–14 Efficiency; 15–19 Lethal Shots; 20 Aimed Shot; 21–23 Hawk Eye; 24 Improved Hunter's Mark.
- 25–29 Mortal Shots; 30 Scatter Shot (19503); 31–33 Barrage; 34 Improved Hunter's Mark; 35–39 Ranged Weapon Specialization; 40 Trueshot Aura (19506).
- 41–45 Improved Aspect of the Hawk; 46–47 Improved Revive Pet; 48–50 Thick Hide; 51 Bestial Swiftness; 52–56 Unleashed Fury; 57–60 Ferocity.

Spec priority additions:

1. Maintain Trueshot Aura whenever combat is expected.
2. Scatter Shot interrupts dangerous casts, peels targets inside the dead zone, or provides a short control window; avoid immediately breaking it with a DoT or Multi-Shot.
3. Aimed Shot is the main active shot and must be threat-aware. Multi-Shot receives elevated priority because Barrage improves it.
4. Feign Death proactively after a burst that overtakes pet/tank threat rather than waiting for critical health.

Sources: [Wowhead MM leveling](https://www.wowhead.com/classic/guide/classes/hunter/marksmanship/leveling-tips), [Icy Veins MM leveling](https://www.icy-veins.com/wow-classic/marksmanship-hunter-leveling-guide-1-60), [Wowhead Hunter PvE builds](https://www.wowhead.com/classic/guide/classes/hunter/dps-talent-builds-pve).

### Hunter — Survival

Role and assumptions: ranged utility/control DPS with stronger personal defenses. It is intentionally ranged-first even though the tree contains melee talents.

Level-60 allocation: 0 Beast Mastery / 20 Marksmanship / 31 Survival.

- Survival: Monster Slaying 3, Humanoid Slaying 3, Deflection 5, Clever Traps 2, Survivalist 5, Deterrence 1, Surefooted 3, Killer Instinct 3, Lightning Reflexes 5, Wyvern Sting 1.
- Marksmanship: Efficiency 5, Lethal Shots 5, Aimed Shot 1, Hawk Eye 3, Improved Hunter's Mark 1, Mortal Shots 5.

Purchase order:

- 10–12 Monster Slaying; 13–15 Humanoid Slaying; 16–20 Deflection.
- 21–22 Clever Traps; 23–27 Survivalist; 28 Deterrence (19263); 29–31 Surefooted.
- 32–34 Killer Instinct; 35–39 Lightning Reflexes; 40 Wyvern Sting (19386).
- 41–45 Efficiency; 46–50 Lethal Shots; 51 Aimed Shot; 52–54 Hawk Eye; 55 Improved Hunter's Mark; 56–60 Mortal Shots.

Spec priority additions:

1. Wyvern Sting is long control for an un-dotted secondary target or an emergency cast stop. Its follow-up damage-over-time effect means it must not be assigned to a target expected to remain crowd controlled indefinitely.
2. Deterrence is used against dangerous melee pressure or while buying time for pet/tank threat recovery.
3. Frost/Freezing Trap logic is preferred for planned control; Immolation/Explosive Trap is used only when placement is safe and crowd control will not be broken.
4. Aimed Shot remains the sustained ranged attack. The AI should not choose melee merely because Survival has melee support talents.

Sources: [Icy Veins Survival leveling](https://www.icy-veins.com/wow-classic/survival-hunter-leveling-guide-1-60), [Wowhead Hunter leveling comparison](https://www.wowhead.com/classic/guide/classes/hunter/leveling-tips), [Wowhead Hunter PvE builds](https://www.wowhead.com/classic/guide/classes/hunter/dps-talent-builds-pve).

Survival caveat: deep Survival is materially weaker for ordinary leveling than BM/MM. This profile deliberately preserves the tree's identity and converts its extra control and defenses into reliable bot behavior rather than pretending it is equal raw DPS.

## Rogue common combat contract

- The main-hand weapon drives builders; the off-hand should be faster for poison application when item choice permits. Maintain appropriate weapon skills.
- Kick dangerous casts immediately. Gouge or Blind is a secondary cast stop/peel only when its damage-breaking behavior is safe.
- Use Evasion against multiple melee attackers or dangerous physical pressure; Vanish for an unrecoverable pull or party threat reset; Sprint to close, escape, or reposition.
- In parties, use Feint when threat approaches the tank. Do not spend combo points on a target that is about to die unless the finisher will land before death.
- Slice and Dice is favored for bosses, durable targets, and chain pulls. Eviscerate is favored for short open-world targets. Expose Armor is only used under an explicit party policy so it does not overwrite stronger armor debuffs.
- Instant Poison is the general short-fight choice. Crippling belongs on targets that must be controlled; Deadly is reserved for long targets where its DoT will not break control.

### Rogue — Assassination

Role and assumptions: crit/poison dagger DPS. Prefer a slow main-hand dagger and fast off-hand; in solo combat the AI must tolerate losing rear access.

Level-60 allocation: 31 Assassination / 8 Combat / 12 Subtlety.

- Assassination: Improved Eviscerate 3, Ruthlessness 2, Malice 5, Relentless Strikes 1, Lethality 5, Vile Poisons 5, Improved Poisons 3, Cold Blood 1, Seal Fate 5, Vigor 1.
- Combat: Improved Sinister Strike 2, Improved Gouge 3, Improved Backstab 3.
- Subtlety: Opportunity 5, Camouflage 5, Elusiveness 2.

Purchase order:

- 10–14 Malice; 15–17 Improved Eviscerate; 18–19 Ruthlessness; 20 Relentless Strikes; 21–25 Lethality.
- 26–29 Vile Poisons; 30 Cold Blood (14177); 31 finish Vile Poisons; 32–34 Improved Poisons; 35–39 Seal Fate; 40 Vigor (14983).
- 41–42 Improved Sinister Strike; 43–45 Improved Gouge; 46–48 Improved Backstab.
- 49–53 Opportunity; 54–58 Camouflage; 59–60 Elusiveness.

Spec priority additions:

1. From Stealth, Ambush when a dagger and rear arc are available; otherwise use Cheap Shot for control or Garrote only when the target will live and crowd control is not planned.
2. Backstab is the preferred builder from behind; Sinister Strike is the solo/front fallback.
3. Cold Blood is paired with a high-combo Eviscerate. Seal Fate-generated points are observed before choosing the next builder.
4. Slice and Dice at two to five points for long targets; five-point Eviscerate for short targets. Rupture is optional only on long, non-controlled, high-armor targets.

Sources: [Icy Veins general Rogue leveling](https://www.icy-veins.com/wow-classic/classic-rogue-leveling-guide), [Icy Veins dagger leveling](https://www.icy-veins.com/wow-classic/dagger-rogue-leveling-talent-build-from-1-to-60), [Wowhead Rogue talent data](https://www.wowhead.com/classic/talent-calc/rogue).

Assassination caveat: Vanilla has no Mutilate. Deep Seal Fate/Vigor leveling is not the community default, so this is a DBC-valid bot synthesis emphasizing the actual 1.12 Assassination mechanics rather than importing TBC advice.

### Rogue — Combat

Role and assumptions: durable, weapon-flexible melee DPS. Prefer a slow main hand; this baseline avoids a weapon specialization so random gear upgrades cannot silently invalidate four or five points.

Level-60 allocation: 19 Assassination / 32 Combat / 0 Subtlety.

- Assassination: Ruthlessness 3, Malice 5, Murder 2, Improved Slice and Dice 3, Relentless Strikes 1, Lethality 5.
- Combat: Improved Gouge 3, Improved Sinister Strike 2, Deflection 5, Precision 5, Endurance 2, Riposte 1, Improved Sprint 2, Dual Wield Specialization 5, Blade Flurry 1, Weapon Expertise 2, Aggression 3, Adrenaline Rush 1.

Purchase order:

- 10–11 Improved Sinister Strike; 12–14 Improved Gouge; 15–19 Deflection; 20 Riposte (14251).
- 21–22 Endurance; 23–24 Improved Sprint; 25–29 Precision; 30 Blade Flurry (13877); 31–35 Dual Wield Specialization.
- 36–38 Aggression; 39–40 Weapon Expertise; 41 Adrenaline Rush (13750).
- 42–46 Malice; 47–49 Improved Slice and Dice; 50–51 Murder; 52 Relentless Strikes; 53–57 Lethality; 58–60 Ruthlessness.

Spec priority additions:

1. Riposte immediately after a valid parry proc unless an urgent Kick is required.
2. Sinister Strike is the standard builder. Keep Slice and Dice on durable targets and finish with Eviscerate.
3. Blade Flurry is used on two or more safe targets, or as single-target burst when the encounter justifies the cooldown. Pair Adrenaline Rush with Blade Flurry on multi-target pulls when possible, without waiting so long that either cooldown is wasted.
4. A sword-pinned gear profile may use a later variant that trades one Weapon Expertise rank plus Ruthlessness 3 for Sword Specialization 4. That variant must not be enabled until gear scoring guarantees swords.

Sources: [Icy Veins sword Rogue leveling](https://www.icy-veins.com/wow-classic/sword-rogue-leveling-talent-build-from-1-to-60), [Wowhead Combat Swords leveling](https://www.wowhead.com/classic/guide/wow-classic-combat-swords-rogue-leveling-talent-build-1-60), [Icy Veins Rogue leveling overview](https://www.icy-veins.com/wow-classic/classic-rogue-leveling-guide).

### Rogue — Subtlety

Role and assumptions: stealth/control melee DPS using Hemorrhage. A dagger is preferred for Ambush, but Hemorrhage itself remains weapon-flexible.

Level-60 allocation: 21 Assassination / 0 Combat / 30 Subtlety.

- Assassination: Improved Eviscerate 3, Remorseless Attacks 2, Malice 5, Murder 2, Relentless Strikes 1, Lethality 5, Vile Poisons 2, Cold Blood 1.
- Subtlety: Opportunity 5, Elusiveness 1, Camouflage 5, Ghostly Strike 1, Improved Ambush 3, Improved Sap 3, Serrated Blades 3, Heightened Senses 2, Preparation 1, Hemorrhage 1, Deadliness 5.

Purchase order:

- 10–14 Opportunity; 15–19 Camouflage; 20 Ghostly Strike (14278); 21–23 Improved Ambush; 24 Elusiveness.
- 25–27 Serrated Blades; 28–29 Improved Sap; 30 Hemorrhage (16511); 31 Preparation (14185); 32 finish Improved Sap; 33–34 Heightened Senses; 35–39 Deadliness.
- 40–41 Remorseless Attacks; 42–46 Malice; 47–48 Murder; 49 Improved Eviscerate; 50 Relentless Strikes; 51–52 finish Improved Eviscerate; 53–57 Lethality; 58–59 Vile Poisons; 60 Cold Blood.

Spec priority additions:

1. Sap an eligible secondary humanoid before a planned pull. Do not apply poison/DoT/AoE to the sapped target.
2. Open with Ambush for a short kill or Cheap Shot when control/survival matters. Use Hemorrhage as the normal builder after the opener.
3. Ghostly Strike receives high priority while the rogue owns melee threat. Preparation is used only after meaningful cooldowns have been spent; it must not fire just because one resettable cooldown is unavailable.
4. Cold Blood plus Eviscerate is the burst finisher. This profile intentionally chooses Cold Blood over Premeditation; the latter would require giving up the 21-point Assassination package.

Sources: [Icy Veins Subtlety leveling](https://www.icy-veins.com/wow-classic/subtlety-rogue-leveling-talent-build-from-1-to-60), [Wowhead Subtlety leveling](https://www.wowhead.com/classic/guide/classes/rogue/subtlety/leveling-tips), [Icy Veins Rogue leveling overview](https://www.icy-veins.com/wow-classic/classic-rogue-leveling-guide).

Source correction: one current guide rendering describes three ranks of Murder even though the installed 1.12 DBC has two. The sequence above corrects that error and still purchases exactly 51 legal points.

## Druid common combat contract

- Maintain Mark of the Wild and Thorns on appropriate party members. Remove Curse and Abolish Poison take priority when the debuff is dangerous.
- Use Entangling Roots outdoors to isolate adds; Hibernate eligible beasts/dragonkin; Rebirth a critical dead party member when combat recovery is valuable.
- Do not oscillate forms without purpose. A form change must satisfy a role transition, emergency heal/control, travel need, or a deliberate Furor/powershift rule with sufficient mana.
- Innervate is assigned by policy: healer with dangerously low mana first, otherwise self when the current role is mana-bound and the encounter will last.
- Healing chooses a spell rank from actual missing health and predicted incoming damage, not simply the highest learned rank.

### Druid — Balance

Role and assumptions: caster DPS/off-healer using intellect/spirit and nature/arcane damage gear. Moonkin is the normal combat form after level 40.

Level-60 allocation: 31 Balance / 0 Feral Combat / 20 Restoration.

- Balance: Improved Wrath 4, Nature's Grasp 1, Improved Nature's Grasp 4, Improved Moonfire 5, Nature's Reach 2, Vengeance 5, Nature's Grace 1, Moonglow 3, Moonfury 5, Moonkin Form 1.
- Restoration: Improved Mark of the Wild 5, Improved Healing Touch 5, Nature's Focus 1, Reflection 3, Insect Swarm 1, Tranquil Spirit 5.

Purchase order:

- 10 Nature's Grasp (16689); 11–14 Improved Nature's Grasp; 15–19 Improved Moonfire; 20–21 Nature's Reach; 22–24 Improved Wrath.
- 25–29 Vengeance; 30 Nature's Grace (16880); 31–33 Moonglow; 34 finish Improved Wrath; 35–39 Moonfury; 40 Moonkin Form (24858).
- 41–45 Improved Mark of the Wild; 46–50 Improved Healing Touch; 51 Insect Swarm (5570); 52–54 Reflection; 55 Nature's Focus; 56–60 Tranquil Spirit.

Spec priority additions:

1. Open with Starfire when stationary and the target is not already engaged; apply Moonfire and Insect Swarm only when their expected duration and mana threshold justify them.
2. Starfire is the normal mana-efficient filler; Wrath is used when the target is close, moving, nearly dead, or a shorter cast is tactically safer. Consume Nature's Grace on the best safe cast rather than forcing one spell unconditionally.
3. Use Nature's Grasp/Roots to escape melee pressure. Shift out of Moonkin to heal only when healing value exceeds the lost form/global cost.
4. For multiple stable targets, Hurricane is allowed when mana and tank threat are sufficient; otherwise multidot selectively and preserve mana.

Sources: [Icy Veins Balance leveling](https://www.icy-veins.com/wow-classic/balance-druid-leveling-talent-build-from-1-to-60), [Icy Veins Druid leveling overview](https://www.icy-veins.com/wow-classic/classic-druid-leveling-guide), [Nostalrius Vanilla Druid talent discussion](https://forum.nostalrius.org/viewtopic.php?f=41&t=16244).

### Druid — Feral Combat

Role and assumptions: adaptive Cat DPS or Bear tank selected by party role, not by talent-tree inference. Strength/agility leather is preferred; weapon DPS does not drive form damage in Vanilla.

Level-60 allocation: 14 Balance / 32 Feral Combat / 5 Restoration.

- Balance: Nature's Grasp 1, Improved Nature's Grasp 4, Natural Weapons 5, Natural Shapeshifter 3, Omen of Clarity 1.
- Feral Combat: Ferocity 5, Feral Instinct 5, Feline Swiftness 2, Feral Charge 1, Sharpened Claws 3, Predatory Strikes 3, Blood Frenzy 2, Primal Fury 2, Savage Fury 2, Faerie Fire (Feral) 1, Heart of the Wild 5, Leader of the Pack 1.
- Restoration: Furor 5.

Purchase order:

- 10–14 Ferocity; 15–19 Feral Instinct; 20–21 Feline Swiftness; 22–25 Furor.
- 26 Feral Charge (16979); 27–29 Sharpened Claws; 30–31 Primal Fury; 32–34 Predatory Strikes; 35–36 Savage Fury; 37 Faerie Fire (Feral) (16857); 38–39 Blood Frenzy.
- 40–44 Heart of the Wild; 45 Leader of the Pack (17007); 46 finish Furor.
- 47 Nature's Grasp; 48–51 Improved Nature's Grasp; 52–56 Natural Weapons; 57 Omen of Clarity (16864); 58–60 Natural Shapeshifter.

Cat priority:

1. Prowl/Ravage when a safe rear opener is possible; Faerie Fire (Feral) on targets that will live; Rake only when its duration is worthwhile.
2. Shred from behind in groups, otherwise Claw. Rip on long/high-armor targets; Ferocious Bite on short targets or when the remaining duration cannot repay Rip.
3. Tiger's Fury is used when enough attacks remain to repay its energy cost. Powershift only with healthy mana and a clear energy gain; Omen procs are spent on an expensive useful action rather than overwritten.

Bear-tank priority:

1. Faerie Fire pull, Feral Charge a loose/running/casting enemy, Growl only when threat was lost, and Bash dangerous casts.
2. Demoralizing Roar on meaningful packs; Swipe for multiple targets; Maul for primary-target threat without starving emergency rage.
3. Frenzied Regeneration at dangerous health when healer support is insufficient. Enrage is used before safe pulls, not while incoming physical burst would exploit its armor penalty.

Sources: [Icy Veins Feral leveling](https://www.icy-veins.com/wow-classic/feral-druid-leveling-talent-build-from-1-to-60), [Wowhead Feral leveling](https://www.wowhead.com/classic/guide/classes/druid/feral/leveling-tips), [Wowhead Feral talent/build discussion](https://www.wowhead.com/classic/guide/classes/druid/feral/dps-talent-builds-pve).

Feral caveat: one Vanilla tree represents both Cat DPS and Bear tanking. The persisted specialization must therefore include a separate active role; talent identity alone is insufficient.

### Druid — Restoration

Role and assumptions: primary five-player healer/off-DPS using healing power, intellect, and spirit gear.

Level-60 allocation: 20 Balance / 0 Feral Combat / 31 Restoration.

- Balance: Improved Wrath 5, Nature's Grasp 1, Improved Nature's Grasp 4, Improved Moonfire 5, Nature's Reach 2, Improved Starfire 3.
- Restoration: Improved Mark of the Wild 5, Improved Healing Touch 5, Nature's Focus 1, Reflection 3, Insect Swarm 1, Tranquil Spirit 5, Improved Rejuvenation 3, Nature's Swiftness 1, Gift of Nature 5, Improved Regrowth 1, Swiftmend 1.

Purchase order:

- 10–14 Improved Mark of the Wild; 15–19 Improved Healing Touch; 20 Insect Swarm; 21–23 Reflection; 24 Nature's Focus; 25–29 Tranquil Spirit.
- 30 Nature's Swiftness (17116); 31–35 Gift of Nature; 36–38 Improved Rejuvenation; 39 Improved Regrowth; 40 Swiftmend (18562).
- 41–45 Improved Wrath; 46 Nature's Grasp; 47–50 Improved Nature's Grasp; 51–55 Improved Moonfire; 56–57 Nature's Reach; 58–60 Improved Starfire.

Healing priority:

1. Nature's Swiftness plus an appropriately high Healing Touch for a lethal emergency; Swiftmend when a target with Rejuvenation/Regrowth needs immediate recovery.
2. Precast/cancel Healing Touch on the tank and select ranks to avoid overheal. Rejuvenation covers predictable damage; Regrowth is reserved for burst because it is expensive.
3. Use Tranquility for sustained group-wide danger when positioning is safe. Rebirth the tank/healer or another uniquely valuable party member, not simply the first corpse.
4. Dispel, crowd control, and Innervate remain above damage. When the party is stable and mana is healthy, apply Insect Swarm/Moonfire to a durable target and cast Wrath; stop damage early enough to resume healing safely.

Sources: [Icy Veins Restoration leveling](https://www.icy-veins.com/wow-classic/restoration-druid-leveling-talent-build-from-1-to-60), [Wowhead Restoration leveling](https://www.wowhead.com/classic/guide/classes/druid/restoration/leveling-tips), [Icy Veins Druid leveling overview](https://www.icy-veins.com/wow-classic/classic-druid-leveling-guide).

## Implementation consequences found during research

1. Hunter rotations require Auto Shot timing, pet threat/Growl state, dead-zone recovery, and crowd-control-aware AoE. The current rotation slate cannot express these.
2. Rogue rotations require combo points, energy ticks, positional checks, weapon type, parry procs, poison state, and finishers chosen by expected target lifetime.
3. Druid requires form/role state, mana retained across forms, rage/energy, Omen/Clearcasting, positional Cat attacks, and a healing rank selector.
4. The default engine therefore needs a typed combat context and class mechanics helpers. Treating these as a longer list of spell priorities would reproduce the current failure in a larger file.
