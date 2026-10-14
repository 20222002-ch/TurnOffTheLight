using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Campaign contract checks. Uses isolated models, never scenes or PlayerPrefs.</summary>
    public static class LightsOutRentValidation
    {
        [Serializable] sealed class Report
        { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result
        { public string name; public bool passed; public string details; }
        [Serializable] sealed class Plan
        { public int columns, floors; public float roomWidth, duration; public double rentDue; }
        [Serializable] sealed class Snapshot
        {
            public int version, stage, phase, attempts, slot, multiplier;
            public float remaining;
            public double lastIncome;
            public bool lastCompleted, rouletteUsed, settled;
            public uint random;
            public string work;
            public Plan plan;
        }

        [UnityEditor.MenuItem("Lights Out/Run rent, timer and roulette checks")]
        static void RunFromMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            Report report = new Report();
            Check(report, "held-work opening mall and five-opportunity rent budget", OpeningDifficulty);
            Check(report, "timeout closes rewards without charging another opportunity", TimeoutOnce);
            Check(report, "full completion retains exactly one started opportunity", CompletionOnce);
            Check(report, "deadline caps simulation and rejects invalid elapsed time", DeadlineBoundary);
            Check(report, "attempt income stays separate from the bank until settlement", DeferredSettlement);
            Check(report, "roulette keeps 21 IDs with weighted x2 37%, loss 60%, x10 3%", RouletteContract);
            Check(report, "roulette stakes only this attempt and settles exactly once", RouletteScope);
            Check(report, "selected roulette outcome persists with no reload reroll", RoulettePersistence);
            Check(report, "early rent deducts exactly and carries upgrades and remaining cash", EarlyRent);
            Check(report, "upgrade-versus-rent choice remains available before the final opportunity", UpgradeTradeoff);
            Check(report, "fifth opportunity can pay rent and cannot spend its rent reserve", FinalRentReserve);
            Check(report, "fifth checkout allows a roulette rescue before failure", FifthRouletteRescue);
            Check(report, "five failed opportunities bankrupt and reset the campaign", BankruptcyAndReset);
            Check(report, "all five campaign phases and partial work round-trip", SavePhases);
            Check(report, "invalid campaign saves reject atomically", InvalidSaves);
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
        static Snapshot Read(LightsOutRentModel model) { return JsonUtility.FromJson<Snapshot>(model.ExportJson()); }
        static string Number(string json, string field, double value)
        {
            string pattern = "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)[^,}]+";
            Regex expression = new Regex(pattern);
            Require(expression.IsMatch(json), "Fixture field missing: " + field);
            return expression.Replace(json, match => match.Groups[1].Value + value.ToString("R", CultureInfo.InvariantCulture), 1);
        }
        static string Levels(string json, int[] skills)
        {
            Regex expression = new Regex("\"skills\"\\s*:\\s*\\[[^\\]]*\\]");
            Require(expression.IsMatch(json), "Fixture skill array missing");
            return expression.Replace(json, "\"skills\":[" + string.Join(",", Array.ConvertAll(skills, x => x.ToString(CultureInfo.InvariantCulture))) + "]", 1);
        }
        static LightsOutRentModel Fixture(double money = 0, int[] skills = null, uint seed = 97193711)
        {
            var model = new LightsOutRentModel(); Require(model.StartReadyWork(), "A new lobby could not start its first work opportunity"); Snapshot save = Read(model);
            save.random = seed;
            save.work = Number(Number(save.work, "money", money), "earned", money);
            if (skills != null) save.work = Levels(save.work, skills);
            Require(model.ImportJson(JsonUtility.ToJson(save)), "Valid isolated campaign fixture was rejected");
            return model;
        }
        static void Finish(LightsOutRentModel model)
        {
            Require(model.Phase == RentPhase.Working, "Finish fixture is not working");
            for (int floor = 0; floor < model.Work.FloorCount; floor++)
                for (int col = 0; col < model.Work.FloorRooms; col++) model.Work.Contact(floor, col);
            model.Tick(.001f); model.EndAttempt(true);
            Require(model.Phase == RentPhase.Checkout && model.LastCompleted, "A fully extinguished building did not enter completed checkout");
        }
        static void Expire(LightsOutRentModel model)
        {
            Require(model.Phase == RentPhase.Working, "Expire fixture is not working");
            model.Tick(model.TimeRemaining + 1);
            Require(model.Phase == RentPhase.Checkout, "Deadline did not open checkout");
        }
        static void ReachFifth(LightsOutRentModel model)
        {
            for (int i = 0; i < 4; i++)
            {
                Expire(model); Require(model.SkipRoulette(), "Earlier checkout could not settle");
                Require(model.StartNextAttempt(), "An unused opportunity could not start");
            }
            Require(model.AttemptsUsed == 5 && model.AttemptNumber == 5 && model.AttemptsRemaining == 0, "The fifth working opportunity was not consumed at start");
        }
        static double BuildingIncome(LightsOutVerticalModel work)
        { double total = 0; for (int floor = 0; floor < work.FloorCount; floor++) total += work.GetRoomReward(floor) * work.FloorRooms; return total; }

        static string OpeningDifficulty()
        {
            var model = Fixture();
            Require(model.Stage == 1 && model.AttemptsUsed == 1 && model.AttemptsRemaining == 4 && model.AttemptNumber == 1, "Starting work did not consume the first of five opportunities");
            Require(model.Work.FloorCount == 2 && model.Work.FloorRooms == 20 && model.TimeRemaining > 0 && model.RentDue > 0, "The opening mall is not twenty columns by two floors");
            Near(80, model.AttemptDuration, "The opening DAY does not allow eighty seconds");
            double gross = BuildingIncome(model.Work); Require(gross * 5 >= model.RentDue, "Even five complete baseline shifts cannot pay the first rent");
            var go = new GameObject("Isolated opening mall validation"); go.hideFlags = HideFlags.HideAndDontSave;
            float elapsed = 0; double paid = 0; var seen = new HashSet<int>();
            model.Work.Extinguished += e => { paid += e.Reward; foreach (int id in e.AffectedIds) Require(seen.Add(id), "The opening manual route paid a light twice"); };
            try
            {
                var mover = go.AddComponent<LightsOutMover>();
                mover.ResetMotion(new Vector2(-model.Work.HalfWidth + model.Work.RoomWidth * .5f, .9f)); mover.Initialize(model.Work);
                for (int floor = 0; floor < model.Work.FloorCount && model.Phase == RentPhase.Working; floor++)
                    for (int index = 0; index < model.Work.FloorRooms && model.Phase == RentPhase.Working; index++)
                    {
                        int column = floor % 2 == 0 ? index : model.Work.FloorRooms - 1 - index;
                        Vector2 target = new Vector2(-model.Work.HalfWidth + (column + .5f) * model.Work.RoomWidth, (floor + .5f) * 1.8f);
                        Walk(model, mover, target, ref elapsed);
                        while (model.Work.IsLightOn(floor, column) && model.Phase == RentPhase.Working)
                        { mover.StepInput(Vector2.zero, .05f); model.Tick(.05f); elapsed += .05f; }
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            model.EndAttempt(true);
            Require(model.LastCompleted && elapsed < model.AttemptDuration && seen.Count == 40, "An unupgraded held-work route cannot clear all forty opening rooms before its deadline");
            Near(paid, model.LastShiftIncome, "The complete manual route missed or duplicated income"); Near(0, model.Work.Money, "Unsettled opening work paid the bank early");
            return "20 columns x 2 floors cleared using baseline movement and one-second work in " + elapsed.ToString("0.0") + "s / 80s; all forty lights paid once";
        }
        static void Walk(LightsOutRentModel model, LightsOutMover mover, Vector2 target, ref float elapsed)
        {
            while (Vector2.Distance(mover.Position, target) > .001f && model.Phase == RentPhase.Working)
            {
                float dt = Math.Min(.05f, Vector2.Distance(mover.Position, target) / model.Work.MoveSpeed);
                mover.StepInput((target - mover.Position).normalized, dt); model.Tick(dt); elapsed += dt;
            }
        }
        static string TimeoutOnce()
        {
            var model = Fixture(); model.Work.Contact(0, 0); Expire(model);
            Require(model.AttemptsUsed == 1 && model.AttemptsRemaining == 4 && !model.LastCompleted && model.TimeRemaining == 0, "Incomplete timeout did not consume one opportunity");
            double income = model.LastShiftIncome; long off = model.Work.TotalOff;
            model.EndAttempt(false); model.EndAttempt(true); model.Tick(60);
            Require(model.AttemptsUsed == 1 && model.Work.TotalOff == off && !model.Work.Contact(0, 1), "A closed shift can consume another opportunity or earn more money");
            Near(income, model.LastShiftIncome, "Closed shift income changed after the deadline");
            return "Timeout closes work, consumes one of five, and repeated completion/timeout calls do nothing";
        }
        static string CompletionOnce()
        {
            var model = Fixture(); Finish(model); int used = model.AttemptsUsed;
            Require(used == 1 && model.AttemptsRemaining == 4, "Complete shift did not use exactly one opportunity");
            model.EndAttempt(true); model.EndAttempt(false); model.Tick(100);
            Require(model.AttemptsUsed == used && !model.StartNextAttempt() && !model.PayRent(), "Unsettled checkout changed stage/opportunity or accepted payment");
            Require(model.SkipRoulette() && model.StartNextAttempt() && model.AttemptsUsed == 2 && model.AttemptNumber == 2, "Next work did not charge exactly one opportunity at start");
            return "First completion stays at one used chance; starting the second work charges the second chance once";
        }
        static string DeadlineBoundary()
        {
            int[] skills = new int[LightsOutVerticalModel.Skills.Length]; skills[15] = 1; var model = Fixture(0, skills);
            Snapshot save = Read(model); save.remaining = .1f; Require(model.ImportJson(JsonUtility.ToJson(save)), "Short-deadline fixture rejected");
            string before = model.ExportJson(); model.Tick(float.NaN); model.Tick(float.PositiveInfinity); model.Tick(-1); model.Tick(0);
            Require(model.ExportJson() == before, "Invalid elapsed time altered a shift");
            float session = model.Work.SessionSeconds; model.Tick(1);
            Near(.1, model.Work.SessionSeconds - session, "Simulation ran beyond the actual remaining deadline");
            Require(model.Work.TotalOff == 0 && model.LastShiftIncome == 0 && model.Phase == RentPhase.Checkout, "A deadline overshoot granted post-deadline automatic income");
            Require(!model.Work.Contact(0, 0), "Direct room contact earned income after deadline");
            return "1-second overshoot simulates only 0.1 seconds; NaN/infinity/zero/negative ticks do nothing";
        }
        static string DeferredSettlement()
        {
            var model = Fixture(40); model.Work.Contact(0, 0); model.Work.Contact(0, 1);
            Near(40, model.Work.Money, "Unsettled shift income was added to the bank early");
            Require(!model.CanBuySkill(0) && !model.BuySkill(0), "Working shift can spend its unbanked income");
            Expire(model); Near(10, model.LastShiftIncome, "Two extinguished rooms did not become the shift stake");
            Require(!model.CanBuySkill(0) && !model.BuySkill(0), "Unresolved roulette checkout allows an upgrade");
            Require(model.SkipRoulette(), "Guaranteed settlement was rejected"); Near(50, model.Work.Money, "Guaranteed settlement altered old bank money");
            Require(model.SettlementResolved && !model.SkipRoulette() && !model.SpinRoulette(), "Already settled income can settle or gamble again");
            Near(50, model.Work.Money, "Repeated settlement duplicated income");
            Require(model.CanBuySkill(0), "Previously unbanked income did not become spendable after settlement");
            return "Bank 40 + this shift 10 -> 50 once; unbanked income cannot purchase a 50-cost upgrade";
        }
        static Dictionary<int, uint> OutcomeSeeds()
        {
            var seeds = new Dictionary<int, uint>(); var model = Fixture(100);
            model.Work.Contact(0, 0); Expire(model); Snapshot checkout = Read(model);
            for (uint sequence = 1; sequence <= 4096 && seeds.Count < 21; sequence++)
            {
                uint seed = unchecked(sequence * 2654435761u);
                checkout.random = seed; var trial = new LightsOutRentModel();
                Require(trial.ImportJson(JsonUtility.ToJson(checkout)) && trial.SpinRoulette(), "Seeded roulette fixture failed");
                Require(trial.LastRouletteSlot >= 0 && trial.LastRouletteSlot < 21, "Roulette selected a slot outside the 21-slot wheel");
                if (!seeds.ContainsKey(trial.LastRouletteSlot)) seeds.Add(trial.LastRouletteSlot, seed);
            }
            Require(seeds.Count == 21, "The seeded wheel could not reach all 21 outcomes"); return seeds;
        }
        static string RouletteContract()
        {
            int doubleSlots = 0, lostSlots = 0, jackpotSlots = 0;
            foreach (var item in OutcomeSeeds())
            {
                var model = Fixture(100, null, item.Value); model.Work.Contact(0, 0); Expire(model); Require(model.SpinRoulette(), "Seeded spin rejected");
                Require(model.LastRouletteSlot == item.Key, "A bank-neutral seed did not reproduce its selected slot");
                if (model.LastRouletteMultiplier == 2) doubleSlots++;
                else if (model.LastRouletteMultiplier == 0) lostSlots++;
                else if (model.LastRouletteMultiplier == 10) jackpotSlots++;
                else Require(false, "Wheel contains an unauthorized multiplier");
            }
            Require(doubleSlots == 10 && lostSlots == 10 && jackpotSlots == 1, "Wheel does not contain ten x2, ten loss, and one x10 outcomes");
            Near(.37, LightsOutRentModel.RouletteProbabilityForMultiplier(2), "Double-income probability differs");
            Near(.60, LightsOutRentModel.RouletteProbabilityForMultiplier(0), "Nothing probability differs");
            Near(.03, LightsOutRentModel.RouletteProbabilityForMultiplier(10), "Jackpot probability differs");
            double probability = 0;
            double degrees = 0;
            for (int slot = 0; slot < 21; slot++)
            {
                probability += LightsOutRentModel.RouletteProbabilityForSlot(slot);
                int visual = LightsOutRoulettePopup.VisualIndexForSlot(slot);
                Require(LightsOutRoulettePopup.SlotForVisualIndex(visual) == slot, "Alternating presentation changed a saved canonical ID");
                Require(LightsOutRoulettePopup.VisualIndexAtAngle(LightsOutRoulettePopup.AngleForSlot(slot)) == visual,
                    "A selected canonical sector does not land at its displayed visual centre");
                degrees += LightsOutRoulettePopup.WidthForVisualIndex(visual);
                if (visual < 20) Require(LightsOutRentModel.MultiplierForSlot(LightsOutRoulettePopup.SlotForVisualIndex(visual)) == (visual % 2 == 0 ? 2 : 0),
                    "Double and NOTHING sectors are not alternating");
            }
            Near(1, probability, "Canonical sector probabilities do not sum to one");
            Near(360, degrees, "Weighted visual sectors do not cover a complete wheel");
            return "All 21 canonical IDs reachable; group weights x2 37%, NOTHING 60%, x10 3%; normalized slot probability sum 1";
        }
        static string RouletteScope()
        {
            foreach (var item in OutcomeSeeds())
            {
                var model = Fixture(100, null, item.Value); model.Work.Contact(0, 0); Expire(model); Require(model.SpinRoulette(), "Spin rejected");
                double expected = 100 + 5 * model.LastRouletteMultiplier;
                Near(expected, model.Work.Money, "Roulette multiplied or lost the saved bank instead of only current shift income");
                int slot = model.LastRouletteSlot; model.SpinRoulette(); model.SkipRoulette(); model.EndAttempt(false);
                Near(expected, model.Work.Money, "Repeated checkout interaction paid twice");
                Require(model.LastRouletteSlot == slot && model.AttemptsUsed == 1 && model.RouletteUsed && model.SettlementResolved, "Resolved spin changed outcome or attempt count");
            }
            return "Bank 100 is preserved for every slot; this shift 5 settles to 0, 10, or 50 exactly once";
        }
        static string RoulettePersistence()
        {
            foreach (var item in OutcomeSeeds())
            {
                var original = Fixture(100, null, item.Value); original.Work.Contact(0, 0); Expire(original);
                var beforeSpin = new LightsOutRentModel(); Require(beforeSpin.ImportJson(original.ExportJson()), "Unresolved checkout reload rejected");
                Require(original.SpinRoulette() && beforeSpin.SpinRoulette(), "Reloaded unresolved checkout cannot spin");
                Require(original.LastRouletteSlot == beforeSpin.LastRouletteSlot, "Reloading unresolved checkout rerolled its RNG outcome");
                Near(original.Work.Money, beforeSpin.Work.Money, "Reloaded payout differs");
                var afterSpin = new LightsOutRentModel(); Require(afterSpin.ImportJson(original.ExportJson()), "Selected spin reload rejected");
                Require(afterSpin.LastRouletteSlot == original.LastRouletteSlot && !afterSpin.SpinRoulette() && !afterSpin.SkipRoulette(), "Reload allows reroll or duplicate settlement");
                Near(original.Work.Money, afterSpin.Work.Money, "Reloaded settled payout changed");
            }
            return "Unresolved saved RNG reproduces all 21 slots; selected results persist and cannot reroll";
        }
        static string EarlyRent()
        {
            int[] skills = new int[LightsOutVerticalModel.Skills.Length]; skills[0] = 3; var model = Fixture(1000, skills); var work = model.Work;
            Expire(model); Require(model.SkipRoulette() && model.CanPayRent, "Affordable first checkout cannot pay rent");
            double bank = model.Work.Money, rent = model.RentDue; int floors = model.Work.FloorCount;
            Require(model.PayRent(), "Early rent payment was rejected");
            Require(ReferenceEquals(work, model.Work), "Stage change replaced the live work model and its event subscriptions");
            Near(bank - rent, model.Work.Money, "Rent did not deduct exactly and carry the remaining cash");
            Require(model.Stage == 2 && model.AttemptsUsed == 0 && model.AttemptsRemaining == 5 && model.Phase == RentPhase.Lobby,
                "Early payment did not discard remaining chances and prepare a new five-opportunity DAY lobby");
            Require(model.Work.SkillLevels[0] == 3 && model.Work.FloorCount == LightsOutRentModel.FloorsForStage(2) && model.Work.FloorRooms == LightsOutRentModel.ColumnsForStage(2) && model.RentDue > rent,
                "New DAY lost upgrades or failed to adopt the intended new building/rent");
            Require(!model.PayRent(), "The same stage payment could execute twice");
            return "Early payment subtracts rent, carries cash/upgrades, increases building/rent, and starts five fresh opportunities";
        }
        static string UpgradeTradeoff()
        {
            var model = Fixture(LightsOutRentModel.RentForStage(1) + 20); Expire(model); Require(model.SkipRoulette() && model.CanPayRent, "Tradeoff fixture cannot afford rent");
            double cost = model.Work.GetSkillCost(0), bank = model.Work.Money;
            Require(model.CanBuySkill(0) && model.BuySkill(0), "An early opportunity incorrectly forces rent reservation");
            Near(bank - cost, model.Work.Money, "Upgrade did not deduct its exact cost");
            Require(!model.CanPayRent && model.AttemptsRemaining == 4 && model.StartNextAttempt(), "Upgrading instead of paying did not leave remaining work opportunities");
            Require(model.Work.SkillLevels[0] == 1, "Purchased upgrade was lost at the next opportunity");
            return "A bank twenty above rent can invest fifty, postpone payment and use its remaining opportunities with stronger movement";
        }
        static string FinalRentReserve()
        {
            double boundary = LightsOutRentModel.RentForStage(1) + LightsOutVerticalModel.Skills[0].BaseCost;
            var model = Fixture(boundary); ReachFifth(model); Expire(model); Require(model.SkipRoulette(), "Fifth guaranteed settlement rejected");
            Require(model.Phase == RentPhase.BetweenAttempts && model.CanPayRent && !model.StartNextAttempt(), "A solvent final shift cannot pay rent or incorrectly grants a sixth chance");
            Require(model.CanBuySkill(0) && model.BuySkill(0), "A final upgrade leaving exactly enough rent was rejected"); Near(model.RentDue, model.Work.Money, "Boundary upgrade did not leave exact rent");
            Require(!model.CanBuySkill(22) && !model.BuySkill(22) && !string.IsNullOrEmpty(model.GetSkillLockReason(22)), "A final DAY-one-legal upgrade spent the protected rent reserve");
            Require(model.PayRent() && model.Stage == 2, "Solvent final opportunity cannot advance after safe upgrade");
            var belowBoundary = Fixture(boundary - .01); ReachFifth(belowBoundary); Expire(belowBoundary); belowBoundary.SkipRoulette();
            Require(!belowBoundary.CanBuySkill(0) && !belowBoundary.BuySkill(0), "A sub-rent remainder was allowed at the final opportunity");
            return "No sixth chance; an upgrade leaving exactly rent is allowed, leaving one cent less is blocked; final payment advances";
        }
        static string FifthRouletteRescue()
        {
            Dictionary<int, uint> seeds = OutcomeSeeds();
            uint jackpotSeed = seeds[20]; var model = Fixture(0, null, jackpotSeed); ReachFifth(model); Finish(model);
            Require(model.AttemptsUsed == 5 && model.AttemptsRemaining == 0 && model.Phase == RentPhase.Checkout && !model.SettlementResolved,
                "Fifth work bankrupts before its last roulette choice");
            Require(model.SpinRoulette() && model.LastRouletteMultiplier == 10 && model.Phase == RentPhase.BetweenAttempts && model.CanPayRent,
                "The final jackpot cannot rescue an otherwise insufficient bank");
            Require(model.PayRent() && model.Stage == 2 && model.AttemptsRemaining == 5, "Rescued fifth shift cannot pay and advance");
            var loss = Fixture(100, null, seeds[10]); ReachFifth(loss); Finish(loss);
            Require(loss.SpinRoulette() && loss.LastRouletteMultiplier == 0 && loss.Phase == RentPhase.Bankrupt,
                "An insufficient final roulette loss did not bankrupt after its selected result");
            Near(100, loss.Work.Money, "Final roulette loss incorrectly removed money from earlier shifts");
            Require(!loss.SpinRoulette() && !loss.StartNextAttempt(), "A final loss permits a reroll or sixth opportunity");
            return "Fifth checkout waits for its choice: x10 rescues rent; x0 fails after resolution and preserves the earlier bank";
        }
        static string BankruptcyAndReset()
        {
            var model = Fixture(); var work = model.Work; ReachFifth(model); Expire(model);
            Require(model.Phase == RentPhase.Checkout, "Fifth deadline failed before settlement");
            Require(model.SkipRoulette() && model.Phase == RentPhase.Bankrupt && model.AttemptsUsed == 5, "Five unpaid opportunities did not bankrupt after settlement");
            Require(!model.PayRent() && !model.StartNextAttempt() && !model.SpinRoulette() && !model.BuySkill(0), "Bankruptcy allows further play, gambling, or payment");
            model.Reset(); Require(ReferenceEquals(work, model.Work), "Reset replaced the live work model");
            Require(model.Stage == 1 && model.AttemptsUsed == 0 && model.AttemptsRemaining == 5 && model.Work.Money == 0 && model.Phase == RentPhase.Lobby,
                "Bankruptcy reset did not restart the campaign from the beginning");
            foreach (int level in model.Work.SkillLevels) Require(level == 0, "Restart retained failed-campaign upgrades");
            return "Five unpaid settled opportunities bankrupt; reset starts stage 1 with zero bank/upgrades and five new chances";
        }
        static void Same(LightsOutRentModel a, LightsOutRentModel b)
        {
            Require(a.Phase == b.Phase && a.Stage == b.Stage && a.AttemptsUsed == b.AttemptsUsed && a.AttemptsRemaining == b.AttemptsRemaining &&
                a.LastCompleted == b.LastCompleted && a.LastRouletteSlot == b.LastRouletteSlot && a.LastRouletteMultiplier == b.LastRouletteMultiplier &&
                a.RouletteUsed == b.RouletteUsed && a.SettlementResolved == b.SettlementResolved, "Campaign counters/outcome differ after load");
            Near(a.TimeRemaining, b.TimeRemaining, "Remaining timer differs"); Near(a.LastShiftIncome, b.LastShiftIncome, "Shift income differs");
            Near(a.AttemptDuration, b.AttemptDuration, "Saved DAY duration differs"); Near(a.RentDue, b.RentDue, "Saved DAY rent differs");
            Near(a.Work.Money, b.Work.Money, "Bank differs"); Near(a.Work.TotalEarned, b.Work.TotalEarned, "Cumulative earnings differ");
            Near(a.Work.BaseRoomWidth, b.Work.BaseRoomWidth, "Base building width differs");
            Require(a.Work.FloorCount == b.Work.FloorCount && a.Work.TotalOff == b.Work.TotalOff && a.Work.PendingEventCount == b.Work.PendingEventCount, "Work building/progression differs");
            for (int i = 0; i < LightsOutVerticalModel.Skills.Length; i++) Require(a.Work.SkillLevels[i] == b.Work.SkillLevels[i], "Upgrade differs after load");
            for (int floor = 0; floor < a.Work.FloorCount; floor++) for (int col = 0; col < a.Work.FloorRooms; col++)
                Require(a.Work.IsLightOn(floor, col) == b.Work.IsLightOn(floor, col), "Light state differs after load");
        }
        static void RoundTrip(LightsOutRentModel original)
        {
            var loaded = new LightsOutRentModel(); var persistentWork = loaded.Work;
            Require(loaded.ImportJson(original.ExportJson()) && ReferenceEquals(persistentWork, loaded.Work), "Valid phase save rejected or replaced live work reference");
            Same(original, loaded);
            for (int i = 0; i < 30; i++) { original.Tick(.1f); loaded.Tick(.1f); Same(original, loaded); }
        }
        static string SavePhases()
        {
            int[] skills = new int[LightsOutVerticalModel.Skills.Length]; skills[5] = 5; skills[15] = 1;
            var lobby = new LightsOutRentModel(); RoundTrip(lobby);
            var working = Fixture(100, skills); working.Work.Contact(0, 0); working.Tick(.04f);
            Require(working.Work.PendingEventCount > 0 && working.Work.AutomationProgress > 0, "Working save lacks queued chain/fractional automatic progress"); RoundTrip(working);
            var checkout = Fixture(100); checkout.Work.Contact(0, 0); Expire(checkout); RoundTrip(checkout);
            var between = Fixture(100); Expire(between); between.SkipRoulette(); RoundTrip(between);
            var bankrupt = Fixture(); ReachFifth(bankrupt); Expire(bankrupt); bankrupt.SkipRoulette(); RoundTrip(bankrupt);
            return "Lobby, working (queue/fractional work), unresolved checkout, settled between-attempts and bankruptcy all persist without replacing model references";
        }
        static string InvalidSaves()
        {
            var model = Fixture(100); model.Work.Contact(0, 0); string before = model.ExportJson(); int rejected = 0;
            Action<Action<Snapshot>> reject = mutate =>
            {
                Snapshot saved = JsonUtility.FromJson<Snapshot>(before); mutate(saved);
                Require(!model.ImportJson(JsonUtility.ToJson(saved)), "Invalid campaign snapshot accepted");
                Require(model.ExportJson() == before, "Rejected campaign snapshot partially changed live progress"); rejected++;
            };
            Require(!model.ImportJson(null) && !model.ImportJson("not json"), "Malformed JSON accepted");
            reject(s => s.version = 0); reject(s => s.stage = 0); reject(s => s.phase = 99); reject(s => s.attempts = -1); reject(s => s.attempts = 6);
            reject(s => s.remaining = -1); reject(s => s.remaining = 99999); reject(s => s.lastIncome = -1); reject(s => s.slot = 21);
            reject(s => s.work = "not json"); reject(s => s.work = Number(s.work, "money", -1));
            reject(s => s.plan.duration = 0); reject(s => s.plan.columns = 1); reject(s => s.plan.rentDue = -1);
            reject(s => s.settled = true); reject(s => s.rouletteUsed = true);
            Require(model.ExportJson() == before, "Malformed save rejection changed live state");
            return "Malformed JSON and " + rejected + " invalid campaign/work contracts rejected atomically";
        }
    }
}
