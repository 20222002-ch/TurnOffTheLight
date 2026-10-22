using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Isolated payment and ordinary career-reset checks. Never reads or deletes real saves.</summary>
    public static class LightsOutPointsResetValidation
    {
        [Serializable] sealed class Report { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result { public string name; public bool passed; public string details; }
        [Serializable] sealed class PermanentSave
        { public int version, points, highest; public long totalExperience; public int[] levels, equipped; }
        [Serializable] sealed class CampaignSave
        {
            public int version, stage, phase, attempts, slot, multiplier;
            public float remaining, timeExtended;
            public double lastIncome;
            public bool lastCompleted, rouletteUsed, settled;
            public uint random;
            public string work;
            public LightsOutRentModel.StagePlan plan;
        }

        [UnityEditor.MenuItem("Lights Out/Run early-payment points and career reset checks")]
        static void RunFromMenu() { Debug.Log(Run()); }
        public static string Run() { return LightsOutIncomeValidation.WithPureRules(RunPure); }
        static string RunPure()
        {
            var report = new Report();
            Check(report, "remaining opportunities 0 through 4 award exact new and recorded DAY points", PaymentTable);
            Check(report, "highest DAY 29 does not block DAY 2 early payment", RecordedDayPayment);
            Check(report, "only a new highest paid DAY awards the extra milestone point", HighestDayMilestone);
            Check(report, "insufficient, working and unresolved payments award nothing", RejectedPayments);
            Check(report, "campaign and permanent reload preserve payment boundaries", ReloadPayment);
            Check(report, "ordinary restart preserves progression and permits a new real early payment", RestartReplay);
            Check(report, "legacy permanent records retain gear and accept replay early bonuses", LegacyPermanent);
            return JsonUtility.ToJson(report, true);
        }
        static void Check(Report report, string name, Func<string> test)
        {
            try { string details = test(); report.passed++; report.checks.Add(new Result { name = name, passed = true, details = details }); }
            catch (Exception ex) { report.failed++; report.checks.Add(new Result { name = name, passed = false, details = ex.Message }); }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Near(double expected, double actual, string message)
        { Require(Math.Abs(expected - actual) < .000001, message + " (expected " + expected + ", actual " + actual + ")"); }
        static LightsOutPermanentModel Permanent(int highest = 29, int version = 2)
        {
            var result = new LightsOutPermanentModel();
            Require(result.ImportJson(JsonUtility.ToJson(new PermanentSave
            {
                version = version, points = 7, highest = highest, totalExperience = version == 1 ? 0 : 125,
                levels = new[] { 1, 4, 1, 1, 2 }, equipped = new[] { 4, 1, 0 }
            })), "Permanent fixture was rejected");
            return result;
        }
        static void SeedBank(LightsOutRentModel campaign, double money)
        {
            var saved = JsonUtility.FromJson<CampaignSave>(campaign.ExportJson());
            saved.work = LightsOutIncomeValidation.Number(LightsOutIncomeValidation.Number(saved.work, "money", money), "earned", money);
            Require(campaign.ImportJson(JsonUtility.ToJson(saved)), "Wallet fixture was rejected");
        }
        static LightsOutRentModel Working(LightsOutPermanentModel permanent, int day, int remaining, double money)
        {
            var result = new LightsOutRentModel(permanent);
            Require(result.StartReadyWork(), "Working fixture could not leave the lobby");
            var saved = JsonUtility.FromJson<CampaignSave>(result.ExportJson());
            var profile = LightsOutBalanceSettings.Shared.ProfileForDay(day);
            var work = new LightsOutVerticalModel(profile.Floors, true, profile.RoomWidth, profile.Columns);
            work.SetAccessories(permanent.CaptureEffects()); work.PrepareShift(day, permanent.Level);
            saved.stage = day; saved.attempts = LightsOutRentModel.AttemptsPerStage - remaining; saved.remaining = profile.Duration;
            saved.random = 97193711;
            saved.plan = new LightsOutRentModel.StagePlan
            {
                columns = profile.Columns, floors = profile.Floors, roomWidth = profile.RoomWidth,
                duration = profile.Duration, rentDue = LightsOutRentModel.RentForStage(day)
            };
            saved.work = work.ExportJson();
            Require(result.ImportJson(JsonUtility.ToJson(saved)), "DAY and remaining-opportunity fixture was rejected");
            SeedBank(result, money); return result;
        }
        static void Settle(LightsOutRentModel campaign)
        { Require(campaign.EndAttempt(false) && campaign.SkipRoulette(), "A real working opportunity could not finish and settle"); }
        static void RejectPaymentUnchanged(LightsOutRentModel campaign)
        {
            string career = campaign.ExportJson(), permanent = campaign.Permanent.ExportJson();
            Require(!campaign.PayRent(), "An unavailable or duplicate payment succeeded");
            Require(career == campaign.ExportJson() && permanent == campaign.Permanent.ExportJson(), "A rejected payment changed money, points or progression");
        }

        static string PaymentTable()
        {
            for (int remaining = 0; remaining <= 4; remaining++)
                foreach (bool recorded in new[] { false, true })
                {
                    var permanent = Permanent(recorded ? 29 : 0); string before = permanent.ExportJson();
                    int expected = Math.Min(3, remaining) + (recorded ? 0 : 1);
                    Require(permanent.PreviewEarlyBonus(2, remaining) == Math.Min(3, remaining) && permanent.PreviewPaymentPoints(2, remaining) == expected,
                        "Preview differs for remaining " + remaining + ", recorded=" + recorded);
                    Require(permanent.ExportJson() == before, "Preview itself awarded points");
                    double rent = LightsOutRentModel.RentForStage(2);
                    var campaign = Working(permanent, 2, remaining, rent + 12.5); Settle(campaign);
                    Require(campaign.AttemptsRemaining == remaining && campaign.PayRent(), "A funded payment failed at remaining " + remaining);
                    Require(permanent.Points == 7 + expected && permanent.HighestPaidDay == (recorded ? 29 : 2), "Actual payment points differ from preview");
                    Near(12.5, campaign.Work.Money, "Rent did not deduct exactly before carrying cash");
                    RejectPaymentUnchanged(campaign);
                }
            return "Ten actual payments: recorded DAY 0/1/2/3/3; new highest DAY 1/2/3/4/4; preview is read-only and duplicate PayRent rejects";
        }
        static string RecordedDayPayment()
        {
            var permanent = Permanent(); var campaign = Working(permanent, 2, 4, LightsOutRentModel.RentForStage(2) + 40);
            Settle(campaign); Require(campaign.PayRent(), "Recorded DAY 2 could not pay rent");
            Require(permanent.Points == 10 && permanent.HighestPaidDay == 29, "Highest DAY 29 blocked the DAY 2 three-point early bonus or granted a milestone");
            Require(campaign.Day == 3 && campaign.Phase == RentPhase.Lobby && campaign.AttemptsUsed == 0, "Payment did not enter the next DAY lobby");
            Near(40, campaign.Work.Money, "Recorded-DAY payment lost the remaining wallet");
            RejectPaymentUnchanged(campaign);
            return "Fixture points 7 / highest DAY 29 pays DAY 2 with four chances left: points 10, highest 29, next DAY 3";
        }
        static string HighestDayMilestone()
        {
            var atRecord = Permanent(); var noBonus = Working(atRecord, 29, 0, LightsOutRentModel.RentForStage(29));
            Settle(noBonus); Require(noBonus.PayRent() && atRecord.Points == 7 && atRecord.HighestPaidDay == 29, "A recorded final-chance payment repeated its milestone point");
            var newRecord = Permanent(); var milestone = Working(newRecord, 30, 0, LightsOutRentModel.RentForStage(30));
            Settle(milestone); Require(milestone.PayRent() && newRecord.Points == 8 && newRecord.HighestPaidDay == 30, "A new highest final-chance payment did not award exactly one point");
            Require(newRecord.PreviewPaymentPoints(30, 0) == 0 && newRecord.PreviewPaymentPoints(30, 2) == 2 && newRecord.PreviewPaymentPoints(31, 2) == 3,
                "The next preview did not distinguish recorded and new highest DAY");
            return "A recorded final-chance clear earns zero; DAY 30 above record 29 earns one, then its milestone cannot repeat";
        }
        static string RejectedPayments()
        {
            var permanent = Permanent(); double rent = LightsOutRentModel.RentForStage(2);
            var campaign = Working(permanent, 2, 4, rent - 1);
            RejectPaymentUnchanged(campaign);
            Require(campaign.EndAttempt(false), "Working fixture could not reach checkout"); RejectPaymentUnchanged(campaign);
            Require(campaign.SkipRoulette() && !campaign.CanPayRent, "Insufficient wallet unexpectedly covered rent"); RejectPaymentUnchanged(campaign);
            Require(permanent.Points == 7 && permanent.HighestPaidDay == 29, "A failed payment awarded points");
            Require(permanent.PreviewPaymentPoints(0, 4) == 0 && permanent.PreviewPaymentPoints(100001, 4) == 0 && permanent.PreviewEarlyBonus(2, -1) == 0,
                "Invalid DAY or negative remaining opportunities awarded points");
            return "Working, unresolved checkout, insufficient wallet and invalid DAY cannot award points or alter either save";
        }
        static string ReloadPayment()
        {
            var permanent = Permanent(); var campaign = Working(permanent, 2, 1, LightsOutRentModel.RentForStage(2) + 123.25); Settle(campaign);
            var restoredPermanent = new LightsOutPermanentModel(); Require(restoredPermanent.ImportJson(permanent.ExportJson()), "Permanent JSON could not reload");
            var restored = new LightsOutRentModel(restoredPermanent); Require(restored.ImportJson(campaign.ExportJson()), "Pre-payment career JSON could not reload");
            Require(restored.Permanent == restoredPermanent && restored.PayRent() && restoredPermanent.Points == 8, "Reloaded actual payment did not award exactly its one-point early bonus");
            string paidPermanent = restoredPermanent.ExportJson(), paidCareer = restored.ExportJson();
            var reloadedPermanent = new LightsOutPermanentModel(); Require(reloadedPermanent.ImportJson(paidPermanent), "Paid permanent JSON could not reload");
            var reloaded = new LightsOutRentModel(reloadedPermanent); Require(reloaded.ImportJson(paidCareer), "Paid next-DAY career JSON could not reload");
            Require(reloadedPermanent.ExportJson() == paidPermanent && reloaded.ExportJson() == paidCareer, "Round-trip changed the committed payment");
            RejectPaymentUnchanged(reloaded); Near(123.25, reloaded.Work.Money, "Reload changed carried cash");
            Require(reloadedPermanent.TotalExperience == 125 && reloadedPermanent.Levels[1] == 4 && reloadedPermanent.IsEquipped(4), "Reload lost independent permanent XP or equipment");
            return "Both JSONs reload before and after payment; the committed next-DAY lobby cannot pay twice and retains XP, gear and carried cash";
        }
        static string RestartReplay()
        {
            var permanent = Permanent(); var campaign = new LightsOutRentModel(permanent);
            for (int replay = 0; replay < 2; replay++)
            {
                string preserved = permanent.ExportJson(); campaign.Reset();
                Require(campaign.Permanent == permanent && permanent.ExportJson() == preserved && campaign.Day == 1 && campaign.Phase == RentPhase.Lobby,
                    "Ordinary career reset erased or replaced permanent progress");
                Require(campaign.StartReadyWork(), "Restarted DAY could not begin");
                SeedBank(campaign, campaign.RentDue + 1); Settle(campaign);
                Require(campaign.PayRent() && permanent.Points == 7 + (replay + 1) * 3 && permanent.HighestPaidDay == 29,
                    "A newly completed replay payment did not grant its early bonus");
                RejectPaymentUnchanged(campaign);
            }
            Require(permanent.TotalExperience == 125 && permanent.Levels[1] == 4 && permanent.IsEquipped(4), "Restart replay damaged permanent XP or gear");
            return "Two separate restarted DAY 1 payments each earn three early points, never another milestone; ordinary reset keeps XP and gear";
        }
        static string LegacyPermanent()
        {
            var permanent = Permanent(version: 1);
            Require(permanent.Points == 7 && permanent.HighestPaidDay == 29 && permanent.Level == 1 && permanent.TotalExperience == 0,
                "Legacy permanent migration changed points, highest DAY or initial XP");
            var campaign = Working(permanent, 2, 4, LightsOutRentModel.RentForStage(2)); Settle(campaign);
            Require(campaign.PayRent() && permanent.Points == 10 && permanent.HighestPaidDay == 29, "Legacy highest record still blocked replay early rewards");
            var loaded = new LightsOutPermanentModel(); Require(loaded.ImportJson(permanent.ExportJson()), "Upgraded permanent JSON could not reload");
            Require(loaded.TotalExperience == 0 && loaded.Levels[1] == 4 && loaded.IsEquipped(4) && loaded.Points == 10 && loaded.HighestPaidDay == 29,
                "Legacy-to-current round-trip lost the new award or prior equipment");
            return "Legacy v1 points 7 / highest 29 / owned gear migrate to v2 and receive the DAY 2 early bonus without invented XP";
        }
    }
}
