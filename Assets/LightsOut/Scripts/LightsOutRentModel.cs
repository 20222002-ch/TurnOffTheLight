using System;
using UnityEngine;

namespace LightsOut
{
    public enum RentPhase { Working, Checkout, BetweenAttempts, Bankrupt }

    /// <summary>
    /// Five timed shifts per rent stage. Shift income remains separate from the bank until
    /// the player chooses a normal checkout or the one-use departure roulette.
    /// </summary>
    public sealed class LightsOutRentModel
    {
        public const int AttemptsPerStage = 5;
        public const int RouletteSectors = 21;
        const int MaximumStage = 100000;
        static readonly int[] BuildingSizes = { 3, 5, 8, 12, 18, 28, 42, 64, 96, 144, 216, 300 };

        public event Action Changed;
        public LightsOutVerticalModel Work { get; private set; }
        public RentPhase Phase { get; private set; }
        public int Stage { get; private set; }
        public int AttemptsUsed { get; private set; }
        public int AttemptsRemaining { get { return AttemptsPerStage - AttemptsUsed; } }
        public int AttemptNumber { get { return Phase == RentPhase.Working ? AttemptsUsed + 1 : Math.Max(1, AttemptsUsed); } }
        public float TimeRemaining { get; private set; }
        public float AttemptDuration { get { return DurationForStage(Stage); } }
        public double RentDue { get { return RentForStage(Stage); } }
        public double LastShiftIncome { get; private set; }
        public bool LastCompleted { get; private set; }
        public int LastRouletteSlot { get; private set; }
        public int LastRouletteMultiplier { get; private set; }
        public bool RouletteUsed { get; private set; }
        public bool SettlementResolved { get; private set; }
        public bool CanPayRent { get { return Phase == RentPhase.BetweenAttempts && Work.Money >= RentDue; } }
        public bool CanStartNextAttempt { get { return Phase == RentPhase.BetweenAttempts && AttemptsUsed < AttemptsPerStage; } }
        public double LastSettledIncome { get { return SettlementResolved ? LastShiftIncome * LastRouletteMultiplier : 0; } }

        uint randomState;
        bool suppressChanges;

        public LightsOutRentModel()
        {
            Work = new LightsOutVerticalModel(BuildingSizes[0], true);
            Work.Changed += OnWorkChanged;
            Reset();
        }

        public static int FloorsForStage(int stage)
        { return BuildingSizes[Math.Max(0, Math.Min(BuildingSizes.Length - 1, stage - 1))]; }
        public static float DurationForStage(int stage)
        { return Math.Min(80, 30 + Math.Max(0, stage - 2) * 5); }
        public static double BaseIncomeForStage(int stage)
        {
            int floors = FloorsForStage(stage);
            return 4 * floors * (5 + .125 * (floors - 1) / 2);
        }
        public static double RentForStage(int stage)
        { return (double)decimal.Ceiling((decimal)BaseIncomeForStage(stage) * (2.6m + Math.Max(0, stage - BuildingSizes.Length) * .08m)); }
        public static int MultiplierForSlot(int slot)
        { return slot < 0 || slot >= RouletteSectors ? -1 : slot < 10 ? 2 : slot < 20 ? 0 : 10; }

        public void Reset()
        {
            suppressChanges = true;
            Stage = 1; AttemptsUsed = 0;
            randomState = (uint)DateTime.UtcNow.Ticks ^ 0xA341316Cu;
            if (randomState == 0) randomState = 2463534242u;
            Work.Reset(BuildingSizes[0]);
            BeginShiftState();
            suppressChanges = false;
            Notify();
        }

        void BeginShiftState()
        {
            Phase = RentPhase.Working;
            TimeRemaining = AttemptDuration;
            LastShiftIncome = 0; LastCompleted = false;
            LastRouletteSlot = -1; LastRouletteMultiplier = 1;
            RouletteUsed = false; SettlementResolved = false;
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
            LastShiftIncome = Work.PendingIncome;
            AttemptsUsed++;
            Work.CloseBuilding();
            Phase = RentPhase.Checkout;
            LastRouletteSlot = -1; LastRouletteMultiplier = 1;
            RouletteUsed = false; SettlementResolved = false;
            suppressChanges = false;
            Notify();
            return true;
        }

        public bool SkipRoulette()
        {
            if (Phase != RentPhase.Checkout || SettlementResolved) return false;
            return ResolveSettlement(false, -1, 1);
        }

        public bool SpinRoulette()
        {
            if (Phase != RentPhase.Checkout || SettlementResolved) return false;
            // Rejection sampling gives all 21 equal sectors the same probability.
            uint value, limit = uint.MaxValue - uint.MaxValue % RouletteSectors;
            do { value = NextRandom() - 1; } while (value >= limit);
            int slot = (int)(value % RouletteSectors);
            return ResolveSettlement(true, slot, MultiplierForSlot(slot));
        }

        bool ResolveSettlement(bool roulette, int slot, int multiplier)
        {
            suppressChanges = true;
            if (!Work.SettlePending(multiplier)) { suppressChanges = false; return false; }
            RouletteUsed = roulette;
            LastRouletteSlot = slot; LastRouletteMultiplier = multiplier;
            SettlementResolved = true;
            Phase = AttemptsUsed == AttemptsPerStage && Work.Money < RentDue ? RentPhase.Bankrupt : RentPhase.BetweenAttempts;
            suppressChanges = false;
            Notify();
            return true;
        }

        public bool PayRent()
        {
            if (!CanPayRent || Stage >= MaximumStage) return false;
            suppressChanges = true;
            if (!Work.TrySpend(RentDue)) { suppressChanges = false; return false; }
            Stage++; AttemptsUsed = 0;
            Work.BeginBuilding(FloorsForStage(Stage));
            BeginShiftState();
            suppressChanges = false;
            Notify();
            return true;
        }

        public bool StartNextAttempt()
        {
            if (!CanStartNextAttempt) return false;
            suppressChanges = true;
            Work.BeginBuilding(FloorsForStage(Stage));
            BeginShiftState();
            suppressChanges = false;
            Notify();
            return true;
        }

        public bool CanBuySkill(int id)
        {
            if (Phase == RentPhase.Checkout || Phase == RentPhase.Bankrupt || !Work.CanBuySkill(id)) return false;
            return AttemptsUsed < AttemptsPerStage || Work.Money - Work.GetSkillCost(id) >= RentDue;
        }

        public string GetSkillLockReason(int id)
        {
            if (Phase == RentPhase.Checkout) return "퇴근 정산을 먼저 선택하세요. 이번 근무 수익은 아직 보유금에 포함되지 않습니다.";
            if (Phase == RentPhase.Bankrupt) return "월세를 갚지 못했습니다. 처음부터 다시 시작하세요.";
            if (!Work.CanBuySkill(id)) return Work.GetLockReason(id);
            if (AttemptsUsed >= AttemptsPerStage && Work.Money - Work.GetSkillCost(id) < RentDue)
                return "마지막 기회가 끝났습니다. 월세 납부금을 남겨야 합니다.";
            return "강화 가능 · 이번 근무의 미정산 수익은 사용할 수 없습니다.";
        }

        public bool BuySkill(int id)
        { return CanBuySkill(id) && Work.BuySkill(id); }

        void OnWorkChanged() { if (!suppressChanges) Notify(); }
        void Notify() { if (Changed != null) Changed(); }
        uint NextRandom()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return randomState;
        }

        [Serializable] sealed class SaveState
        {
            public int version, stage, phase, attempts;
            public float remaining;
            public double lastIncome;
            public bool lastCompleted, rouletteUsed, settled;
            public int slot, multiplier;
            public uint random;
            public string work;
        }

        public string ExportJson()
        {
            return JsonUtility.ToJson(new SaveState
            {
                version = 1, stage = Stage, phase = (int)Phase, attempts = AttemptsUsed,
                remaining = TimeRemaining, lastIncome = LastShiftIncome, lastCompleted = LastCompleted,
                rouletteUsed = RouletteUsed, settled = SettlementResolved, slot = LastRouletteSlot,
                multiplier = LastRouletteMultiplier, random = randomState, work = Work.ExportJson()
            });
        }

        public bool ImportJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2100000) return false;
            SaveState saved;
            try { saved = JsonUtility.FromJson<SaveState>(json); }
            catch (Exception) { return false; }
            if (saved == null || saved.version != 1 || saved.stage < 1 || saved.stage > MaximumStage ||
                saved.phase < 0 || saved.phase > 3 || saved.attempts < 0 || saved.attempts > AttemptsPerStage ||
                !Finite(saved.remaining) || saved.remaining < 0 || saved.remaining > DurationForStage(saved.stage) ||
                !Finite(saved.lastIncome) || saved.lastIncome < 0 || saved.lastIncome > BaseIncomeForStage(saved.stage) * 2 + .000001 ||
                saved.random == 0 || string.IsNullOrEmpty(saved.work)) return false;

            // Validate into an isolated candidate; keep the live Work instance and its observers.
            LightsOutVerticalModel candidate = new LightsOutVerticalModel();
            if (!candidate.ImportJson(saved.work) || candidate.FloorCount != FloorsForStage(saved.stage) || !candidate.RewardsDeferred) return false;
            RentPhase phase = (RentPhase)saved.phase;
            if (phase == RentPhase.Working)
            {
                if (saved.attempts >= AttemptsPerStage || saved.remaining <= 0 || saved.lastIncome != 0 || saved.lastCompleted ||
                    saved.rouletteUsed || saved.settled || saved.slot != -1 || saved.multiplier != 1 || candidate.BuildingClosed || candidate.PendingSettled) return false;
            }
            else
            {
                if (saved.attempts < 1 || !candidate.BuildingClosed || saved.lastCompleted != candidate.IsComplete ||
                    (!saved.lastCompleted && saved.remaining != 0)) return false;
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
                    {
                        if (saved.attempts != AttemptsPerStage || candidate.Money >= RentForStage(saved.stage)) return false;
                    }
                    else if (saved.attempts == AttemptsPerStage && candidate.Money < RentForStage(saved.stage)) return false;
                }
            }

            suppressChanges = true;
            Work.ImportJson(saved.work);
            Stage = saved.stage; Phase = phase; AttemptsUsed = saved.attempts; TimeRemaining = saved.remaining;
            LastShiftIncome = saved.lastIncome; LastCompleted = saved.lastCompleted;
            LastRouletteSlot = saved.slot; LastRouletteMultiplier = saved.multiplier;
            RouletteUsed = saved.rouletteUsed; SettlementResolved = saved.settled; randomState = saved.random;
            suppressChanges = false;
            Notify();
            return true;
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
