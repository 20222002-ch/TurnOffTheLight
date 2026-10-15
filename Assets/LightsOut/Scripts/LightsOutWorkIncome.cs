using System;
using UnityEngine;

namespace LightsOut
{
    public enum RoomSpecialKind { Normal, Golden, Electrical, Security, Linked, Generator, Faulty }

    [Serializable]
    public sealed class LightsOutShiftStats
    {
        public int DirectCount, PropagatedCount, AutomatedCount, UnclassifiedCount, TotalCount, MaxCombo, CashCount;
        public double RawBaseIncome, GradeAdjustment, RandomBonus, CashBonus, SpecialBonus, ComboBonus, SkillBonus;
        public string GradeName = "일반 건물";
        public double GradeMultiplier = 1;
        public float PeakComboMultiplier = 1;
        public int XPDirect, XPPropagation, XPSpecial, XPFloors, XPBuilding;
        public int[] SpecialCounts = new int[6];
        public bool LegacyReceipt;
        public double TotalIncome { get { return RawBaseIncome + GradeAdjustment + RandomBonus + CashBonus + SpecialBonus + ComboBonus + SkillBonus; } }
        public int ShiftExperienceTotal { get { return XPDirect + XPPropagation + XPSpecial + XPFloors + XPBuilding; } }
    }

    [Serializable]
    public sealed class LightsOutIncomeState
    {
        public LightsOutShiftStats stats = new LightsOutShiftStats();
        public int day = 1, unlockLevel = 1, grade = 1;
        public bool prepared, buildingXpAwarded;
        public double legacyBaseline, badgeBonus;
        public bool[] paid, instantAttempts, floorAwarded, faultyUsed, movementBonuses;
        public int[] kinds;
        public float[] randomFactors, cashFactors, specialFactors, comboFactors, workMultipliers;
        public double[] faultyDue;

        public static LightsOutIncomeState Create(int rooms, int floors)
        {
            var state = new LightsOutIncomeState
            {
                paid = new bool[rooms], instantAttempts = new bool[rooms], floorAwarded = new bool[floors], faultyUsed = new bool[rooms],
                movementBonuses = new bool[rooms], kinds = new int[rooms], randomFactors = new float[rooms], cashFactors = new float[rooms],
                specialFactors = new float[rooms], comboFactors = new float[rooms], workMultipliers = new float[rooms], faultyDue = new double[rooms]
            };
            for (int i = 0; i < rooms; i++) { state.randomFactors[i] = -1; state.workMultipliers[i] = 1; }
            return state;
        }
    }

    internal static class LightsOutIncomeRules
    {
        public static float Safe(float value, float fallback, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }
        public static int Weighted(float[] weights, Func<double> random, int fallback)
        {
            if (weights == null || weights.Length == 0) return fallback;
            double total = 0;
            for (int i = 0; i < weights.Length; i++) total += Safe(weights[i], 0, 0, 100000);
            if (total <= 0) return Mathf.Clamp(fallback, 0, weights.Length - 1);
            double sample = random() * total, cumulative = 0;
            int last = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = Safe(weights[i], 0, 0, 100000);
                if (weight <= 0) continue;
                last = i; cumulative += weight;
                if (sample < cumulative) return i;
            }
            return last;
        }
        static float ArrayValue(float[] values, int index, float fallback, float min, float max)
        { return values == null || index < 0 || index >= values.Length ? fallback : Safe(values[index], fallback, min, max); }

        public static void Prepare(LightsOutIncomeState state, int day, int level, LightsOutBalanceSettings settings, Func<double> random)
        {
            if (state.prepared) return;
            state.prepared = true; state.day = Mathf.Clamp(day, 1, 100000); state.unlockLevel = Mathf.Clamp(level, 1, 1000);
            state.grade = Weighted(settings.BuildingGradeWeights, random, 1);
            state.stats.GradeMultiplier = ArrayValue(settings.BuildingGradeMultipliers, state.grade, 1, .1f, 5);
            state.stats.GradeName = settings.BuildingGradeNames != null && state.grade < settings.BuildingGradeNames.Length && !string.IsNullOrEmpty(settings.BuildingGradeNames[state.grade]) ? settings.BuildingGradeNames[state.grade] : "일반 건물";

            float[] probabilities = new float[6];
            double specialTotal = 0;
            for (int kind = 0; kind < 6; kind++)
            {
                int unlock = settings.SpecialUnlockLevels != null && kind < settings.SpecialUnlockLevels.Length ? Math.Max(1, settings.SpecialUnlockLevels[kind]) : 1000;
                if (state.unlockLevel < unlock) continue;
                probabilities[kind] = ArrayValue(settings.SpecialRoomProbabilities, kind, 0, 0, .5f) + Safe(settings.SpecialChancePerDay, 0, 0, .01f) * (state.day - 1);
                probabilities[kind] = Mathf.Clamp(probabilities[kind], 0, .5f); specialTotal += probabilities[kind];
            }
            double normalization = specialTotal > .5 ? .5 / specialTotal : 1;
            int normalCount = 0;
            for (int id = 0; id < state.kinds.Length; id++)
            {
                double sample = random(), cumulative = 0;
                int selected = 0;
                for (int kind = 0; kind < 6; kind++)
                { cumulative += probabilities[kind] * normalization; if (sample < cumulative) { selected = kind + 1; break; } }
                state.kinds[id] = selected;
                if (selected == 0) normalCount++;
                else if (selected == (int)RoomSpecialKind.Golden) state.specialFactors[id] = Safe(settings.GoldenBonusFactor, 4, 0, 100);
                else if (selected == (int)RoomSpecialKind.Security)
                { state.specialFactors[id] = Safe(settings.SecurityBonusFactor, 3, 0, 100); state.workMultipliers[id] = Safe(settings.SecurityWorkMultiplier, 2, 1, 20); }
                else if (selected == (int)RoomSpecialKind.Faulty) state.specialFactors[id] = Safe(settings.FaultyBonusFactor, 1, 0, 100);
            }
            if (normalCount == 0) return;
            int[] candidates = new int[normalCount];
            for (int id = 0, at = 0; id < state.kinds.Length; id++) if (state.kinds[id] == 0) candidates[at++] = id;
            int minimum = Mathf.Clamp(settings.MinimumCashBags, 0, 20), maximum = Mathf.Clamp(settings.MaximumCashBags, minimum, 20);
            int bags = Math.Min(normalCount, Math.Min(20, minimum + (int)(random() * (maximum - minimum + 1)) + Mathf.Clamp(settings.CashBagCountBonus, 0, 20)));
            float factorMin = Safe(settings.MinimumCashBagFactor, 5, 0, 100), factorMax = Safe(settings.MaximumCashBagFactor, 20, factorMin, 100);
            for (int i = 0; i < bags; i++)
            {
                int chosen = i + (int)(random() * (normalCount - i));
                int temp = candidates[i]; candidates[i] = candidates[chosen]; candidates[chosen] = temp;
                state.cashFactors[candidates[i]] = factorMin + (float)random() * (factorMax - factorMin);
            }
        }

        public static double CreditRoom(LightsOutIncomeState state, int id, double raw, float comboMultiplier, bool automatic, bool direct, bool movementBonus, LightsOutBalanceSettings settings, Func<double> random)
        {
            if (state.paid[id]) return 0;
            state.paid[id] = true;
            state.comboFactors[id] = comboMultiplier;
            state.movementBonuses[id] = movementBonus;
            if (state.kinds[id] == 0)
            {
                int bonus = Weighted(settings.NormalBonusWeights, random, 0);
                state.randomFactors[id] = ArrayValue(settings.NormalBonusFactors, bonus, 0, 0, 100);
            }
            else state.randomFactors[id] = 0;
            var stats = state.stats;
            double graded = raw * stats.GradeMultiplier;
            double gradeAdjustment = graded - raw, normalBonus = raw * state.randomFactors[id], cash = raw * state.cashFactors[id];
            double special = raw * state.specialFactors[id], combo = raw * (comboMultiplier - 1);
            double skill = movementBonus ? graded + normalBonus + cash + special + combo : 0;
            stats.RawBaseIncome += raw; stats.GradeAdjustment += gradeAdjustment; stats.RandomBonus += normalBonus; stats.CashBonus += cash;
            stats.SpecialBonus += special; stats.ComboBonus += combo; stats.SkillBonus += skill; stats.TotalCount++;
            if (state.cashFactors[id] > 0) stats.CashCount++;
            if (automatic) stats.AutomatedCount++; else if (direct) stats.DirectCount++; else stats.PropagatedCount++;
            if (state.kinds[id] > 0) stats.SpecialCounts[state.kinds[id] - 1]++;
            stats.PeakComboMultiplier = Mathf.Max(stats.PeakComboMultiplier, comboMultiplier);
            return raw + gradeAdjustment + normalBonus + cash + special + combo + skill;
        }

        public static bool Validate(LightsOutIncomeState state, int roomCount, int floors, int columns, double clock, bool[] on)
        {
            if (state == null || state.stats == null || state.day < 1 || state.day > 100000 || state.unlockLevel < 1 || state.unlockLevel > 1000 || state.grade < 0 || state.grade > 100 ||
                !Finite(state.legacyBaseline) || state.legacyBaseline < 0 || state.legacyBaseline > 1e15 || !Finite(state.badgeBonus) || state.badgeBonus < 0 || state.badgeBonus > 1e15) return false;
            if (!Length(state.paid, roomCount) || !Length(state.instantAttempts, roomCount) || !Length(state.floorAwarded, floors) || !Length(state.faultyUsed, roomCount) || !Length(state.movementBonuses, roomCount) ||
                !Length(state.kinds, roomCount) || !Length(state.randomFactors, roomCount) || !Length(state.cashFactors, roomCount) || !Length(state.specialFactors, roomCount) ||
                !Length(state.comboFactors, roomCount) || !Length(state.workMultipliers, roomCount) || !Length(state.faultyDue, roomCount)) return false;
            var stats = state.stats;
            if (string.IsNullOrEmpty(stats.GradeName) || stats.GradeName.Length > 100 || !Range(stats.GradeMultiplier, .1, 5) || !Range(stats.PeakComboMultiplier, 1, 10) ||
                !Length(stats.SpecialCounts, 6) || stats.DirectCount < 0 || stats.PropagatedCount < 0 || stats.AutomatedCount < 0 || stats.TotalCount < 0 || stats.TotalCount > roomCount ||
                stats.UnclassifiedCount < 0 || stats.DirectCount + stats.PropagatedCount + stats.AutomatedCount + stats.UnclassifiedCount != stats.TotalCount || stats.MaxCombo < 0 || stats.MaxCombo > stats.TotalCount ||
                stats.XPDirect < 0 || stats.XPPropagation < 0 || stats.XPSpecial < 0 || stats.XPFloors < 0 || stats.XPBuilding < 0 ||
                (long)stats.XPDirect + stats.XPPropagation + stats.XPSpecial + stats.XPFloors + stats.XPBuilding > int.MaxValue) return false;
            if (!state.prepared && (stats.TotalCount != 0 || stats.TotalIncome != 0 || stats.ShiftExperienceTotal != 0 || state.buildingXpAwarded || state.legacyBaseline != 0 || state.badgeBonus != 0)) return false;
            int paid = 0, specialCount = 0, legacyCount = 0, cashCount = 0;
            double rawSum = state.legacyBaseline, gradeSum = 0, randomSum = 0, cashSum = 0, specialSum = 0, comboSum = 0, skillSum = state.badgeBonus;
            for (int i = 0; i < roomCount; i++)
            {
                if (state.kinds[i] < 0 || state.kinds[i] > 6 || !Range(state.randomFactors[i], -1, 100) || !Range(state.cashFactors[i], 0, 100) || !Range(state.specialFactors[i], 0, 100) ||
                    !Range(state.comboFactors[i], 0, 10) || !Range(state.workMultipliers[i], 1, 20) || !Finite(state.faultyDue[i]) || state.faultyDue[i] < 0 || state.faultyDue[i] > clock + 900 ||
                    (state.kinds[i] != 0 && state.cashFactors[i] != 0) || (!on[i] && !state.paid[i]) || (state.faultyDue[i] > 0 && (state.kinds[i] != 6 || !state.faultyUsed[i] || on[i]))) return false;
                if (!state.prepared && (state.kinds[i] != 0 || state.cashFactors[i] != 0 || state.specialFactors[i] != 0 || state.instantAttempts[i] || state.faultyUsed[i] || state.workMultipliers[i] != 1)) return false;
                if (!state.paid[i]) { if (state.comboFactors[i] != 0 || state.randomFactors[i] != -1 || state.movementBonuses[i]) return false; continue; }
                paid++;
                if (state.cashFactors[i] > 0) cashCount++;
                if (state.kinds[i] > 0) specialCount++;
                if (state.comboFactors[i] == 0) { if (!stats.LegacyReceipt) return false; legacyCount++; continue; }
                if (state.comboFactors[i] < 1 || state.randomFactors[i] < 0 || (state.kinds[i] != 0 && state.randomFactors[i] != 0)) return false;
                double raw = 5 + (i / columns) * .125, graded = raw * stats.GradeMultiplier;
                rawSum += raw; gradeSum += graded - raw; randomSum += raw * state.randomFactors[i]; cashSum += raw * state.cashFactors[i];
                double random = raw * state.randomFactors[i], cash = raw * state.cashFactors[i], special = raw * state.specialFactors[i], combo = raw * (state.comboFactors[i] - 1);
                specialSum += special; comboSum += combo; skillSum += state.movementBonuses[i] ? graded + random + cash + special + combo : 0;
            }
            int recordedSpecial = 0;
            for (int i = 0; i < 6; i++) { if (stats.SpecialCounts[i] < 0) return false; recordedSpecial += stats.SpecialCounts[i]; }
            return paid == stats.TotalCount && cashCount == stats.CashCount && legacyCount == stats.UnclassifiedCount && recordedSpecial == specialCount && Near(stats.RawBaseIncome, rawSum) && Near(stats.GradeAdjustment, gradeSum) &&
                Near(stats.RandomBonus, randomSum) && Near(stats.CashBonus, cashSum) && Near(stats.SpecialBonus, specialSum) && Near(stats.ComboBonus, comboSum) && Near(stats.SkillBonus, skillSum) &&
                Range(stats.TotalIncome, 0, 1e15) && (stats.LegacyReceipt || state.legacyBaseline == 0);
        }
        static bool Length(Array value, int length) { return value != null && value.Length == length; }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool Range(double value, double min, double max) { return Finite(value) && value >= min && value <= max; }
        static bool Near(double a, double b) { return Finite(a) && Finite(b) && Math.Abs(a - b) <= Math.Max(.000001, Math.Abs(b) * 1e-9); }
    }
}
