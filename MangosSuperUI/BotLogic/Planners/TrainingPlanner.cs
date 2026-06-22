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

        // A negated/expired WAIT surfaced a failure → give up this trip (back to questing/grinding).
        // Covers route MOVE_FAILED/PATH_UNSAFE, TRAIN_FAIL (executor negates it), and any deadline.
        if (ctx.Failure != null)
        {
            var reason = ctx.Failure.Reason;
            ctx.Failure = null;
            return GiveUp(ctx, $"fail:{reason}");
        }

        var t = ctx.Train;

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
        return StepResult.Send(
            new BridgeCommand("MOVE_TO", new { mapId = t.TrainerPos.Map, x = t.TrainerPos.X, y = t.TrainerPos.Y, z = t.TrainerPos.Z }),
            "TASK_COMPLETE", RouteDeadline);
    }

    // Abort the trip: clear HasUnlearnedSpells so it does NOT re-loop toward the same unreachable
    // trainer, set a cooldown, drop the scratch. The flag re-arms on the next LEVEL_UP, and the
    // cooldown lapses, so the bot retries later from wherever it then is.
    private StepResult GiveUp(BotContext ctx, string why)
    {
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
