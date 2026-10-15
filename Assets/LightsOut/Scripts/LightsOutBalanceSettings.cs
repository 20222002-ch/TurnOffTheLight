using System;
using UnityEngine;

namespace LightsOut
{
    [CreateAssetMenu(fileName = "LightsOutBalanceSettings", menuName = "Lights Out/Balance Settings")]
    public sealed class LightsOutBalanceSettings : ScriptableObject
    {
        [Serializable]
        public struct DayKeyframe
        {
            public int day, columns, floors;
            public float duration, rentMultiplier;
            public DayKeyframe(int day, int columns, int floors, float duration, float rentMultiplier = 2.6f)
            { this.day = day; this.columns = columns; this.floors = floors; this.duration = duration; this.rentMultiplier = rentMultiplier; }
        }
        public struct DayProfile
        {
            public int Day, Columns, Floors;
            public float RoomWidth, Duration, RentMultiplier;
        }

        public DayKeyframe[] DayKeyframes =
        {
            new DayKeyframe(1, 20, 2, 80), new DayKeyframe(5, 14, 5, 90),
            new DayKeyframe(10, 9, 12, 100), new DayKeyframe(20, 6, 28, 110),
            new DayKeyframe(40, 4, 60, 120)
        };
        [Range(1.2f, 8)] public float RoomWidth = 2.4f;
        [Range(1, 4096)] public int MaximumFloors = 4096;
        [Min(0)] public int FloorsPerDayAfterLast = 2;
        [Min(0)] public float SecondsPerDayAfterLast = 1;
        [Min(1)] public float MaximumDuration = 150;
        public float[] ExtinguishSeconds = { 1, .8f, .6f, .45f, .3f, .2f, .15f };
        [Range(.1f, .2f)] public float MinExtinguishSeconds = .15f;
        public float[] InstantExtinguishChances = { 0, .05f, .1f, .15f, .2f, .25f };
        public int[] SkillMinimumDays = { 1, 5, 5, 10, 30, 10, 12, 15, 20, 30, 5, 10, 15, 20, 30, 15, 20, 25, 30, 40, 10, 20, 1, 1, 10, 1 };
        [Range(.1f, 10)] public float WorkHoldSeconds = 1.5f;
        [Range(.1f, 60)] public float ComboWindowSeconds = 4;
        public int[] ComboThresholds = { 1, 5, 10, 20 };
        public float[] ComboMultipliers = { 1, 1.2f, 1.5f, 2 };
        // Order: x2, x0, x10. Values are normalized when used, including Inspector edits.
        public float[] RouletteWeights = { .37f, .60f, .03f };
        public int[] AccessoryBaseCosts = { 3, 4, 5, 5, 6 };
        [Min(1)] public float AccessoryCostGrowth = 1.8f;
        [Range(1, 100)] public int MaxAccessoryLevel = 5;
        [Min(1)] public int ExperienceBase = 50;
        [Min(0)] public int ExperienceGrowth = 25;
        [Range(1, 1000)] public int MaximumLevel = 100;
        public int XPDirectRoom = 1, XPPropagatedRoom = 1, XPSpecialRoom = 3, XPFloorComplete = 2, XPBuildingComplete = 10;
        public int[] SpecialUnlockLevels = { 3, 5, 8, 12, 16, 20 };
        public string[] SpecialNames = { "황금 전등", "배전실", "보안실", "연결 전등", "비상 발전기", "고장 난 전등" };
        public string[] SpecialDescriptions = { "높은 특별 수익", "같은 층의 주변 방 소등", "작업 시간이 길고 보상이 큼", "주변 방을 순차 소등", "남은 근무 시간 추가", "근무 중 한 번 재점등 · 중복 수익 없음" };
        public float[] SpecialRoomProbabilities = { .02f, .015f, .015f, .012f, .01f, .02f };
        [Min(0)] public float SpecialChancePerDay = .00025f;
        [Min(0)] public float GoldenBonusFactor = 4, SecurityBonusFactor = 3, FaultyBonusFactor = 1;
        [Min(1)] public float SecurityWorkMultiplier = 2;
        public int ElectricalMinimumRooms = 3, ElectricalMaximumRooms = 6, LinkedMinimumRooms = 2, LinkedMaximumRooms = 3;
        [Min(.01f)] public float LinkedInterval = .08f;
        [Min(0)] public float GeneratorTimeBonus = 3, MaximumTimeExtension = 30;
        [Min(.1f)] public float FaultyRelightSeconds = 5;
        public float[] BuildingGradeWeights = { .20f, .50f, .25f, .05f };
        public float[] BuildingGradeMultipliers = { .8f, 1, 1.3f, 1.8f };
        public string[] BuildingGradeNames = { "절전 건물", "일반 건물", "야근 건물", "초호화 건물" };
        public float[] NormalBonusWeights = { .65f, .25f, .08f, .02f };
        public float[] NormalBonusFactors = { 0, .5f, 2, 10 };
        public int MinimumCashBags = 1, MaximumCashBags = 3, CashBagCountBonus = 0;
        public float MinimumCashBagFactor = 5, MaximumCashBagFactor = 20;
        [Range(1, 128)] public int MaxRoomsPerAction = 12;
        [Range(1, 128)] public int MaxAutomaticRoomsPerAction = 8;
        [Range(1, 12)] public int MaxPropagationDepth = 4;

        static LightsOutBalanceSettings shared;
        public static LightsOutBalanceSettings Shared
        {
            get
            {
                if (!shared)
                {
                    shared = Resources.Load<LightsOutBalanceSettings>("LightsOutBalanceSettings");
                    if (!shared) { shared = CreateInstance<LightsOutBalanceSettings>(); shared.hideFlags = HideFlags.HideAndDontSave; }
                }
                return shared;
            }
        }
        public static void ResetSharedCache() { shared = null; }

        public DayProfile ProfileForDay(int day)
        {
            day = Mathf.Clamp(day, 1, 100000);
            DayKeyframe lower = new DayKeyframe(1, 20, 2, 80), upper = lower;
            bool foundLower = false, foundUpper = false;
            if (DayKeyframes != null)
            {
                for (int i = 0; i < DayKeyframes.Length; i++)
                {
                    DayKeyframe candidate = DayKeyframes[i];
                    if (candidate.day < 1) continue;
                    if (candidate.day <= day && (!foundLower || candidate.day > lower.day)) { lower = candidate; foundLower = true; }
                    if (candidate.day > day && (!foundUpper || candidate.day < upper.day)) { upper = candidate; foundUpper = true; }
                }
            }
            if (!foundLower && foundUpper) lower = upper;
            float columns = lower.columns, floors = lower.floors, duration = lower.duration, multiplier = lower.rentMultiplier;
            if (foundUpper && upper.day > lower.day && day > lower.day)
            {
                float blend = (day - lower.day) / (float)(upper.day - lower.day);
                columns = Mathf.Lerp(lower.columns, upper.columns, blend);
                floors = Mathf.Lerp(lower.floors, upper.floors, blend);
                duration = Mathf.Lerp(lower.duration, upper.duration, blend);
                multiplier = Mathf.Lerp(lower.rentMultiplier, upper.rentMultiplier, blend);
            }
            else if (!foundUpper && day > lower.day)
            {
                floors = (float)Math.Min(4096, floors + (double)(day - lower.day) * Mathf.Max(0, FloorsPerDayAfterLast));
                duration += (day - lower.day) * Safe(SecondsPerDayAfterLast, 1, 0, 900);
            }
            return new DayProfile
            {
                Day = day, Columns = Mathf.Clamp(Mathf.CeilToInt(columns), 2, 20),
                Floors = Mathf.Clamp(Mathf.RoundToInt(floors), 1, Mathf.Clamp(MaximumFloors, 1, 4096)),
                RoomWidth = Safe(RoomWidth, 2.4f, 1.2f, 8),
                Duration = Safe(duration, 80, 1, Safe(MaximumDuration, 150, 1, 900)),
                RentMultiplier = Safe(multiplier, 2.6f, .1f, 100)
            };
        }
        public float ExtinguishSecondsForLevel(int level)
        {
            float minimum = Safe(MinExtinguishSeconds, .15f, .1f, .2f);
            if (ExtinguishSeconds == null || ExtinguishSeconds.Length == 0) return Mathf.Max(minimum, 1 - Mathf.Clamp(level, 0, 6) / 6f);
            return Safe(ExtinguishSeconds[Mathf.Clamp(level, 0, ExtinguishSeconds.Length - 1)], 1, minimum, 20);
        }
        public float InstantChanceForLevel(int level)
        { return InstantExtinguishChances == null || InstantExtinguishChances.Length == 0 ? Mathf.Clamp(level * .05f, 0, 1) : Safe(InstantExtinguishChances[Mathf.Clamp(level, 0, InstantExtinguishChances.Length - 1)], 0, 0, 1); }
        public double RentIncomeBasisForDay(int day)
        {
            // Integer floor/column rounding must not make rent fall on a later DAY.
            DayKeyframe lower=default,upper=default;bool foundLower=false,foundUpper=false;
            if(DayKeyframes!=null)foreach(var key in DayKeyframes)
            {
                if(key.day<1)continue;
                if(key.day<=day&&(!foundLower||key.day>lower.day)){lower=key;foundLower=true;}
                if(key.day>day&&(!foundUpper||key.day<upper.day)){upper=key;foundUpper=true;}
            }
            double current=ProfileIncome(ProfileForDay(day));
            if(!foundLower)return current;
            double from=ProfileIncome(ProfileForDay(lower.day));
            if(!foundUpper)return Math.Max(from,current);
            double to=Math.Max(from,ProfileIncome(ProfileForDay(upper.day)));
            double blend=Math.Max(0,Math.Min(1,(day-lower.day)/(double)(upper.day-lower.day)));
            return from+(to-from)*blend;
        }
        static double ProfileIncome(DayProfile profile)
        {return profile.Columns*(double)profile.Floors*(5+.125*(profile.Floors-1)/2);}
        public int MinimumDayForSkill(int id)
        { return id < 0 || id >= LightsOutVerticalModel.Skills.Length ? int.MaxValue : SkillMinimumDays == null || id >= SkillMinimumDays.Length ? 1 : Mathf.Max(1, SkillMinimumDays[id]); }
        public float ComboMultiplierFor(int combo)
        {
            float multiplier = 1;
            int best = -1;
            if (ComboThresholds == null || ComboMultipliers == null) return multiplier;
            for (int i = 0; i < Math.Min(ComboThresholds.Length, ComboMultipliers.Length); i++)
                if (combo >= ComboThresholds[i] && ComboThresholds[i] >= best) { best = ComboThresholds[i]; multiplier = Safe(ComboMultipliers[i], 1, 1, 10); }
            return multiplier;
        }
        public float MaximumComboMultiplier
        {
            get
            {
                float maximum = 1;
                if (ComboMultipliers != null) for (int i = 0; i < ComboMultipliers.Length; i++) maximum = Mathf.Max(maximum, Safe(ComboMultipliers[i], 1, 1, 10));
                return maximum;
            }
        }
        public float RouletteProbabilityForMultiplier(int multiplier)
        {
            int index = multiplier == 2 ? 0 : multiplier == 0 ? 1 : multiplier == 10 ? 2 : -1;
            if (index < 0) return 0;
            double total = 0;
            for (int i = 0; i < 3; i++) total += Weight(i);
            return total > 0 ? (float)(Weight(index) / total) : index == 0 ? .37f : index == 1 ? .60f : .03f;
        }
        float Weight(int index)
        { return RouletteWeights == null || index >= RouletteWeights.Length ? 0 : Safe(RouletteWeights[index], 0, 0, 100000); }
        static float Safe(float value, float fallback, float minimum, float maximum)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum); }
    }
}
