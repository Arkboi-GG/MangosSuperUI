using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// TrainingPlanner — Goal.Training (port of the old TrainingDomain to the spine).
//
// route → TRAIN_AT_NPC → ack. The C++ side owns the actual spell learning
// (TRAIN_AT_NPC → TRAIN_ACK / TRAIN_FAIL, already wired and previously working);
// C# only needs to know WHERE the class trainer is (SpellProgressionLoader) and
// to drive the trip. Thin, like GrindPlanner — no C# spell logic.
//
// Economy (Nico's rule, kept simple): the GoalSelector only routes here when the
// bot has unlearned spells AND enough gold to buy something. A broke bot does NOT
// trek (it'd learn nothing) — it keeps questing/grinding to earn, and trains once
// it can afford it. C++ buys whatever the bot can afford in one visit; the rest
// waits for the next LEVEL_UP, which re-flags HasUnlearnedSpells (BotBrainService).
//
// Flow (ctx.Step + the WAIT drive it, same shape as QuestPlanner's legs):
//   (enter) → find nearest class trainer on this map → MOVE_TO it (to_trainer)
//   to_trainer → TASK_COMPLETE (arrived) → TRAIN_AT_NPC (train)
//   train     → TRAIN_ACK → clear HasUnlearnedSpells, Done
//   any route/train failure (no_path / path_unsafe / TRAIN_FAIL / deadline) → GiveUp
//     (clear the flag so it doesn't re-loop toward an unreachable trainer, set a
//      cooldown, drop to questing/grinding; the flag re-arms on the next LEVEL_UP).
//
// "Armed" = ctx.Train != null. The brain nulls scratch on a goal CHANGE; the
// GoalSelector pins the bot in Training while ctx.Train is set (the in-flight hold).
// ============================================================================
public sealed class TrainingPlanner : IBotPlanner
{
    private readonly SpellProgressionLoader _trainers;
    private readonly ILogger<TrainingPlanner> _log;

    private static readonly TimeSpan RouteDeadline = TimeSpan.FromMinutes(8);     // continuation travel can be long (match QuestPlanner.TravelDeadline)
    private static readonly TimeSpan TrainAckDeadline = TimeSpan.FromSeconds(30); // TRAIN_AT_NPC → TRAIN_ACK is near-instant; deadline is the TRAIN_FAIL backstop
    private const float TrainerReachYards = 10f;        // close enough to TRAIN without a fresh MOVE_TO (C++ finds the NPC within ~10-15yd)
    private const double TrainGiveupCooldownSec = 300;  // after a give-up (unreachable / fail), don't retry training this long

    public TrainingPlanner(SpellProgressionLoader trainers, ILogger<TrainingPlanner> log)
    {
        _trainers = trainers;
        _log = log;
    }

    public Goal Handles => Goal.Training;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        var id = ctx.Identity;
        var t = ctx.Train;

        // (A) Consume a failure: a teleport-assist hop fail, an approach no_path (→ teleport-assist),
        //     or anything else (→ give up the trip).
        if (ctx.Failure != null)
        {
            var f = ctx.Failure;
            ctx.Failure = null;

            // The TELEPORT_TO hop ITSELF failed/deadlined (CommandType distinguishes it from a
            // TRAIN_FAIL, which flows to the give-up below and returns-to-anchor via GiveUp).
            if (f.CommandType == "TELEPORT_TO" && ctx.Teleport is { } tpf)
            {
                var phase = tpf.Phase;
                bool wasFailed = tpf.Failed;
                ctx.Teleport = null;
                if (phase == TpPhase.Inbound)
                {
                    // Business already attempted; just couldn't get home (the bot is at the trainer —
                    // a safe town NPC). Exit per the trip outcome.
                    ctx.Train = null;
                    return wasFailed ? GiveUp(ctx, $"teleport-return:{f.Reason}") : StepResult.Complete();
                }
                return GiveUp(ctx, $"teleport:{f.Reason}");   // couldn't hop to the trainer
            }

            // A no_path on the final approach to the trainer, in the vicinity → teleport the last
            // few yards instead of giving up. The first no_path retries (continuation travel may
            // close it); the second, within reach, teleports.
            if (t != null && ctx.Step == "to_trainer" && TeleportAssist.IsApproachNoPath(f))
            {
                t.ApproachFails++;
                switch (TeleportAssist.Decide(t.ApproachFails, ctx.Pos, t.TrainerPos, ctx.MapId))
                {
                    case TeleportAssist.TpDecision.Teleport:
                        _log.LogInformation("[TRAIN] {Name} trainer unreachable ({N}× no_path, {D:F0}yd) — TELEPORT_TO entry={Entry}",
                            ctx.Name, t.ApproachFails, ctx.Pos.Dist2D(t.TrainerPos.Pos), t.TrainerEntry);
                        return StepResult.Send(TeleportAssist.BeginOutbound(ctx, t.TrainerPos), "TELEPORT_ACK", TeleportAssist.AckDeadline);
                    case TeleportAssist.TpDecision.Retry:
                        return RouteToTrainer(t);   // one more chance to path closer
                                                    // TpDecision.GiveUp → fall through (NPC genuinely far — not a final-approach pocket)
                }
            }

            return GiveUp(ctx, $"fail:{f.Reason}");
        }

        // (B) Advance a committed teleport-assist round-trip (TELEPORT_ACK arrivals).
        if (ctx.Teleport is { Phase: TpPhase.Outbound })
        {
            // Hopped to the trainer — the executor already set ctx.Pos from the ack, so we're AT it.
            ctx.Teleport.Phase = TpPhase.AtTarget;
            ctx.SetStep("train");
            _log.LogInformation("[TRAIN] {Name} teleported in → TRAIN_AT_NPC entry={Entry}", ctx.Name, t!.TrainerEntry);
            return StepResult.Send(
                new BridgeCommand("TRAIN_AT_NPC", new { npc_entry = t.TrainerEntry }),
                "TRAIN_ACK", TrainAckDeadline);
        }
        if (ctx.Teleport is { Phase: TpPhase.Inbound } tpr)
        {
            // Returned to the pre-teleport anchor. The flag was cleared at TRAIN_ACK on success; a
            // failed train set Failed and gives up here (after the safe return) instead of finishing.
            bool failed = tpr.Failed;
            ctx.Teleport = null;
            if (failed) return GiveUp(ctx, "train failed at trainer (returned)");
            ctx.Train = null;
            _log.LogInformation("[TRAIN] {Name} trained + returned to anchor — done (cu={Cu})", ctx.Name, ctx.Copper);
            return StepResult.Complete();
        }

        // First entry → find the nearest class trainer on this map. None in range → give up.
        if (t == null)
        {
            if (id == null || !_trainers.IsLoaded)
                return GiveUp(ctx, "no-loader");

            var trainer = _trainers.GetNearestTrainer(id.ClassId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y);
            if (trainer == null)
                return GiveUp(ctx, "no-trainer-in-range");

            ctx.Train = t = new TrainScratch
            {
                TrainerEntry = trainer.NpcEntry,
                TrainerPos = new Vec4(trainer.X, trainer.Y, trainer.Z, trainer.Map),
                StartedUtc = DateTime.UtcNow
            };
            _log.LogInformation("[TRAIN] {Name} → {Trainer} (entry={Entry}) @ ({X:F0},{Y:F0}) cu={Cu}",
                ctx.Name, trainer.NpcName, trainer.NpcEntry, trainer.X, trainer.Y, ctx.Copper);
            // fall through to issue the route below
        }

        // Apply the leg whose WAIT just cleared.
        switch (ctx.Step)
        {
            case "to_trainer":
                // TASK_COMPLETE = arrived → train.
                ctx.SetStep("train");
                _log.LogInformation("[TRAIN] {Name} arrived → TRAIN_AT_NPC entry={Entry}", ctx.Name, t.TrainerEntry);
                return StepResult.Send(
                    new BridgeCommand("TRAIN_AT_NPC", new { npc_entry = t.TrainerEntry }),
                    "TRAIN_ACK", TrainAckDeadline);

            case "train":
                // TRAIN_ACK cleared the WAIT → C++ learned whatever the bot could afford. Clear the
                // flag regardless of count; the next LEVEL_UP re-flags (and the bot has more gold by
                // then for what it couldn't afford this time — "buy what you can next level up").
                if (id != null)
                {
                    id.HasUnlearnedSpells = false;
                    id.TicksSinceLastTrained = 0;
                }
                // If we teleported into a final-approach pocket, return to the anchor BEFORE finishing
                // — the bot's next goal would otherwise try to MOVE_TO out of the same pocket and no_path.
                if (ctx.Teleport is { Phase: TpPhase.AtTarget })
                {
                    _log.LogInformation("[TRAIN] {Name} trained (cu={Cu}) — teleporting back to anchor", ctx.Name, ctx.Copper);
                    return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
                }
                _log.LogInformation("[TRAIN] {Name} trained (cu={Cu}) — done", ctx.Name, ctx.Copper);
                ctx.Train = null;
                return StepResult.Complete();
        }

        // No leg applied yet (fresh entry) → at the trainer already? train; else route to it.
        if (AtTrainer(ctx, t))
        {
            ctx.SetStep("train");
            return StepResult.Send(
                new BridgeCommand("TRAIN_AT_NPC", new { npc_entry = t.TrainerEntry }),
                "TRAIN_ACK", TrainAckDeadline);
        }

        ctx.SetStep("to_trainer");
        return RouteToTrainer(t);
    }

    // The MOVE_TO to the trainer — issued on first entry, on a teleport-assist retry, and on resume.
    private static StepResult RouteToTrainer(TrainScratch t)
        => StepResult.Send(
            new BridgeCommand("MOVE_TO", new { mapId = t.TrainerPos.Map, x = t.TrainerPos.X, y = t.TrainerPos.Y, z = t.TrainerPos.Z }),
            "TASK_COMPLETE", RouteDeadline);

    // Abort the trip: clear HasUnlearnedSpells so it does NOT re-loop toward the same unreachable
    // trainer, set a cooldown, drop the scratch. The flag re-arms on the next LEVEL_UP, and the
    // cooldown lapses, so the bot retries later from wherever it then is.
    private StepResult GiveUp(BotContext ctx, string why)
    {
        // If we teleported into the trainer and the trip is now failing, return to the anchor FIRST
        // (never strand the bot in the nav pocket it teleported into — its next MOVE_TO out would
        // no_path). The Inbound completion re-enters GiveUp with ctx.Teleport cleared and runs the
        // real give-up below.
        if (ctx.Teleport is { Phase: TpPhase.AtTarget } tp)
        {
            tp.Failed = true;
            _log.LogInformation("[TRAIN] {Name} give-up at trainer ({Why}) — teleporting back to anchor first", ctx.Name, why);
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        }

        if (ctx.Identity is { } id)
        {
            id.HasUnlearnedSpells = false;
            id.TrainCooldownUntil = DateTime.UtcNow.AddSeconds(TrainGiveupCooldownSec);
        }
        ctx.Train = null;
        _log.LogWarning("[TRAIN] {Name} GIVEUP why='{Why}' (cooldown {Sec}s) cu={Cu} z={Zone} pos=({X:F0},{Y:F0})@{Map}",
            ctx.Name, why, TrainGiveupCooldownSec, ctx.Copper, ctx.ZoneId, ctx.Pos.X, ctx.Pos.Y, ctx.MapId);
        return StepResult.Complete();
    }

    private static bool AtTrainer(BotContext ctx, TrainScratch t)
        => t.TrainerPos.Map == ctx.MapId
           && Dist2(ctx.Pos.X, ctx.Pos.Y, t.TrainerPos.X, t.TrainerPos.Y) <= TrainerReachYards;

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Lenient backstop — the route WAIT and the TRAIN WAIT own real liveness. Arm grace on entry,
    // then a long no-progress ceiling so a wedged trip eventually reselects instead of hanging.
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < 30) return true;
        return ctx.TimeSinceProgressSec < 300;
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "train:no_progress");
}