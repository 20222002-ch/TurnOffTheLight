using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightsOut
{
    public sealed class VerticalSkillDefinition
    {
        public readonly int Id, Branch, MaxLevel, PrerequisiteId, PrerequisiteLevel;
        public readonly string Name, EffectDescription;
        public readonly double BaseCost, CostGrowth;
        public VerticalSkillDefinition(int id, int branch, string name, int max, double cost, double growth, int predecessor, int prerequisiteLevel, string effect)
        { Id=id; Branch=branch; Name=name; MaxLevel=max; BaseCost=cost; CostGrowth=growth; PrerequisiteId=predecessor; PrerequisiteLevel=prerequisiteLevel; EffectDescription=effect; }
    }

    public enum LightOrigin { Movement, Automatic }

    public sealed class VerticalExtinguishEvent
    {
        public int[] AffectedIds;
        public double Reward;
        public bool Automated;
        public LightOrigin Origin;
        public long ActionId;
        public bool FloorCompleted;
        public int[] CompletedFloorIds;
        public bool CashFound, HighBonus;
        public double CashReward, RandomBonus;
    }

    /// <summary>Logical rooms, growth, finite delayed propagation and saves. No input or rendering.</summary>
    public sealed class LightsOutVerticalModel
    {
        public static readonly VerticalSkillDefinition[] Skills =
        {
            new VerticalSkillDefinition(0,0,"이동속도 증가",10,50,1.65,-1,0,"기본 이동속도 +12%/레벨"),
            new VerticalSkillDefinition(1,0,"소등 범위 확장",5,120,1.9,0,3,"접촉 소등 반경 +0.45/레벨"),
            new VerticalSkillDefinition(2,0,"대시",5,300,1.9,1,2,"대시 해금 · 강화 시 재사용 시간 감소"),
            new VerticalSkillDefinition(3,0,"대시 거리 증가",5,900,1.95,2,2,"대시 이동거리 +2/레벨"),
            new VerticalSkillDefinition(4,0,"이동 중 보너스 수익",1,14000,1,3,3,"이동에서 시작한 소등 수입 2배 · 배지 보너스는 별도"),
            new VerticalSkillDefinition(5,1,"연쇄 소등",5,45,1.8,-1,0,"이동 소등에서 연쇄 발동 확률 +20%/레벨"),
            new VerticalSkillDefinition(6,1,"연쇄 확장",5,160,1.9,5,2,"연쇄의 수평 전파 거리 +1칸/레벨"),
            new VerticalSkillDefinition(7,1,"연쇄 가속",5,550,1.9,6,2,"연쇄 전파 간격 -15%/레벨 · 최대 75%"),
            new VerticalSkillDefinition(8,1,"과부하",5,2200,1.9,7,2,"연쇄 발동 시 위아래 층 전파 확률 +5%/레벨"),
            new VerticalSkillDefinition(9,1,"블랙아웃",1,16000,1,8,3,"연쇄 소등 30개 누적 후 다음 연쇄가 층 전체 소등"),
            new VerticalSkillDefinition(10,2,"수직 소등",5,65,1.8,-1,0,"이동 소등에서 위층 전파 확률 +15%/레벨"),
            new VerticalSkillDefinition(11,2,"층 전체 정전",5,220,1.9,10,2,"수직 발동 시 층 전체 정전 확률 +10%/레벨"),
            new VerticalSkillDefinition(12,2,"수직 관통",5,800,1.9,11,2,"수직 소등이 위쪽으로 추가 1개 층/레벨 전파"),
            new VerticalSkillDefinition(13,2,"상승 가속",5,2600,1.9,12,2,"연속 층 완료 중 카메라 추적 속도 +15%/레벨"),
            new VerticalSkillDefinition(14,2,"초고속 돌파",1,18000,1,13,3,"10개 층 연속 완료 시 5초간 수직 확률 +25%p"),
            new VerticalSkillDefinition(15,3,"자동 스위치",10,150,1.6,-1,0,"자동 소등 0.2개/초/레벨 · 소수량 누적"),
            new VerticalSkillDefinition(16,3,"자동 소등 속도",10,380,1.6,15,3,"자동 소등 주기 -8%/레벨 · 최대 80%"),
            new VerticalSkillDefinition(17,3,"자동 층 정전",5,1300,1.9,16,3,"자동 소등 30회마다 추가 1개 층 · 레벨별 추가효과 검토 중"),
            new VerticalSkillDefinition(18,3,"자동 연쇄",5,3600,1.9,17,2,"자동 소등에서 연쇄 발동 확률 +10%/레벨"),
            new VerticalSkillDefinition(19,3,"완전 자동화",1,20000,1,18,3,"자동 소등 속도 2배 · 자동 연쇄 확률 +20%p"),
            new VerticalSkillDefinition(20,2,"건물 가로 축소",5,450,1.9,10,2,"건물 가로 폭 -7%/레벨 · 최대 35%"),
            new VerticalSkillDefinition(21,2,"수직 돌파",3,9000,2,12,3,"위로 8개 층 순간 상승 · 강화 시 거리 증가 및 재사용 시간 감소"),
            new VerticalSkillDefinition(22,0,"소등 속도 강화",6,35,1.6,-1,0,"방 소등 시간 단축 · 최소 작업 시간은 설정값 유지"),
            new VerticalSkillDefinition(23,2,"상층 연동 소등",3,45,1.8,-1,0,"직접 소등 완료 시 같은 위치 위쪽 1개 층/레벨 함께 소등"),
            new VerticalSkillDefinition(24,2,"층 완료 정전",3,180,1.8,11,2,"층 완료 시 위아래 인접층 1개 방/레벨 소등 · 최대 2회 전파"),
            new VerticalSkillDefinition(25,0,"즉시 소등",5,120,1.8,22,2,"처음 방에 들어갈 때 즉시 소등 확률 +5%/레벨 · 방마다 한 번 추첨")
        };

        public const int MaximumFloors=4096, MaximumRooms=20;
        const int MaxPending=8192, PerTickBudget=256;
        const double MaxMoney=1e15;
        static readonly string[] StageNames={"작은 사무실","사무동","대형 빌딩","복합 건물","야간 도시","도시 전력망"};
        static readonly int[] StageStarts={0,12,36,72,120,200};
        public event Action Changed;
        public event Action<VerticalExtinguishEvent> Extinguished;
        public event Action<int> ExperienceEarned;
        public event Action<float> TimeBonusRequested;
        public LightsOutShiftStats ShiftStats { get { return income.stats; } }
        public double BaseIncome { get { return ShiftStats.RawBaseIncome; } }
        public double GradeAdjustment { get { return ShiftStats.GradeAdjustment; } }
        public double RandomBonus { get { return ShiftStats.RandomBonus; } }
        public double CashBonus { get { return ShiftStats.CashBonus; } }
        public double SpecialBonus { get { return ShiftStats.SpecialBonus; } }
        public double ComboBonus { get { return ShiftStats.ComboBonus; } }
        public double SkillBonus { get { return ShiftStats.SkillBonus; } }
        public double TotalWorkIncome { get { return ShiftStats.TotalIncome; } }
        public int TotalRoomOff { get { return ShiftStats.TotalCount; } }
        public int DirectCount { get { return ShiftStats.DirectCount; } }
        public int PropagatedCount { get { return ShiftStats.PropagatedCount; } }
        public int AutomatedCount { get { return ShiftStats.AutomatedCount; } }
        public int XPDirect { get { return ShiftStats.XPDirect; } }
        public int XPPropagation { get { return ShiftStats.XPPropagation; } }
        public int XPSpecial { get { return ShiftStats.XPSpecial; } }
        public int XPFloors { get { return ShiftStats.XPFloors; } }
        public int XPBuilding { get { return ShiftStats.XPBuilding; } }
        public int ShiftExperienceTotal { get { return ShiftStats.ShiftExperienceTotal; } }
        public string BuildingGradeName { get { return ShiftStats.GradeName; } }
        public double BuildingGradeMultiplier { get { return ShiftStats.GradeMultiplier; } }
        public int PermanentLevelAtStart { get { return income.unlockLevel; } }
        public float InstantExtinguishChance { get { return LightsOutBalanceSettings.Shared.InstantChanceForLevel(SkillLevels[25]); } }
        public int FloorCount { get { return floorCount; } }
        public int FloorRooms { get { return roomsPerFloor; } }
        public int Combo { get; private set; }
        public int PeakCombo { get; private set; }
        public float ComboRemaining { get; private set; }
        public float ComboRewardMultiplier { get { return LightsOutBalanceSettings.Shared.ComboMultiplierFor(Combo); } }
        public float ExtinguishSeconds { get { return LightsOutBalanceSettings.Shared.ExtinguishSecondsForLevel(SkillLevels[22]); } }
        public int LinkFloors { get { return SkillLevels[23]; } }
        public float HoldSeconds { get { return Mathf.Clamp(LightsOutBalanceSettings.Shared.WorkHoldSeconds,.1f,10)+(accessories==null?0:accessories.HoldBonusSeconds); } }
        public float WorkHoldSeconds { get { return HoldSeconds; } }
        public int OpeningKeyCount { get { return accessories==null?0:accessories.KeyOpeningRooms; } }
        public float RingChance { get { return accessories==null?0:accessories.RingChance; } }
        public int RingRange { get { return accessories==null?0:accessories.RingRange; } }
        public float BadgeBonusPerCombo { get { return accessories==null?0:accessories.BadgeBonusPerCombo; } }
        public float BaseRoomWidth { get; private set; } = 2.4f;
        public float WidthShrink { get { return .07f * SkillLevels[20]; } }
        public float RoomWidth { get { return BaseRoomWidth * (1 - WidthShrink); } }
        public float BuildingWidth { get { return roomsPerFloor * RoomWidth; } }
        public float HalfWidth { get { return BuildingWidth * .5f; } }
        public double Money { get; private set; }
        public double PendingIncome { get; private set; }
        public bool RewardsDeferred { get; private set; }
        public bool BuildingClosed { get; private set; }
        public bool PendingSettled { get; private set; }
        public double TotalEarned { get; private set; }
        public long TotalOff { get; private set; }
        public int CurrentFloor { get; private set; }
        public int ClearedFloors { get; private set; }
        public int HighestClearedFloor { get; private set; }
        public int[] SkillLevels { get; private set; }
        public int RunNumber { get; private set; }
        public float SessionSeconds { get; private set; }
        public float SurgeRemaining { get; private set; }
        public int BlackoutCharge { get; private set; }
        public int ConsecutiveFloors { get; private set; }
        public int AutoFloorCharge { get; private set; }
        public bool IsComplete { get { return ClearedFloors==floorCount; } }
        public bool CanGoHome { get { return IsComplete; } }
        public int PendingEventCount { get { return queue.Count; } }
        public float MoveSpeed { get { return 4.8f*(1+.12f*SkillLevels[0])*(1+Math.Min(Combo,20)*(accessories==null?0:accessories.ComboMovePerCount)); } }
        public float ExtinguishRadius { get { return .12f+.45f*SkillLevels[1]; } }
        public bool DashUnlocked { get { return SkillLevels[2]>0; } }
        public float DashDistance { get { return 5f+2f*SkillLevels[3]; } }
        public float DashCooldown { get { return Math.Max(2.6f,5f-.6f*Math.Max(0,SkillLevels[2]-1)); } }
        public bool RiseUnlocked { get { return SkillLevels[21] > 0; } }
        public int RiseFloors { get { return RiseUnlocked ? 8 + 4 * (SkillLevels[21] - 1) : 0; } }
        public float RiseDistance { get { return RiseFloors * 1.8f; } }
        public float RiseCooldown { get { return RiseUnlocked ? 14 - 2 * (SkillLevels[21] - 1) : 0; } }
        public float RiseDuration { get { return .28f; } }
        public float CameraSpeedMultiplier { get { return ConsecutiveFloors>=2 ? 1+.15f*SkillLevels[13] : 1; } }
        public float ChainProbability { get { return ClampChance(SkillLevels[5]*.2f); } }
        public float VerticalProbability { get { return ClampChance(SkillLevels[10]*.15f+(SurgeRemaining>0?.25f:0)); } }
        public float AutoChainProbability { get { return ClampChance(SkillLevels[18]*.1f+(SkillLevels[19]>0?.2f:0)); } }
        public float ChainInterval { get { return Math.Max(.055f,.22f*(1-Math.Min(.75f,SkillLevels[7]*.15f))); } }
        public float AutoPeriodMultiplier { get { return Math.Max(.2f,1-SkillLevels[16]*.08f); } }
        public float AutomationRate { get { return SkillLevels[15]*.2f/AutoPeriodMultiplier*(SkillLevels[19]>0?2:1); } }
        public float AutomationProgress { get { return (float)Math.Min(1,automaticAccumulator); } }
        public int StageIndex
        {
            get { int stage=0; for(int i=1;i<StageStarts.Length;i++) if(CurrentFloor>=StageStarts[i]) stage=i; return stage; }
        }
        public string StageName { get { return StageNames[StageIndex]; } }

        int floorCount,roomsPerFloor=4;
        LightsOutIncomeState income;
        bool legacyIncome;
        AccessoryEffects accessories;
        bool openingKeysApplied,departureBonusApplied;
        bool[] on;
        int[] floorRemaining;
        uint randomState;
        double clock,automaticAccumulator,lastFloorCompletion;
        long nextActionId,nextSequence;
        int autoCursor;
        readonly List<Pending> queue=new List<Pending>();
        readonly List<int> relights=new List<int>();
        enum PropagationKind { Contact, Chain, Vertical, WholeFloor, AutoBase, Linked, SpecialLinked, RangeWork }
        int schedulingDepth;
        sealed class RootAction
        {
            public long Id; public bool Automatic;public int CompletionWaves,AffectedCount,Limit;
            public readonly HashSet<int> Visited=new HashSet<int>();
        }
        sealed class Pending
        {
            public int Id,Depth; public double Due; public long Sequence;
            public PropagationKind Kind; public RootAction Root;
        }
        sealed class Batch
        {
            public RootAction Root; public double Reward,CashReward,RandomBonus;public bool CashFound,HighBonus;
            public readonly List<int> Ids=new List<int>();
            public readonly List<int> Floors=new List<int>();
        }

        public LightsOutVerticalModel(int floors=300,bool deferRewards=false,float roomWidth=2.4f,int rooms=4)
        {
            if(floors<1||floors>MaximumFloors)throw new ArgumentOutOfRangeException("floors");
            if(!ValidRoomWidth(roomWidth))throw new ArgumentOutOfRangeException("roomWidth");
            if(!ValidRoomCount(rooms))throw new ArgumentOutOfRangeException("rooms");
            floorCount=floors;roomsPerFloor=rooms;RewardsDeferred=deferRewards;BaseRoomWidth=roomWidth;Reset();
        }
        public void Reset(int floors,float roomWidth=2.4f,int rooms=4)
        {
            if(floors<1||floors>MaximumFloors)throw new ArgumentOutOfRangeException("floors");
            if(!ValidRoomWidth(roomWidth))throw new ArgumentOutOfRangeException("roomWidth");
            if(!ValidRoomCount(rooms))throw new ArgumentOutOfRangeException("rooms");
            floorCount=floors;roomsPerFloor=rooms;BaseRoomWidth=roomWidth;Reset();
        }
        public void Reset()
        {
            Money=0;TotalEarned=0;TotalOff=0;RunNumber=1;SessionSeconds=0;clock=0;
            SkillLevels=new int[Skills.Length];nextActionId=0;nextSequence=0;
            randomState=(uint)DateTime.UtcNow.Ticks^0x9E3779B9u;if(randomState==0)randomState=97193711;
            RenewBuilding();Notify();
        }
        void RenewBuilding()
        {
            on=new bool[floorCount*roomsPerFloor];for(int i=0;i<on.Length;i++)on[i]=true;
            floorRemaining=new int[floorCount];for(int i=0;i<floorCount;i++)floorRemaining[i]=roomsPerFloor;
            CurrentFloor=0;ClearedFloors=0;HighestClearedFloor=-1;
            SurgeRemaining=0;BlackoutCharge=0;ConsecutiveFloors=0;AutoFloorCharge=0;
            automaticAccumulator=0;lastFloorCompletion=-100;autoCursor=0;queue.Clear();relights.Clear();
            PendingIncome=0;BuildingClosed=false;PendingSettled=false;
            Combo=PeakCombo=0;ComboRemaining=0;openingKeysApplied=departureBonusApplied=false;
            income=LightsOutIncomeState.Create(on.Length,floorCount);legacyIncome=false;
        }
        public bool StartNextRun()
        { if(!IsComplete||(RewardsDeferred&&!PendingSettled))return false;return BeginBuilding(floorCount,BaseRoomWidth,roomsPerFloor); }
        public bool BeginBuilding(int floors,float roomWidth=2.4f,int rooms=4)
        {
            if(floors<1||floors>MaximumFloors||PendingIncome>0||!ValidRoomWidth(roomWidth)||!ValidRoomCount(rooms))return false;
            floorCount=floors;roomsPerFloor=rooms;BaseRoomWidth=roomWidth;RunNumber++;RenewBuilding();Notify();return true;
        }
        public void SetAccessories(AccessoryEffects effects) { if(openingKeysApplied&&!BuildingClosed)return;accessories=effects;Notify(); }
        public void CaptureSpecialUnlocks(int level) { if(!income.prepared)income.unlockLevel=Mathf.Clamp(level,1,1000); }
        public void PrepareShift(int day,int level)
        { if(income.prepared)return;LightsOutIncomeRules.Prepare(income,day,level,LightsOutBalanceSettings.Shared,NextRandom);Notify(); }
        public void PrepareIncome() { EnsureIncomePrepared(); }
        void EnsureIncomePrepared() { if(!income.prepared)PrepareShift(income.day,income.unlockLevel); }
        public bool StartAccessoryEffects()
        {
            if(BuildingClosed||openingKeysApplied)return false;
            EnsureIncomePrepared();
            openingKeysApplied=true;int keys=Math.Min(11,Math.Max(0,OpeningKeyCount));
            for(int i=0;i<keys&&!IsComplete;i++)
            {
                int id=(int)(NextRandom()*on.Length),checkedCount=0;
                while(!on[id]&&checkedCount<on.Length){id=(id+1)%on.Length;checkedCount++;}
                if(checkedCount>=on.Length)break;
                RootAction root=NewAction(true);root.Visited.Add(id);Batch batch=new Batch{Root=root};
                Process(new Pending{Id=id,Root=root,Kind=PropagationKind.AutoBase,Due=clock},batch);Emit(batch);
            }
            Notify();return true;
        }
        public bool FinalizeDepartureBonus()
        {
            if(BuildingClosed||departureBonusApplied)return false;
            departureBonusApplied=true;double bonus=PeakCombo*BadgeBonusPerCombo;
            if(bonus>0){income.badgeBonus=bonus;ShiftStats.SkillBonus+=bonus;CreditIncome(bonus);}Notify();return true;
        }
        public bool AwardBuildingExperience()
        {
            if(BuildingClosed||!IsComplete||income.buildingXpAwarded)return false;
            income.buildingXpAwarded=true;int amount=SafeExperience(LightsOutBalanceSettings.Shared.XPBuildingComplete);
            ShiftStats.XPBuilding+=amount;EmitExperience(amount);Notify();return true;
        }
        public void CloseBuilding()
        { if(BuildingClosed)return;BuildingClosed=true;queue.Clear();relights.Clear();automaticAccumulator=0;Array.Clear(income.faultyDue,0,income.faultyDue.Length);Notify(); }
        public bool SettlePending(int multiplier)
        {
            if(!RewardsDeferred||!BuildingClosed||PendingSettled||
                (multiplier!=0&&multiplier!=1&&multiplier!=2&&multiplier!=10))return false;
            double paid=PendingIncome*multiplier;PendingIncome=0;PendingSettled=true;
            CreditBank(paid);Notify();return true;
        }
        public bool TrySpend(double amount)
        { if(!Finite(amount)||amount<=0||amount>Money)return false;Money-=amount;Notify();return true; }
        public bool IsLightOn(int floor,int col)
        { return ValidRoom(floor,col)&&on[floor*roomsPerFloor+col]; }
        public LightKind GetLightKind(int floor,int col) { return LightKind.Normal; }
        public RoomSpecialKind GetRoomKind(int floor,int col)
        { return ValidRoom(floor,col)?(RoomSpecialKind)income.kinds[floor*roomsPerFloor+col]:RoomSpecialKind.Normal; }
        public float GetRoomWorkDuration(int floor,int col)
        { return ExtinguishSeconds*(ValidRoom(floor,col)?income.workMultipliers[floor*roomsPerFloor+col]:1); }
        public float GetRoomWorkMultiplier(int floor,int col)
        { return ValidRoom(floor,col)?income.workMultipliers[floor*roomsPerFloor+col]:1; }
        public float GetCashBagFactor(int floor,int col)
        { return ValidRoom(floor,col)?income.cashFactors[floor*roomsPerFloor+col]:0; }
        public bool HasDiscoveredCashBag(int floor,int col)
        { return ValidRoom(floor,col)&&income.paid[floor*roomsPerFloor+col]&&GetCashBagFactor(floor,col)>0; }
        public bool HasTriedInstant(int floor,int col)
        { return ValidRoom(floor,col)&&income.instantAttempts[floor*roomsPerFloor+col]; }
        public bool TryInstantContact(int floor,int col,bool physical=true)
        {
            if(BuildingClosed||!IsLightOn(floor,col)||IsComplete)return false;
            EnsureIncomePrepared();int id=floor*roomsPerFloor+col;
            if(income.instantAttempts[id])return false;
            income.instantAttempts[id]=true;
            if(Roll(InstantExtinguishChance))return CompleteRoomWork(floor,col,physical);
            Notify();return false;
        }
        public int GetRemainingRooms(int floor) { return floor>=0&&floor<floorCount?floorRemaining[floor]:0; }
        public double GetRoomReward(int floor,bool automated=false)
        { if(floor<0||floor>=floorCount)return 0;return (5+floor*.125)*(automated||SkillLevels[4]==0?1:2); }

        public bool Contact(int floor,int col)
        {
            if(BuildingClosed||!IsLightOn(floor,col)||IsComplete)return false;
            EnsureIncomePrepared();
            RootAction root=NewAction(false);int id=floor*roomsPerFloor+col;root.Visited.Add(id);
            // Reserve linked targets before normal propagation so their completion is immediate.
            for(int step=1;step<=LinkFloors&&floor+step<floorCount;step++)
            { int linked=(floor+step)*roomsPerFloor+col;if(on[linked])root.Visited.Add(linked); }
            Batch batch=new Batch{Root=root};
            Process(new Pending{Id=id,Due=clock,Root=root,Kind=PropagationKind.Contact},batch);
            for(int step=1;step<=LinkFloors&&floor+step<floorCount;step++)
                Process(new Pending{Id=(floor+step)*roomsPerFloor+col,Depth=1,Due=clock,Root=root,Kind=PropagationKind.Linked},batch);
            if(Roll(RingChance))
            {
                int target=FindRingTarget(floor,col,root);
                if(target>=0&&root.Visited.Add(target))Process(new Pending{Id=target,Depth=1,Due=clock,Root=root,Kind=PropagationKind.Linked},batch);
            }
            Emit(batch);Notify();return true;
        }
        public bool CompleteRoomWork(int floor,int col,bool physical=true)
        {
            if(physical)return Contact(floor,col);
            if(BuildingClosed||!IsLightOn(floor,col)||IsComplete)return false;
            EnsureIncomePrepared();RootAction root=NewAction(false);int id=floor*roomsPerFloor+col;root.Visited.Add(id);
            Batch batch=new Batch{Root=root};Process(new Pending{Id=id,Root=root,Kind=PropagationKind.RangeWork,Due=clock},batch);
            Emit(batch);Notify();return true;
        }

        public bool CanBuySkill(int id)
        {
            return id>=0&&id<Skills.Length&&SkillLevels[id]<Skills[id].MaxLevel&&
                (Skills[id].PrerequisiteId<0||SkillLevels[Skills[id].PrerequisiteId]>=Skills[id].PrerequisiteLevel)&&Money>=GetSkillCost(id);
        }
        public double GetSkillCost(int id)
        { if(id<0||id>=Skills.Length||SkillLevels[id]>=Skills[id].MaxLevel)return double.PositiveInfinity;return Math.Ceiling(Skills[id].BaseCost*Math.Pow(Skills[id].CostGrowth,SkillLevels[id])); }
        public bool BuySkill(int id)
        { if(!CanBuySkill(id))return false;Money-=GetSkillCost(id);SkillLevels[id]++;Notify();return true; }
        public string GetLockReason(int id)
        {
            if(id<0||id>=Skills.Length)return "알 수 없는 스킬입니다.";
            VerticalSkillDefinition s=Skills[id];
            if(SkillLevels[id]>=s.MaxLevel)return "최대 레벨에 도달했습니다.";
            if(s.PrerequisiteId>=0&&SkillLevels[s.PrerequisiteId]<s.PrerequisiteLevel)return Skills[s.PrerequisiteId].Name+" Lv."+s.PrerequisiteLevel+" 필요";
            if(Money<GetSkillCost(id))return "성과급이 부족합니다. 필요한 금액: "+GetSkillCost(id).ToString("N0");
            return "강화 가능";
        }
        public string GetCurrentEffect(int id)
        { return id<0||id>=Skills.Length?string.Empty:DescribeEffect(id,SkillLevels[id]); }
        public string GetNextEffect(int id)
        { if(id<0||id>=Skills.Length)return string.Empty;if(SkillLevels[id]>=Skills[id].MaxLevel)return "최대 레벨 · "+GetCurrentEffect(id);return DescribeEffect(id,SkillLevels[id]+1); }
        string DescribeEffect(int id,int level)
        {
            if(level==0)return "미해금 · "+Skills[id].EffectDescription;
            switch(id)
            {
                case 0:return "이동속도 "+(4.8f*(1+.12f*level)).ToString("0.0")+" (+"+(level*12)+"%)";
                case 1:return "주변 소등 반경 "+(.12f+.45f*level).ToString("0.00");
                case 2:return "대시 해금 · 재사용 "+Math.Max(2.6f,5-.6f*(level-1)).ToString("0.0")+"초";
                case 3:return "대시 거리 "+(5+2*level).ToString("0")+" (기본 5)";
                case 4:return "이동에서 시작한 소등 수입 ×2 · 배지 보너스는 별도";
                case 5:return "이동 소등 연쇄 확률 "+(level*20)+"%";
                case 6:return "수평 연쇄 최대 거리 "+(1+level)+"칸";
                case 7:return "전파 간격 "+Math.Max(.055f,.22f*(1-level*.15f)).ToString("0.000")+"초 (-"+(level*15)+"%)";
                case 8:return "연쇄 발동 시 위아래 전파 "+(level*5)+"%";
                case 9:return "연쇄 30개마다 다음 연쇄가 층 전체 소등";
                case 10:return "위층 같은 방 수직 소등 "+(level*15)+"%";
                case 11:return "수직 발동 시 층 전체 소등 "+(level*10)+"%";
                case 12:return "수직 전파 총 "+(1+level)+"개 층";
                case 13:return "연속 층 완료 중 카메라 추적 +"+(level*15)+"%";
                case 14:return "10개 층 연속 완료: 5초간 수직 확률 +25%p";
                case 15:return "자동 소등 기본 "+(level*.2f).ToString("0.0")+"개 / 초";
                case 16:return "자동 주기 -"+(level*8)+"% (원래의 "+(100-level*8)+"%)";
                case 17:return "자동 소등 30회마다 추가 1층 · 레벨별 추가효과 검토 중";
                case 18:return "자동 연쇄 발동 "+(level*10)+"%";
                case 19:return "자동 속도 ×2 · 자동 연쇄 확률 +20%p";
                case 20:return "건물 가로 폭 -"+(level*7)+"% (기본 폭의 "+(100-level*7)+"%)";
                case 21:return "위로 "+(8+4*(level-1))+"개 층 순간 상승 · 재사용 "+(14-2*(level-1))+"초";
                case 22:return "방 소등에 "+LightsOutBalanceSettings.Shared.ExtinguishSecondsForLevel(level).ToString("0.##")+"초 필요 · 즉시 소등은 별도 스킬";
                case 23:return "직접 소등 완료 시 같은 위치 위쪽 "+level+"개 층 즉시 소등";
                case 24:return "층 완료 시 위아래 층 "+level+"개 방 소등 · 최대 2회 전파";
                case 25:return "방마다 첫 진입 시 즉시 소등 "+(LightsOutBalanceSettings.Shared.InstantChanceForLevel(level)*100).ToString("0.#")+"% · 재추첨 없음";
                default:return string.Empty;
            }
        }

        public void Tick(float dt)
        {
            if(BuildingClosed||float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
            dt=Math.Min(dt,60);clock+=dt;SessionSeconds=Math.Min(315360000f,SessionSeconds+dt);
            EnsureIncomePrepared();bool dirty=TickFaultyLights();
            if(ComboRemaining>0){ComboRemaining=Math.Max(0,ComboRemaining-dt);if(ComboRemaining==0){Combo=0;dirty=true;}}
            if(SurgeRemaining>0){SurgeRemaining=Math.Max(0,SurgeRemaining-dt);dirty=SurgeRemaining==0;}
            if(ConsecutiveFloors>0&&clock-lastFloorCompletion>3){ConsecutiveFloors=0;dirty=true;}
            if(IsComplete){if(dirty)Notify();return;}
            Dictionary<long,Batch> batches=new Dictionary<long,Batch>();
            automaticAccumulator=Math.Min(10000,automaticAccumulator+AutomationRate*dt);
            int budget=PerTickBudget;
            while(automaticAccumulator>=1&&budget>0&&!IsComplete)
            {
                int id=FindNextOn();if(id<0){automaticAccumulator=0;break;}
                automaticAccumulator-=1;budget--;RootAction root=NewAction(true);root.Visited.Add(id);
                Batch batch=new Batch{Root=root};batches.Add(root.Id,batch);
                Process(new Pending{Id=id,Root=root,Kind=PropagationKind.AutoBase,Due=clock},batch);dirty=true;
            }
            while(budget>0&&queue.Count>0&&queue[0].Due<=clock&&!IsComplete)
            {
                Pending item=Pop();budget--;if(!on[item.Id])continue;
                Batch batch;if(!batches.TryGetValue(item.Root.Id,out batch)){batch=new Batch{Root=item.Root};batches.Add(item.Root.Id,batch);}
                Process(item,batch);dirty=true;
            }
            if(IsComplete)queue.Clear();
            foreach(Batch batch in batches.Values)Emit(batch);
            if(dirty)Notify();
        }

        RootAction NewAction(bool automatic)
        {
            var settings=LightsOutBalanceSettings.Shared;
            return new RootAction{Id=++nextActionId,Automatic=automatic,Limit=Mathf.Clamp(automatic?settings.MaxAutomaticRoomsPerAction:settings.MaxRoomsPerAction,1,128)};
        }
        void Process(Pending item,Batch batch)
        {
            int maximumDepth=Mathf.Clamp(LightsOutBalanceSettings.Shared.MaxPropagationDepth,1,12);
            if(BuildingClosed||!on[item.Id]||item.Root.AffectedCount>=item.Root.Limit||item.Depth>maximumDepth)return;
            schedulingDepth=item.Depth;
            int floor=item.Id/roomsPerFloor,col=item.Id%roomsPerFloor;
            bool first=!income.paid[item.Id];
            on[item.Id]=false;floorRemaining[floor]--;item.Root.AffectedCount++;batch.Ids.Add(item.Id);
            if(first)
            {
                TotalOff=Math.Min(9000000000000000L,TotalOff+1);
                Combo++;PeakCombo=Math.Max(PeakCombo,Combo);income.stats.MaxCombo=PeakCombo;
                ComboRemaining=LightsOutIncomeRules.Safe(LightsOutBalanceSettings.Shared.ComboWindowSeconds,4,.1f,60);
                double reward=LightsOutIncomeRules.CreditRoom(income,item.Id,5+floor*.125,ComboRewardMultiplier,item.Root.Automatic,item.Kind==PropagationKind.Contact||item.Kind==PropagationKind.RangeWork,
                    !item.Root.Automatic&&SkillLevels[4]>0,LightsOutBalanceSettings.Shared,NextRandom);
                CreditIncome(reward);batch.Reward+=reward;
                double raw=5+floor*.125;
                batch.CashReward+=raw*income.cashFactors[item.Id];batch.RandomBonus+=raw*income.randomFactors[item.Id];
                batch.CashFound|=income.cashFactors[item.Id]>0;
                batch.HighBonus|=income.randomFactors[item.Id]>=2||income.kinds[item.Id]==(int)RoomSpecialKind.Golden;
                int experience;
                if(income.kinds[item.Id]>0){experience=SafeExperience(LightsOutBalanceSettings.Shared.XPSpecialRoom);income.stats.XPSpecial+=experience;}
                else if(!item.Root.Automatic&&(item.Kind==PropagationKind.Contact||item.Kind==PropagationKind.RangeWork)){experience=SafeExperience(LightsOutBalanceSettings.Shared.XPDirectRoom);income.stats.XPDirect+=experience;}
                else {experience=SafeExperience(LightsOutBalanceSettings.Shared.XPPropagatedRoom);income.stats.XPPropagation+=experience;}
                EmitExperience(experience);
                ProcessSpecial(item,floor,col);
            }
            if(first&&item.Kind==PropagationKind.Chain&&SkillLevels[9]>0)
            {
                if(BlackoutCharge>=30){BlackoutCharge=0;ScheduleFloor(item.Root,floor,ChainInterval);}
                BlackoutCharge=Math.Min(30,BlackoutCharge+1);
            }
            if(floorRemaining[floor]==0)
            {
                ClearedFloors++;HighestClearedFloor=Math.Max(HighestClearedFloor,floor);
                int previous=CurrentFloor;
                while(CurrentFloor<floorCount-1&&floorRemaining[CurrentFloor]==0)CurrentFloor++;
                if(!income.floorAwarded[floor])
                {
                    income.floorAwarded[floor]=true;batch.Floors.Add(floor);
                    int experience=SafeExperience(LightsOutBalanceSettings.Shared.XPFloorComplete);
                    income.stats.XPFloors+=experience;EmitExperience(experience);
                    int advance=CurrentFloor-previous;if(IsComplete)advance=Math.Max(1,advance);
                    if(advance>0)
                    {
                        ConsecutiveFloors=clock-lastFloorCompletion<=3?ConsecutiveFloors+advance:advance;lastFloorCompletion=clock;
                        if(ConsecutiveFloors>=10&&SkillLevels[14]>0){SurgeRemaining=5;ConsecutiveFloors%=10;}
                    }
                    if(item.Kind!=PropagationKind.RangeWork&&SkillLevels[24]>0&&item.Root.CompletionWaves<2)
                    {
                        item.Root.CompletionWaves++;
                        for(int direction=-1;direction<=1;direction+=2)
                        {
                            int adjacent=floor+direction;if(adjacent<0||adjacent>=floorCount)continue;
                            int selected=0;
                            for(int offset=0;offset<roomsPerFloor&&selected<SkillLevels[24];offset++)
                            {
                                int nextCol=(col+offset)%roomsPerFloor,target=adjacent*roomsPerFloor+nextCol;
                                if(!on[target]||item.Root.Visited.Contains(target))continue;
                                Schedule(item.Root,target,PropagationKind.WholeFloor,ChainInterval);selected++;
                            }
                        }
                    }
                }
            }
            if(first&&item.Root.Automatic&&SkillLevels[17]>0)
            {
                AutoFloorCharge++;
                if(AutoFloorCharge>=30){AutoFloorCharge-=30;int next=FindNextOn();if(next>=0)ScheduleFloor(item.Root,next/roomsPerFloor,ChainInterval);}
            }
            // Only the direct room and horizontal chains may roll the horizontal passive.
            bool chainEligible=item.Kind==PropagationKind.Contact||item.Kind==PropagationKind.Chain||item.Kind==PropagationKind.AutoBase;
            float chainChance=item.Root.Automatic?AutoChainProbability:ChainProbability;
            if(first&&chainEligible&&Roll(chainChance))
            {
                int target=FindHorizontalTarget(floor,col,item.Root);
                if(target>=0)Schedule(item.Root,target,PropagationKind.Chain,ChainInterval);
                if(Roll(SkillLevels[8]*.05f))
                {
                    Schedule(item.Root,(floor-1)*roomsPerFloor+col,PropagationKind.Chain,ChainInterval*1.5);
                    Schedule(item.Root,(floor+1)*roomsPerFloor+col,PropagationKind.Chain,ChainInterval*1.5);
                }
            }
            // A vertical passive is rolled once at the original contact, never at its descendants.
            if(first&&item.Kind==PropagationKind.Contact&&!item.Root.Automatic&&Roll(VerticalProbability))
            {
                bool wholeUpperFloor=Roll(SkillLevels[11]*.1f);
                for(int step=1;step<=1+SkillLevels[12];step++)
                {
                    int upper=floor+step;if(upper>=floorCount)break;
                    Schedule(item.Root,upper*roomsPerFloor+col,PropagationKind.Vertical,ChainInterval*step);
                    if(step==1&&wholeUpperFloor)ScheduleFloor(item.Root,upper,ChainInterval);
                }
            }
            schedulingDepth=0;
        }
        void ProcessSpecial(Pending item,int floor,int col)
        {
            var settings=LightsOutBalanceSettings.Shared;
            RoomSpecialKind kind=(RoomSpecialKind)income.kinds[item.Id];
            if(item.Kind==PropagationKind.SpecialLinked&&(kind==RoomSpecialKind.Electrical||kind==RoomSpecialKind.Linked))return;
            if(kind==RoomSpecialKind.Electrical)
            {
                int min=Mathf.Clamp(settings.ElectricalMinimumRooms,1,20),max=Mathf.Clamp(settings.ElectricalMaximumRooms,min,20);
                ScheduleSpecialRooms(item.Root,floor,col,min+(int)(NextRandom()*(max-min+1)),ChainInterval,false);
            }
            else if(kind==RoomSpecialKind.Linked)
            {
                int min=Mathf.Clamp(settings.LinkedMinimumRooms,1,20),max=Mathf.Clamp(settings.LinkedMaximumRooms,min,20);
                ScheduleSpecialRooms(item.Root,floor,col,min+(int)(NextRandom()*(max-min+1)),LightsOutIncomeRules.Safe(settings.LinkedInterval,.08f,.01f,5),true);
            }
            else if(kind==RoomSpecialKind.Generator)
            {
                float seconds=LightsOutIncomeRules.Safe(settings.GeneratorTimeBonus,3,0,900);
                if(seconds>0&&TimeBonusRequested!=null)TimeBonusRequested(seconds);
            }
            else if(kind==RoomSpecialKind.Faulty&&!income.faultyUsed[item.Id])
            {
                income.faultyUsed[item.Id]=true;
                income.faultyDue[item.Id]=clock+LightsOutIncomeRules.Safe(settings.FaultyRelightSeconds,5,.1f,900);
                relights.Add(item.Id);
            }
        }
        void ScheduleSpecialRooms(RootAction root,int floor,int col,int count,float interval,bool domino)
        {
            // Special rooms can schedule neighbors once; a domino descendant cannot start another domino.
            int selected=0;
            for(int distance=1;distance<roomsPerFloor&&selected<count;distance++)
                for(int direction=1;direction>=-1&&selected<count;direction-=2)
                {
                    int next=col+direction*distance;if(next<0||next>=roomsPerFloor)continue;
                    int target=floor*roomsPerFloor+next;if(!on[target]||root.Visited.Contains(target))continue;
                    selected++;Schedule(root,target,PropagationKind.SpecialLinked,domino?interval*selected:interval);
                }
        }
        bool TickFaultyLights()
        {
            bool changed=false;
            for(int at=relights.Count-1;at>=0;at--)
            {
                int id=relights[at];if(income.faultyDue[id]>clock)continue;
                income.faultyDue[id]=0;
                relights[at]=relights[relights.Count-1];relights.RemoveAt(relights.Count-1);
                if(on[id])continue;
                int floor=id/roomsPerFloor;
                if(floorRemaining[floor]==0)ClearedFloors--;
                on[id]=true;floorRemaining[floor]++;changed=true;
            }
            if(changed)
            {
                CurrentFloor=0;while(CurrentFloor<floorCount-1&&floorRemaining[CurrentFloor]==0)CurrentFloor++;
                HighestClearedFloor=-1;for(int floor=0;floor<floorCount;floor++)if(floorRemaining[floor]==0)HighestClearedFloor=floor;
            }
            return changed;
        }
        void CreditIncome(double amount)
        { AddMoney(amount);if(RewardsDeferred&&!PendingSettled)PendingIncome=income.stats.TotalIncome; }
        static int SafeExperience(int amount) { return Mathf.Clamp(amount,0,1000); }
        void EmitExperience(int amount) { if(amount>0&&ExperienceEarned!=null)ExperienceEarned(amount); }
        public void InitializeLegacyReceipt(double gross)
        {
            if(!legacyIncome||!Finite(gross)||gross<0||gross>MaxMoney)return;
            income.legacyBaseline=gross;
            income.stats.RawBaseIncome=gross;income.stats.GradeAdjustment=income.stats.RandomBonus=income.stats.CashBonus=income.stats.SpecialBonus=income.stats.ComboBonus=income.stats.SkillBonus=0;
            income.badgeBonus=0;income.stats.LegacyReceipt=true;
            if(RewardsDeferred&&!PendingSettled)PendingIncome=gross;
        }
        int FindHorizontalTarget(int floor,int col,RootAction root)
        {
            int range=1+SkillLevels[6];bool leftFirst=NextRandom()<.5;
            for(int distance=1;distance<=range;distance++)
            {
                int a=col+(leftFirst?-distance:distance),b=col+(leftFirst?distance:-distance);
                if(a>=0&&a<roomsPerFloor){int id=floor*roomsPerFloor+a;if(on[id]&&!root.Visited.Contains(id))return id;}
                if(b>=0&&b<roomsPerFloor){int id=floor*roomsPerFloor+b;if(on[id]&&!root.Visited.Contains(id))return id;}
            }
            return -1;
        }
        int FindRingTarget(int floor,int col,RootAction root)
        {
            for(int distance=1;distance<=RingRange;distance++)
                for(int direction=-1;direction<=1;direction+=2)
                {
                    int next=col+direction*distance;if(next<0||next>=roomsPerFloor)continue;
                    int id=floor*roomsPerFloor+next;if(on[id]&&!root.Visited.Contains(id))return id;
                }
            return -1;
        }
        int FindNextOn()
        {
            for(int checkedCount=0;checkedCount<on.Length;checkedCount++)
            { int id=(autoCursor+checkedCount)%on.Length;if(on[id]){autoCursor=(id+1)%on.Length;return id;} }
            return -1;
        }
        void ScheduleFloor(RootAction root,int floor,double delay)
        { if(floor<0||floor>=floorCount)return;for(int col=0;col<roomsPerFloor;col++)Schedule(root,floor*roomsPerFloor+col,PropagationKind.WholeFloor,delay); }
        void Schedule(RootAction root,int id,PropagationKind kind,double delay)
        {
            int depth=schedulingDepth+1;
            if(id<0||id>=on.Length||!on[id]||root.AffectedCount>=root.Limit||depth>Mathf.Clamp(LightsOutBalanceSettings.Shared.MaxPropagationDepth,1,12)||queue.Count>=MaxPending||root.Visited.Count>=on.Length||!root.Visited.Add(id))return;
            Push(new Pending{Id=id,Root=root,Depth=depth,Kind=kind,Due=clock+Math.Max(.001,delay),Sequence=++nextSequence});
        }
        static bool Earlier(Pending a,Pending b) { return a.Due<b.Due||(a.Due==b.Due&&a.Sequence<b.Sequence); }
        void Push(Pending item)
        {
            queue.Add(item);int at=queue.Count-1;
            while(at>0){int parent=(at-1)/2;if(!Earlier(queue[at],queue[parent]))break;Pending temp=queue[parent];queue[parent]=queue[at];queue[at]=temp;at=parent;}
        }
        Pending Pop()
        {
            Pending result=queue[0],last=queue[queue.Count-1];queue.RemoveAt(queue.Count-1);if(queue.Count==0)return result;
            queue[0]=last;int at=0;
            while(true){int child=at*2+1;if(child>=queue.Count)break;if(child+1<queue.Count&&Earlier(queue[child+1],queue[child]))child++;if(!Earlier(queue[child],queue[at]))break;Pending temp=queue[at];queue[at]=queue[child];queue[child]=temp;at=child;}
            return result;
        }
        void Emit(Batch batch)
        {
            if(batch.Ids.Count==0||Extinguished==null)return;
            Extinguished(new VerticalExtinguishEvent{AffectedIds=batch.Ids.ToArray(),Reward=batch.Reward,Automated=batch.Root.Automatic,
                Origin=batch.Root.Automatic?LightOrigin.Automatic:LightOrigin.Movement,ActionId=batch.Root.Id,FloorCompleted=batch.Floors.Count>0,CompletedFloorIds=batch.Floors.ToArray(),
                CashFound=batch.CashFound,HighBonus=batch.HighBonus,CashReward=batch.CashReward,RandomBonus=batch.RandomBonus});
        }
        void AddMoney(double amount)
        { if(RewardsDeferred)PendingIncome=Math.Min(MaxMoney,PendingIncome+amount);else CreditBank(amount); }
        void CreditBank(double amount) { Money=Math.Min(MaxMoney,Money+amount);TotalEarned=Math.Min(MaxMoney,TotalEarned+amount); }
        bool Roll(float chance) { return chance>0&&NextRandom()<ClampChance(chance); }
        double NextRandom() { randomState^=randomState<<13;randomState^=randomState>>17;randomState^=randomState<<5;return randomState/4294967296.0; }
        static float ClampChance(float chance) { return Math.Max(0,Math.Min(1,chance)); }
        bool ValidRoom(int floor,int col) { return floor>=0&&floor<floorCount&&col>=0&&col<roomsPerFloor; }
        void Notify() { if(Changed!=null)Changed(); }

        [Serializable] sealed class SavedPending
        { public int id,kind,waves,depth,affected,limit;public double delay;public long action,sequence;public bool automated; }
        [Serializable] sealed class SaveState
        {
            public int version,run;public double money,earned,clock,automaticAccumulator,lastFloorCompletion;
            public long off,action,sequence;public uint random;
            public float session,surge;public int blackout,consecutive,autoFloor,autoCursor;
            public int[] skills;public bool[] lights;public List<SavedPending> pending;
            public int floors,rooms,combo,peakCombo;public float roomWidth,comboRemaining;public bool deferred,closed,pendingSettled,openingKeysApplied,departureBonusApplied;public double pendingIncome;
            public float holdBonusSeconds,comboMovePerCount,ringChance,badgeBonusPerCombo;public int keyOpeningRooms,ringRange;
            public LightsOutIncomeState income;
        }
        public string ExportJson()
        {
            List<SavedPending> pending=new List<SavedPending>(queue.Count);
            foreach(Pending item in queue)pending.Add(new SavedPending{id=item.Id,kind=(int)item.Kind,waves=item.Root.CompletionWaves,depth=item.Depth,affected=item.Root.AffectedCount,limit=item.Root.Limit,
                delay=Math.Max(0,item.Due-clock),action=item.Root.Id,sequence=item.Sequence,automated=item.Root.Automatic});
            return JsonUtility.ToJson(new SaveState{version=5,run=RunNumber,money=Money,earned=TotalEarned,off=TotalOff,clock=clock,
                action=nextActionId,sequence=nextSequence,random=randomState,session=SessionSeconds,surge=SurgeRemaining,blackout=BlackoutCharge,
                consecutive=ConsecutiveFloors,autoFloor=AutoFloorCharge,autoCursor=autoCursor,automaticAccumulator=automaticAccumulator,
                lastFloorCompletion=lastFloorCompletion,skills=SkillLevels,lights=on,pending=pending,
                floors=floorCount,rooms=roomsPerFloor,roomWidth=BaseRoomWidth,deferred=RewardsDeferred,closed=BuildingClosed,pendingSettled=PendingSettled,pendingIncome=PendingIncome,
                combo=Combo,peakCombo=PeakCombo,comboRemaining=ComboRemaining,openingKeysApplied=openingKeysApplied,departureBonusApplied=departureBonusApplied,
                holdBonusSeconds=accessories==null?0:accessories.HoldBonusSeconds,comboMovePerCount=accessories==null?0:accessories.ComboMovePerCount,
                keyOpeningRooms=OpeningKeyCount,ringChance=RingChance,ringRange=RingRange,badgeBonusPerCombo=BadgeBonusPerCombo,income=income});
        }
        public bool ImportJson(string json)
        {
            if(string.IsNullOrEmpty(json)||json.Length>12000000)return false;SaveState s;
            try{s=JsonUtility.FromJson<SaveState>(json);}catch(Exception){return false;}
            int savedFloors=s==null?0:(s.floors==0&&s.version<4?300:s.floors);
            int savedRooms=s==null?0:(s.version<4?4:s.rooms);
            float savedRoomWidth=s==null?0:(s.version==2?2.4f:s.roomWidth);
            int skillCount=s==null||s.skills==null?0:s.skills.Length;
            bool validSkills=s!=null&&(s.version==2?skillCount==20:s.version==3?skillCount==22:s.version==4?skillCount==24||skillCount==25:skillCount==Skills.Length);
            if(s==null||s.version<2||s.version>5||!validSkills||!ValidRoomWidth(savedRoomWidth)||!ValidRoomCount(savedRooms)||s.run<1||s.run>=1000000000||
                !Finite(s.money)||s.money<0||s.money>MaxMoney||!Finite(s.earned)||s.earned<s.money||s.earned>MaxMoney||s.off<0||s.off>9000000000000000L||
                !Finite(s.clock)||s.clock<0||s.clock>3153600000||!Finite(s.automaticAccumulator)||s.automaticAccumulator<0||s.automaticAccumulator>10000||
                !Finite(s.lastFloorCompletion)||s.lastFloorCompletion>s.clock||!Finite(s.session)||s.session<0||s.session>315360000||
                !Finite(s.surge)||s.surge<0||s.surge>5||s.blackout<0||s.blackout>30||savedFloors<1||savedFloors>MaximumFloors||s.consecutive<0||s.consecutive>savedFloors||
                s.autoFloor<0||s.autoFloor>=30||s.autoCursor<0||s.autoCursor>=savedFloors*savedRooms||s.action<0||s.sequence<0||
                s.lights==null||s.lights.Length!=savedFloors*savedRooms||s.pending==null||s.pending.Count>MaxPending||!Finite(s.pendingIncome)||s.pendingIncome<0||s.pendingIncome>MaxMoney||
                (!s.deferred&&(s.pendingIncome!=0||s.pendingSettled))||(s.pendingSettled&&(!s.closed||s.pendingIncome!=0))||(s.closed&&s.pending.Count>0))return false;
            if(s.version<4)
            {
                s.combo=s.peakCombo=0;s.comboRemaining=0;s.openingKeysApplied=s.departureBonusApplied=false;
                s.holdBonusSeconds=s.comboMovePerCount=s.ringChance=s.badgeBonusPerCombo=0;s.keyOpeningRooms=s.ringRange=0;
            }
            if(s.combo<0||s.peakCombo<s.combo||!Finite(s.comboRemaining)||s.comboRemaining<0||s.comboRemaining>60||(s.combo==0?s.comboRemaining!=0:s.comboRemaining<=0)||
                !InRange(s.holdBonusSeconds,0,30)||!InRange(s.comboMovePerCount,0,1)||s.keyOpeningRooms<0||s.keyOpeningRooms>100||
                !InRange(s.ringChance,0,2)||s.ringRange<0||s.ringRange>20||!InRange(s.badgeBonusPerCombo,0,100))return false;
            int[] savedSkills=new int[Skills.Length];Array.Copy(s.skills,savedSkills,s.skills.Length);
            for(int i=0;i<Skills.Length;i++)
            { if(savedSkills[i]<0||savedSkills[i]>Skills[i].MaxLevel)return false;int p=Skills[i].PrerequisiteId;if(savedSkills[i]>0&&p>=0&&savedSkills[p]<Skills[i].PrerequisiteLevel)return false; }
            double rawOff=0;int currentOff=0;
            for(int i=0;i<s.lights.Length;i++)if(!s.lights[i]){rawOff+=5+(i/savedRooms)*.125;currentOff++;}
            LightsOutIncomeState savedIncome;
            if(s.version<5)
            {
                double maximumPending=rawOff*2*(s.version<4?1:LightsOutBalanceSettings.Shared.MaximumComboMultiplier)+(s.departureBonusApplied?s.peakCombo*s.badgeBonusPerCombo:0);
                if(s.off<currentOff||s.peakCombo>currentOff||s.pendingIncome>maximumPending+.000001||(s.deferred&&!s.pendingSettled&&s.pendingIncome+.000001<rawOff))return false;
                savedIncome=LightsOutIncomeState.Create(s.lights.Length,savedFloors);
                savedIncome.prepared=true;savedIncome.stats.LegacyReceipt=true;savedIncome.stats.GradeName="기존 근무";savedIncome.stats.GradeMultiplier=1;
                savedIncome.legacyBaseline=s.deferred&&!s.pendingSettled?s.pendingIncome:rawOff*(savedSkills[4]>0?2:1);
                savedIncome.stats.RawBaseIncome=savedIncome.legacyBaseline;savedIncome.stats.TotalCount=currentOff;savedIncome.stats.UnclassifiedCount=currentOff;
                savedIncome.stats.MaxCombo=s.peakCombo;savedIncome.stats.PeakComboMultiplier=LightsOutBalanceSettings.Shared.ComboMultiplierFor(s.peakCombo);
                for(int i=0;i<s.lights.Length;i++)if(!s.lights[i]){savedIncome.paid[i]=true;savedIncome.randomFactors[i]=0;}
                for(int floor=0;floor<savedFloors;floor++)
                {
                    bool complete=true;for(int col=0;col<savedRooms;col++)if(s.lights[floor*savedRooms+col]){complete=false;break;}
                    savedIncome.floorAwarded[floor]=complete;
                }
                savedIncome.buildingXpAwarded=s.closed&&currentOff==s.lights.Length;
            }
            else
            {
                savedIncome=s.income;
                if(!LightsOutIncomeRules.Validate(savedIncome,s.lights.Length,savedFloors,savedRooms,s.clock,s.lights)||s.off<savedIncome.stats.TotalCount||s.peakCombo>savedIncome.stats.TotalCount||
                    savedIncome.stats.MaxCombo!=s.peakCombo||(!s.deferred&&s.pendingIncome!=0)||(s.deferred&&!s.pendingSettled&&Math.Abs(s.pendingIncome-savedIncome.stats.TotalIncome)>Math.Max(.000001,s.pendingIncome*1e-9)))return false;
                if(s.closed)for(int i=0;i<savedIncome.faultyDue.Length;i++)if(savedIncome.faultyDue[i]!=0)return false;
            }
            Dictionary<long,bool> origins=new Dictionary<long,bool>();
            Dictionary<long,int> waves=new Dictionary<long,int>(),affected=new Dictionary<long,int>(),limits=new Dictionary<long,int>();
            foreach(SavedPending item in s.pending)
            {
                if(item==null||item.id<0||item.id>=s.lights.Length||item.kind<0||item.kind>(s.version<4?4:s.version<5?5:7)||item.waves<0||item.waves>2||!Finite(item.delay)||item.delay<0||item.delay>60||
                    item.action<1||item.action>s.action||item.sequence<0||item.sequence>s.sequence)return false;
                if(s.version<5){item.depth=1;item.affected=0;item.limit=Mathf.Clamp(item.automated?LightsOutBalanceSettings.Shared.MaxAutomaticRoomsPerAction:LightsOutBalanceSettings.Shared.MaxRoomsPerAction,1,128);}
                if(item.depth<0||item.depth>12||item.limit<1||item.limit>128||item.affected<0||item.affected>item.limit)return false;
                bool origin;if(origins.TryGetValue(item.action,out origin)&&origin!=item.automated)return false;origins[item.action]=item.automated;
                int wave;if(waves.TryGetValue(item.action,out wave)&&wave!=item.waves)return false;waves[item.action]=item.waves;
                int count;if(affected.TryGetValue(item.action,out count)&&count!=item.affected)return false;affected[item.action]=item.affected;
                int limit;if(limits.TryGetValue(item.action,out limit)&&limit!=item.limit)return false;limits[item.action]=item.limit;
            }
            // All candidates have passed validation; only now replace live state.
            floorCount=savedFloors;roomsPerFloor=savedRooms;BaseRoomWidth=savedRoomWidth;RewardsDeferred=s.deferred;BuildingClosed=s.closed;PendingSettled=s.pendingSettled;PendingIncome=s.pendingIncome;
            on=s.lights;income=savedIncome;legacyIncome=s.version<5;SkillLevels=savedSkills;Money=s.money;TotalEarned=s.earned;TotalOff=s.off;clock=s.clock;SessionSeconds=s.session;
            RunNumber=s.run;nextActionId=s.action;nextSequence=s.sequence;randomState=s.random==0?97193711:s.random;
            SurgeRemaining=s.surge;BlackoutCharge=s.blackout;ConsecutiveFloors=s.consecutive;AutoFloorCharge=s.autoFloor;
            Combo=s.combo;PeakCombo=s.peakCombo;ComboRemaining=s.comboRemaining;openingKeysApplied=s.openingKeysApplied;departureBonusApplied=s.departureBonusApplied;
            accessories=new AccessoryEffects(s.holdBonusSeconds,s.comboMovePerCount,s.keyOpeningRooms,s.ringChance,s.ringRange,s.badgeBonusPerCombo);
            autoCursor=s.autoCursor;automaticAccumulator=s.automaticAccumulator;lastFloorCompletion=s.lastFloorCompletion;schedulingDepth=0;
            floorRemaining=new int[floorCount];ClearedFloors=0;HighestClearedFloor=-1;
            for(int f=0;f<floorCount;f++)
            { for(int c=0;c<roomsPerFloor;c++)if(on[f*roomsPerFloor+c])floorRemaining[f]++;if(floorRemaining[f]==0){ClearedFloors++;HighestClearedFloor=f;} }
            CurrentFloor=0;while(CurrentFloor<floorCount-1&&floorRemaining[CurrentFloor]==0)CurrentFloor++;
            queue.Clear();relights.Clear();Dictionary<long,RootAction> roots=new Dictionary<long,RootAction>();
            for(int id=0;id<income.faultyDue.Length;id++)if(income.faultyDue[id]>0)relights.Add(id);
            foreach(SavedPending item in s.pending)
            {
                if(!on[item.id]||IsComplete)continue;RootAction root;
                if(!roots.TryGetValue(item.action,out root)){root=new RootAction{Id=item.action,Automatic=item.automated,CompletionWaves=item.waves,AffectedCount=item.affected,Limit=item.limit};roots.Add(root.Id,root);}
                if(!root.Visited.Add(item.id))continue;
                Push(new Pending{Id=item.id,Root=root,Depth=item.depth,Kind=(PropagationKind)item.kind,Due=clock+item.delay,Sequence=item.sequence});
            }
            Notify();return true;
        }
        static bool Finite(double value) { return !double.IsNaN(value)&&!double.IsInfinity(value); }
        static bool ValidRoomWidth(float value) { return Finite(value)&&value>=1.2f&&value<=8f; }
        static bool ValidRoomCount(int value) { return value>=2&&value<=MaximumRooms; }
        static bool InRange(double value,double minimum,double maximum) { return Finite(value)&&value>=minimum&&value<=maximum; }
    }
}
