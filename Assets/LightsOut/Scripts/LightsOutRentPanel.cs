using TMPro;
using UnityEngine;

namespace LightsOut
{
    /// <summary>Main checkout desk. The wheel opens in a separate modal.</summary>
    public sealed class LightsOutRentPanel : MonoBehaviour
    {
        static readonly Color Ink = LightsOutStyle.Hex("071321"), Panel = LightsOutStyle.Hex("0D2031"), Card = LightsOutStyle.Hex("132A3E");
        static readonly Color Cream = LightsOutStyle.Hex("F5EFDA"), Muted = LightsOutStyle.Hex("8DA6BA"), Amber = LightsOutStyle.Hex("F6C96D"), Mint = LightsOutStyle.Hex("8DE7C5"), Red = LightsOutStyle.Hex("F6928A");
        LightsOutVerticalGame owner;
        RectTransform panel;
        LightsOutRoulettePopup popup;
        TMP_Text heading, subheading, stage, phase, rent, wallet, income, attempts, decision, rentAdvice, result, summaryAmount, summaryCaption, keepLabel, spinLabel, payLabel, nextLabel, footer;
        readonly LightsOutShape[] pips = new LightsOutShape[5];
        readonly TMP_Text[] pipNumbers = new TMP_Text[5];
        UnityEngine.UI.Button keepButton, spinButton, payButton, upgradeButton, nextButton, restartButton;
        float refreshTimer;
        public bool IsSpinning => popup != null && popup.IsBusy;
        public bool IsRouletteOpen => popup != null && popup.IsOpen;
        public float WheelAngle => popup != null ? popup.WheelAngle : 0;
        public bool TryCloseRoulette() => popup != null && popup.TryClose();

        public void Initialize(LightsOutVerticalGame game)
        {
            owner = game;
            if (panel == null) Build();
            popup=gameObject.AddComponent<LightsOutRoulettePopup>();
            popup.Initialize(owner,Show);
            Hide();
        }

        public void Show()
        {
            if (panel == null || owner == null || owner.Career == null) return;
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            Refresh();
        }

        public void Hide()
        {
            if (IsRouletteOpen) return;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        void Update()
        {
            if (panel == null || !panel.gameObject.activeSelf) return;
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= .12f) { refreshTimer = 0; Refresh(); }
        }

        void Keep()
        {
            if (IsSpinning) return;
            if (owner.KeepShiftIncome()) Refresh();
        }

        void Spin()
        {
            if(IsRouletteOpen)return;
            if(owner.Career.Phase!=RentPhase.Checkout&&!owner.Career.RouletteUsed)return;
            popup.Show();
        }

        void Build()
        {
            panel = Rect(owner.UIRoot, "RentDesk", 0, 0, 1600, 900);
            Box(panel, "NightBackdrop", 0, 0, 1600, 900, Ink, 0, true);
            for (int x = 25; x < 1600; x += 60) Box(panel, "NightGrid", x, 140, 1, 652, Alpha(Muted, .035f), 0);
            DrawCity();
            Box(panel, "HeaderAccent", 38, 35, 5, 74, Amber, 2);
            heading = Text(panel, "Heading", "퇴근 정산", 60, 24, 870, 63, 42, Cream, FontStyles.Bold);
            subheading = Text(panel, "Subheading", "건물의 불을 끄고, 밀린 월세를 갚으세요.", 63, 95, 1230, 36, 20, Muted);
            Text(panel, "LedgerNumber", "NIGHT WORK  /  RENT DESK", 1150, 40, 410, 39, 18, Amber, FontStyles.Bold, TextAlignmentOptions.Right);
            Text(panel, "LedgerCaption", "정산 중에는 근무 시간이 멈춥니다", 1145, 91, 417, 29, 15, Muted, FontStyles.Normal, TextAlignmentOptions.Right);

            Box(panel, "LedgerBorder", 38, 156, 510, 637, LightsOutStyle.Hex("365269"), 14);
            Box(panel, "LedgerFace", 40, 158, 506, 633, Panel, 12);
            Text(panel, "LedgerHeading", "월세 장부", 65, 181, 220, 42, 25, Cream, FontStyles.Bold);
            stage = Text(panel, "Stage", "1단계", 284, 181, 235, 42, 23, Amber, FontStyles.Bold, TextAlignmentOptions.Right);
            phase = Text(panel, "Phase", "퇴근", 65, 230, 450, 32, 17, Muted);
            Box(panel, "RentCard", 63, 280, 462, 103, Card, 10);
            Text(panel, "RentCaption", "이번 단계의 월세", 82, 288, 415, 30, 18, Muted);
            rent = Text(panel, "RentDue", "₩ 0", 81, 327, 420, 47, 35, Amber, FontStyles.Bold);
            Text(panel, "WalletCaption", "보유금", 67, 411, 205, 33, 20, Muted);
            wallet = Text(panel, "Wallet", "₩ 0", 236, 400, 277, 51, 31, Cream, FontStyles.Bold, TextAlignmentOptions.Right);
            Text(panel, "IncomeCaption", "이번 근무 수입", 67, 469, 230, 34, 20, Muted);
            income = Text(panel, "ShiftIncome", "₩ 0", 265, 458, 248, 49, 29, Mint, FontStyles.Bold, TextAlignmentOptions.Right);
            Box(panel, "LedgerRule", 66, 526, 456, 1, LightsOutStyle.Hex("2C475E"), 0);
            attempts = Text(panel, "Attempts", "사용 1 / 5  ·  남은 4회", 67, 543, 450, 33, 20, Cream, FontStyles.Bold);
            for (int i = 0; i < 5; i++)
            {
                pips[i] = Box(panel, "AttemptPip" + i, 67 + i * 94, 590, 80, 45, Card, 8);
                pipNumbers[i] = Text(pips[i].rectTransform, "Number", (i + 1).ToString(), 0, 0, 80, 45, 22, Muted, FontStyles.Bold, TextAlignmentOptions.Center);
            }
            decision = Text(panel, "Decision", "", 67, 652, 454, 59, 19, Cream);
            rentAdvice = Text(panel, "RentAdvice", "", 67, 728, 454, 48, 16, Muted);

            Box(panel,"SettlementBorder",570,156,992,637,LightsOutStyle.Hex("365269"),14);
            Box(panel,"SettlementFace",572,158,988,633,Panel,12);
            Text(panel,"SummaryHeading","이번 근무 정산",600,181,510,46,28,Cream,FontStyles.Bold);
            Text(panel,"ReceiptStamp","NIGHT SHIFT  /  RECEIPT",1153,190,380,30,16,Muted,FontStyles.Normal,TextAlignmentOptions.Right);
            Box(panel,"IncomeReceipt",598,253,936,196,Card,12);
            Box(panel,"ReceiptAccent",598,253,5,196,Mint,2);
            summaryCaption=Text(panel,"SettlementCaption","이번 근무에서 번 돈",626,274,850,37,21,Muted);
            summaryAmount=Text(panel,"SettlementAmount","₩ 0",624,321,855,96,64,Mint,FontStyles.Bold);
            result=Text(panel,"SettlementResult","",610,463,915,40,22,Cream,FontStyles.Bold);
            Box(panel,"ReceiveChoice",598,523,450,164,Card,11);
            Box(panel,"RouletteChoice",1065,523,469,164,Card,11);
            Text(panel,"ReceiveTitle","그대로 받기  ×1",620,545,405,42,25,Mint,FontStyles.Bold);
            Text(panel,"ReceiveDescription","이번 수입을 보유금에 더합니다.\n정산 후 월세 납부와 강화가 가능합니다.",620,598,405,66,18,Muted);
            Text(panel,"RouletteTitle","퇴근 룰렛",1087,545,420,42,25,Amber,FontStyles.Bold);
            Text(panel,"RouletteDescription","×2 10칸 · ×0 10칸 · ×10 1칸\n별도 룰렛 창에서 수입을 걸고 돌립니다.",1087,598,420,66,18,Muted);

            keepButton = ActionButton("KeepIncomeButton", "그대로 받기  ×1", 596, 718, 427, 55, Card, Cream, Keep, out keepLabel);
            spinButton = ActionButton("RollIncomeButton", "룰렛 창 열기 →", 1065, 718, 469, 55, Amber, Ink, Spin, out spinLabel);
            payButton = ActionButton("PayRentButton", "월세 내고 다음 단계 →", 38, 814, 510, 58, Mint, Ink, () => { if (!IsSpinning) owner.PayRent(); }, out payLabel);
            upgradeButton = ActionButton("RentUpgradeButton", "스킬 / 도구 강화", 570, 814, 370, 58, Card, Cream, () => { if (!IsSpinning) owner.OpenSkillTree(); }, out _);
            nextButton = ActionButton("NextAttemptButton", "다음 근무 시작 →", 962, 814, 600, 58, Amber, Ink, () => { if (!IsSpinning) owner.StartNextShift(); }, out nextLabel);
            restartButton = ActionButton("RestartCareerButton", "1단계부터 다시 시작", 570, 814, 992, 58, Red, Ink, () => { if (!IsSpinning) owner.RestartCareer(); }, out _);
            footer = Text(panel, "Footer", "소등 완료와 시간 만료 모두 기회를 1회 사용합니다.", 38, 875, 1520, 24, 14, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        void Refresh()
        {
            if (panel == null || !panel.gameObject.activeSelf || owner == null || owner.Career == null) return;
            // Keep the desk's previous receipt until the popup reveals and dismisses its draw.
            if(IsRouletteOpen)return;
            var career = owner.Career;
            bool bankrupt = career.Phase == RentPhase.Bankrupt && !IsSpinning;
            bool checkout = career.Phase == RentPhase.Checkout;
            bool between = career.Phase == RentPhase.BetweenAttempts;
            bool settled = career.SettlementResolved;
            double money = career.Work.Money;
            heading.text = bankrupt ? "월세를 갚지 못했습니다" : "퇴근 정산";
            subheading.text = bankrupt ? "5번의 근무가 끝났습니다. 다음에는 더 빠르게, 더 신중하게." : "먼저 월세를 갚을까요, 다음 근무를 위해 강화할까요?";
            stage.text = career.Stage + "단계";
            phase.text = IsSpinning ? "퇴근 룰렛 정산 중" : bankrupt ? "기회 5회 소진" : career.LastCompleted ? "건물 소등 완료 · 기회 1회 사용" : "제한 시간 종료 · 기회 1회 사용";
            rent.text = LightsOutStyle.Format(career.RentDue);
            wallet.text = LightsOutStyle.Format(money);
            income.text = LightsOutStyle.Format(career.LastShiftIncome);
            attempts.text = "사용 " + career.AttemptsUsed + " / 5  ·  남은 " + career.AttemptsRemaining + "회";
            for (int i = 0; i < 5; i++)
            {
                bool used = i < career.AttemptsUsed;
                pips[i].color = used ? bankrupt ? Color.Lerp(Card, Red, .36f) : Color.Lerp(Card, Amber, .36f) : Card;
                pipNumbers[i].color = used ? bankrupt ? Red : Amber : Muted;
            }
            if (IsSpinning)
            {
                result.text = "룰렛이 돌고 있습니다…";
                result.color = Amber;
                decision.text = "룰렛이 멈출 때까지 잠시 기다려 주세요.";
            }
            else if (!settled)
            {
                result.text = career.LastShiftIncome > 0 ? "이번 수입을 그대로 받거나, 룰렛에 걸어보세요." : "이번 근무 수입이 없습니다. 그대로 받기로 정산하세요.";
                result.color = Amber;
                decision.text = "이번 수입의 정산 방법을 선택하세요.\n월세 납부는 정산 후 선택할 수 있습니다.";
            }
            else
            {
                int multiplier = career.RouletteUsed ? career.LastRouletteMultiplier : 1;
                result.text = (career.RouletteUsed ? "룰렛 결과  ×" + multiplier : "수입 그대로 받기  ×1") + "   ·   " + LightsOutStyle.Format(career.LastShiftIncome * multiplier);
                result.color = multiplier == 0 ? Red : multiplier == 10 ? Amber : Mint;
                decision.text = bankrupt ? "5회 안에 월세를 갚지 못했습니다.\n돈과 강화를 초기화하고 1단계부터 시작합니다." : money >= career.RentDue ? "월세를 낼 수 있습니다!\n지금 납부하거나, 남은 기회를 활용하세요." : career.AttemptsRemaining > 0 ? "아직 월세가 부족합니다.\n강화하거나 다음 근무에서 더 벌어보세요." : "마지막 근무 정산을 확인하세요.";
            }
            rentAdvice.text = bankrupt ? "재시작은 버튼을 눌렀을 때 진행됩니다." : "월세는 보유금에서 차감됩니다.\n남은 돈과 강화는 다음 단계로 이월됩니다.";
            summaryCaption.text=settled?"확정 정산액":"이번 근무에서 번 돈";
            summaryAmount.text=LightsOutStyle.Format(settled?career.LastSettledIncome:career.LastShiftIncome);
            summaryAmount.color=settled&&career.LastRouletteMultiplier==0?Red:career.RouletteUsed&&career.LastRouletteMultiplier==10?Amber:Mint;
            keepButton.interactable = checkout && !settled && !IsSpinning;
            spinButton.interactable = (checkout&&!settled&&career.LastShiftIncome>0)||career.RouletteUsed;
            keepLabel.text = settled ? "수입 정산 완료" : "그대로 받기  ×1";
            spinLabel.text = career.RouletteUsed?"룰렛 결과 다시 보기":settled?"이번 수입 정산 완료":career.LastShiftIncome<=0?"수입이 없어 룰렛 사용 불가":"룰렛 창 열기 →";
            payButton.interactable = between && settled && !IsSpinning && money >= career.RentDue;
            payLabel.text = money >= career.RentDue ? "월세 내고 다음 단계 →" : "월세까지 " + LightsOutStyle.Format(System.Math.Max(0, career.RentDue - money)) + " 부족";
            upgradeButton.interactable = between && settled && !IsSpinning;
            nextButton.interactable = between && settled && !IsSpinning && career.AttemptsRemaining > 0;
            nextLabel.text = career.AttemptsRemaining > 0 ? "다음 근무 시작  ·  " + (career.AttemptsUsed + 1) + " / 5  →" : "이번 단계의 근무 기회를 모두 사용했습니다";
            payButton.gameObject.SetActive(!bankrupt);
            upgradeButton.gameObject.SetActive(!bankrupt);
            nextButton.gameObject.SetActive(!bankrupt);
            restartButton.gameObject.SetActive(bankrupt);
            restartButton.interactable = bankrupt && !IsSpinning;
            footer.text = bankrupt ? "새 출발: 보유금과 스킬 초기화 · 1단계의 낮은 건물부터 시작" : "소등 완료와 시간 만료 모두 기회를 1회 사용합니다.  ·  단계마다 총 5회";
        }

        void DrawCity()
        {
            var random = new System.Random(729);
            for (int x = 0; x < 1600; x += 62)
            {
                int height = random.Next(45, 125);
                var building = Box(panel, "RentNightCity", x, 900 - height, 56, height, LightsOutStyle.Hex("0D2336"), 0);
                for (int row = 0; row < height / 14; row++)
                    for (int column = 0; column < 3; column++)
                        if (random.NextDouble() < .22) Box(building.rectTransform, "Window", 10 + column * 14, 12 + row * 14, 4, 6, Alpha(Amber, .15f), 0);
            }
        }

        UnityEngine.UI.Button ActionButton(string name, string label, float x, float y, float w, float h, Color face, Color foreground, UnityEngine.Events.UnityAction action, out TMP_Text caption)
        {
            var graphic = Box(panel, name, x, y, w, h, face, 9, true);
            var button = graphic.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = graphic;
            button.onClick.AddListener(action);
            var colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(.78f, .78f, .78f);
            colors.disabledColor = new Color(.42f, .48f, .53f, 1);
            colors.fadeDuration = .09f; button.colors = colors;
            var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
            caption = Text(graphic.rectTransform, "Label", label, 13, 6, w - 26, h - 12, 21, foreground, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            StyleText(text, value, size, color, style, alignment);
            return text;
        }
        void StyleText(TMP_Text text, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            text.font = owner.Font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = alignment;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Truncate;
            text.enableAutoSizing = true; text.fontSizeMax = size; text.fontSizeMin = size * .75f;
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var transform = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            transform.SetParent(parent, false); transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            transform.anchoredPosition = new Vector2(x, -y); transform.sizeDelta = new Vector2(w, h);
            return transform;
        }
        static LightsOutShape Box(Transform parent, string name, float x, float y, float w, float h, Color color, float radius, bool raycast = false)
        {
            var shape = Rect(parent, name, x, y, w, h).gameObject.AddComponent<LightsOutShape>();
            shape.form = LightsOutShape.Form.Rounded; shape.radius = radius; shape.color = color; shape.raycastTarget = raycast;
            return shape;
        }
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
    }

}
