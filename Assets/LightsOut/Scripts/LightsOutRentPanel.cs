using System;
using TMPro;
using UnityEngine;

namespace LightsOut
{
    /// <summary>The work receipt stays over the played building; optional roulette remains a separate modal.</summary>
    public sealed class LightsOutRentPanel : MonoBehaviour
    {
        static readonly Color Ink=LightsOutStyle.Hex("071321"), Panel=LightsOutStyle.Hex("10202D"), Card=LightsOutStyle.Hex("091520");
        static readonly Color Cream=LightsOutStyle.Hex("F5EFDA"), Muted=LightsOutStyle.Hex("9AAABB"), Gold=LightsOutStyle.Hex("F6C96D"), Mint=LightsOutStyle.Hex("8DE7C5"), Red=LightsOutStyle.Hex("F6928A");
        static readonly Color Orange=LightsOutStyle.Hex("FFA457"), ExperienceGreen=LightsOutStyle.Hex("76ED80");
        static readonly Color[] SpecialColors={LightsOutStyle.Hex("F7D36D"),LightsOutStyle.Hex("F3C955"),LightsOutStyle.Hex("6DC6EB"),LightsOutStyle.Hex("77D998"),LightsOutStyle.Hex("F4C57B"),LightsOutStyle.Hex("ED8290")};
        static readonly string[] SpecialNames={"황금 전등","배전실","보안실","연결 전등","비상 발전기","고장 난 전등"};
        static readonly string[] SpecialDescriptions={"희귀 고수익 방","연결 구역 동시 소등","긴 작업, 높은 수익","연결된 방 연쇄 소등","남은 근무시간 증가","시간이 지나면 다시 점등"};
        static readonly int[] DefaultUnlockLevels={3,5,8,12,16,20};
        static readonly string[] IncomeNames={"기본 소등 수익","건물 등급 보정","무작위 보너스","돈다발 보너스","특수 방 보너스","콤보 보너스","스킬·액세서리 보너스"};
        LightsOutVerticalGame owner; RectTransform panel, fullScreenShade; LightsOutRoulettePopup popup;
        Vector2 fittedCanvasSize=new Vector2(-1,-1);
        TMP_Text heading, subtitle, grade, totalIncome, settlementNote, walletCaption, wallet, rent, rentAfter, shortfall, opportunities, rentPoints, footer;
        TMP_Text comboCount, comboMultiplier, experienceGain, playerLevel, experienceFraction, sidebarGain, unlockAlert;
        TMP_Text keepLabel, payLabel, nextLabel, rouletteLabel, payHint, nextHint;
        readonly TMP_Text[] countValues=new TMP_Text[3], incomeValues=new TMP_Text[7], incomeLabels=new TMP_Text[7], xpDetails=new TMP_Text[5], specialCounts=new TMP_Text[6];
        readonly TMP_Text[] unlockNames=new TMP_Text[6], unlockConditions=new TMP_Text[6], unlockDescriptions=new TMP_Text[6], pipLabels=new TMP_Text[5];
        readonly LightsOutShape[] unlockFaces=new LightsOutShape[6], pips=new LightsOutShape[5];
        readonly LightsOutShape[][] unlockIconParts=new LightsOutShape[6][];
        readonly RectTransform[] closedLocks=new RectTransform[6], openLocks=new RectTransform[6];
        readonly float[] unlockGlow=new float[6];
        UnityEngine.UI.Button keepButton, skillButton, accessoryButton, payButton, rouletteButton, nextButton;
        RectTransform rentFill, experienceFill;
        float refreshTimer, segmentTime, segmentDuration=.8f, unlockAlertTime;
        long displayedExperience, targetExperience, segmentStart, segmentEnd, lastReceiptTarget=-1;
        int lastReceiptRun=-1;
        bool experienceAnimating, pendingLevelUp;

        public bool IsSpinning=>popup!=null&&popup.IsBusy;
        public bool IsRouletteOpen=>popup!=null&&popup.IsOpen;
        public float WheelAngle=>popup!=null?popup.WheelAngle:0;
        public bool TryCloseRoulette()=>popup!=null&&popup.TryClose();
        public bool IsExperienceAnimating=>experienceAnimating;
        public int DisplayedLevel=>LightsOutPermanentModel.LevelForExperience(displayedExperience);
        public float UIFitScale=>panel!=null?panel.localScale.x:1;

        public void Initialize(LightsOutVerticalGame game)
        {
            owner=game;
            if(panel==null)Build();
            if(popup==null){popup=GetComponent<LightsOutRoulettePopup>();if(popup==null)popup=gameObject.AddComponent<LightsOutRoulettePopup>();}
            popup.Initialize(owner,Show);Hide();
        }
        public void Show()
        {
            if(panel==null||owner==null||owner.Career==null)return;
            panel.gameObject.SetActive(true);panel.SetAsLastSibling();
            FitToCanvas();
            BeginExperiencePresentation();Refresh();RefreshExperience();
        }
        public void Hide()
        {
            if(IsRouletteOpen)return;
            SkipExperienceAnimation();if(panel!=null)panel.gameObject.SetActive(false);
        }
        void Update()
        {
            if(panel==null||!panel.gameObject.activeSelf||IsRouletteOpen)return;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            AdvanceExperience(dt);
            unlockAlertTime=Mathf.Max(0,unlockAlertTime-dt);unlockAlert.gameObject.SetActive(unlockAlertTime>0);
            for(int i=0;i<unlockGlow.Length;i++)unlockGlow[i]=Mathf.Max(0,unlockGlow[i]-dt*.65f);
            RefreshUnlocks();
            refreshTimer+=dt;if(refreshTimer>=.12f){refreshTimer=0;Refresh();}
        }
        void LateUpdate(){if(panel!=null&&panel.gameObject.activeSelf)FitToCanvas();}
        void OnRectTransformDimensionsChange(){if(panel!=null&&owner!=null)FitToCanvas();}
        void FitToCanvas()
        {
            if(owner==null||owner.UIRoot==null||panel==null||fullScreenShade==null)return;
            Vector2 size=owner.UIRoot.rect.size;
            if(size.x<=0||size.y<=0||size==fittedCanvasSize)return;
            fittedCanvasSize=size;float scale=Mathf.Min(size.x/1600,size.y/900);
            Vector2 offset=new Vector2((size.x-1600*scale)*.5f,-(size.y-900*scale)*.5f);
            panel.localScale=new Vector3(scale,scale,1);panel.anchoredPosition=offset;
            fullScreenShade.anchoredPosition=-offset/scale;fullScreenShade.sizeDelta=size/scale;
        }
        void Keep(){if(!IsRouletteOpen&&owner.KeepShiftIncome())Refresh();}
        void Spin(){if(!IsRouletteOpen&&owner.Career.Phase==RentPhase.Checkout&&!owner.Career.SettlementResolved&&owner.Career.LastShiftIncome>0)popup.Show();}

        void Build()
        {
            panel=Rect(owner.UIRoot,"RentDesk",0,0,1600,900);
            fullScreenShade=Box(panel,"PlayedBuildingShade",0,0,1600,900,new Color(.012f,.025f,.04f,.68f),0,true).rectTransform;
            heading=Text(panel,"Heading","근무 종료!",30,22,1190,80,52,Gold,FontStyles.Bold,TextAlignmentOptions.Center);
            subtitle=Text(panel,"Subheading","오늘도 수고했어요!",30,103,1190,34,20,Cream,FontStyles.Normal,TextAlignmentOptions.Center);
            DrawIcon(panel,0,310,46,40,Gold);DrawIcon(panel,0,906,46,40,Gold);
            Frame("WorkResults",30,150,1190,460);
            DrawIcon(panel,7,53,170,30,Gold);
            Text(panel,"ResultsHeading","이번 근무 결과",94,164,440,44,25,Gold,FontStyles.Bold);
            grade=Text(panel,"BuildingGrade","",556,176,642,31,16,Muted,FontStyles.Normal,TextAlignmentOptions.Right);
            Box(panel,"CountsFace",48,222,696,87,Alpha(Card,.83f),5);
            string[] counts={"직접 소등한 방 수","연쇄·자동 소등한 방 수","전체 소등한 방 수"};
            for(int i=0;i<3;i++)
            {
                float y=228+i*25;DrawIcon(panel,i==0?0:i==1?3:7,61,y+2,19,i==2?Gold:Mint);
                Text(panel,"CountLabel"+i,counts[i],92,y,397,25,17,Muted);
                countValues[i]=Text(panel,"CountValue"+i,"0개",499,y,224,25,18,Cream,FontStyles.Bold,TextAlignmentOptions.Right);
            }
            Box(panel,"IncomeFace",48,312,696,204,Alpha(Card,.76f),5);
            for(int i=0;i<7;i++)
            {
                float y=316+i*28;DrawIcon(panel,i==0?8:i==1?7:i==2?9:i==3?8:i==4?0:i==5?9:3,61,y+4,18,i==0?Cream:Gold);
                incomeLabels[i]=Text(panel,"IncomeLabel"+i,IncomeNames[i],92,y,377,27,17,i==0?Cream:Muted);
                incomeValues[i]=Text(panel,"IncomeValue"+i,"₩ 0",468,y,255,27,18,i==0?Cream:Gold,FontStyles.Bold,TextAlignmentOptions.Right);
            }
            Box(panel,"IncomeRule",61,523,663,1,Alpha(Muted,.32f),0);
            DrawIcon(panel,8,61,541,28,Mint);
            Text(panel,"TotalCaption","이번 근무 총수익",102,537,273,45,23,Gold,FontStyles.Bold);
            totalIncome=Text(panel,"SettlementAmount","₩ 0",356,531,367,54,35,Gold,FontStyles.Bold,TextAlignmentOptions.Right);
            settlementNote=Text(panel,"SettlementResult","",63,583,345,24,13,Muted);
            keepButton=ActionButton("KeepIncomeButton","수입 그대로 확정  ×1",420,581,304,25,Alpha(Card,.92f),Mint,Keep,out keepLabel,13,false);

            Box(panel,"ComboFace",764,222,437,96,Alpha(Card,.88f),5);
            DrawIcon(panel,9,783,238,25,Gold);Text(panel,"ComboHeading","최대 콤보",818,234,189,28,20,Gold,FontStyles.Bold);
            comboCount=Text(panel,"MaximumCombo","0 콤보",783,269,245,42,34,Cream,FontStyles.Bold,TextAlignmentOptions.Center);
            Text(panel,"ComboMultiplierCaption","최대 수익 배율",1020,237,158,27,14,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            comboMultiplier=Text(panel,"MaximumComboMultiplier","×1.0",1020,272,158,37,28,LightsOutStyle.Hex("8CD3E8"),FontStyles.Bold,TextAlignmentOptions.Center);
            Box(panel,"ExperienceFace",764,326,437,169,Alpha(Card,.88f),5);
            DrawIcon(panel,9,783,341,25,Gold);Text(panel,"ExperienceHeading","획득 경험치",818,337,300,32,20,Gold,FontStyles.Bold);
            experienceGain=Text(panel,"ShiftExperience","+0 XP",782,383,184,65,31,Cream,FontStyles.Bold,TextAlignmentOptions.Center);
            Text(panel,"ExperienceNote","중복 지급 없이 누적",782,451,184,28,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            for(int i=0;i<5;i++)xpDetails[i]=Text(panel,"XPDetail"+i,"",982,373+i*22,198,22,14,Muted,FontStyles.Normal,TextAlignmentOptions.Right);
            Box(panel,"SpecialCountsFace",764,503,437,101,Alpha(Card,.88f),5);
            Text(panel,"SpecialCountsHeading","특수 방 소등 내역",783,507,392,25,17,Gold,FontStyles.Bold);
            for(int i=0;i<6;i++)
            {
                float x=781+i*69;DrawIcon(panel,i,x+20,538,28,SpecialColors[i]);
                Text(panel,"SpecialCountName"+i,SpecialName(i),x-1,568,69,18,10,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
                specialCounts[i]=Text(panel,"SpecialCount"+i,"0",x,584,68,18,14,Cream,FontStyles.Bold,TextAlignmentOptions.Center);
            }

            Frame("MoneyAndRent",30,625,1190,160);
            DrawIcon(panel,8,53,641,26,Gold);Text(panel,"MoneyHeading","보유 금액 및 월세",91,635,571,36,23,Gold,FontStyles.Bold);
            rentPoints=Text(panel,"PaymentPointPreview","",680,644,518,24,14,Mint,FontStyles.Normal,TextAlignmentOptions.Right);
            float[] left={48,354,658,958};float[] widths={296,294,290,244};
            for(int i=0;i<4;i++)Box(panel,"MoneyCell"+i,left[i],678,widths[i],94,Alpha(Card,.87f),5);
            walletCaption=Text(panel,"WalletCaption","현재 보유 금액",64,685,263,25,16,Muted);
            wallet=Text(panel,"Wallet","₩ 0",64,719,263,43,31,Cream,FontStyles.Bold);
            Text(panel,"RentCaption","납부할 월세",370,685,262,25,16,Muted);
            rent=Text(panel,"RentDue","₩ 0",370,714,262,30,26,Cream,FontStyles.Bold);
            rentAfter=Text(panel,"RentRemainder","",370,747,262,21,13,Muted);
            Text(panel,"ShortfallCaption","월세까지 남은 금액",674,685,258,25,16,Muted);
            shortfall=Text(panel,"Shortfall","₩ 0",674,714,258,30,25,Red,FontStyles.Bold);
            Box(panel,"RentProgressTrack",678,750,248,12,LightsOutStyle.Hex("26394B"),3);
            rentFill=Box(panel,"RentProgressFill",678,750,0,12,Red,3).rectTransform;
            Text(panel,"AttemptsCaption","남은 근무 기회",973,685,214,24,16,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            for(int i=0;i<5;i++){pips[i]=Box(panel,"AttemptPip"+i,991+i*37,716,27,22,Card,3);pipLabels[i]=Text(pips[i].rectTransform,"State",(i+1).ToString(),0,0,27,22,13,Muted,FontStyles.Bold,TextAlignmentOptions.Center);}
            opportunities=Text(panel,"Attempts","5 / 5회",972,744,219,25,20,Cream,FontStyles.Bold,TextAlignmentOptions.Center);

            Frame("PermanentSidebar",1240,20,335,765);
            DrawIcon(panel,7,1258,42,28,Gold);Text(panel,"PermanentHeading","관리인 레벨 (영구)",1296,40,262,30,20,Gold,FontStyles.Bold);
            Box(panel,"LevelFace",1255,81,305,136,Alpha(Card,.9f),5);
            playerLevel=Text(panel,"PlayerLevel","Lv. 1",1270,89,137,46,35,Gold,FontStyles.Bold);
            experienceFraction=Text(panel,"ExperienceFraction","0 / 50 XP",1380,103,165,26,15,Muted,FontStyles.Normal,TextAlignmentOptions.Right);
            Box(panel,"PermanentXPTrack",1270,144,274,16,LightsOutStyle.Hex("263B4C"),4);
            experienceFill=Box(panel,"PermanentXPFill",1270,144,0,16,ExperienceGreen,4).rectTransform;
            sidebarGain=Text(panel,"PermanentXPGain","이번 근무 +0 XP",1270,177,274,29,18,ExperienceGreen,FontStyles.Bold,TextAlignmentOptions.Right);
            Box(panel,"UnlockRule",1257,231,302,1,Alpha(Muted,.34f),0);
            Text(panel,"UnlockHeading","특수 방 해금 목록",1258,244,304,34,20,Gold,FontStyles.Bold);
            for(int i=0;i<6;i++)
            {
                float y=285+i*76;unlockFaces[i]=Box(panel,"UnlockCard"+i,1255,y,305,70,Card,5);
                var icon=DrawIcon(panel,i,1267,y+17,34,Muted);unlockIconParts[i]=icon.GetComponentsInChildren<LightsOutShape>();
                unlockNames[i]=Text(panel,"UnlockName"+i,SpecialName(i),1313,y+7,230,23,17,Muted,FontStyles.Bold);
                unlockConditions[i]=Text(panel,"UnlockCondition"+i,"",1313,y+30,230,19,12,Muted);
                unlockDescriptions[i]=Text(panel,"UnlockDescription"+i,SpecialDescription(i),1313,y+49,230,17,11,Muted);
                closedLocks[i]=DrawIcon(panel,12,1526,y+29,18,Muted);
                openLocks[i]=DrawIcon(panel,13,1526,y+29,18,Mint);
            }
            Text(panel,"PermanentFooter","게임오버 후에도 레벨과 해금은 유지됩니다",1255,751,305,23,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            unlockAlert=Text(panel,"NewSpecialUnlock","",1240,786,335,23,14,Gold,FontStyles.Bold,TextAlignmentOptions.Center);unlockAlert.gameObject.SetActive(false);

            skillButton=ActionButton("RentUpgradeButton","스킬 강화",30,813,286,63,LightsOutStyle.Hex("234C75"),Cream,()=>owner.SettleAndOpenSkills(),out _,22,true,6);
            Text(skillButton.transform,"Hint","이번 도전에서만 적용",71,39,202,18,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            accessoryButton=ActionButton("RentAccessoryButton","액세서리",338,813,286,63,LightsOutStyle.Hex("59366F"),Cream,()=>owner.OpenAccessoriesFromCheckout(),out _,22,true,10);
            Text(accessoryButton.transform,"Hint","영구 장비 확인",71,39,202,18,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            payButton=ActionButton("PayRentButton","월세 납부",646,813,286,63,LightsOutStyle.Hex("73582B"),Cream,()=>owner.SettleAndPayRent(),out payLabel,22,true,7);
            payHint=Text(payButton.transform,"Hint","",71,39,202,18,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            rouletteButton=ActionButton("RollIncomeButton","퇴근 룰렛",954,813,286,63,LightsOutStyle.Hex("753345"),Cream,Spin,out rouletteLabel,22,true,11);
            Text(rouletteButton.transform,"Hint","이번 근무 수익에만 적용",71,39,202,18,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            nextButton=ActionButton("NextAttemptButton","다음 근무",1262,813,286,63,LightsOutStyle.Hex("315A38"),Cream,()=>{if(owner.Career.Phase==RentPhase.Bankrupt)owner.RestartCareer();else owner.SettleAndStartNextShift();},out nextLabel,22,true,6);
            nextHint=Text(nextButton.transform,"Hint","",71,39,202,18,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
            footer=Text(panel,"Footer","",30,880,1545,19,12,Muted,FontStyles.Normal,TextAlignmentOptions.Center);
        }

        void Refresh()
        {
            if(panel==null||!panel.gameObject.activeSelf||owner==null||owner.Career==null||IsRouletteOpen)return;
            var career=owner.Career;var stats=owner.Model.ShiftStats;
            bool checkout=career.Phase==RentPhase.Checkout,between=career.Phase==RentPhase.BetweenAttempts,bankrupt=career.Phase==RentPhase.Bankrupt;
            bool unsettled=checkout&&!career.SettlementResolved;
            double available=owner.Model.Money+(unsettled?career.LastShiftIncome:0),needed=Math.Max(0,career.RentDue-available);
            heading.text=bankrupt?"월세 납부 실패":"근무 종료!";heading.color=bankrupt?Red:Gold;
            subtitle.text=bankrupt?"다시 도전하세요. 관리인 레벨과 액세서리는 남아 있습니다.":unsettled?"스킬·월세·다음 근무 선택 시 수입 ×1 확정  ·  퇴근 룰렛은 선택 사항입니다.":"오늘도 수고했어요! 다음 근무를 준비하세요.";
            grade.text="DAY "+career.Stage+"  /  "+(stats.LegacyReceipt?"이전 저장 · 등급 기록 없음":stats.GradeName+"  ×"+stats.GradeMultiplier.ToString("0.##"))+"  /  "+(career.LastCompleted?"건물 소등 완료":"제한 시간 종료");
            countValues[0].text=stats.LegacyReceipt?"미분류":stats.DirectCount+"개";
            countValues[1].text=stats.LegacyReceipt?"기록 없음":(stats.PropagatedCount+stats.AutomatedCount)+"개";
            countValues[2].text=stats.TotalCount+"개";
            double[] money={stats.RawBaseIncome,stats.GradeAdjustment,stats.RandomBonus,stats.CashBonus,stats.SpecialBonus,stats.ComboBonus,stats.SkillBonus};
            for(int i=0;i<money.Length;i++)
            {
                incomeValues[i].text=i==0?Money(money[i]):SignedMoney(money[i]);
                incomeValues[i].color=i==0?Cream:money[i]<0?Red:money[i]==0?Muted:Orange;
            }
            incomeLabels[0].text=stats.LegacyReceipt?"이전 근무 총수익 (미분류)":IncomeNames[0];
            incomeLabels[3].text="숨겨진 돈다발 보너스 ("+stats.CashCount+"회)";
            incomeLabels[5].text="콤보 보너스  (최대 ×"+stats.PeakComboMultiplier.ToString("0.##")+")";
            totalIncome.text=Money(career.LastShiftIncome);
            settlementNote.text=unsettled?"수입 미확정 · 룰렛은 선택 사항":career.RouletteUsed?"룰렛 ×"+career.LastRouletteMultiplier+" · 확정 "+Money(career.LastSettledIncome):"수입 그대로 확정 ×1";
            settlementNote.color=career.RouletteUsed&&career.LastRouletteMultiplier==0?Red:Mint;
            keepButton.gameObject.SetActive(unsettled);keepButton.interactable=unsettled&&!IsSpinning;
            comboCount.text=stats.MaxCombo+" 콤보";comboMultiplier.text="×"+stats.PeakComboMultiplier.ToString("0.##");
            experienceGain.text="+"+stats.ShiftExperienceTotal+" XP";sidebarGain.text="이번 근무 +"+stats.ShiftExperienceTotal+" XP";
            string[] labels={"일반 방 소등","연쇄·자동 소등","특수 방 소등","층 전체 소등","건물 전체 소등"};
            int[] xp={stats.XPDirect,stats.XPPropagation,stats.XPSpecial,stats.XPFloors,stats.XPBuilding};
            for(int i=0;i<5;i++)xpDetails[i].text=labels[i]+"   +"+xp[i];
            for(int i=0;i<6;i++)specialCounts[i].text=(stats.SpecialCounts!=null&&i<stats.SpecialCounts.Length?stats.SpecialCounts[i]:0).ToString();
            walletCaption.text=unsettled?"수입 ×1 확정 후 예상금":"현재 보유 금액";
            wallet.text=Money(available);rent.text=Money(career.RentDue);
            rentAfter.text=needed>0?"납부 후 "+SignedMoney(available-career.RentDue)+" (부족)":"납부 후 잔액 "+Money(available-career.RentDue);
            rentAfter.color=needed>0?Red:Mint;shortfall.text=needed>0?Money(needed):"납부 가능";shortfall.color=needed>0?Red:Mint;
            rentFill.sizeDelta=new Vector2(248*Mathf.Clamp01((float)(available/career.RentDue)),12);rentFill.GetComponent<LightsOutShape>().color=needed>0?Red:Mint;
            opportunities.text=career.AttemptsRemaining+" / 5회";
            for(int i=0;i<5;i++){bool used=i<career.AttemptsUsed;pips[i].color=used?LightsOutStyle.Hex("354450"):Alpha(Mint,.38f);pipLabels[i].text=used?"✓":(i+1).ToString();pipLabels[i].color=used?Muted:Mint;}
            int points=owner.Permanent.PreviewPaymentPoints(career.Stage,career.AttemptsRemaining),early=owner.Permanent.PreviewEarlyBonus(career.Stage,career.AttemptsRemaining);
            rentPoints.text=needed<=0&&!bankrupt?"월세 납부 보상 +"+points+" 포인트 (조기 +"+early+")":"월세는 보유금에서 차감됩니다";
            skillButton.interactable=(checkout||between)&&!bankrupt&&!IsSpinning;accessoryButton.interactable=!IsSpinning;
            payButton.interactable=(checkout||between)&&needed<=0&&!bankrupt&&!IsSpinning;
            payLabel.text="월세 납부";payHint.text=needed>0?Money(needed)+" 부족":"다음 DAY 준비";payHint.color=needed>0?Red:Mint;
            rouletteButton.interactable=unsettled&&career.LastShiftIncome>0&&!IsSpinning;rouletteLabel.text=career.RouletteUsed?"룰렛 정산 완료":"퇴근 룰렛";
            nextButton.interactable=bankrupt||((checkout||between)&&career.AttemptsRemaining>0&&!IsSpinning);
            nextLabel.text=bankrupt?"새 도전 시작":"다음 근무";nextHint.text=bankrupt?"레벨·액세서리 유지":"남은 근무 "+career.AttemptsRemaining+" / 5회";
            footer.text=bankrupt?"새 도전은 보유금과 일반 스킬을 초기화합니다. 영구 경험치, 특수 방 해금, 액세서리는 유지됩니다.":unsettled?"스킬 강화·월세 납부·다음 근무를 선택하면 이번 수입을 그대로 확정합니다. · 액세서리는 확인만 가능합니다.":"근무 시작 시 기회 1회 사용 · DAY마다 총 5회 · 최고 DAY 최초 완납 보상과 조기 납부 보너스 유지";
        }

        void BeginExperiencePresentation()
        {
            long total=owner.Permanent.TotalExperience;
            if(lastReceiptRun==owner.Model.RunNumber&&lastReceiptTarget==total)return;
            lastReceiptRun=owner.Model.RunNumber;lastReceiptTarget=total;targetExperience=total;
            displayedExperience=Math.Max(0,total-owner.Model.ShiftStats.ShiftExperienceTotal);
            int spans=LightsOutPermanentModel.LevelForExperience(total)-DisplayedLevel+1;
            segmentDuration=Math.Min(.8f,6f/Math.Max(1,spans));experienceAnimating=displayedExperience<targetExperience;
            pendingLevelUp=false;BeginExperienceSegment();
        }
        void BeginExperienceSegment()
        {
            segmentTime=0;segmentStart=displayedExperience;
            int level=DisplayedLevel;
            segmentEnd=level>=LightsOutPermanentModel.MaximumPlayerLevel?targetExperience:Math.Min(targetExperience,LightsOutPermanentModel.ExperienceAtStartOfLevel(level+1));
        }
        void AdvanceExperience(float dt)
        {
            if(!experienceAnimating)return;
            if(pendingLevelUp)
            {
                int before=DisplayedLevel;displayedExperience=segmentEnd;pendingLevelUp=false;ShowNewUnlocks(before,DisplayedLevel);
                if(displayedExperience>=targetExperience)experienceAnimating=false;else BeginExperienceSegment();
                RefreshExperience();return;
            }
            segmentTime+=dt;float u=Mathf.Clamp01(segmentTime/segmentDuration),ease=1-Mathf.Pow(1-u,3);
            displayedExperience=segmentStart+(long)Math.Round((segmentEnd-segmentStart)*ease);
            if(u<1&&displayedExperience>=segmentEnd)displayedExperience=Math.Max(segmentStart,segmentEnd-1);
            if(u>=1)
            {
                int level=LightsOutPermanentModel.LevelForExperience(segmentStart);
                bool crosses=level<LightsOutPermanentModel.MaximumPlayerLevel&&segmentEnd==LightsOutPermanentModel.ExperienceAtStartOfLevel(level+1);
                // Preserve one full-bar frame before increasing the displayed level.
                if(crosses)
                {
                    displayedExperience=segmentEnd-1;pendingLevelUp=true;RefreshExperience();
                    experienceFill.sizeDelta=new Vector2(274,16);int required=LightsOutPermanentModel.RequiredExperienceForLevel(level);
                    experienceFraction.text=required+" / "+required+" XP";return;
                }
                displayedExperience=segmentEnd;
                if(displayedExperience>=targetExperience)experienceAnimating=false;else BeginExperienceSegment();
            }
            RefreshExperience();
        }
        public void SkipExperienceAnimation()
        {
            if(owner==null)return;
            targetExperience=owner.Permanent.TotalExperience;displayedExperience=targetExperience;experienceAnimating=false;pendingLevelUp=false;
            if(experienceFraction!=null)RefreshExperience();
        }
        public void ResetPresentationState()
        {
            lastReceiptRun=-1;lastReceiptTarget=-1;unlockAlertTime=0;
            Array.Clear(unlockGlow,0,unlockGlow.Length);
            if(unlockAlert!=null)unlockAlert.gameObject.SetActive(false);
            SkipExperienceAnimation();
        }
        void RefreshExperience()
        {
            int level=DisplayedLevel;playerLevel.text="Lv. "+level;
            bool maximum=level>=LightsOutPermanentModel.MaximumPlayerLevel;
            long progress=Math.Max(0,displayedExperience-LightsOutPermanentModel.ExperienceAtStartOfLevel(level));
            int needed=LightsOutPermanentModel.RequiredExperienceForLevel(level);
            experienceFraction.text=maximum?"MAX LEVEL":progress+" / "+needed+" XP";
            experienceFill.sizeDelta=new Vector2(274*(maximum?1:Mathf.Clamp01(progress/(float)needed)),16);RefreshUnlocks();
        }
        void ShowNewUnlocks(int before,int after)
        {
            for(int i=0;i<6;i++)if(before<UnlockLevel(i)&&after>=UnlockLevel(i))
            {unlockGlow[i]=1;unlockAlert.text="새로운 특수 방 해금! "+SpecialName(i);unlockAlertTime=2.6f;unlockAlert.gameObject.SetActive(true);}
        }
        void RefreshUnlocks()
        {
            int level=DisplayedLevel;
            for(int i=0;i<6;i++)
            {
                bool unlocked=level>=UnlockLevel(i);Color tint=unlocked?SpecialColors[i]:Muted;
                unlockNames[i].text=SpecialName(i);unlockDescriptions[i].text=SpecialDescription(i);
                unlockNames[i].color=tint;unlockDescriptions[i].color=unlocked?Muted:Alpha(Muted,.68f);
                unlockConditions[i].text="Lv. "+UnlockLevel(i)+" 해금 · "+(unlocked?"✓ 해금됨":"미해금");unlockConditions[i].color=unlocked?Mint:Muted;
                closedLocks[i].gameObject.SetActive(!unlocked);openLocks[i].gameObject.SetActive(unlocked);
                unlockFaces[i].color=unlocked?Color.Lerp(Card,SpecialColors[i],.08f+unlockGlow[i]*(.15f+.08f*Mathf.Sin(Time.unscaledTime*12))):Card;
                var pieces=unlockIconParts[i];for(int p=0;p<pieces.Length;p++)pieces[p].color=unlocked?tint:Alpha(Muted,.48f);
            }
        }
        static int UnlockLevel(int index)
        {var levels=LightsOutBalanceSettings.Shared.SpecialUnlockLevels;return levels!=null&&index<levels.Length?Math.Max(1,levels[index]):DefaultUnlockLevels[index];}
        static string SpecialName(int index)
        {var names=LightsOutBalanceSettings.Shared.SpecialNames;return names!=null&&index<names.Length&&!string.IsNullOrEmpty(names[index])?names[index]:SpecialNames[index];}
        static string SpecialDescription(int index)
        {var descriptions=LightsOutBalanceSettings.Shared.SpecialDescriptions;return descriptions!=null&&index<descriptions.Length&&!string.IsNullOrEmpty(descriptions[index])?descriptions[index]:SpecialDescriptions[index];}
        void Frame(string name,float x,float y,float w,float h)
        {Box(panel,name+"Border",x,y,w,h,Alpha(LightsOutStyle.Hex("536473"),.85f),8);Box(panel,name+"Face",x+2,y+2,w-4,h-4,Alpha(Panel,.95f),6);}

        UnityEngine.UI.Button ActionButton(string name,string label,float x,float y,float w,float h,Color face,Color foreground,UnityEngine.Events.UnityAction action,out TMP_Text caption,float fontSize=21,bool icon=false,int kind=0)
        {
            var graphic=Box(panel,name,x,y,w,h,face,5,true);var button=graphic.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=graphic;button.onClick.AddListener(action);
            var colors=button.colors;colors.highlightedColor=new Color(1.18f,1.18f,1.18f);colors.pressedColor=new Color(.72f,.72f,.72f);colors.disabledColor=new Color(.45f,.48f,.51f);colors.fadeDuration=.08f;button.colors=colors;
            var navigation=button.navigation;navigation.mode=UnityEngine.UI.Navigation.Mode.None;button.navigation=navigation;
            if(icon)DrawIcon(graphic.transform,kind,18,13,36,foreground);
            caption=Text(graphic.transform,"Label",label,icon?71:8,icon?7:0,icon?w-85:w-16,icon?34:h,fontSize,foreground,FontStyles.Bold,TextAlignmentOptions.Center);return button;
        }
        TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size,Color color,FontStyles style=FontStyles.Normal,TextAlignmentOptions alignment=TextAlignmentOptions.TopLeft)
        {
            var text=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();text.font=owner.Font;text.text=value;text.fontSize=size;text.color=color;text.fontStyle=style;text.alignment=alignment;
            text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Truncate;text.enableAutoSizing=true;text.fontSizeMax=size;text.fontSizeMin=size*.8f;return text;
        }
        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
        static LightsOutShape Box(Transform parent,string name,float x,float y,float w,float h,Color color,float radius=0,bool raycast=false)
        {var shape=Rect(parent,name,x,y,w,h).gameObject.AddComponent<LightsOutShape>();shape.form=LightsOutShape.Form.Rounded;shape.radius=radius;shape.color=color;shape.raycastTarget=raycast;return shape;}
        static Color Alpha(Color color,float alpha){color.a=alpha;return color;}
        static string Money(double amount)=>"₩ "+amount.ToString("#,0.####");
        static string SignedMoney(double amount)=>amount==0?Money(0):(amount<0?"− ":"+ ")+Money(Math.Abs(amount));

        // Icons are editable pixel geometry, rather than a flattened reference image.
        static readonly string[][] IconPixels={
            new[]{"   ###   ","  #####  "," ####### "," ####### ","  #####  ","   ###   ","   ###   ","   ###   ","    #    "},
            new[]{"     ##  ","    ##   ","   ##    ","  ###### "," ######  ","    ##   ","   ##    ","  ##     "," ##      "},
            new[]{"  #####  "," ##   ## ","##  #  ##","#   #   #","#   ### #","#       #","##     ##"," ##   ## ","  #####  "},
            new[]{"  ###    "," #####   ","##   ##  "," #  #### ","   ### # ","  #### # ","  ##   ##","   ##### ","    ###  "},
            new[]{" ####### "," ##   ## ","  ## ##  ","   ###   ","    #    ","   ###   ","  ## ##  "," ##   ## "," ####### "},
            new[]{"  #####  "," ##   ###","##    ###","#        ","#       #","        #","###    ##","###   ## ","  #####  "},
            new[]{"  ##  ## ","   ##  ##","    ##  #","     ##  ","      ## ","     ##  ","    ##  #","   ##  ##","  ##  ## "},
            new[]{"    #    ","   ###   ","  #####  "," ####### ","#########"," ##   ## "," ## # ## "," ## # ## "," ####### "},
            new[]{"  #####  "," ##   ## ","## ### ##","#  #    #","#  ###  #","#    #  #","## ### ##"," ##   ## ","  #####  "},
            new[]{"    #    ","    #    ","  #####  ","#########"," ####### ","  #####  ","  ## ##  "," ##   ## "," #     # "},
            new[]{"   ###   ","   # #   ","  #####  "," ##   ## ","##     ##","##     ##","##     ##"," ##   ## ","  #####  "},
            new[]{"  #####  "," ## # ## ","##  #  ##","#   #   #","#########","#   #   #","##  #  ##"," ## # ## ","  #####  "},
            new[]{"   ###   ","  #   #  ","  #   #  "," ####### "," ####### "," ### ### "," ### ### "," ####### ","         "},
            new[]{"     ### ","    #   #","    #    "," ####### "," ####### "," ### ### "," ### ### "," ####### ","         "}
        };
        static RectTransform DrawIcon(Transform parent,int kind,float x,float y,float size,Color color)
        {
            var root=Rect(parent,"PixelIcon"+kind,x,y,size,size);var pixels=IconPixels[Mathf.Clamp(kind,0,IconPixels.Length-1)];float unit=size/9;
            for(int row=0;row<pixels.Length;row++)for(int col=0;col<pixels[row].Length;col++)if(pixels[row][col]=='#')Box(root,"Pixel",col*unit,row*unit,unit,unit,color,0);
            return root;
        }
    }
}
