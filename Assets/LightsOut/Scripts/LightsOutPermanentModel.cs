using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut
{
    /// <summary>Immutable effects captured when an outfit is committed before work starts.</summary>
    public sealed class AccessoryEffects
    {
        public float HoldBonusSeconds { get; private set; }
        public float ComboMovePerCount { get; private set; }
        public int KeyOpeningRooms { get; private set; }
        public int OpeningKeyCount { get { return KeyOpeningRooms; } }
        public float RingChance { get; private set; }
        public int RingRange { get; private set; }
        public float BadgeBonusPerCombo { get; private set; }

        public AccessoryEffects(float holdBonusSeconds = 0, float comboMovePerCount = 0,
            int keyOpeningRooms = 0, float ringChance = 0, int ringRange = 0, float badgeBonusPerCombo = 0)
        {
            HoldBonusSeconds = holdBonusSeconds;
            ComboMovePerCount = comboMovePerCount;
            KeyOpeningRooms = keyOpeningRooms;
            RingChance = ringChance;
            RingRange = ringRange;
            BadgeBonusPerCombo = badgeBonusPerCombo;
        }
    }

    /// <summary>Separate permanent progress. No file IO and no dependency on a campaign reset.</summary>
    public sealed class LightsOutPermanentModel
    {
        public const int AccessoryCount = 5, EquipmentSlots = 3;
        const int MaximumPoints = 1000000000, MaximumDay = 100000;
        public static readonly string[] Names = { "절전 장갑", "야근 운동화", "관리인 열쇠", "누전 반지", "황금 명찰" };
        public static readonly string[] Descriptions =
        {
            "방을 떠난 뒤 소등 진행도 유지 시간 +0.5초/레벨",
            "콤보마다 이동속도 증가 · 레벨에 따라 콤보당 효과 강화",
            "근무 시작 시 3개 방 자동 소등 · 추가 레벨당 2개 방 증가",
            "소등 성공 시 주변 소등 발동 확률 +10%/레벨",
            "근무 종료 시 최고 콤보당 추가 수익 +0.25/레벨"
        };

        public event Action Changed;
        public int Points { get; private set; }
        public int HighestPaidDay { get; private set; }
        public int[] Levels { get; private set; }
        public int[] Equipped { get; private set; }
        public int MaxLevel { get { return Math.Max(1, Math.Min(5, LightsOutBalanceSettings.Shared.MaxAccessoryLevel)); } }

        public LightsOutPermanentModel()
        {
            Levels = new int[AccessoryCount];
            Equipped = new int[EquipmentSlots];
            for (int i = 0; i < EquipmentSlots; i++) Equipped[i] = -1;
        }

        public int UpgradeCost(int id)
        {
            if (!ValidId(id) || Levels[id] >= MaxLevel) return int.MaxValue;
            var settings = LightsOutBalanceSettings.Shared;
            int[] prices = settings.AccessoryBaseCosts;
            int initial = prices != null && prices.Length > id && prices[id] > 0 ? prices[id] : DefaultBaseCost(id);
            float growth = settings.AccessoryCostGrowth;
            if (float.IsNaN(growth) || float.IsInfinity(growth) || growth < 1) growth = 1.8f;
            double cost = Math.Ceiling(initial * Math.Pow(growth, Levels[id]));
            return cost >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)cost);
        }

        public bool CanUpgrade(int id)
        { return ValidId(id) && Levels[id] < MaxLevel && UpgradeCost(id) <= Points; }

        public bool Upgrade(int id)
        {
            if (!CanUpgrade(id)) return false;
            Points -= UpgradeCost(id); Levels[id]++;
            Notify(); return true;
        }

        public bool IsEquipped(int id)
        {
            if (!ValidId(id) || Levels[id] == 0) return false;
            for (int i = 0; i < EquipmentSlots; i++) if (Equipped[i] == id) return true;
            return false;
        }

        public bool ToggleEquip(int id)
        {
            if (!ValidId(id) || Levels[id] <= 0) return false;
            for (int i = 0; i < EquipmentSlots; i++)
                if (Equipped[i] == id) { Equipped[i] = -1; Notify(); return true; }
            for (int i = 0; i < EquipmentSlots; i++)
                if (Equipped[i] < 0) { Equipped[i] = id; Notify(); return true; }
            return false;
        }

        public AccessoryEffects CaptureEffects()
        {
            int gloves = EquippedLevel(0), shoes = EquippedLevel(1), keys = EquippedLevel(2);
            int ring = EquippedLevel(3), badge = EquippedLevel(4);
            return new AccessoryEffects(gloves * .5f, shoes > 0 ? .01f + .005f * (shoes - 1) : 0,
                keys > 0 ? 3 + (keys - 1) * 2 : 0, ring * .1f, ring > 0 ? (ring >= 5 ? 2 : 1) : 0,
                badge * .25f);
        }

        public int PreviewEarlyBonus(int day, int attemptsRemaining)
        { return IsNewClear(day) ? Math.Max(0, Math.Min(3, attemptsRemaining)) : 0; }

        public int PreviewPaymentPoints(int day, int attemptsRemaining)
        { return IsNewClear(day) ? 1 + PreviewEarlyBonus(day, attemptsRemaining) : 0; }

        public int RecordRentPayment(int day, int attemptsRemaining)
        {
            int award = PreviewPaymentPoints(day, attemptsRemaining);
            if (award == 0) return 0;
            HighestPaidDay = day;
            int actual = Math.Min(award, MaximumPoints - Points);
            Points += actual;
            Notify(); return actual;
        }

        int EquippedLevel(int id) { return IsEquipped(id) ? Levels[id] : 0; }
        bool IsNewClear(int day) { return day >= 1 && day <= MaximumDay && day > HighestPaidDay; }
        static bool ValidId(int id) { return id >= 0 && id < AccessoryCount; }
        static int DefaultBaseCost(int id) { return id == 0 ? 3 : id == 1 ? 4 : id == 4 ? 6 : 5; }
        void Notify() { if (Changed != null) Changed(); }

        [Serializable] sealed class SaveState
        { public int version, points, highest; public int[] levels, equipped; }

        public string ExportJson()
        {
            return JsonUtility.ToJson(new SaveState
            { version = 1, points = Points, highest = HighestPaidDay, levels = Levels, equipped = Equipped });
        }

        public bool ImportJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16384) return false;
            SaveState saved;
            try { saved = JsonUtility.FromJson<SaveState>(json); }
            catch (Exception) { return false; }
            if (saved == null || saved.version != 1 || saved.points < 0 || saved.points > MaximumPoints ||
                saved.highest < 0 || saved.highest > MaximumDay || saved.levels == null || saved.levels.Length != AccessoryCount ||
                saved.equipped == null || saved.equipped.Length != EquipmentSlots) return false;
            for (int i = 0; i < AccessoryCount; i++) if (saved.levels[i] < 0 || saved.levels[i] > MaxLevel) return false;
            HashSet<int> slots = new HashSet<int>();
            for (int i = 0; i < EquipmentSlots; i++)
            {
                int id = saved.equipped[i];
                if (id == -1) continue;
                if (!ValidId(id) || saved.levels[id] == 0 || !slots.Add(id)) return false;
            }
            Points = saved.points; HighestPaidDay = saved.highest;
            Levels = (int[])saved.levels.Clone(); Equipped = (int[])saved.equipped.Clone();
            Notify(); return true;
        }
    }
}
