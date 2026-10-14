using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Isolated checks for building progression, held work and permanent equipment. No player save IO.</summary>
    public static class LightsOutBuildingValidation
    {
        [Serializable] sealed class Report { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result { public string name; public bool passed; public string details; }
        [Serializable] sealed class PermanentSave { public int version, points, highest; public int[] levels, equipped; }
        [Serializable] sealed class DayPlan { public int columns, floors; public float roomWidth, duration; public double rentDue; }
        [Serializable] sealed class LegacyCampaign
        {
            public int version = 1, stage = 2, phase = 1, attempts = 4, slot = -1, multiplier = 1;
            public float remaining = 12;
            public double lastIncome = 105;
            public bool lastCompleted = true, rouletteUsed, settled;
            public uint random = 97193711;
            public string work;
            public DayPlan plan;
        }

        [UnityEditor.MenuItem("Lights Out/Run building, held work and permanent checks")]
        static void RunFromMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            var report = new Report();
            Check(report, "DAY progression changes a wide mall into a narrow tower", DayGeometry);
            Check(report, "width shrink preserves logical rooms and base reward budget", WidthIdentity);
            Check(report, "held work takes a full second and cancels after closure", HeldCompletion);
            Check(report, "room work resumes within its hold window and expires afterward", RetainedWork);
            Check(report, "speed upgrades reach instant swept contact and rise stays bounded", InstantRise);
            Check(report, "upper links stop after their configured reach and combo pays only unique rooms", NonrecursiveLinks);
            Check(report, "20, 22 and 24-node saves migrate without losing progress", LegacyWork);
            Check(report, "legacy rent checkout retains bank, rent and payout until the next DAY", LegacyRent);
            Check(report, "permanent equipment uses points, three slots and equipped-only effects", EquipmentContract);
            Check(report, "first clear rewards persist across campaign reset without farming", PermanentRewards);
            Check(report, "invalid permanent saves reject atomically", InvalidPermanentSaves);
            Check(report, "large floor geometry and modern widths save and reject atomically", GeometrySaveBoundaries);
            return JsonUtility.ToJson(report, true);
        }

        static void Check(Report report, string name, Func<string> test)
        {
            try { string details = test(); report.passed++; report.checks.Add(new Result { name = name, passed = true, details = details }); }
            catch (Exception ex) { report.failed++; report.checks.Add(new Result { name = name, passed = false, details = ex.Message }); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual, string message)
        { Require(Math.Abs(expected - actual) <= Math.Max(.0001, Math.Abs(expected) * .00001), message + " (expected " + expected + ", actual " + actual + ")"); }

        static string Number(string json, string field, double value)
        {
            var regex = new Regex("(\"" + Regex.Escape(field) + "\"\\s*:\\s*)[^,}]+");
            Require(regex.IsMatch(json), "Fixture field is missing: " + field);
            return regex.Replace(json, m => m.Groups[1].Value + value.ToString("R", CultureInfo.InvariantCulture), 1);
        }
        static string Levels(string json, int[] levels)
        {
            var regex = new Regex("\"skills\"\\s*:\\s*\\[[^\\]]*\\]");
            Require(regex.IsMatch(json), "Fixture skill array is missing");
            return regex.Replace(json, "\"skills\":[" + string.Join(",", Array.ConvertAll(levels, x => x.ToString(CultureInfo.InvariantCulture))) + "]", 1);
        }
        static LightsOutVerticalModel Work(int floors = 20, int rooms = 4, float width = 2.4f, int[] skills = null, double money = 0)
        {
            var model = new LightsOutVerticalModel(floors, false, width, rooms);
            string save = Number(Number(Number(model.ExportJson(), "money", money), "earned", money), "random", 97193711);
            if (skills != null) save = Levels(save, skills);
            Require(model.ImportJson(save), "Isolated work fixture was rejected");
            return model;
        }
        static int[] Skills() { return new int[LightsOutVerticalModel.Skills.Length]; }
        static int[] RiseSkills(int rise, bool instant)
        { var skills = Skills(); skills[10] = 2; skills[11] = 2; skills[12] = 3; skills[21] = rise; if (instant) skills[22] = 6; return skills; }
        static Vector2 Centre(LightsOutVerticalModel work, int floor, int room)
        { return new Vector2(-work.HalfWidth + (room + .5f) * work.RoomWidth, (floor + .5f) * LightsOutMover.FloorHeight); }
        static string WithMover(LightsOutVerticalModel model, Func<LightsOutMover, string> test)
        {
            var go = new GameObject("Isolated building validation");
            go.hideFlags = HideFlags.HideAndDontSave;
            try { var mover = go.AddComponent<LightsOutMover>(); mover.ResetMotion(Centre(model, 0, 0)); mover.Initialize(model); return test(mover); }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static string DayGeometry()
        {
            int[] days = { 1, 5, 10, 20, 40 };
            int[] columns = { 20, 14, 9, 6, 4 }, floors = { 2, 5, 12, 28, 60 };
            float previousWidth = float.PositiveInfinity; int previousFloors = 0;
            for (int i = 0; i < days.Length; i++)
            {
                Require(LightsOutRentModel.ColumnsForStage(days[i]) == columns[i] && LightsOutRentModel.FloorsForStage(days[i]) == floors[i],
                    "DAY " + days[i] + " does not match the intended mall-to-tower milestone");
                float width = columns[i] * LightsOutRentModel.RoomWidthForStage(days[i]);
                Require(width < previousWidth && floors[i] > previousFloors, "Building milestones do not grow upward while narrowing");
                previousWidth = width; previousFloors = floors[i];
            }
            Require(LightsOutRentModel.FloorsForStage(41) == 62 && LightsOutRentModel.ColumnsForStage(41) == 4, "After DAY 40 the tower does not add two floors per DAY");
            Require(LightsOutRentModel.FloorsForStage(100000) == 4096, "Late geometry is not capped at 4096 floors");
            Near(80, LightsOutRentModel.DurationForStage(1), "Opening work does not allow 80 seconds");
            var early = new LightsOutRentModel();
            var readyWork = new LightsOutVerticalModel(2, true, 2.4f, 20); var prereqs = Skills(); prereqs[10] = 2; prereqs[11] = 2;
            string earlyJson = Levels(Number(Number(readyWork.ExportJson(), "money", 1000000), "earned", 1000000), prereqs);
            var earlyData = JsonUtility.FromJson<LegacyCampaign>(early.ExportJson()); earlyData.work = earlyJson;
            Require(early.ImportJson(JsonUtility.ToJson(earlyData)) && !early.CanBuySkill(24), "DAY 1 bypassed the DAY 10 completion-chain gate");
            var profile = LightsOutBalanceSettings.Shared.ProfileForDay(10);
            var lateWork = new LightsOutVerticalModel(profile.Floors, true, profile.RoomWidth, profile.Columns);
            var data = new LegacyCampaign
            {
                version = 2, stage = 10, phase = (int)RentPhase.Lobby, attempts = 0, remaining = profile.Duration, lastIncome = 0, lastCompleted = false,
                work = Levels(Number(Number(lateWork.ExportJson(), "money", 1000000), "earned", 1000000), prereqs),
                plan = new DayPlan { columns = profile.Columns, floors = profile.Floors, roomWidth = profile.RoomWidth, duration = profile.Duration, rentDue = LightsOutRentModel.RentForStage(10) }
            };
            var late = new LightsOutRentModel(); Require(late.ImportJson(JsonUtility.ToJson(data)) && late.CanBuySkill(24), "DAY 10 did not unlock completion chain with its existing passive prerequisite");
            return "DAY 1: 20 x 2; DAY 5: 14 x 5; DAY 10: 9 x 12; DAY 20: 6 x 28; DAY 40: 4 x 60; bounded later growth";
        }

        static string WidthIdentity()
        {
            var plain = Work(12, 9, 2.4f); var compressed = Work(12, 9, 2.4f);
            double bank = compressed.Money;
            compressed.SkillLevels[20] = 5;
            Near(plain.BuildingWidth * .65, compressed.BuildingWidth, "Maximum compression does not reduce width by 35%");
            for (int floor = 0; floor < plain.FloorCount; floor++)
                for (int room = 0; room < plain.FloorRooms; room++)
                {
                    Near(plain.GetRoomReward(floor), compressed.GetRoomReward(floor), "Geometry changed a room's base reward");
                    Require(plain.IsLightOn(floor, room) == compressed.IsLightOn(floor, room), "Compression removed or remapped a logical room");
                }
            Near(bank, compressed.Money, "Compression itself changed the wallet");
            Require(compressed.TotalOff == 0 && compressed.FloorRooms == 9 && compressed.FloorCount == 12, "Compression changed room counts or paid free income");
            return "108 logical rooms and their reward budget survive 35% physical width reduction";
        }

        static string HeldCompletion()
        {
            var model = Work();
            return WithMover(model, mover =>
            {
                Require(model.TotalOff == 0, "Entering an unupgraded room extinguished it immediately");
                mover.StepInput(Vector2.zero, .99f);
                Require(model.IsLightOn(0, 0) && mover.IsWorking, "A room completed before a full second of work");
                mover.StepInput(Vector2.zero, .02f);
                Require(!model.IsLightOn(0, 0) && model.TotalOff == 1, "A full second did not extinguish exactly one room");
                mover.ResetMotion(Centre(model, 0, 1)); mover.StepInput(Vector2.zero, .4f);
                model.CloseBuilding(); mover.StepInput(Vector2.zero, 1);
                Require(!mover.IsWorking && model.IsLightOn(0, 1) && model.TotalOff == 1, "Work continued after the timed building closed");
                return "0.99 seconds does not pay; 1.01 seconds pays once; closure cancels unfinished work";
            });
        }

        static string RetainedWork()
        {
            var model = Work();
            return WithMover(model, mover =>
            {
                mover.StepInput(Vector2.zero, .6f);
                mover.transform.position = new Vector3(Centre(model, 0, 1).x, .9f, -1); mover.StepInput(Vector2.zero, .4f);
                mover.transform.position = new Vector3(Centre(model, 0, 0).x, .9f, -1); mover.StepInput(Vector2.zero, .41f);
                Require(!model.IsLightOn(0, 0), "Returning within 1.5 seconds did not resume the room's partial work");
                mover.ResetMotion(Centre(model, 1, 0)); mover.CancelWork(); mover.StepInput(Vector2.zero, .6f);
                mover.transform.position = new Vector3(Centre(model, 1, 1).x, 2.7f, -1); mover.StepInput(Vector2.zero, 1.6f);
                mover.transform.position = new Vector3(Centre(model, 1, 0).x, 2.7f, -1); mover.StepInput(Vector2.zero, .41f);
                Require(model.IsLightOn(1, 0), "An expired room retained its old work progress");
                string retained = mover.ExportWorkJson(); float partial = mover.WorkElapsed;
                mover.CancelWork(); Require(mover.RetainedWorkCount == 0, "Cancel work retained room states");
                Require(mover.ImportWorkJson(retained), "A valid partial-work save was rejected");
                Near(partial, mover.WorkElapsed, "Reload lost a room's partial work");
                Require(!mover.ImportWorkJson(Number(retained, "roomWidth", 8)) && mover.ExportWorkJson() == retained,
                    "A wrong-geometry work save was accepted or replaced valid progress");
                mover.SetInputEnabled(false); float before = mover.WorkElapsed;
                mover.StepInput(Vector2.zero, 10);
                Near(before, mover.WorkElapsed, "Pausing advanced work or its expiry clock");
                mover.SetInputEnabled(true); mover.StepInput(Vector2.zero, .6f);
                Require(!model.IsLightOn(1, 0), "Resuming did not continue the current partial room");
                return "Partial work resumes, expires, freezes on pause and round-trips; wrong geometry rejects without replacing progress";
            });
        }

        static string InstantRise()
        {
            float[] seconds = { 1, .8f, .65f, .5f, .35f, .2f, 0 };
            for (int level = 0; level < seconds.Length; level++)
            { var speed = Work(); speed.SkillLevels[22] = level; Near(seconds[level], speed.ExtinguishSeconds, "Incorrect work-time upgrade at level " + level); }
            var held = Work(100, 4, 1.8f, RiseSkills(3, false));
            WithMover(held, mover =>
            {
                mover.StepInput(Vector2.zero, .28f, false, true);
                Near(Centre(held, 0, 0).y + 16 * 1.8f, mover.Position.y, "Late rise did not travel sixteen complete floors");
                Require(held.TotalOff == 0, "An unfinished held-work rise extinguished crossed rooms immediately");
                return "";
            });
            var instant = Work(100, 4, 1.8f, RiseSkills(3, true));
            return WithMover(instant, mover =>
            {
                mover.StepInput(Vector2.zero, .28f, false, true);
                Require(instant.TotalOff >= 17, "Instant work failed to sweep every crossed floor during rise");
                float landed = mover.Position.y; float cooldown = mover.RiseCooldown;
                mover.StepInput(Vector2.zero, .01f, false, true);
                Near(landed, mover.Position.y, "Cooldown allowed a second upward burst"); Require(mover.RiseCooldown > 0, "Rise did not retain a cooldown");
                mover.SetInputEnabled(false); mover.StepInput(Vector2.up, 5, false, true); Near(landed, mover.Position.y, "Disabled input allowed movement");
                Near(cooldown - .01f, mover.RiseCooldown, "Paused rise cooldown changed");
                mover.ResetMotion(Centre(instant, 0, 0)); Require(mover.RiseRemaining == 0 && mover.RiseCooldown == 0, "New work retained rise motion or cooldown");
                return "Seven speed levels end at instant swept contact; sixteen-floor rise is bounded, gated by cooldown and frozen on pause";
            });
        }

        static string NonrecursiveLinks()
        {
            var levels = Skills(); levels[23] = 3;
            var model = Work(10, 9, 2.4f, levels);
            Require(model.Contact(0, 4), "Direct completion was rejected");
            for (int floor = 0; floor < 4; floor++) Require(!model.IsLightOn(floor, 4), "Linked same-column room stayed lit at floor " + floor);
            Require(model.IsLightOn(4, 4) && model.IsLightOn(0, 3) && model.IsLightOn(0, 5), "Upper links recursed or affected a neighbouring column");
            Require(model.TotalOff == 4, "Upper-link completion counted duplicates");
            Require(model.Combo == 4 && model.PeakCombo == 4, "Linked lights did not contribute once to the combo");
            double expected = 5 + 5.125 + 5.25 + 5.375;
            Near(expected, model.Money, "The four initial linked rooms paid incorrect normal income");
            double paid = model.Money; Require(!model.Contact(0, 4), "An off linked origin could pay again"); Near(paid, model.Money, "Repeated origin paid a second reward");
            Require(model.PendingEventCount == 0, "Immediate links introduced a recursive delayed chain");
            Require(model.Contact(0, 5) && model.Combo == 8, "The next four unique linked rooms did not advance combo exactly four times");
            Near(1.2, model.ComboRewardMultiplier, "Five-or-more combo threshold is incorrect");
            Near(expected * 2.2, model.Money, "The second linked batch did not apply the 1.2 combo threshold");
            model.Tick(4.01f); Require(model.Combo == 0 && model.PeakCombo == 8, "Combo expiry did not reset current count while retaining peak record");
            Near(1, model.ComboRewardMultiplier, "Expired combo retained its reward multiplier");
            Near(1.5, LightsOutBalanceSettings.Shared.ComboMultiplierFor(10), "Ten-combo reward threshold differs");
            Near(2, LightsOutBalanceSettings.Shared.ComboMultiplierFor(20), "Twenty-combo reward threshold differs");
            return "Immediate same-column links are nonrecursive, pay only unique lights, cross the five-combo threshold and reset after four seconds";
        }

        static string LegacyWork()
        {
            foreach (int oldCount in new[] { 20, 22, 24 })
            {
                var original = Work(8, 4, 3.2f); original.Contact(0, 1);
                int[] oldSkills = new int[oldCount]; oldSkills[0] = 2;
                string old = Levels(Number(original.ExportJson(), "version", oldCount == 20 ? 2 : oldCount == 22 ? 3 : 4), oldSkills);
                var loaded = new LightsOutVerticalModel(); Require(loaded.ImportJson(old), oldCount + "-node save was rejected");
                Require(loaded.SkillLevels.Length == 25 && loaded.SkillLevels[0] == 2 && loaded.SkillLevels[24] == 0, "Migration lost prior skill levels or granted a new skill");
                Require(!loaded.IsLightOn(0, 1) && loaded.TotalOff == original.TotalOff && loaded.FloorRooms == 4, "Migration lost lights or logical geometry");
                Near(original.Money, loaded.Money, "Migration changed saved income");
                Near(oldCount == 20 ? 2.4f : 3.2f, loaded.BaseRoomWidth, "Legacy width migration differs");
                var reloaded = new LightsOutVerticalModel(); Require(reloaded.ImportJson(loaded.ExportJson()), "Upgraded save could not round-trip");
            }
            return "20-, 22- and 24-node saves extend to 25 nodes with saved lights, wallet, old levels and widths preserved";
        }

        static string LegacyRent()
        {
            var work = new LightsOutVerticalModel(5, true, 2.4f, 4);
            string json = Levels(Number(Number(Number(Number(work.ExportJson(), "version", 3), "money", 189.25), "earned", 189.25), "off", 20), new int[22]);
            json = new Regex("\"lights\"\\s*:\\s*\\[[^\\]]*\\]").Replace(json, "\"lights\":[" + string.Join(",", Array.ConvertAll(new bool[20], b => "false")) + "]", 1);
            json = Number(json, "pendingIncome", 105);
            json = new Regex("\"closed\"\\s*:\\s*false").Replace(json, "\"closed\":true", 1);
            var campaign = new LegacyCampaign { work = json };
            var loaded = new LightsOutRentModel(); Require(loaded.ImportJson(JsonUtility.ToJson(campaign)), "Legacy checkout was rejected");
            Require(loaded.Stage == 2 && loaded.AttemptsUsed == 4 && loaded.Phase == RentPhase.Checkout, "Legacy phase or chance count changed");
            Near(189.25, loaded.Work.Money, "Legacy bank changed"); Near(105, loaded.LastShiftIncome, "Legacy shift income changed"); Near(273, loaded.RentDue, "Legacy rent was replaced with the new DAY budget");
            Require(loaded.SkipRoulette() && loaded.CanPayRent, "Old gross income could not settle and afford old rent");
            Near(294.25, loaded.Work.Money, "Legacy settlement paid an incorrect amount");
            Require(loaded.PayRent(), "Legacy checkout could not pay rent");
            Near(21.25, loaded.Work.Money, "Legacy payment did not carry the precise remainder");
            Require(loaded.Stage == 3 && loaded.Work.FloorRooms == LightsOutRentModel.ColumnsForStage(3) && loaded.Work.FloorCount == LightsOutRentModel.FloorsForStage(3),
                "The next DAY did not adopt the new building profile");
            return "Legacy DAY 2 checkout: bank 189.25 + gross 105 - rent 273 = 21.25; only next DAY uses the new profile";
        }

        static LightsOutPermanentModel Permanent(int points = 0, int[] levels = null, int[] equipped = null)
        {
            var model = new LightsOutPermanentModel();
            var saved = new PermanentSave { version = 1, points = points, highest = 0, levels = levels ?? new int[5], equipped = equipped ?? new[] { -1, -1, -1 } };
            Require(model.ImportJson(JsonUtility.ToJson(saved)), "Valid permanent fixture was rejected"); return model;
        }
        static string EquipmentContract()
        {
            var model = Permanent(10000);
            Require(!model.ToggleEquip(0), "An unowned accessory could be equipped");
            for (int id = 0; id < 5; id++)
            {
                int initial = model.Points, cost = model.UpgradeCost(id);
                Require(model.CanUpgrade(id) && model.Upgrade(id), "First accessory level could not be purchased");
                Require(initial - model.Points == cost && model.Levels[id] == 1, "Accessory purchase did not spend its exact cost and gain one level");
            }
            Require(model.ToggleEquip(0) && model.ToggleEquip(1) && model.ToggleEquip(2) && !model.ToggleEquip(3), "Equipment does not enforce three owned slots");
            AccessoryEffects snapshot = model.CaptureEffects(); Near(.5, snapshot.HoldBonusSeconds, "Equipped gloves effect differs"); Near(.01, snapshot.ComboMovePerCount, "Equipped shoes effect differs");
            Require(snapshot.KeyOpeningRooms == 3 && snapshot.RingChance == 0 && snapshot.BadgeBonusPerCombo == 0, "Unequipped accessories affected work");
            Require(model.ToggleEquip(0) && model.ToggleEquip(3), "Equipment could not release and reuse a slot");
            Near(.5, snapshot.HoldBonusSeconds, "An outfit change mutated a previously captured work snapshot");
            Near(0, model.CaptureEffects().HoldBonusSeconds, "Unequipped gloves still affected work");
            for (int id = 0; id < 5; id++) while (model.Levels[id] < model.MaxLevel) Require(model.Upgrade(id), "Accessory maximum was unreachable");
            Require(!model.Upgrade(0) && model.UpgradeCost(0) == int.MaxValue, "A maximum accessory can be purchased again");
            var allEffects = Permanent(0, new[] { 5, 5, 5, 5, 5 }, new[] { 0, 1, 2 }).CaptureEffects();
            Near(2.5, allEffects.HoldBonusSeconds, "Maximum gloves effect differs"); Near(.03, allEffects.ComboMovePerCount, "Maximum shoes effect differs"); Require(allEffects.KeyOpeningRooms == 11, "Maximum opening keys effect differs");
            var secondary = Permanent(0, new[] { 5, 5, 5, 5, 5 }, new[] { 3, 4, -1 }).CaptureEffects();
            Near(.5, secondary.RingChance, "Maximum ring chance differs"); Require(secondary.RingRange == 2, "Maximum ring range differs"); Near(1.25, secondary.BadgeBonusPerCombo, "Maximum badge effect differs");
            return "Permanent points upgrade all five accessories; three unique owned slots apply only their captured effects";
        }

        static string PermanentRewards()
        {
            var permanent = new LightsOutPermanentModel();
            Require(permanent.PreviewPaymentPoints(1, 4) == 4 && permanent.RecordRentPayment(1, 4) == 4, "First early payment did not award one base point plus capped three-point bonus");
            Require(permanent.RecordRentPayment(1, 4) == 0 && permanent.RecordRentPayment(0, 5) == 0, "The same or an invalid DAY awarded farmable points");
            Require(permanent.RecordRentPayment(2, 0) == 1 && permanent.Points == 5 && permanent.HighestPaidDay == 2, "Final-chance clear did not award the first-clear base point");
            Require(permanent.Upgrade(0) && permanent.ToggleEquip(0), "Permanent progress fixture could not buy and equip gloves");
            string before = permanent.ExportJson(); var campaign = new LightsOutRentModel(permanent); campaign.Reset();
            Require(permanent.ExportJson() == before, "A campaign restart erased permanent points, equipment or first clears");
            var loaded = new LightsOutPermanentModel(); Require(loaded.ImportJson(before) && loaded.ExportJson() == before, "Permanent progress did not round-trip");
            Require(loaded.PreviewPaymentPoints(1, 4) == 0 && loaded.PreviewPaymentPoints(3, 1) == 2, "Reload forgot the anti-farming milestone or early payment bonus");
            return "First paid DAY rewards once; capped early bonuses, equipment and highest clear survive career reset and reload";
        }

        static string InvalidPermanentSaves()
        {
            var model = Permanent(50, new[] { 1, 1, 1, 0, 0 }, new[] { 0, 1, 2 }); string before = model.ExportJson(); int rejected = 0;
            Action<Action<PermanentSave>> reject = mutate =>
            {
                var saved = JsonUtility.FromJson<PermanentSave>(before); mutate(saved);
                Require(!model.ImportJson(JsonUtility.ToJson(saved)), "Invalid permanent state was accepted"); Require(model.ExportJson() == before, "Rejected permanent state partially changed progress"); rejected++;
            };
            reject(s => s.version = 0); reject(s => s.points = -1); reject(s => s.highest = -1);
            reject(s => s.levels = new int[4]); reject(s => s.levels[0] = 6); reject(s => s.equipped = new int[4]);
            reject(s => s.equipped[2] = 1); reject(s => s.equipped[2] = 3); reject(s => s.equipped[2] = -2);
            Require(!model.ImportJson(null) && !model.ImportJson("not json") && model.ExportJson() == before, "Malformed permanent JSON altered live state");
            return rejected + " invalid contracts and malformed JSON reject without partially replacing valid permanent progress";
        }

        static string GeometrySaveBoundaries()
        {
            var model = Work(4096, 4, 1.8f); model.Contact(4095, 3);
            var loaded = new LightsOutVerticalModel(); Require(loaded.ImportJson(model.ExportJson()), "Large tower save was rejected");
            Require(loaded.FloorCount == 4096 && loaded.FloorRooms == 4 && !loaded.IsLightOn(4095, 3), "Large tower save lost its uppermost light");
            Near(model.BaseRoomWidth, loaded.BaseRoomWidth, "Large tower lost its width"); string before = loaded.ExportJson();
            foreach (double invalid in new[] { 0, 1.19, 8.01 })
            { Require(!loaded.ImportJson(Number(before, "roomWidth", invalid)) && loaded.ExportJson() == before, "Invalid width was accepted or changed valid geometry"); }
            Require(!loaded.ImportJson(Number(before, "rooms", 21)) && loaded.ExportJson() == before, "Out-of-range room columns changed valid geometry");
            Require(!loaded.ImportJson(Number(before, "floors", 4097)) && loaded.ExportJson() == before, "Out-of-range floors changed valid geometry");
            return "4096-floor saved geometry round-trips; invalid width, column and floor limits reject atomically";
        }
    }
}
