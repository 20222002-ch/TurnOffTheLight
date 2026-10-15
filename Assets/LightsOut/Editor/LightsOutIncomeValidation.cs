using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Isolated income/work checks. Rule overrides use a temporary clone and never dirty the authored asset.</summary>
    public static class LightsOutIncomeValidation
    {
        [Serializable] sealed class Report { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result { public string name; public bool passed; public string details; }
        [Serializable] sealed class IncomeRead { public LightsOutIncomeState income; }
        [Serializable] sealed class WorkProgressSave { public int version = 1, run, floors, columns; public float roomWidth, duration; public ProgressEntry[] roomstates; }
        [Serializable] sealed class ProgressEntry { public int id; public float elapsed, duration, retentionremaining; }
        public sealed class RulesScope : IDisposable
        {
            readonly LightsOutBalanceSettings original;
            readonly FieldInfo sharedField;
            public LightsOutBalanceSettings Config { get; private set; }
            public RulesScope(bool pure = true)
            {
                original = LightsOutBalanceSettings.Shared;
                sharedField = typeof(LightsOutBalanceSettings).GetField("shared", BindingFlags.NonPublic | BindingFlags.Static);
                if (sharedField == null) throw new InvalidOperationException("Balance cache field is unavailable");
                Config = UnityEngine.Object.Instantiate(original); Config.hideFlags = HideFlags.HideAndDontSave;
                if (pure)
                {
                    Config.BuildingGradeWeights = new float[] { 0, 1, 0, 0 };
                    Config.NormalBonusWeights = new float[] { 1, 0, 0, 0 };
                    Config.MinimumCashBags = Config.MaximumCashBags = Config.CashBagCountBonus = 0;
                    Config.SpecialRoomProbabilities = new float[6]; Config.SpecialChancePerDay = 0;
                    Config.InstantExtinguishChances = new float[6];
                }
                sharedField.SetValue(null, Config);
            }
            public void Dispose()
            {
                if (!Config) return;
                sharedField.SetValue(null, original); UnityEngine.Object.DestroyImmediate(Config); Config = null;
            }
        }
        public static string WithPureRules(Func<string> run)
        { using (var scope = new RulesScope()) return run(); }
        [UnityEditor.MenuItem("Lights Out/Run income, first-entry and late movement checks")]
        static void RunFromMenu() { Debug.Log(Run()); }
        public static string Run()
        {
            var report = new Report();
            Check(report, "minimum work time remains positive and security work takes twice as long", MinimumWork);
            Check(report, "instant contact attempts cannot reroll after leaving or reloading", FirstEntryPersistence);
            Check(report, "expanded work range advances one nearest target only", SingleRangeTarget);
            Check(report, "legacy partial work migrates by its normalized ratio", ProgressMigration);
            Check(report, "late up-only movement does not automatically clear the whole tower", LateUpOnly);
            Check(report, "normal bonus outcomes follow their saved weighted distribution", BonusDistribution);
            Check(report, "building grade generation follows 20/50/25/5 and survives saves", GradeDistribution);
            Check(report, "hidden cash is unique, normal-room-only and one-shot", CashContract);
            Check(report, "signed grade and bonus ledger adds exactly to banked income", ExactLedger);
            Check(report, "experience categories are exclusive and permanent experience survives reset", ExperienceContract);
            Check(report, "electrical, linked, generator and faulty effects execute once within their limits", SpecialEffects);
            Check(report, "special generation respects captured unlock levels and preserves its saved layout", SpecialUnlocks);
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
        public static string Number(string json, string field, double value)
        {
            var expression = new Regex("(\"" + Regex.Escape(field) + "\"\\s*:\\s*)[^,}]+");
            Require(expression.IsMatch(json), "Missing fixture field " + field);
            return expression.Replace(json, m => m.Groups[1].Value + value.ToString("R", CultureInfo.InvariantCulture), 1);
        }
        public static string Levels(string json, int[] levels)
        {
            var expression = new Regex("\"skills\"\\s*:\\s*\\[[^\\]]*\\]");
            Require(expression.IsMatch(json), "Fixture skills are absent");
            return expression.Replace(json, "\"skills\":[" + string.Join(",", Array.ConvertAll(levels, n => n.ToString(CultureInfo.InvariantCulture))) + "]", 1);
        }
        static uint RandomState(LightsOutVerticalModel model)
        { return uint.Parse(Regex.Match(model.ExportJson(), "\"random\"\\s*:\\s*(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture); }
        static LightsOutIncomeState Income(LightsOutVerticalModel model)
        { var state = JsonUtility.FromJson<IncomeRead>(model.ExportJson()); Require(state != null && state.income != null, "Work save lacks income state"); return state.income; }
        static string ReplaceIncome(string json, LightsOutIncomeState income)
        {
            int name = json.IndexOf("\"income\"", StringComparison.Ordinal), start = name < 0 ? -1 : json.IndexOf('{', name);
            Require(start >= 0, "Income object is missing"); int depth = 0, finish = -1; bool quoted = false, escaped = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') quoted = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) { finish = i + 1; break; }
            }
            Require(finish > start, "Income JSON object is incomplete"); return json.Substring(0, start) + JsonUtility.ToJson(income) + json.Substring(finish);
        }
        static LightsOutVerticalModel Work(int floors = 2, int rooms = 20, int[] levels = null, bool deferred = false)
        {
            var model = new LightsOutVerticalModel(floors, deferred, 2.4f, rooms);
            model.PrepareIncome(); string save = Number(model.ExportJson(), "random", 97193711);
            if (levels != null) save = Levels(save, levels);
            Require(model.ImportJson(save), "Income work fixture rejected"); return model;
        }
        static void ForceRoom(LightsOutVerticalModel model, int id, RoomSpecialKind kind, float special = 0, float cash = 0)
        {
            var state = Income(model); state.prepared = true; state.kinds[id] = (int)kind; state.specialFactors[id] = special;
            state.cashFactors[id] = cash; state.workMultipliers[id] = kind == RoomSpecialKind.Security ? 2 : 1;
            Require(model.ImportJson(ReplaceIncome(model.ExportJson(), state)), "Forced room fixture rejected");
        }
        static Vector2 Centre(LightsOutVerticalModel model, int floor = 0, int column = 0)
        { return new Vector2(-model.HalfWidth + (column + .5f) * model.RoomWidth, (floor + .5f) * LightsOutMover.FloorHeight); }
        static void WithMover(LightsOutVerticalModel model, Action<LightsOutMover> test)
        {
            var item = new GameObject("Isolated income work validation") { hideFlags = HideFlags.HideAndDontSave };
            try { var mover = item.AddComponent<LightsOutMover>(); mover.ResetMotion(Centre(model)); mover.Initialize(model); test(mover); }
            finally { UnityEngine.Object.DestroyImmediate(item); }
        }
        static string MinimumWork()
        {
            using (var rules = new RulesScope())
            {
                float[] expected = { 1, .8f, .6f, .45f, .3f, .2f, .15f };
                for (int level = 0; level < 7; level++) Near(expected[level], rules.Config.ExtinguishSecondsForLevel(level), "Speed table differs at level " + level);
                rules.Config.ExtinguishSeconds[6] = 0; Near(.15, rules.Config.ExtinguishSecondsForLevel(6), "Zero Inspector value bypassed the minimum");
                var levels = new int[LightsOutVerticalModel.Skills.Length]; levels[22] = 6;
                var normal = Work(levels: levels); WithMover(normal, mover => { mover.StepInput(Vector2.zero, .14f); Require(normal.IsLightOn(0, 0), "Level-six speed became guaranteed instant"); mover.StepInput(Vector2.zero, .02f); Require(!normal.IsLightOn(0, 0), "Minimum work did not finish"); });
                var security = Work(levels: levels); ForceRoom(security, 0, RoomSpecialKind.Security, 3);
                Near(.3, security.GetRoomWorkDuration(0, 0), "Security duration is not twice the final minimum");
                WithMover(security, mover => { mover.StepInput(Vector2.zero, .29f); Require(security.IsLightOn(0, 0), "Security completed early"); Near(.29 / .3, mover.WorkProgress, "Security gauge used the base duration"); mover.StepInput(Vector2.zero, .02f); Require(!security.IsLightOn(0, 0), "Security did not complete after its own duration"); });
                return "All seven speed levels remain positive, Inspector zero clamps to 0.15 seconds, security uses a 0.30-second gauge";
            }
        }
        static string FirstEntryPersistence()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.InstantExtinguishChances = new float[] { 0, .05f, .1f, .15f, .2f, .25f };
                var levels = new int[LightsOutVerticalModel.Skills.Length]; levels[22] = 2; levels[25] = 5; var model = Work(levels: levels);
                Require(!model.TryInstantContact(0, 0) && model.IsLightOn(0, 0), "The deterministic first-entry failure fixture unexpectedly won");
                uint savedRandom = RandomState(model); for (int i = 0; i < 20; i++) Require(!model.TryInstantContact(0, 0), "Same room rerolled a failed trial");
                Require(RandomState(model) == savedRandom && Income(model).instantAttempts[0], "Repeated trials consumed randomness or lost the saved attempt flag");
                WithMover(model, mover =>
                {
                    mover.StepInput(Vector2.zero, .2f); string progress = mover.ExportWorkJson(), work = model.ExportJson();
                    var loaded = Work(levels: levels); Require(loaded.ImportJson(work), "Trial snapshot rejected");
                    WithMover(loaded, restored =>
                    {
                        Require(restored.ImportWorkJson(progress), "Retained trial progress rejected"); uint before = RandomState(loaded);
                        restored.StepInput(Vector2.zero, .01f); Require(RandomState(loaded) == before && loaded.IsLightOn(0, 0), "Reload gave a fresh instant attempt");
                        restored.transform.position = new Vector3(Centre(loaded, 0, 1).x, .9f, -1); restored.StepInput(Vector2.zero, 1.6f);
                        restored.transform.position = new Vector3(Centre(loaded).x, .9f, -1); before = RandomState(loaded); restored.StepInput(Vector2.zero, .01f);
                        Require(RandomState(loaded) == before && loaded.IsLightOn(0, 0), "Expired partial work reset its model-owned instant trial");
                    });
                });
                var success = Work(levels: levels); Require(success.ImportJson(Number(success.ExportJson(), "random", 1)), "Success seed rejected");
                Require(success.TryInstantContact(0, 0) && !success.IsLightOn(0, 0) && !success.TryInstantContact(0, 0), "Successful trial did not credit exactly once");
                return "A failed first entry never rerolls after 20 retries, expiry or model/progress reload; seeded success pays once";
            }
        }
        static string SingleRangeTarget()
        {
            using (var rules = new RulesScope())
            {
                var levels = new int[LightsOutVerticalModel.Skills.Length]; levels[0] = 3; levels[1] = 5;
                var model = Work(2, 4, levels); model.Contact(0, 0);
                WithMover(model, mover => { mover.StepInput(Vector2.zero, .4f); Require(mover.RetainedWorkCount == 1 && mover.WorkingRoomId >= 0, "Range either did nothing or advanced multiple overlaps"); Near(.4, mover.WorkElapsed, "Nearest target did not receive exactly one timestep"); Require(model.TotalOff == 1, "Range completed extra rooms before a full duration"); });
                return "An extinguished physical room targets one nearby lit room through the upgraded range; one state and one timestep only";
            }
        }
        static string ProgressMigration()
        {
            using (var rules = new RulesScope())
            {
                var levels = new int[LightsOutVerticalModel.Skills.Length]; levels[22] = 6; var model = Work(2, 4, levels);
                WithMover(model, mover =>
                {
                    var old = new WorkProgressSave { run = model.RunNumber, floors = 2, columns = 4, roomWidth = model.RoomWidth, duration = .65f,
                        roomstates = new[] { new ProgressEntry { id = 0, elapsed = .325f, retentionremaining = 1.5f } } };
                    Require(mover.ImportWorkJson(JsonUtility.ToJson(old)), "Version-one work save rejected instead of migrating");
                    Near(.5, mover.WorkProgress, "Migration changed the normalized work ratio"); Near(.075, mover.WorkElapsed, "Migration did not scale to the current minimum");
                    string migrated = mover.ExportWorkJson(); mover.CancelWork(); Require(mover.ImportWorkJson(migrated), "Version-two room duration did not round-trip");
                    Near(.5, mover.WorkProgress, "Modern duration round-trip changed work ratio");
                    Require(!mover.ImportWorkJson(Number(migrated, "roomWidth", 8)) && mover.ExportWorkJson() == migrated, "Wrong geometry replaced valid migrated progress");
                });
                return "Legacy 0.325/0.65-second work becomes 0.075/0.15 without loss, modern duration round-trips and invalid geometry rejects atomically";
            }
        }
        static string LateUpOnly()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.InstantExtinguishChances = new float[] { 0, .05f, .1f, .15f, .2f, .25f };
                var levels = new int[LightsOutVerticalModel.Skills.Length];
                for (int i = 0; i < levels.Length; i++) levels[i] = i >= 15 && i <= 19 ? 0 : LightsOutVerticalModel.Skills[i].MaxLevel;
                var model = Work(60, 4, levels); WithMover(model, mover =>
                {
                    for (int step = 0; step < 2400 && mover.Position.y < 60 * 1.8f - .2201f && !model.IsComplete; step++) { mover.StepInput(Vector2.up, 1f / 60); model.Tick(1f / 60); }
                    Require(!model.IsComplete && model.TotalOff < 60 * 4 * .85, "Up-only input cleared at least 85 percent of the entire tower without horizontal work");
                    Require(mover.Position.y >= 60 * 1.8f - .221f, "Late route did not reach the top boundary");
                });
                return model.TotalOff + "/240 rooms after reaching the top with up-only input, maximum non-automatic skills and preserved minimum dwell";
            }
        }
        static string BonusDistribution()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.NormalBonusWeights = new float[] { .65f, .25f, .08f, .02f }; rules.Config.ComboMultipliers = new float[] { 1, 1, 1, 1 };
                var model = Work(300, 20); int[] counts = new int[4];
                for (int id = 0; id < 6000; id++)
                {
                    Require(model.Contact(id / 20, id % 20), "Normal bonus room rejected");
                }
                var state = Income(model); float[] factors = { 0, .5f, 2, 10 };
                for (int id = 0; id < state.randomFactors.Length; id++) { int tier = Array.IndexOf(factors, state.randomFactors[id]); Require(tier >= 0, "Unexpected saved random factor"); counts[tier]++; }
                double[] expected = { .65, .25, .08, .02 }; for (int i = 0; i < 4; i++) Require(Math.Abs(counts[i] / 6000.0 - expected[i]) < .025, "Normal bonus distribution deviated at tier " + i);
                string saved = model.ExportJson(); var loaded = Work(); Require(loaded.ImportJson(saved), "Bonus outcomes did not save"); Near(model.Money, loaded.Money, "Saved normal bonus income changed");
                return "6000 saved normal outcomes: " + string.Join("/", counts) + "; 65/25/8/2 weights and realized money survive reload";
            }
        }
        static string GradeDistribution()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.BuildingGradeWeights = new float[] { .2f, .5f, .25f, .05f }; var model = Work(2, 4); int[] counts = new int[4];
                for (int i = 0; i < 2000; i++)
                {
                    Require(model.BeginBuilding(2, 2.4f, 4) && model.StartAccessoryEffects(), "Grade generation rejected a fresh building");
                    var state = Income(model); Require(state.grade >= 0 && state.grade < 4, "Unknown building grade"); counts[state.grade]++;
                    Near(rules.Config.BuildingGradeMultipliers[state.grade], state.stats.GradeMultiplier, "Grade and multiplier disagree");
                }
                double[] expected = { .2, .5, .25, .05 }; for (int i = 0; i < 4; i++) Require(Math.Abs(counts[i] / 2000.0 - expected[i]) < .035, "Grade distribution deviated at tier " + i);
                var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()), "Grade snapshot rejected"); Require(Income(loaded).grade == Income(model).grade, "Reload rerolled grade");
                return "2000 sequential seeded buildings: " + string.Join("/", counts) + "; grade probabilities, multipliers and reload identity agree";
            }
        }
        static string CashContract()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.MinimumCashBags = 1; rules.Config.MaximumCashBags = 3; rules.Config.SpecialRoomProbabilities[0] = .5f;
                rules.Config.SpecialUnlockLevels[0] = 1; var model = Work(2, 20); int bags = 0; var state = Income(model);
                for (int id = 0; id < state.kinds.Length; id++)
                    if (state.cashFactors[id] > 0) { bags++; Require(state.kinds[id] == 0, "Cash overlaps a special room"); Require(state.cashFactors[id] >= 5 && state.cashFactors[id] <= 20, "Cash factor is out of range"); }
                Require(bags >= 1 && bags <= 3, "Cash generation did not choose one to three unique normal rooms");
                var cashIds = new List<int>(); for (int id = 0; id < state.cashFactors.Length; id++) if (state.cashFactors[id] > 0) cashIds.Add(id);
                foreach (int id in cashIds) { Require(model.Contact(id / 20, id % 20), "Cash room rejected"); double paid = model.Money; Require(!model.Contact(id / 20, id % 20), "Cash room could be harvested twice"); Near(paid, model.Money, "Cash paid twice"); }
                var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()), "Cash snapshot rejected"); Near(Income(model).stats.CashBonus, Income(loaded).stats.CashBonus, "Reload changed discovered cash");
                return bags + " cash bags occupy distinct normal rooms, remain in range and pay once across reload";
            }
        }
        static string ExactLedger()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.NormalBonusWeights = new float[] { 0, 1, 0, 0 };
                var levels = new int[LightsOutVerticalModel.Skills.Length]; for (int i = 0; i < 5; i++) levels[i] = LightsOutVerticalModel.Skills[i].MaxLevel;
                var model = Work(2, 20, levels, true); var state = Income(model); state.grade = 0; state.stats.GradeName = "불량 건물"; state.stats.GradeMultiplier = .8;
                Require(model.ImportJson(ReplaceIncome(model.ExportJson(), state)), "Signed grade fixture rejected"); ForceRoom(model, 0, RoomSpecialKind.Normal, cash: 5); ForceRoom(model, 1, RoomSpecialKind.Golden, special: 4);
                for (int id = 0; id < 6; id++) Require(model.Contact(0, id), "Ledger room rejected");
                state = Income(model); var receipt = state.stats;
                double sum = receipt.RawBaseIncome + receipt.GradeAdjustment + receipt.RandomBonus + receipt.CashBonus + receipt.SpecialBonus + receipt.ComboBonus + receipt.SkillBonus;
                Require(receipt.GradeAdjustment < 0 && receipt.RandomBonus > 0 && receipt.CashBonus > 0 && receipt.SpecialBonus > 0 && receipt.ComboBonus > 0 && receipt.SkillBonus > 0,
                    "Ledger fixture did not exercise every signed and positive row"); Near(sum, model.PendingIncome, "Ledger rows do not add to the exact gross");
                model.CloseBuilding(); Require(model.SettlePending(2), "Ledger double settlement rejected"); Near(sum * 2, model.Money, "Roulette multiplier did not apply to the exact row sum");
                var loaded = Work(); Require(loaded.ImportJson(model.ExportJson()), "Settled ledger snapshot rejected"); Near(sum, Income(loaded).stats.TotalIncome, "Reload changed the signed receipt");
                return "Negative grade adjustment plus base/random/cash/special/combo/skill rows equal gross exactly; x2 settlement and reload preserve the sum";
            }
        }
        static string ExperienceContract()
        {
            using (var rules = new RulesScope())
            {
                var levels = new int[LightsOutVerticalModel.Skills.Length]; levels[23] = 1;
                var model = Work(3, 4, levels); ForceRoom(model, 1, RoomSpecialKind.Golden, special: 4);
                Require(model.Contact(0, 0) && model.Contact(0, 1), "Experience origins rejected"); var stats = Income(model).stats;
                Require(stats.DirectCount == 2 && stats.PropagatedCount == 2 && stats.TotalCount == 4, "Normal/special propagated room identity overlaps");
                Require(stats.XPDirect == 1 && stats.XPPropagation == 2 && stats.XPSpecial == 3, "Special XP stacked with normal direct XP");
                int first = stats.ShiftExperienceTotal; double paid = model.Money; Require(!model.Contact(0, 1), "Special XP could be repeated");
                Near(paid, model.Money, "Repeated special completion paid again"); Require(Income(model).stats.ShiftExperienceTotal == first, "Repeated special completion granted more XP");
                for (int floor = 0; floor < 3; floor++) for (int column = 0; column < 4; column++) model.Contact(floor, column);
                Require(model.AwardBuildingExperience() && !model.AwardBuildingExperience(), "Building experience was not a one-shot end-of-work award");
                stats = Income(model).stats; Require(stats.XPFloors == 6 && stats.XPBuilding == 10, "Floor/building XP was not awarded exactly once");
                var permanent = new LightsOutPermanentModel(); permanent.AddExperience(stats.ShiftExperienceTotal); string saved = permanent.ExportJson(); long total = permanent.TotalExperience;
                var campaign = new LightsOutRentModel(permanent); campaign.Reset(); Require(permanent.TotalExperience == total, "Career reset erased permanent experience");
                var loaded = new LightsOutPermanentModel(); Require(loaded.ImportJson(saved) && loaded.TotalExperience == total && loaded.Level == permanent.Level, "Permanent XP/level did not round-trip");
                return "Normal, propagated and special XP are exclusive; three floors and one building award once, accumulated XP/level survive reset and reload";
            }
        }
        static string SpecialEffects()
        {
            using (var rules = new RulesScope())
            {
                var electrical = Work(2, 9); ForceRoom(electrical, 0, RoomSpecialKind.Electrical);
                Require(electrical.Contact(0, 0) && electrical.TotalRoomOff == 1 && electrical.PendingEventCount >= 3, "Electrical room did not schedule its neighbours");
                electrical.Tick(.23f); Require(electrical.TotalRoomOff >= 4 && electrical.TotalRoomOff <= 7 && electrical.PendingEventCount == 0,
                    "Electrical room exceeded or missed its three-to-six neighbour budget");
                var linked = Work(2, 9); ForceRoom(linked, 0, RoomSpecialKind.Linked);
                Require(linked.Contact(0, 0) && linked.TotalRoomOff == 1 && linked.PendingEventCount >= 2, "Linked room did not queue a delayed sequence");
                linked.Tick(.081f); Require(linked.TotalRoomOff == 2 && linked.PendingEventCount > 0, "Linked sequence became one simultaneous or recursive batch");
                linked.Tick(.3f); Require(linked.TotalRoomOff >= 3 && linked.TotalRoomOff <= 4 && linked.PendingEventCount == 0, "Linked sequence did not stop after two-to-three neighbours");
                var career = new LightsOutRentModel(); Require(career.StartReadyWork(), "Generator career could not start"); float before = career.TimeRemaining;
                for (int column = 0; column < 12; column++) { ForceRoom(career.Work, column, RoomSpecialKind.Generator); Require(career.Work.Contact(0, column), "Generator fixture rejected"); }
                Near(30, career.TimeExtended, "Generator time exceeded or missed the thirty-second extension cap"); Near(before + 30, career.TimeRemaining, "Generator extension changed the wrong timer");
                Require(!career.Work.Contact(0, 0), "Generator could grant time twice"); Near(before + 30, career.TimeRemaining, "Repeated generator contact extended time again");
                var loadedCareer = new LightsOutRentModel(); Require(loadedCareer.ImportJson(career.ExportJson()), "Extended timer snapshot rejected"); Near(career.TimeRemaining, loadedCareer.TimeRemaining, "Reload lost extended work time");
                var faulty = Work(2, 4); ForceRoom(faulty, 0, RoomSpecialKind.Faulty, 1); Require(faulty.Contact(0, 0), "Faulty room rejected");
                double paid = faulty.Money; int xp = faulty.ShiftExperienceTotal; faulty.Tick(5.01f); Require(faulty.IsLightOn(0, 0), "Faulty room never relit");
                Require(faulty.Contact(0, 0), "Relit faulty room could not be extinguished"); Near(paid, faulty.Money, "Relit faulty room paid duplicate income");
                Require(faulty.ShiftExperienceTotal == xp && faulty.TotalRoomOff == 1, "Relit faulty room duplicated XP or unique-room count");
                faulty.Tick(5.01f); Require(!faulty.IsLightOn(0, 0), "Faulty room relit more than once");
                var loadedFaulty = Work(); Require(loadedFaulty.ImportJson(faulty.ExportJson()), "Resolved faulty snapshot rejected"); Near(paid, loadedFaulty.Money, "Faulty one-shot income changed on reload");
                return "Electrical 3..6 neighbours, delayed linked 2..3 neighbours, generator +30-second cap/reload, faulty one relight with one income/XP grant";
            }
        }
        static string SpecialUnlocks()
        {
            using (var rules = new RulesScope())
            {
                rules.Config.SpecialRoomProbabilities = new float[] { .1f, .1f, .1f, .1f, .1f, .1f };
                rules.Config.SpecialUnlockLevels = new int[] { 3, 5, 8, 12, 16, 20 };
                var early = new LightsOutVerticalModel(30, false, 2.4f, 20);
                Require(early.ImportJson(Number(early.ExportJson(), "random", 97193711)), "Unprepared unlock fixture rejected"); early.PrepareShift(1, 1);
                var earlyState = Income(early); foreach (int kind in earlyState.kinds) Require(kind == 0, "Level-one work spawned a locked special room");
                var late = new LightsOutVerticalModel(300, false, 2.4f, 20);
                Require(late.ImportJson(Number(late.ExportJson(), "random", 97193711)), "Late unlock seed rejected"); late.PrepareShift(40, 20);
                var layout = Income(late); var counts = new int[7]; foreach (int kind in layout.kinds) { Require(kind >= 0 && kind < 7, "Unknown room kind spawned"); counts[kind]++; }
                for (int kind = 1; kind <= 6; kind++) Require(counts[kind] > 0, "Unlocked special kind never appeared: " + kind);
                Require(counts[0] > 0, "Special generation displaced every normal room"); uint random = RandomState(late);
                late.CaptureSpecialUnlocks(1); late.PrepareShift(1, 1); Require(Income(late).unlockLevel == 20 && RandomState(late) == random, "Mid-work level changes rerolled the captured layout");
                var loaded = Work(); Require(loaded.ImportJson(late.ExportJson()), "Generated special layout rejected on reload");
                for (int id = 0; id < layout.kinds.Length; id++) Require((int)loaded.GetRoomKind(id / 20, id % 20) == layout.kinds[id], "Reload rerolled a saved special room");
                Require(RandomState(loaded) == random, "Reading room kinds consumed gameplay randomness");
                return "Level 1 stays normal; captured level 20 permits all six specials among 6000 rooms, later level changes and reload preserve the layout";
            }
        }
    }
}
