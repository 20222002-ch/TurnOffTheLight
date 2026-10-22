using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Isolated growth/equipment/weighted-draw checks. Does not access player save files or edit balance assets.</summary>
    public static class LightsOutGrowthValidation
    {
        [Serializable] sealed class Report { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result { public string name; public bool passed; public string details; }
        [Serializable] sealed class PendingSave { public int id, kind, waves, depth, affected, limit; public double delay; public long action, sequence; public bool automated; }
        [Serializable] sealed class WorkSave
        {
            public int version, run; public double money, earned, clock, automaticAccumulator, lastFloorCompletion;
            public long off, action, sequence; public uint random; public float session, surge;
            public int blackout, consecutive, autoFloor, autoCursor; public int[] skills; public bool[] lights; public List<PendingSave> pending;
            public int floors, rooms, combo, peakCombo, keyOpeningRooms, ringRange;
            public float roomWidth, comboRemaining, holdBonusSeconds, comboMovePerCount, ringChance, badgeBonusPerCombo;
            public bool deferred, closed, pendingSettled, openingKeysApplied, departureBonusApplied; public double pendingIncome;
            public LightsOutIncomeState income;
        }
        [Serializable] sealed class CampaignSave
        {
            public int version, stage, phase, attempts, slot, multiplier;
            public float remaining, timeExtended; public double lastIncome; public bool lastCompleted, rouletteUsed, settled;
            public uint random; public string work; public LightsOutRentModel.StagePlan plan;
        }
        [Serializable] sealed class PermanentSave { public int version, points, highest; public int[] levels, equipped; }

        [UnityEditor.MenuItem("Lights Out/Run combo, equipment and weighted roulette checks")]
        static void RunFromMenu() { Debug.Log(Run()); }
        public static string Run()
        { return LightsOutIncomeValidation.WithPureRules(RunPure); }
        static string RunPure()
        {
            var report = new Report();
            Check(report, "combo reward thresholds are exact and never retroactive", ComboThresholds);
            Check(report, "upper links, ring and chain pay each unique room only once", CompoundPropagation);
            Check(report, "completion waves are bounded and preserved by pending saves", CompletionWaves);
            Check(report, "equipped gloves extend actual retained room work", GlovesWork);
            Check(report, "equipped shoes scale with combo and reset after expiry", ShoesCombo);
            Check(report, "opening keys affect three unique rooms only once per building", OpeningKeys);
            Check(report, "badge pays the peak combo once and freezes at checkout", BadgeDeparture);
            Check(report, "weighted sector widths and center angles match their probabilities", WeightedWheel);
            Check(report, "seeded weighted draws follow the 37/60/3 distribution", WeightedDraws);
            Check(report, "every early payment earns its bonus, milestone points remain unique and DAY gates apply", RewardsAndDayGates);
            return JsonUtility.ToJson(report, true);
        }
        static void Check(Report report, string name, Func<string> test)
        {
            try { string details = test(); report.passed++; report.checks.Add(new Result { name = name, passed = true, details = details }); }
            catch (Exception ex) { report.failed++; report.checks.Add(new Result { name = name, passed = false, details = ex.Message }); }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual, string message)
        { Require(Math.Abs(expected - actual) <= Math.Max(.0001, Math.Abs(expected) * .00001), message + " (expected " + expected + ", actual " + actual + ")"); }
        static float Multiplier(int combo) { return combo >= 20 ? 2 : combo >= 10 ? 1.5f : combo >= 5 ? 1.2f : 1; }
        static int[] Skills() { return new int[LightsOutVerticalModel.Skills.Length]; }
        static LightsOutVerticalModel Work(int floors = 8, int rooms = 20, int[] levels = null, bool deferred = false, double money = 0)
        {
            var model = new LightsOutVerticalModel(floors, deferred, 2.4f, rooms);
            model.PrepareIncome();
            WorkSave saved = JsonUtility.FromJson<WorkSave>(model.ExportJson());
            saved.random = 97193711; saved.skills = levels ?? Skills(); saved.money = saved.earned = money;
            Require(model.ImportJson(JsonUtility.ToJson(saved)), "Growth fixture was rejected"); return model;
        }
        static LightsOutPermanentModel Equipped(int id, int level = 1)
        {
            var permanent = new LightsOutPermanentModel(); var levels = new int[5]; levels[id] = level;
            Require(permanent.ImportJson(JsonUtility.ToJson(new PermanentSave { version = 1, points = 0, highest = 0, levels = levels, equipped = new[] { id, -1, -1 } })),
                "Equipment fixture was rejected"); return permanent;
        }
        static Vector2 Centre(LightsOutVerticalModel model, int floor, int column)
        { return new Vector2(-model.HalfWidth + (column + .5f) * model.RoomWidth, (floor + .5f) * LightsOutMover.FloorHeight); }
        static void WithMover(LightsOutVerticalModel model, Action<LightsOutMover> test)
        {
            var item = new GameObject("Isolated growth validation") { hideFlags = HideFlags.HideAndDontSave };
            try { var mover = item.AddComponent<LightsOutMover>(); mover.ResetMotion(Centre(model, 0, 0)); mover.Initialize(model); test(mover); }
            finally { UnityEngine.Object.DestroyImmediate(item); }
        }

        static string ComboThresholds()
        {
            var model = Work(2); double expected = 0;
            for (int n = 1; n <= 20; n++)
            {
                double before = model.Money; Require(model.Contact(0, n - 1), "Unique combo room rejected at " + n);
                double reward = 5 * Multiplier(n); expected += reward;
                Near(reward, model.Money - before, "Wrong marginal reward at combo " + n);
                Near(expected, model.Money, "Prior rewards changed at combo " + n);
                Near(Multiplier(n), model.ComboRewardMultiplier, "Wrong combo threshold at " + n);
            }
            Near(135, model.Money, "Twenty rooms should total 135, with each tier applied only to subsequent rewards");
            double earned = model.Money; Require(!model.Contact(0, 19), "Repeated room advanced combo"); Near(earned, model.Money, "Repeated room paid again");
            model.Tick(4.01f); Require(model.Combo == 0 && model.PeakCombo == 20, "Expiry lost peak or retained current combo");
            Require(model.Contact(1, 0), "First room after expiry rejected"); Near(5.125, model.Money - earned, "Expired combo kept its multiplier");
            return "All counts 1..20 have exact marginal tiers 1/1.2/1.5/2; total 135, no retroactive pay and post-expiry base reward";
        }
        static string CompoundPropagation()
        {
            var levels = Skills(); levels[5] = levels[6] = levels[7] = levels[8] = 5; levels[9] = 1;
            levels[10] = levels[11] = levels[12] = 5; levels[23] = levels[24] = 3;
            var model = Work(8, 9, levels); model.SetAccessories(new AccessoryEffects(ringChance: 1, ringRange: 2));
            var unique = new HashSet<int>(); double expected = 0, eventRewards = 0; int combo = 0;
            model.Extinguished += e =>
            {
                foreach (int id in e.AffectedIds)
                {
                    Require(unique.Add(id), "Propagation emitted duplicate room " + id); combo++;
                    expected += (5 + (id / model.FloorRooms) * .125) * Multiplier(combo);
                }
                eventRewards += e.Reward;
            };
            Require(model.Contact(0, 4), "Compound origin rejected");
            for (int i = 0; i < 400 && model.PendingEventCount > 0; i++)
            { Require(model.PendingEventCount <= 72, "Propagation queued more targets than rooms"); model.Tick(.05f); }
            Require(model.PendingEventCount == 0 && unique.Count > 4 && unique.Count <= 72, "Compound propagation did not terminate within its room budget");
            Require(model.TotalOff == unique.Count, "Compound counter differs from unique event IDs"); Near(expected, model.Money, "Compound propagation paid duplicate or incorrect rewards");
            Near(expected, eventRewards, "Feedback reward sum differs from actual bank");
            double paid = model.Money; Require(!model.Contact(0, 4), "Compound origin could pay again"); model.Tick(10); Near(paid, model.Money, "A drained chain kept paying");
            return unique.Count + " unique rooms across linked/ring/chain/vertical effects; one reward per ID and finite drained queue";
        }
        static string CompletionWaves()
        {
            var levels = Skills(); levels[10] = levels[11] = 2; levels[24] = 2;
            var model = Work(6, 2, levels); var completed = new HashSet<int>();
            model.Extinguished += e => { foreach (int floor in e.CompletedFloorIds) Require(completed.Add(floor), "Floor completed twice: " + floor); };
            Require(model.Contact(2, 0) && model.Contact(2, 1), "Completion origin rejected");
            Require(model.PendingEventCount == 4, "First completion did not queue two rooms on both adjacent floors");
            var first = JsonUtility.FromJson<WorkSave>(model.ExportJson());
            foreach (PendingSave item in first.pending) Require(item.waves == 1, "First-wave counter differs");
            model.Tick(.22f); Require(model.PendingEventCount == 2, "The second wave did not remain finite");
            string saved = model.ExportJson(); var second = JsonUtility.FromJson<WorkSave>(saved);
            foreach (PendingSave item in second.pending) Require(item.waves == 2, "Second-wave counter was not saved");
            var restored = Work(6, 2); Require(restored.ImportJson(saved), "Pending completion-wave save rejected");
            model.Tick(.22f); restored.Tick(.22f);
            Require(model.ExportJson() == restored.ExportJson(), "Reload changed completion-wave results or random continuation");
            Require(model.PendingEventCount == 0 && model.TotalOff == 8 && completed.Count == 4, "Completion waves ran beyond two neighbour propagations");
            Require(model.IsLightOn(4, 0) && model.IsLightOn(5, 0), "Completion chain propagated beyond its configured budget");
            string before = restored.ExportJson(); second.pending[0].waves = 3;
            Require(!restored.ImportJson(JsonUtility.ToJson(second)) && restored.ExportJson() == before, "Invalid wave counter replaced valid progress");
            return "Two queued completion waves round-trip with exact random continuation; four floors complete once and further floors stay lit";
        }
        static string GlovesWork()
        {
            var bare = Work(2, 4); var gloves = Work(2, 4); gloves.SetAccessories(Equipped(0).CaptureEffects());
            Near(1.5, bare.HoldSeconds, "Base retention differs"); Near(2, gloves.HoldSeconds, "Equipped gloves did not add 0.5 seconds");
            Action<LightsOutMover> leaveAndReturn = mover =>
            {
                mover.StepInput(Vector2.zero, .6f);
                mover.transform.position = new Vector3(-mover.Model.HalfWidth + mover.Model.RoomWidth, .9f, -1);
                mover.StepInput(Vector2.zero, 1.7f);
                mover.transform.position = new Vector3(Centre(mover.Model, 0, 0).x, .9f, -1);
                mover.StepInput(Vector2.zero, .41f);
            };
            WithMover(bare, leaveAndReturn); WithMover(gloves, leaveAndReturn);
            Require(bare.IsLightOn(0, 0) && !gloves.IsLightOn(0, 0), "Actual glove retention did not let the 0.6-second room resume after 1.7 seconds");
            return "Bare progress expires at 1.5 seconds; equipped Lv.1 gloves retain it for 2 seconds and resume to completion";
        }
        static string ShoesCombo()
        {
            var levels = Skills(); levels[0] = 3; var model = Work(2, 20, levels);
            model.SetAccessories(Equipped(1).CaptureEffects()); float baseline = model.MoveSpeed;
            for (int i = 0; i < 20; i++) Require(model.Contact(0, i), "Shoe combo room rejected");
            Near(baseline * 1.2, model.MoveSpeed, "Twenty combos did not add 20 percent equipped shoe speed");
            Require(model.Contact(1, 0), "Shoe cap fixture rejected"); Near(baseline * 1.2, model.MoveSpeed, "Shoe speed exceeded its twenty-combo cap");
            model.Tick(4.01f); Near(baseline, model.MoveSpeed, "Shoe speed remained after combo expired");
            Require(model.Combo == 0 && model.PeakCombo == 21 && model.SkillLevels[0] == 3, "Expiry erased movement skills or peak combo");
            var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()), "Shoe snapshot rejected"); Near(baseline, loaded.MoveSpeed, "Shoe effects did not round-trip");
            return "Equipped shoes add 1 percent per combo up to 20, then return exactly to base movement after four seconds";
        }
        static string OpeningKeys()
        {
            var permanent = Equipped(2); var model = Work(2, 20); model.SetAccessories(permanent.CaptureEffects());
            var ids = new HashSet<int>(); model.Extinguished += e =>
            { Require(e.Automated, "Opening key completion was labelled manual"); foreach (int id in e.AffectedIds) Require(ids.Add(id), "Opening keys selected a duplicate room"); };
            Require(model.StartAccessoryEffects() && model.TotalOff == 3 && ids.Count == 3, "Lv.1 keys did not select three unique rooms");
            double paid = model.Money; Require(!model.StartAccessoryEffects(), "Keys could apply twice to one building"); Near(paid, model.Money, "Repeated start paid key income again");
            var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()) && !loaded.StartAccessoryEffects(), "Reload reapplied opening keys");
            Require(model.BeginBuilding(2, 2.4f, 20), "New key building rejected"); ids.Clear();
            Require(model.StartAccessoryEffects() && ids.Count == 3 && model.TotalOff == 6, "Fresh building did not apply exactly three fresh opening keys");
            return "Three distinct automated rooms per building; repeated startup and reload cannot duplicate the grant, new building can";
        }
        static string BadgeDeparture()
        {
            var model = Work(2, 20, deferred: true); model.SetAccessories(Equipped(4).CaptureEffects());
            for (int i = 0; i < 8; i++) Require(model.Contact(0, i), "Badge combo room rejected");
            model.Tick(4.01f); Require(model.Contact(1, 0) && model.Combo == 1 && model.PeakCombo == 8, "Badge fixture did not retain its peak");
            double gross = model.PendingIncome; Require(model.FinalizeDepartureBonus(), "Badge bonus rejected"); Near(2, model.PendingIncome - gross, "Badge used current combo instead of peak 8 x 0.25");
            double final = model.PendingIncome; Require(!model.FinalizeDepartureBonus(), "Badge could pay twice");
            model.CloseBuilding(); model.Tick(10); Require(!model.Contact(1, 1) && !model.FinalizeDepartureBonus(), "Checkout accepted new badge or room income");
            Near(final, model.PendingIncome, "Closed work changed its finalized income");
            var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()), "Finalized badge snapshot rejected"); Near(final, loaded.PendingIncome, "Finalized badge income did not round-trip");
            Require(loaded.SettlePending(1) && !loaded.SettlePending(1), "Finalized badge settlement was not one-shot"); Near(final, loaded.Money, "Settled badge income differs");
            return "Peak combo 8 adds exactly 2 once despite current combo 1; checkout and reload preserve the finalized payout";
        }
        static string WeightedWheel()
        {
            Near(.37, LightsOutRentModel.RouletteProbabilityForMultiplier(2), "Double probability differs");
            Near(.60, LightsOutRentModel.RouletteProbabilityForMultiplier(0), "Loss probability differs");
            Near(.03, LightsOutRentModel.RouletteProbabilityForMultiplier(10), "Jackpot probability differs");
            double probability = 0, widths = 0; var canonical = new HashSet<int>();
            for (int visual = 0; visual < 21; visual++)
            {
                int slot = LightsOutRoulettePopup.SlotForVisualIndex(visual); Require(canonical.Add(slot), "Visual slot mapping is not bijective");
                Require(LightsOutRoulettePopup.VisualIndexForSlot(slot) == visual, "Visual mapping did not invert");
                int expected = visual == 20 ? 10 : visual % 2 == 0 ? 2 : 0;
                Require(LightsOutRentModel.MultiplierForSlot(slot) == expected, "Double/loss sectors stopped alternating");
                float width = LightsOutRoulettePopup.WidthForVisualIndex(visual), angle = LightsOutRoulettePopup.AngleForSlot(slot);
                Near(LightsOutRentModel.RouletteProbabilityForSlot(slot) * 360, width, "Weighted sector width differs");
                Require(LightsOutRoulettePopup.VisualIndexAtAngle(angle) == visual
                    && LightsOutRoulettePopup.VisualIndexAtAngle(angle - width * .49f) == visual
                    && LightsOutRoulettePopup.VisualIndexAtAngle(angle + width * .49f) == visual, "Sector center or interior landed in another outcome");
                probability += LightsOutRentModel.RouletteProbabilityForSlot(slot); widths += width;
            }
            Near(1, probability, "Canonical probabilities do not sum to one"); Near(360, widths, "Visual widths do not cover the entire wheel");
            return "21 alternating canonical outcomes cover exactly 360 degrees using 37/60/3 weights; all center and interior angles map correctly";
        }
        static uint Next(ref uint value) { value ^= value << 13; value ^= value >> 17; value ^= value << 5; return value; }
        static string WeightedDraws()
        {
            var model = new LightsOutRentModel(); Require(model.StartReadyWork() && model.Work.Contact(0, 0) && model.EndAttempt(false), "Weighted checkout fixture rejected");
            CampaignSave saved = JsonUtility.FromJson<CampaignSave>(model.ExportJson());
            const int samples = 6000; int doubles = 0, losses = 0, jackpots = 0; uint stream = 97193711;
            for (int i = 0; i < samples; i++)
            {
                saved.random = Next(ref stream); Require(model.ImportJson(JsonUtility.ToJson(saved)) && model.SpinRoulette(), "Seeded roulette draw rejected");
                int multiplier = model.LastRouletteMultiplier;
                Require(LightsOutRentModel.MultiplierForSlot(model.LastRouletteSlot) == multiplier, "Persisted slot and payout multiplier differ");
                if (multiplier == 2) doubles++; else if (multiplier == 0) losses++; else if (multiplier == 10) jackpots++; else Require(false, "Unknown payout multiplier");
                Near(5 * multiplier, model.LastSettledIncome, "Weighted draw paid the wrong stake");
            }
            Require(Math.Abs(doubles / (double)samples - .37) < .025 && Math.Abs(losses / (double)samples - .60) < .025
                && Math.Abs(jackpots / (double)samples - .03) < .025 && jackpots > 0, "6000 seeded draws deviate from configured weights");
            return samples + " deterministic draws: x2=" + doubles + ", loss=" + losses + ", x10=" + jackpots + "; all stored slots and gross payouts agree";
        }
        static LightsOutRentModel ReadyDay(int day, int[] levels)
        {
            var model = new LightsOutRentModel(); var profile = LightsOutBalanceSettings.Shared.ProfileForDay(day);
            CampaignSave saved = JsonUtility.FromJson<CampaignSave>(model.ExportJson());
            saved.stage = day; saved.remaining = profile.Duration;
            saved.plan = new LightsOutRentModel.StagePlan { columns = profile.Columns, floors = profile.Floors, roomWidth = profile.RoomWidth,
                duration = profile.Duration, rentDue = LightsOutRentModel.RentForStage(day) };
            var work = new LightsOutVerticalModel(profile.Floors, true, profile.RoomWidth, profile.Columns);
            work.PrepareShift(day, 1);
            WorkSave rich = JsonUtility.FromJson<WorkSave>(work.ExportJson()); rich.skills = levels; rich.money = rich.earned = 1000000;
            saved.work = JsonUtility.ToJson(rich); Require(model.ImportJson(JsonUtility.ToJson(saved)), "DAY gate fixture rejected"); return model;
        }
        static string RewardsAndDayGates()
        {
            var permanent = new LightsOutPermanentModel(); int total = 0;
            for (int remaining = 0; remaining <= 4; remaining++)
            {
                int day = remaining + 1, bonus = Math.Min(3, remaining);
                Require(permanent.PreviewEarlyBonus(day, remaining) == bonus && permanent.RecordRentPayment(day, remaining) == 1 + bonus, "Early-payment bonus differs at remaining " + remaining);
                total += 1 + bonus;
                Require(permanent.PreviewPaymentPoints(day, remaining) == bonus, "A recorded DAY retained its milestone point or lost its replay early bonus");
            }
            Near(total, permanent.Points, "Permanent point sum differs"); string preserved = permanent.ExportJson();
            var campaign = new LightsOutRentModel(permanent); campaign.Reset();
            Require(permanent.ExportJson() == preserved && permanent.PreviewPaymentPoints(1, 4) == 3 && permanent.PreviewPaymentPoints(5, 4) == 3,
                "Campaign reset erased milestones or disabled replay early bonuses");
            Require(campaign.StartReadyWork(), "Replay bonus fixture could not start work");
            CampaignSave replay = JsonUtility.FromJson<CampaignSave>(campaign.ExportJson());
            replay.work = LightsOutIncomeValidation.Number(LightsOutIncomeValidation.Number(replay.work, "money", 10000), "earned", 10000);
            Require(campaign.ImportJson(JsonUtility.ToJson(replay)) && campaign.EndAttempt(false) && campaign.SkipRoulette(), "Replay bonus fixture could not settle");
            Require(campaign.PayRent() && permanent.Points == total + 3 && permanent.HighestPaidDay == 5, "A real replay early payment did not earn exactly three points");
            string paid = permanent.ExportJson(), paidCampaign = campaign.ExportJson();
            Require(!campaign.PayRent() && permanent.ExportJson() == paid && campaign.ExportJson() == paidCampaign, "Repeated PayRent awarded points without a second real payment");
            var first = ReadyDay(1, Skills());
            for (int id = 0; id < LightsOutVerticalModel.Skills.Length; id++)
                Require(first.CanBuySkill(id) == (id == 0 || id == 22 || id == 23), "DAY 1 gate differs at skill " + id);
            Require(first.BuySkill(22) && first.BuySkill(22) && first.CanBuySkill(25), "DAY 1 instant chance did not unlock after work speed Lv.2");
            var prereqs = Skills(); prereqs[0] = 3; prereqs[10] = prereqs[11] = 2;
            var day5 = ReadyDay(5, prereqs);var day10 = ReadyDay(10, prereqs);
            Require(day5.CanBuySkill(1) && day5.CanBuySkill(10) && !day5.CanBuySkill(5) && !day5.CanBuySkill(20), "DAY 5 gate did not separate new and later branches");
            Require(day10.CanBuySkill(5) && day10.CanBuySkill(20) && day10.CanBuySkill(24) && !day10.CanBuySkill(21), "DAY 10 gate did not unlock chain/shrink/completion while locking late rise");
            return "Early bonuses 0/1/2/3/3 apply to every real payment, the highest-DAY point applies once and duplicate PayRent rejects; DAY 1/5/10 skill gates remain intact";
        }
    }
}
