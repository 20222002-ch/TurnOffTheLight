using System;
using UnityEngine;

namespace LightsOut
{
    public enum RentPhase { Working, Checkout, BetweenAttempts, Bankrupt, Lobby }

    /// <summary>Five started shifts per DAY. A saved DAY plan preserves its debt and geometry after balancing edits.</summary>
    public sealed class LightsOutRentModel
    {
        public const int AttemptsPerStage = 5, RouletteSectors = 21;
        const int MaximumStage = 100000;
        static readonly int[] LegacyBuildingSizes = { 3, 5, 8, 12, 18, 28, 42, 64, 96, 144, 216, 300 };

        [Serializable]
        public struct StagePlan
        {
            public int columns, floors;
            public float roomWidth, duration;
            public double rentDue;
        }

        public event Action Changed;
        public LightsOutVerticalModel Work { get; private set; }
        public LightsOutPermanentModel Permanent { get; private set; }
        public RentPhase Phase { get; private set; }
        public int Stage { get; private set; }
        public int Day { get { return Stage; } }
        public StagePlan CurrentPlan { get { return currentPlan; } }
        public int AttemptsUsed { get; private set; }
        public int AttemptsRemaining { get { return AttemptsPerStage - AttemptsUsed; } }
        public int AttemptNumber { get { return Math.Max(1, AttemptsUsed); } }
        public float TimeRemaining { get; private set; }
        public float AttemptDuration { get { return currentPlan.duration; } }
        public double RentDue { get { return currentPlan.rentDue; } }
        public double LastShiftIncome { get; private set; }
        public bool LastCompleted { get; private set; }
        public int LastRouletteSlot { get; private set; }
        public int LastRouletteMultiplier { get; private set; }
        public bool RouletteUsed { get; private set; }
        public bool SettlementResolved { get; private set; }
        public bool CanPayRent { get { return Phase == RentPhase.BetweenAttempts && Work.Money >= RentDue; } }
        public bool CanStartNextAttempt { get { return Phase == RentPhase.BetweenAttempts && AttemptsUsed < AttemptsPerStage; } }
        public double LastSettledIncome { get { return SettlementResolved ? LastShiftIncome * LastRouletteMultiplier : 0; } }

        StagePlan currentPlan;
        uint randomState;
        bool suppressChanges;

        public LightsOutRentModel(LightsOutPermanentModel permanent = null)
        {
            Permanent = permanent ?? new LightsOutPermanentModel();
            Work = new LightsOutVerticalModel(2, true, 2.4f, 20);
            Work.Changed += OnWorkChanged;
            Reset();
        }
        public void BindPermanent(LightsOutPermanentModel permanent)
        { if (permanent != null) Permanent = permanent; }

        public static int FloorsForStage(int stage) { return LightsOutBalanceSettings.Shared.ProfileForDay(stage).Floors; }
        public static int ColumnsForStage(int stage) { return LightsOutBalanceSettings.Shared.ProfileForDay(stage).Columns; }
        public static float RoomWidthForStage(int stage) { return LightsOutBalanceSettings.Shared.ProfileForDay(stage).RoomWidth; }
        public static float DurationForStage(int stage) { return LightsOutBalanceSettings.Shared.ProfileForDay(stage).Duration; }
        public static double BaseIncomeForStage(int stage)
        { var profile = LightsOutBalanceSettings.Shared.ProfileForDay(stage); return BaseIncome(profile.Columns, profile.Floors); }
        public static double RentForStage(int stage)
        { var profile = LightsOutBalanceSettings.Shared.ProfileForDay(stage); return RentAmount(LightsOutBalanceSettings.Shared.RentIncomeBasisForDay(stage), profile.RentMultiplier); }
        public static int MultiplierForSlot(int slot)
        { return slot < 0 || slot >= RouletteSectors ? -1 : slot < 10 ? 2 : slot < 20 ? 0 : 10; }
        public static float RouletteProbabilityForMultiplier(int multiplier)
        { return LightsOutBalanceSettings.Shared.RouletteProbabilityForMultiplier(multiplier); }
        public static float RouletteProbabilityForSlot(int slot)
        { int multiplier = MultiplierForSlot(slot); return multiplier < 0 ? 0 : RouletteProbabilityForMultiplier(multiplier) / (multiplier == 10 ? 1 : 10); }

        static double BaseIncome(int columns, int floors)
        { return columns * (double)floors * (5 + .125 * (floors - 1) / 2); }
        static double RentAmount(double baseIncome, float multiplier)
        { return (double)decimal.Ceiling((decimal)baseIncome * (decimal)multiplier); }
        static StagePlan PlanForDay(int day)
        {
            var profile = LightsOutBalanceSettings.Shared.ProfileForDay(day);
            return new StagePlan { columns = profile.Columns, floors = profile.Floors, roomWidth = profile.RoomWidth,
                duration = profile.Duration, rentDue = RentForStage(day) };
        }
        static StagePlan LegacyPlanForDay(int day, float roomWidth)
        {
            int floors = LegacyBuildingSizes[Math.Max(0, Math.Min(LegacyBuildingSizes.Length - 1, day - 1))];
            return new StagePlan { columns = 4, floors = floors, roomWidth = roomWidth,
                duration = Math.Min(80, 30 + Math.Max(0, day - 2) * 5),
                rentDue = (double)decimal.Ceiling((decimal)BaseIncome(4, floors) * (2.6m + Math.Max(0, day - LegacyBuildingSizes.Length) * .08m)) };
        }

        public void Reset()
        {
            suppressChanges = true;
            Stage = 1; AttemptsUsed = 0; currentPlan = PlanForDay(Stage);
            randomState = (uint)DateTime.UtcNow.Ticks ^ 0xA341316Cu;
            if (randomState == 0) randomState = 2463534242u;
            Work.Reset(currentPlan.floors, currentPlan.roomWidth, currentPlan.columns);
            Work.SetAccessories(Permanent.CaptureEffects());
            BeginShiftState(RentPhase.Lobby);
            suppressChanges = false;
            Notify();
        }
        void BeginShiftState(RentPhase phase)
        {
            Phase = phase; TimeRemaining = AttemptDuration;
            LastShiftIncome = 0; LastCompleted = false;
            LastRouletteSlot = -1; LastRouletteMultiplier = 1;
            RouletteUsed = false; SettlementResolved = false;
        }
        public bool StartReadyWork()
        {
            if (Phase != RentPhase.Lobby || AttemptsUsed != 0) return false;
            suppressChanges = true;
            Work.SetAccessories(Permanent.CaptureEffects());
            AttemptsUsed = 1; BeginShiftState(RentPhase.Working); Work.StartAccessoryEffects();
            suppressChanges = false; Notify(); return true;
        }

        public void Tick(float dt)
        {
            if (Phase != RentPhase.Working || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            if (Work.IsComplete) { EndAttempt(true); return; }
            float elapsed = Math.Min(dt, TimeRemaining);
            TimeRemaining = Math.Max(0, TimeRemaining - elapsed);
            Work.Tick(elapsed);
            if (Work.IsComplete) EndAttempt(true);
            else if (TimeRemaining <= 0) EndAttempt(false);
        }
        public bool EndAttempt(bool completed)
        {
            if (Phase != RentPhase.Working) return false;
            suppressChanges = true;
            LastCompleted = Work.IsComplete;
            if (!LastCompleted) TimeRemaining = 0;
            Work.FinalizeDepartureBonus();
            LastShiftIncome = Work.PendingIncome;
            Work.CloseBuilding(); Phase = RentPhase.Checkout;
            LastRouletteSlot = -1; LastRouletteMultiplier = 1;
            RouletteUsed = false; SettlementResolved = false;
            suppressChanges = false; Notify(); return true;
        }
        public bool SkipRoulette()
        { return Phase == RentPhase.Checkout && !SettlementResolved && ResolveSettlement(false, -1, 1); }
        public bool SpinRoulette()
        {
            if (Phase != RentPhase.Checkout || SettlementResolved) return false;
            double value = (NextRandom() - 1) / 4294967295.0;
            double doubleProbability = RouletteProbabilityForMultiplier(2);
            double lossProbability = RouletteProbabilityForMultiplier(0);
            int slot = value < doubleProbability ? UniformIndex(10) : value < doubleProbability + lossProbability ? 10 + UniformIndex(10) : 20;
            return ResolveSettlement(true, slot, MultiplierForSlot(slot));
        }
        int UniformIndex(uint count)
        {
            uint value, limit = uint.MaxValue - uint.MaxValue % count;
            do { value = NextRandom() - 1; } while (value >= limit);
            return (int)(value % count);
        }
        bool ResolveSettlement(bool roulette, int slot, int multiplier)
        {
            suppressChanges = true;
            if (!Work.SettlePending(multiplier)) { suppressChanges = false; return false; }
            RouletteUsed = roulette; LastRouletteSlot = slot; LastRouletteMultiplier = multiplier; SettlementResolved = true;
            Phase = AttemptsUsed == AttemptsPerStage && Work.Money < RentDue ? RentPhase.Bankrupt : RentPhase.BetweenAttempts;
            suppressChanges = false; Notify(); return true;
        }
        public bool PayRent()
        {
            if (!CanPayRent || Stage >= MaximumStage) return false;
            suppressChanges = true;
            if (!Work.TrySpend(RentDue)) { suppressChanges = false; return false; }
            Permanent.RecordRentPayment(Stage, AttemptsRemaining);
            Stage++; AttemptsUsed = 0; currentPlan = PlanForDay(Stage);
            Work.BeginBuilding(currentPlan.floors, currentPlan.roomWidth, currentPlan.columns);
            Work.SetAccessories(Permanent.CaptureEffects()); BeginShiftState(RentPhase.Lobby);
            suppressChanges = false; Notify(); return true;
        }
        public bool StartNextAttempt()
        {
            if (!CanStartNextAttempt) return false;
            suppressChanges = true;
            if (!Work.BeginBuilding(currentPlan.floors, currentPlan.roomWidth, currentPlan.columns)) { suppressChanges = false; return false; }
            Work.SetAccessories(Permanent.CaptureEffects()); AttemptsUsed++;
            BeginShiftState(RentPhase.Working); Work.StartAccessoryEffects();
            suppressChanges = false; Notify(); return true;
        }
        public bool CanBuySkill(int id)
        {
            if (Phase == RentPhase.Checkout || Phase == RentPhase.Bankrupt || Stage < LightsOutBalanceSettings.Shared.MinimumDayForSkill(id) || !Work.CanBuySkill(id)) return false;
            return AttemptsUsed < AttemptsPerStage || Work.Money - Work.GetSkillCost(id) >= RentDue;
        }
        public string GetSkillLockReason(int id)
        {
            if (Phase == RentPhase.Checkout) return "퇴근 정산을 먼저 선택하세요. 이번 근무 수익은 아직 보유금에 포함되지 않습니다.";
            if (Phase == RentPhase.Bankrupt) return "월세를 갚지 못했습니다. 처음부터 다시 시작하세요.";
            int day = LightsOutBalanceSettings.Shared.MinimumDayForSkill(id);
            if (Stage < day) return day == int.MaxValue ? "알 수 없는 스킬입니다." : "DAY " + day + "부터 해금됩니다.";
            if (!Work.CanBuySkill(id)) return Work.GetLockReason(id);
            if (AttemptsUsed >= AttemptsPerStage && Work.Money - Work.GetSkillCost(id) < RentDue) return "마지막 기회가 끝났습니다. 월세 납부금을 남겨야 합니다.";
            return "강화 가능 · 이번 근무의 미정산 수익은 사용할 수 없습니다.";
        }
        public bool BuySkill(int id) { return CanBuySkill(id) && Work.BuySkill(id); }

        void OnWorkChanged() { if (!suppressChanges) Notify(); }
        void Notify() { if (Changed != null) Changed(); }
        uint NextRandom()
        { randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5; return randomState; }

        [Serializable] sealed class SaveState
        {
            public int version, stage, phase, attempts, slot, multiplier;
            public float remaining; public double lastIncome;
            public bool lastCompleted, rouletteUsed, settled;
            public uint random; public string work; public StagePlan plan;
        }
        public string ExportJson()
        {
            return JsonUtility.ToJson(new SaveState { version = 2, stage = Stage, phase = (int)Phase, attempts = AttemptsUsed,
                remaining = TimeRemaining, lastIncome = LastShiftIncome, lastCompleted = LastCompleted,
                rouletteUsed = RouletteUsed, settled = SettlementResolved, slot = LastRouletteSlot,
                multiplier = LastRouletteMultiplier, random = randomState, work = Work.ExportJson(), plan = currentPlan });
        }
        public bool ImportJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2100000) return false;
            SaveState saved;
            try { saved = JsonUtility.FromJson<SaveState>(json); } catch (Exception) { return false; }
            if (saved == null || (saved.version != 1 && saved.version != 2) || saved.stage < 1 || saved.stage > MaximumStage ||
                saved.phase < 0 || saved.phase > (saved.version == 1 ? 3 : 4) || saved.attempts < 0 || saved.attempts > AttemptsPerStage ||
                !Finite(saved.remaining) || saved.remaining < 0 || !Finite(saved.lastIncome) || saved.lastIncome < 0 ||
                saved.random == 0 || string.IsNullOrEmpty(saved.work)) return false;
            LightsOutVerticalModel candidate = new LightsOutVerticalModel();
            if (!candidate.ImportJson(saved.work) || !candidate.RewardsDeferred) return false;
            StagePlan plan = saved.version == 1 ? LegacyPlanForDay(saved.stage, candidate.BaseRoomWidth) : saved.plan;
            if (!ValidPlan(plan) || candidate.FloorCount != plan.floors || candidate.FloorRooms != plan.columns ||
                Math.Abs(candidate.BaseRoomWidth - plan.roomWidth) > .000001 || saved.remaining > plan.duration) return false;
            double maximumIncome = BaseIncome(plan.columns, plan.floors) * 2 * LightsOutBalanceSettings.Shared.MaximumComboMultiplier + candidate.PeakCombo * candidate.BadgeBonusPerCombo;
            if (saved.lastIncome > maximumIncome + .000001) return false;
            RentPhase phase = (RentPhase)saved.phase;
            int startedAttempts = saved.attempts + (saved.version == 1 && phase == RentPhase.Working ? 1 : 0);
            if (phase == RentPhase.Lobby)
            {
                if (startedAttempts != 0 || Math.Abs(saved.remaining - plan.duration) > .000001 || saved.lastIncome != 0 || saved.lastCompleted ||
                    saved.rouletteUsed || saved.settled || saved.slot != -1 || saved.multiplier != 1 || candidate.BuildingClosed || candidate.PendingSettled ||
                    candidate.PendingIncome != 0 || candidate.ClearedFloors != 0 || candidate.Combo != 0 || candidate.PeakCombo != 0) return false;
                for (int f = 0; f < candidate.FloorCount; f++) if (candidate.GetRemainingRooms(f) != candidate.FloorRooms) return false;
            }
            else if (phase == RentPhase.Working)
            {
                if (startedAttempts < 1 || startedAttempts > AttemptsPerStage || saved.remaining <= 0 || saved.lastIncome != 0 || saved.lastCompleted ||
                    saved.rouletteUsed || saved.settled || saved.slot != -1 || saved.multiplier != 1 || candidate.BuildingClosed || candidate.PendingSettled) return false;
            }
            else
            {
                if (startedAttempts < 1 || !candidate.BuildingClosed || saved.lastCompleted != candidate.IsComplete || (!saved.lastCompleted && saved.remaining != 0)) return false;
                if (phase == RentPhase.Checkout)
                {
                    if (saved.rouletteUsed || saved.settled || saved.slot != -1 || saved.multiplier != 1 || candidate.PendingSettled ||
                        Math.Abs(saved.lastIncome - candidate.PendingIncome) > .000001) return false;
                }
                else
                {
                    if (!saved.settled || !candidate.PendingSettled || candidate.PendingIncome != 0 ||
                        (saved.rouletteUsed ? saved.slot < 0 || saved.slot >= RouletteSectors || MultiplierForSlot(saved.slot) != saved.multiplier : saved.slot != -1 || saved.multiplier != 1)) return false;
                    if (phase == RentPhase.Bankrupt)
                    { if (startedAttempts != AttemptsPerStage || candidate.Money >= plan.rentDue) return false; }
                    else if (startedAttempts == AttemptsPerStage && candidate.Money < plan.rentDue) return false;
                }
            }
            suppressChanges = true;
            if (!Work.ImportJson(saved.work)) { suppressChanges = false; return false; }
            Stage = saved.stage; Phase = phase; AttemptsUsed = startedAttempts; currentPlan = plan; TimeRemaining = saved.remaining;
            LastShiftIncome = saved.lastIncome; LastCompleted = saved.lastCompleted;
            LastRouletteSlot = saved.slot; LastRouletteMultiplier = saved.multiplier;
            RouletteUsed = saved.rouletteUsed; SettlementResolved = saved.settled; randomState = saved.random;
            suppressChanges = false; Notify(); return true;
        }
        static bool ValidPlan(StagePlan plan)
        {
            return plan.columns >= 2 && plan.columns <= LightsOutVerticalModel.MaximumRooms && plan.floors >= 1 && plan.floors <= LightsOutVerticalModel.MaximumFloors &&
                Finite(plan.roomWidth) && plan.roomWidth >= 1.2f && plan.roomWidth <= 8 && Finite(plan.duration) && plan.duration >= 1 && plan.duration <= 900 &&
                Finite(plan.rentDue) && plan.rentDue >= 1 && plan.rentDue <= 1e15;
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
