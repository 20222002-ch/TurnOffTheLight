using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Independent fixtures for the movement prototype. Does not edit scenes or player saves.</summary>
    public static class LightsOutVerticalValidation
    {
        [Serializable] sealed class Report
        { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result
        { public string name; public bool passed; public string details; }
        [Serializable] sealed class PendingSave
        { public int id, kind; public double delay; public long action, sequence; public bool automated; }
        [Serializable] sealed class Snapshot
        {
            public int version, run;
            public double money, earned, clock, automaticAccumulator, lastFloorCompletion;
            public long off, action, sequence;
            public uint random;
            public float session, surge;
            public int blackout, consecutive, autoFloor, autoCursor;
            public int[] skills;
            public bool[] lights;
            public List<PendingSave> pending;
        }

        [UnityEditor.MenuItem("Lights Out/Run movement and vertical checks")]
        static void RunFromMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            Report report = new Report();
            Check(report, "20 skills, branch graph, and level caps", SkillContract);
            Check(report, "prerequisites, purchase costs, and maximum-level rejection", Purchases);
            Check(report, "contact rewards once and movement-origin bonus", ContactRewards);
            Check(report, "movement, radius, and dash values", MovementValues);
            Check(report, "probabilities bounded and timing remains positive", ProbabilityAndTiming);
            Check(report, "fractional automatic rate accumulates without truncation", FractionalAutomation);
            Check(report, "delayed chain and vertical propagation terminate without duplicates", FinitePropagation);
            Check(report, "out-of-order floors, final completion, and retained new-run skills", FloorProgression);
            Check(report, "pending propagation and fractional automation survive saves", SaveRoundTrip);
            Check(report, "invalid saves reject atomically", InvalidSaves);
            Check(report, "dash sweep intersects every crossed room", SweptContacts);
            return JsonUtility.ToJson(report, true);
        }

        static void Check(Report report, string name, Func<string> test)
        {
            try { string details = test(); report.passed++; report.checks.Add(new Result { name = name, passed = true, details = details }); }
            catch (Exception ex) { report.failed++; report.checks.Add(new Result { name = name, passed = false, details = ex.Message }); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual, string message)
        { Require(Math.Abs(expected - actual) <= Math.Max(.00001, Math.Abs(expected) * .000001), message + " (expected " + expected + ", actual " + actual + ")"); }
        static Snapshot Read(LightsOutVerticalModel model) { return JsonUtility.FromJson<Snapshot>(model.ExportJson()); }
        static LightsOutVerticalModel Fixture(int[] levels = null, double money = 0, float surge = 0)
        {
            LightsOutVerticalModel model = new LightsOutVerticalModel();
            Snapshot saved = Read(model); saved.random = 97193711; saved.money = money; saved.earned = money;
            saved.skills = levels ?? new int[20]; saved.surge = surge;
            Require(model.ImportJson(JsonUtility.ToJson(saved)), "Deterministic fixture was rejected");
            return model;
        }
        static int[] MaximumLevels()
        {
            int[] levels = new int[20];
            for (int i = 0; i < levels.Length; i++) levels[i] = LightsOutVerticalModel.Skills[i].MaxLevel;
            return levels;
        }

        static string SkillContract()
        {
            var skills = LightsOutVerticalModel.Skills;
            int[] expected = { 10, 5, 5, 5, 1, 5, 5, 5, 5, 1, 5, 5, 5, 5, 1, 10, 10, 5, 5, 1 };
            Require(skills.Length == 20, "The skill tree must contain exactly 20 nodes");
            int[] branches = new int[4];
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                Require(skill.Id == i && skill.Branch == i / 5 && skill.MaxLevel == expected[i], "Incorrect ID, branch, or cap at skill " + i);
                Require(!string.IsNullOrEmpty(skill.Name) && !string.IsNullOrEmpty(skill.EffectDescription), "Missing skill text at " + i);
                Require(skill.BaseCost > 0 && skill.CostGrowth >= 1, "Invalid economy values at " + i);
                branches[skill.Branch]++;
                if (i % 5 == 0) Require(skill.PrerequisiteId < 0, "A branch root requires an unavailable predecessor");
                else Require(skill.PrerequisiteId == i - 1 && skill.PrerequisiteLevel > 0 && skill.PrerequisiteLevel <= skills[i - 1].MaxLevel,
                    "Unreachable or unexpected predecessor at skill " + i);
            }
            foreach (int count in branches) Require(count == 5, "A branch does not contain five skills");
            return "4 branches x 5 nodes; caps match the movement prototype contract";
        }

        static string Purchases()
        {
            LightsOutVerticalModel model = Fixture(null, 1e12);
            Require(!model.BuySkill(-1) && !model.BuySkill(20), "An invalid skill purchase was accepted");
            Require(!model.CanBuySkill(1) && !model.BuySkill(1), "A missing predecessor did not lock its successor");
            int purchases = 0;
            for (int i = 0; i < 20; i++)
            {
                var definition = LightsOutVerticalModel.Skills[i];
                for (int level = 0; level < definition.MaxLevel; level++)
                {
                    double cost = model.GetSkillCost(i), before = model.Money;
                    Require(!double.IsInfinity(cost) && cost > 0 && model.CanBuySkill(i) && model.BuySkill(i), "An affordable legal level was rejected: " + i);
                    Near(cost, before - model.Money, "Purchase did not deduct its exact displayed cost");
                    Require(model.SkillLevels[i] == level + 1, "A purchase advanced more or less than one level");
                    purchases++;
                }
                double atCap = model.Money;
                Require(!model.CanBuySkill(i) && !model.BuySkill(i) && double.IsPositiveInfinity(model.GetSkillCost(i)), "A maximum skill could be purchased again");
                Near(atCap, model.Money, "Rejected purchase deducted money");
            }
            return purchases + " legal level purchases, predecessor locks, exact costs, and 20 cap rejections";
        }

        static string ContactRewards()
        {
            LightsOutVerticalModel model = Fixture(); int events = 0; VerticalExtinguishEvent received = null;
            model.Extinguished += e => { received = e; events++; };
            Require(model.Contact(0, 0), "A lit room rejected contact");
            Near(5, model.Money, "Initial room reward is incorrect");
            Require(model.TotalOff == 1 && model.GetRemainingRooms(0) == 3 && events == 1, "Contact did not extinguish and count once");
            Require(received != null && !received.Automated && received.Origin == LightOrigin.Movement && received.ActionId > 0 && received.AffectedIds.Length == 1,
                "Contact feedback lost its movement origin or identity");
            Require(!model.Contact(0, 0) && !model.Contact(-1, 0) && !model.Contact(0, 4), "Off or invalid rooms accepted contact");
            Near(5, model.Money, "Repeated contact paid twice");
            Require(events == 1 && model.TotalOff == 1, "Repeated contact emitted another completion");

            int[] movement = new int[20]; for (int i = 0; i < 5; i++) movement[i] = LightsOutVerticalModel.Skills[i].MaxLevel;
            LightsOutVerticalModel bonus = Fixture(movement);
            bonus.Contact(0, 0); Near(10, bonus.Money, "Movement-origin bonus did not double the room reward");
            Near(5, bonus.GetRoomReward(0, true), "Movement-origin bonus leaked into automatic rewards");
            return "1 room = 1 reward; movement origin doubles only movement-derived income";
        }

        static string MovementValues()
        {
            LightsOutVerticalModel baseline = Fixture();
            Near(4.8, baseline.MoveSpeed, "Base movement speed differs"); Near(.12, baseline.ExtinguishRadius, "Base contact radius differs");
            Require(!baseline.DashUnlocked, "Dash is unlocked without its skill");
            int[] levels = new int[20]; for (int i = 0; i < 5; i++) levels[i] = LightsOutVerticalModel.Skills[i].MaxLevel;
            LightsOutVerticalModel maximum = Fixture(levels);
            Near(4.8 * 2.2, maximum.MoveSpeed, "Maximum movement speed differs"); Near(.12 + 5 * .45, maximum.ExtinguishRadius, "Maximum range differs");
            Require(maximum.DashUnlocked && maximum.DashCooldown > 0, "Maximum dash is unavailable or has no cooldown");
            Near(15, maximum.DashDistance, "Maximum dash distance differs"); Near(2.6, maximum.DashCooldown, "Maximum dash cooldown differs");
            return "Move 4.8 -> 10.56; radius 0.12 -> 2.37; max dash 15 units / 2.6 seconds";
        }

        static string ProbabilityAndTiming()
        {
            LightsOutVerticalModel model = Fixture(MaximumLevels(), 0, 5);
            Near(1, model.ChainProbability, "Maximum chain probability differs"); Near(1, model.VerticalProbability, "Surge should bring maximum vertical chance to 100%");
            Near(.7, model.AutoChainProbability, "Maximum automatic chain probability differs");
            Require(model.ChainInterval > 0 && model.AutoPeriodMultiplier > 0 && model.AutomationRate > 0, "A rate or propagation period reached zero");
            Near(.055, model.ChainInterval, "Chain interval reduction differs"); Near(.2, model.AutoPeriodMultiplier, "Auto interval reduction differs");
            Near(20, model.AutomationRate, "Fully automated rate differs");
            model.Tick(5.01f);
            Require(model.ChainProbability >= 0 && model.ChainProbability <= 1 && model.VerticalProbability >= 0 && model.VerticalProbability <= 1 &&
                model.AutoChainProbability >= 0 && model.AutoChainProbability <= 1, "A runtime probability exceeded [0, 1]");
            return "100% chain, up to 100% vertical, 70% auto-chain, positive periods, max 20 rooms/second";
        }

        static string FractionalAutomation()
        {
            int[] levels = new int[20]; levels[15] = 1;
            LightsOutVerticalModel model = Fixture(levels); Near(.2, model.AutomationRate, "Level-one automatic rate differs");
            model.Tick(0); model.Tick(float.NaN); model.Tick(float.PositiveInfinity);
            Require(model.TotalOff == 0, "An invalid elapsed-time value advanced automation");
            model.Tick(4.99f); Require(model.TotalOff == 0 && model.AutomationProgress > .99f, "Fractional work was rounded away or paid too early");
            model.Tick(.02f); Require(model.TotalOff == 1, "Five seconds of fractional work did not extinguish one room"); Near(5, model.Money, "First automatic payment differs");
            for (int i = 0; i < 10; i++) model.Tick(.5f);
            Require(model.TotalOff == 2, "Fractional remainder was lost across small ticks"); Near(10, model.Money, "Automatic base rewards differ");
            return "0.2/second accumulates: no early payment, one room near 5 seconds, two near 10 seconds";
        }

        static string FinitePropagation()
        {
            int[] levels = MaximumLevels(); for (int i = 15; i < 20; i++) levels[i] = 0;
            LightsOutVerticalModel model = Fixture(levels);
            HashSet<int> seen = new HashSet<int>(); long root = -1; int batches = 0;
            model.Extinguished += e =>
            {
                batches++; if (root < 0) root = e.ActionId;
                Require(e.ActionId == root && !e.Automated, "A derived movement chain lost its root action");
                foreach (int id in e.AffectedIds) Require(seen.Add(id), "A room was paid twice in the same propagation graph");
                Require(!e.FloorCompleted || e.CompletedFloorIds.Length > 0, "Floor-completion feedback has no completed floor");
            };
            model.Contact(0, 0); Require(model.PendingEventCount > 0, "Maximum chain did not schedule delayed propagation");
            int ticks = 0; while (model.PendingEventCount > 0 && ticks++ < 5000) model.Tick(.1f);
            Require(model.PendingEventCount == 0, "Propagation did not terminate within a bounded tick budget");
            Require(model.TotalOff == seen.Count && seen.Count <= model.FloorCount * model.FloorRooms && seen.Count > 1, "Propagation count is duplicated or out of bounds");
            double paid = model.Money; model.Tick(1); Near(paid, model.Money, "Drained propagation continued paying");
            return seen.Count + " unique rooms in " + batches + " batches; queue drained after " + ticks + " ticks";
        }

        static string FloorProgression()
        {
            int[] levels = new int[20]; levels[0] = 3;
            LightsOutVerticalModel model = Fixture(levels);
            for (int col = 0; col < model.FloorRooms; col++) model.Contact(2, col);
            Require(model.CurrentFloor == 0 && model.ClearedFloors == 1 && model.HighestClearedFloor == 2, "An out-of-order cleared floor skipped unfinished lower floors");
            for (int col = 0; col < model.FloorRooms; col++) model.Contact(0, col);
            Require(model.CurrentFloor == 1, "Completing the first floor did not advance the frontier");
            for (int col = 0; col < model.FloorRooms; col++) model.Contact(1, col);
            Require(model.CurrentFloor == 3 && model.ClearedFloors == 3, "Contiguous advancement did not skip an already-cleared upper floor");
            for (int floor = 3; floor < model.FloorCount; floor++) for (int col = 0; col < model.FloorRooms; col++) model.Contact(floor, col);
            Require(model.IsComplete && model.CanGoHome && model.ClearedFloors == 300 && model.TotalOff == 1200, "The entire building did not complete once");
            double expected = 0; for (int floor = 0; floor < model.FloorCount; floor++) expected += model.GetRoomReward(floor) * model.FloorRooms;
            Near(expected, model.Money, "Building income contains an unexpected duplicate or missing reward");
            double earned = model.Money; model.Tick(60); Require(!model.Contact(0, 0), "Completed building accepted contact"); Near(earned, model.Money, "Completed building continued paying");
            Require(model.StartNextRun() && model.RunNumber == 2 && !model.IsComplete && model.ClearedFloors == 0 && model.CurrentFloor == 0,
                "New building did not reset floor state");
            Require(model.SkillLevels[0] == 3 && model.TotalOff == 1200 && !model.StartNextRun(), "New run lost upgrades/cumulative records or started twice");
            Near(earned, model.Money, "New run altered earned currency");
            return "300 floors / 1200 rooms; ordered frontier, exact income, frozen completion, retained upgrades";
        }

        static string SaveRoundTrip()
        {
            int[] levels = new int[20]; levels[5] = 5; levels[15] = 1;
            LightsOutVerticalModel original = Fixture(levels, 1234); original.Contact(0, 0); original.Tick(.04f);
            Require(original.PendingEventCount > 0 && original.AutomationProgress > 0, "Save fixture lacks a pending chain or fractional work");
            LightsOutVerticalModel loaded = new LightsOutVerticalModel(); Require(loaded.ImportJson(original.ExportJson()), "Valid partial save was rejected");
            SameState(original, loaded);
            for (int i = 0; i < 100; i++) { original.Tick(.1f); loaded.Tick(.1f); SameState(original, loaded); }
            return "Pending delays, RNG, skills, lights, income, charges, and fractional automation reproduce after loading";
        }
        static void SameState(LightsOutVerticalModel a, LightsOutVerticalModel b)
        {
            Near(a.Money, b.Money, "Saved money differs"); Near(a.TotalEarned, b.TotalEarned, "Saved cumulative income differs");
            Near(a.AutomationProgress, b.AutomationProgress, "Saved fractional work differs"); Near(a.SurgeRemaining, b.SurgeRemaining, "Saved surge differs");
            Require(a.TotalOff == b.TotalOff && a.CurrentFloor == b.CurrentFloor && a.ClearedFloors == b.ClearedFloors &&
                a.PendingEventCount == b.PendingEventCount && a.BlackoutCharge == b.BlackoutCharge && a.AutoFloorCharge == b.AutoFloorCharge, "Saved counters or pending queue differ");
            for (int i = 0; i < 20; i++) Require(a.SkillLevels[i] == b.SkillLevels[i], "Saved skill level differs");
            for (int floor = 0; floor < a.FloorCount; floor++) for (int col = 0; col < a.FloorRooms; col++)
                Require(a.IsLightOn(floor, col) == b.IsLightOn(floor, col), "Saved room state differs");
        }

        static string InvalidSaves()
        {
            LightsOutVerticalModel model = Fixture(null, 500); model.Contact(0, 0);
            string before = model.ExportJson();
            Action<Action<Snapshot>> reject = change =>
            {
                Snapshot save = JsonUtility.FromJson<Snapshot>(before); change(save);
                Require(!model.ImportJson(JsonUtility.ToJson(save)), "Invalid snapshot was accepted");
                Require(model.ExportJson() == before, "Invalid snapshot partially replaced valid state");
            };
            Require(!model.ImportJson(null) && !model.ImportJson("not json"), "Malformed save was accepted");
            reject(s => s.version = 1); reject(s => s.money = -1); reject(s => s.earned = 0);
            reject(s => s.skills = new int[19]); reject(s => s.skills[0] = 11); reject(s => s.skills[1] = 1);
            reject(s => s.lights = new bool[1199]); reject(s => s.surge = 6); reject(s => s.automaticAccumulator = -1);
            reject(s => s.autoFloor = 30); reject(s => s.autoCursor = 1200);
            reject(s => s.pending.Add(new PendingSave { id = 1200, kind = 1, delay = .2, action = s.action, sequence = s.sequence }));
            return "Malformed JSON and 12 invalid contracts rejected without altering current progress";
        }

        static string SweptContacts()
        {
            Rect box = new Rect(-1, -1, 2, 2);
            Require(LightsOutMover.SegmentIntersects(new Vector2(-3, 0), new Vector2(3, 0), box), "A through dash missed a room");
            Require(LightsOutMover.SegmentIntersects(Vector2.zero, Vector2.zero, box), "Stationary contact inside a room was missed");
            Require(!LightsOutMover.SegmentIntersects(new Vector2(-3, 2), new Vector2(3, 2), box), "A parallel outside dash contacted a room");
            Require(!LightsOutMover.SegmentIntersects(new Vector2(3, 3), new Vector2(3, 3), box), "Stationary outside point contacted a room");
            LightsOutVerticalModel model = Fixture();
            float left = -model.FloorRooms * LightsOutMover.RoomWidth * .5f;
            Vector2 start = new Vector2(left + .12f, .9f), end = new Vector2(-left - .12f, .9f);
            int contacts = LightsOutMover.Sweep(model, start, end, .12f);
            Require(contacts == 4 && model.ClearedFloors == 1 && model.TotalOff == 4, "Horizontal sweep did not contact every crossed room exactly once");
            Require(LightsOutMover.Sweep(model, start, end, .12f) == 0 && model.TotalOff == 4, "Repeated sweep paid off rooms again");
            LightsOutVerticalModel vertical = Fixture(); float x = left + LightsOutMover.RoomWidth * .5f;
            int acrossFloors = LightsOutMover.Sweep(vertical, new Vector2(x, .9f), new Vector2(x, 6 * LightsOutMover.FloorHeight + .9f), .12f);
            Require(acrossFloors == 7 && vertical.TotalOff == 7, "A long vertical dash missed crossed floors");
            return "Stationary, parallel, through, four-room horizontal, and seven-floor vertical sweeps are correct";
        }
    }
}
