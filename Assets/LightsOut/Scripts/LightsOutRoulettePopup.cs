using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace LightsOut
{
    /// <summary>A separate visual presentation of one persisted, fair checkout draw.</summary>
    public sealed class LightsOutRoulettePopup : MonoBehaviour
    {
        enum Phase { Hidden, Opening, Ready, Preparing, Accelerating, Racing, Braking, Revealing, Result }
        const float SectorAngle = 360f / LightsOutRentModel.RouletteSectors;
        static readonly Color Ink = LightsOutStyle.Hex("071321"), Panel = LightsOutStyle.Hex("0D2031"), Card = LightsOutStyle.Hex("132A3E");
        static readonly Color Cream = LightsOutStyle.Hex("F5EFDA"), Muted = LightsOutStyle.Hex("8DA6BA"), Gold = LightsOutStyle.Hex("F6C96D"), Mint = LightsOutStyle.Hex("8DE7C5"), Red = LightsOutStyle.Hex("F6928A");

        LightsOutVerticalGame owner;
        Action onDismiss;
        Phase phase;
        RectTransform overlay, window, wheel, wheelFrame, pointer, hubRect, flyingStake;
        CanvasGroup overlayGroup, flyingStakeGroup;
        LightsOutShape backdrop, halo, ripple;
        LightsOutPopupRouletteWheel wheelGraphic;
        LightsOutPopupRoulettePointer pointerGraphic;
        readonly LightsOutShape[] rimLights = new LightsOutShape[32];
        readonly LightsOutShape[] rays = new LightsOutShape[16];
        TMP_Text status, hub, stakeLabel, payoutLabel, resultLabel, explanation, startLabel, backLabel, stakeTicketLabel;
        UnityEngine.UI.Button startButton, backButton;
        Vector2 windowHome;
        float angle, pointerKick, tickTime, atmosphere;
        int lastSector;
        double stake;
        bool outcomeRevealed;
        Color outcomeColor;

        public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
        public bool IsBusy => phase == Phase.Opening || phase == Phase.Preparing || phase == Phase.Accelerating || phase == Phase.Racing || phase == Phase.Braking || phase == Phase.Revealing;
        public bool IsOutcomeRevealed => outcomeRevealed;
        public float WheelAngle => Mathf.Repeat(angle, 360f);
        public string PhaseName => phase.ToString();

        // Save files retain canonical slots. Only the presentation alternates wins/losses.
        public static int VisualIndexForSlot(int slot)
        { return slot < 0 || slot > 20 ? -1 : slot < 10 ? slot * 2 : slot < 20 ? (slot - 10) * 2 + 1 : 20; }
        public static int SlotForVisualIndex(int visualIndex)
        { return visualIndex < 0 || visualIndex > 20 ? -1 : visualIndex < 20 ? visualIndex % 2 == 0 ? visualIndex / 2 : 10 + visualIndex / 2 : 20; }

        public void Initialize(LightsOutVerticalGame game, Action dismissed)
        {
            owner = game; onDismiss = dismissed;
            if (overlay == null) Build();
            phase = Phase.Hidden; overlay.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (owner == null || owner.Career == null || overlay == null || IsOpen) return;
            var career = owner.Career;
            if (career.SettlementResolved && !career.RouletteUsed) return;
            stake = career.LastShiftIncome;
            outcomeRevealed = false; outcomeColor = Gold; atmosphere = 0;
            wheelGraphic.SelectedVisualIndex = -1;
            halo.color = Alpha(Gold, .08f); halo.rectTransform.localScale = Vector3.one; ripple.color = Color.clear;
            pointerGraphic.color = Cream; pointer.localRotation = Quaternion.identity; hubRect.localScale = Vector3.one;
            backdrop.color = Alpha(Ink, .83f);
            for (int i = 0; i < rimLights.Length; i++) rimLights[i].color = Gold;
            for (int i = 0; i < rays.Length; i++) rays[i].color = Color.clear;
            flyingStake.gameObject.SetActive(false);
            SetAngle(0); hub.text = "퇴근\n룰렛"; hub.color = Cream;
            stakeLabel.text = LightsOutStyle.Format(stake);
            payoutLabel.text = "?"; payoutLabel.color = Cream;
            resultLabel.text = "이번 근무의 수입만 걸립니다"; resultLabel.color = Cream;
            explanation.text = "기존 보유금은 유지됩니다.\n이번 기회에는 한 번만 돌릴 수 있습니다.";
            phase = Phase.Opening; RefreshButtons();
            overlay.gameObject.SetActive(true); overlay.SetAsLastSibling();
            overlayGroup.alpha = 0; overlayGroup.blocksRaycasts = true;
            owner.SetRouletteTension(0);
            StartCoroutine(OpenAnimation(career.RouletteUsed));
        }

        IEnumerator OpenAnimation(bool savedResult)
        {
            float elapsed = 0;
            while (elapsed < .4f)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / .4f), eased = 1 - Mathf.Pow(1 - u, 3);
                overlayGroup.alpha = eased;
                window.anchoredPosition = windowHome + Vector2.down * (1 - eased) * 26;
                window.localScale = Vector3.one * Mathf.Lerp(.955f, 1, eased);
                yield return null;
            }
            overlayGroup.alpha = 1; window.anchoredPosition = windowHome; window.localScale = Vector3.one;
            if (savedResult)
            {
                SetAngle(VisualIndexForSlot(owner.Career.LastRouletteSlot) * SectorAngle);
                RevealResult(); phase = Phase.Result;
            }
            else { phase = Phase.Ready; status.text = "수입을 걸고 돌릴 준비가 됐습니다"; }
            RefreshButtons();
        }

        public bool TrySpin()
        {
            if (!IsOpen || phase != Phase.Ready || owner.Career.LastShiftIncome <= 0) return false;
            // Draw and persist before animation. Preparing starts immediately after the draw succeeds.
            if (!owner.RollShiftIncome()) return false;
            int savedSlot = owner.Career.LastRouletteSlot;
            phase = Phase.Preparing; RefreshButtons();
            wheelGraphic.SelectedVisualIndex = -1;
            payoutLabel.text = "?"; resultLabel.text = "결과는 멈춘 뒤 공개됩니다";
            explanation.text = "행운을 기다리는 중입니다.\n룰렛이 멈출 때까지 지켜봐 주세요.";
            owner.PlayRouletteCue(3, .8f, .35f);
            StartCoroutine(SpinAnimation(savedSlot));
            return true;
        }

        IEnumerator SpinAnimation(int savedSlot)
        {
            float start = angle, elapsed = 0;
            flyingStake.gameObject.SetActive(true); flyingStakeGroup.alpha = 1;
            stakeTicketLabel.text = LightsOutStyle.Format(stake);
            status.text = "이번 수입을 룰렛에 걸었습니다"; hub.text = "준비";
            while (elapsed < .9f)
            {
                float dt = Time.unscaledDeltaTime; elapsed += dt;
                float u = Mathf.Clamp01(elapsed / .9f), ease = u * u * (3 - 2 * u);
                SetAngle(start + 14 * ease);
                flyingStake.anchoredPosition = Vector2.Lerp(new Vector2(858, -218), new Vector2(305, -410), ease);
                flyingStake.localScale = Vector3.one * Mathf.Lerp(1, .42f, ease);
                flyingStakeGroup.alpha = u < .7f ? 1 : 1 - (u - .7f) / .3f;
                hubRect.localScale = Vector3.one * (1 + .045f * Mathf.Sin(u * Mathf.PI * 4));
                SetAtmosphere(.2f + u * .3f); AnimateRim(.25f, dt, false);
                yield return null;
            }
            flyingStake.gameObject.SetActive(false);
            hub.text = "???"; hubRect.localScale = Vector3.one;
            start = angle; lastSector = Mathf.FloorToInt(angle / SectorAngle); tickTime = Time.unscaledTime;
            phase = Phase.Accelerating; status.text = "회전 시작";
            elapsed = 0;
            while (elapsed < .75f)
            {
                float dt = Time.unscaledDeltaTime; elapsed += dt; float u = Mathf.Clamp01(elapsed / .75f);
                SetAngle(start - 360 * u * u); SetAtmosphere(.45f + .25f * u); AnimateRim(u, dt, true);
                yield return null;
            }
            SetAngle(start - 360); start = angle;
            phase = Phase.Racing; status.text = "아직 결과를 알 수 없습니다";
            elapsed = 0;
            while (elapsed < 1.1f)
            {
                float dt = Time.unscaledDeltaTime; elapsed += dt;
                SetAngle(start - 960 * Mathf.Min(elapsed, 1.1f)); SetAtmosphere(.7f); AnimateRim(1, dt, true);
                yield return null;
            }
            SetAngle(start - 1056); start = angle;
            int visualIndex = VisualIndexForSlot(savedSlot);
            float target = visualIndex * SectorAngle;
            float distance = Mathf.Repeat(start - target, 360) + 1080;
            phase = Phase.Braking; status.text = "서서히 멈추고 있습니다";
            elapsed = 0;
            // Monotone Hermite travel matches the incoming speed and stops at the real saved sector.
            // All outcomes use the same three-second braking curve; there is no fabricated near-miss.
            while (elapsed < 3)
            {
                float dt = Time.unscaledDeltaTime; elapsed += dt; float u = Mathf.Clamp01(elapsed / 3);
                float u2 = u * u, u3 = u2 * u;
                float travel = (u3 - 2 * u2 + u) * 2880 + (-2 * u3 + 3 * u2) * distance;
                SetAngle(start - travel);
                SetAtmosphere(.72f + .18f * Mathf.Sin(u * Mathf.PI));
                AnimateRim(Mathf.Clamp01(1 - u), dt, true);
                if (u > .73f) status.text = "마지막 한 칸까지 지켜보세요";
                yield return null;
            }
            SetAngle(target); pointer.localRotation = Quaternion.identity; phase = Phase.Revealing; RevealResult();
            owner.SetRouletteTension(0); atmosphere = 0;
            owner.PlayRouletteCue(owner.Career.LastRouletteMultiplier > 1 ? 2 : 0, owner.Career.LastRouletteMultiplier == 10 ? 1.1f : .9f, .65f);
            elapsed = 0;
            while (elapsed < .65f)
            {
                elapsed += Time.unscaledDeltaTime; float u = Mathf.Clamp01(elapsed / .65f);
                ripple.rectTransform.localScale = Vector3.one * Mathf.Lerp(.25f, 1.2f, u);
                ripple.color = Alpha(outcomeColor, .25f * (1 - u));
                window.localScale = Vector3.one * (1 + .008f * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(u * Mathf.PI * 3));
                hubRect.localScale = Vector3.one * (1 + .11f * Mathf.Sin(u * Mathf.PI));
                if (owner.Career.LastRouletteMultiplier == 10)
                    for (int i = 0; i < rays.Length; i++)
                    {
                        rays[i].color = Alpha(Gold, .42f * (1 - u));
                        rays[i].rectTransform.localScale = Vector3.one * (1 + u * .45f);
                    }
                yield return null;
            }
            window.localScale = Vector3.one; hubRect.localScale = Vector3.one; ripple.color = Color.clear;
            for (int i = 0; i < rays.Length; i++) rays[i].color = Color.clear;
            phase = Phase.Result; RefreshButtons();
        }

        void RevealResult()
        {
            var career = owner.Career; int multiplier = career.LastRouletteMultiplier;
            outcomeRevealed = true; outcomeColor = multiplier == 0 ? Red : multiplier == 10 ? Gold : Mint;
            wheelGraphic.SelectedVisualIndex = VisualIndexForSlot(career.LastRouletteSlot);
            hub.text = "×" + multiplier; hub.color = outcomeColor;
            payoutLabel.text = LightsOutStyle.Format(career.LastSettledIncome); payoutLabel.color = outcomeColor;
            resultLabel.text = multiplier == 0 ? "이번 근무 수입을 잃었습니다" : multiplier == 10 ? "열 배의 퇴근 보너스!" : "이번 근무 수입이 두 배가 됐습니다";
            resultLabel.color = outcomeColor;
            status.text = "룰렛 결과  ×" + multiplier + "  ·  정산 완료";
            explanation.text = "정산 후 보유금  " + LightsOutStyle.Format(career.Work.Money) + "\n정산 화면으로 돌아가 다음 선택을 하세요.";
            halo.color = Alpha(outcomeColor, .14f); pointerGraphic.color = outcomeColor;
            for (int i = 0; i < rimLights.Length; i++) rimLights[i].color = outcomeColor;
        }

        void AnimateRim(float speed, float dt, bool ticks)
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * (4 + speed * 7));
            halo.rectTransform.localScale = Vector3.one * (1.02f + .022f * pulse);
            halo.color = Alpha(Gold, .06f + atmosphere * .085f * pulse);
            int head = Mathf.FloorToInt(Time.unscaledTime * (8 + speed * 20)) % rimLights.Length;
            for (int i = 0; i < rimLights.Length; i++)
            {
                int behind = (head - i + rimLights.Length) % rimLights.Length;
                rimLights[i].color = Color.Lerp(LightsOutStyle.Hex("2E4150"), Gold, behind < 5 ? 1 - behind * .16f : .16f + .12f * pulse);
            }
            int sector = Mathf.FloorToInt(angle / SectorAngle);
            if (ticks && sector != lastSector && Time.unscaledTime - tickTime >= .055f)
            {
                owner.PlayRouletteCue(0, Mathf.Lerp(.72f, 1.4f, speed), Mathf.Lerp(.26f, .14f, speed));
                tickTime = Time.unscaledTime; pointerKick = 1;
            }
            lastSector = sector;
            pointerKick = Mathf.MoveTowards(pointerKick, 0, dt * 10);
            pointer.localRotation = Quaternion.Euler(0, 0, pointerKick * (5 + speed * 6));
            hubRect.localScale = Vector3.one * (1 + .026f * pulse * atmosphere);
        }

        void SetAtmosphere(float intensity)
        {
            atmosphere = intensity;
            backdrop.color = Alpha(Ink, .83f + intensity * .055f);
            owner.SetRouletteTension(intensity);
        }

        void SetAngle(float value)
        { angle = value; wheel.localRotation = Quaternion.Euler(0, 0, value); }

        void RefreshButtons()
        {
            bool ready = phase == Phase.Ready;
            startButton.interactable = ready && stake > 0;
            backButton.interactable = !IsBusy && phase != Phase.Hidden;
            startLabel.text = phase == Phase.Result ? "이번 기회 룰렛 정산 완료" : IsBusy && phase != Phase.Opening ? "룰렛이 멈출 때까지 기다려 주세요" : "이번 수입 걸고 돌리기";
            backLabel.text = phase == Phase.Result ? "결과 확인하고 정산으로" : "정산으로 돌아가기";
        }

        public bool TryClose()
        {
            if (!IsOpen || IsBusy) return false;
            Close(); onDismiss?.Invoke(); return true;
        }
        public void OnEscape() { TryClose(); }
        public void Hide() { if (!IsBusy && IsOpen) Close(); }
        void Close()
        {
            StopAllCoroutines(); phase = Phase.Hidden;
            owner.SetRouletteTension(0); pointer.localRotation = Quaternion.identity;
            window.localScale = Vector3.one; overlay.gameObject.SetActive(false);
        }
        void OnDestroy() { if (owner != null) owner.SetRouletteTension(0); }

        void Build()
        {
            overlay = Rect(owner.UIRoot, "RoulettePopup", 0, 0, 1600, 900);
            overlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            backdrop = Box(overlay, "RouletteBackdrop", 0, 0, 1600, 900, Alpha(Ink, .83f), 0, true);
            Box(overlay, "WindowShadow", 226, 74, 1160, 780, Alpha(Color.black, .4f), 18);
            window = Rect(overlay, "RouletteWindow", 220, 60, 1160, 780); CenterPivot(window); windowHome = window.anchoredPosition;
            Box(window, "WindowBorder", 0, 0, 1160, 780, LightsOutStyle.Hex("4A6273"), 16);
            Box(window, "WindowFace", 2, 2, 1156, 776, Panel, 14, true);
            Box(window, "HeaderRule", 34, 128, 1092, 1, LightsOutStyle.Hex("2E4558"), 0);
            Text(window, "RouletteTitle", "퇴근 룰렛", 35, 17, 750, 66, 41, Cream, FontStyles.Bold);
            Text(window, "RouletteEyebrow", "NIGHT SHIFT  /  ONE CHANCE", 780, 37, 340, 32, 17, Gold, FontStyles.Bold, TextAlignmentOptions.Right);
            status = Text(window, "RouletteStatus", "수입을 걸고 돌릴 준비가 됐습니다", 38, 87, 1080, 32, 19, Gold);

            wheelFrame = Rect(window, "WheelFrame", 45, 155, 520, 520);
            halo = Box(wheelFrame, "WheelHalo", -17, -17, 554, 554, Alpha(Gold, .08f), 554); CenterPivot(halo.rectTransform);
            ripple = Box(wheelFrame, "OutcomeRipple", -20, -20, 560, 560, Color.clear, 560); CenterPivot(ripple.rectTransform);
            Box(wheelFrame, "OuterRim", 0, 0, 520, 520, LightsOutStyle.Hex("355365"), 520);
            Box(wheelFrame, "GoldRim", 6, 6, 508, 508, Gold, 508);
            Box(wheelFrame, "RimInner", 12, 12, 496, 496, Ink, 496);
            wheel = CenterRect(wheelFrame, "AlternatingResultWheel", 478, 478, Vector2.zero);
            wheelGraphic = wheel.gameObject.AddComponent<LightsOutPopupRouletteWheel>(); wheelGraphic.raycastTarget = false;
            for (int i = 0; i < 21; i++)
            {
                float a = (90 - i * SectorAngle) * Mathf.Deg2Rad;
                var label = CenterRect(wheel, "VisualSlot" + i, 53, 34, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 181);
                label.localRotation = Quaternion.Euler(0, 0, -i * SectorAngle);
                var text = label.gameObject.AddComponent<TextMeshProUGUI>();
                StyleText(text, i == 20 ? "×10" : i % 2 == 0 ? "×2" : "×0", 22, i == 20 ? Ink : Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            }
            for (int i = 0; i < rimLights.Length; i++)
            {
                float a = i * Mathf.PI * 2 / rimLights.Length;
                rimLights[i] = Box(wheelFrame, "RimLamp" + i, 257 + Mathf.Cos(a) * 254, 257 + Mathf.Sin(a) * 254, 6, 6, Gold, 6);
            }
            for (int i = 0; i < rays.Length; i++)
            {
                var ray = CenterRect(wheelFrame, "JackpotRay" + i, 7, 114, Vector2.zero);
                ray.localRotation = Quaternion.Euler(0, 0, i * 360f / rays.Length);
                ray.anchoredPosition = Quaternion.Euler(0, 0, i * 360f / rays.Length) * new Vector2(0, 184);
                rays[i] = ray.gameObject.AddComponent<LightsOutShape>(); rays[i].form = LightsOutShape.Form.Rounded; rays[i].radius = 4; rays[i].color = Color.clear; rays[i].raycastTarget = false;
            }
            hubRect = Rect(wheelFrame, "StaticHub", 186, 186, 148, 148);
            // Pivot around the hub center so its breathing animation cannot drift.
            hubRect.pivot = new Vector2(.5f, .5f); hubRect.anchoredPosition += new Vector2(74, -74);
            Box(hubRect, "HubBorder", 0, 0, 148, 148, Gold, 148);
            Box(hubRect, "HubFace", 6, 6, 136, 136, Ink, 136);
            hub = Text(hubRect, "HubLabel", "퇴근\n룰렛", 14, 24, 120, 97, 28, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            pointer = Rect(wheelFrame, "FixedPointer", 238, -15, 44, 44);
            pointer.pivot = new Vector2(.5f, 1); pointer.anchoredPosition += new Vector2(22, 0);
            pointerGraphic = pointer.gameObject.AddComponent<LightsOutPopupRoulettePointer>(); pointerGraphic.color = Cream; pointerGraphic.raycastTarget = false;

            Box(window, "StakeCard", 615, 155, 500, 106, Card, 12);
            Text(window, "StakeCaption", "이번 근무에서 번 돈", 637, 166, 450, 29, 18, Muted);
            stakeLabel = Text(window, "StakeAmount", "₩ 0", 636, 202, 455, 46, 33, Cream, FontStyles.Bold);
            OddsRow("Double", 280, Mint, "×2", "수입 두 배", "10 / 21  ·  47.62%");
            OddsRow("Lose", 365, Red, "×0", "이번 수입 전액 손실", "10 / 21  ·  47.62%");
            OddsRow("Jackpot", 450, Gold, "×10", "수입 열 배", "1 / 21  ·  4.76%");
            Box(window, "PayoutCard", 615, 550, 500, 123, Card, 12);
            resultLabel = Text(window, "ResultCaption", "이번 근무의 수입만 걸립니다", 635, 558, 457, 28, 18, Muted);
            payoutLabel = Text(window, "PayoutAmount", "?", 635, 588, 457, 44, 33, Cream, FontStyles.Bold);
            explanation = Text(window, "ResultExplanation", "", 635, 636, 457, 31, 13, Muted);
            startButton = ActionButton("StartRouletteButton", "이번 수입 걸고 돌리기", 45, 695, 520, 56, Gold, Ink, () => TrySpin(), out startLabel);
            backButton = ActionButton("BackToSettlementButton", "정산으로 돌아가기", 615, 695, 500, 56, Card, Cream, () => TryClose(), out backLabel);
            Text(window, "RouletteFooter", "21칸의 넓이와 확률은 동일합니다.  ·  룰렛 진행 중에는 정산 선택이 잠깐 멈춥니다.", 35, 753, 1090, 23, 13, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            flyingStake = Rect(window, "StakeTransferTicket", 748, 189, 220, 58);
            flyingStake.pivot = Vector2.one * .5f; flyingStake.anchoredPosition += new Vector2(110, -29);
            flyingStakeGroup = flyingStake.gameObject.AddComponent<CanvasGroup>(); flyingStakeGroup.blocksRaycasts = false;
            Box(flyingStake, "TicketFace", 0, 0, 220, 58, Gold, 10);
            stakeTicketLabel = Text(flyingStake, "TicketAmount", "₩ 0", 10, 3, 200, 52, 24, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            flyingStake.gameObject.SetActive(false);
        }

        void OddsRow(string name, float y, Color accent, string multiplier, string caption, string odds)
        {
            Box(window, name + "Odds", 615, y, 500, 74, Card, 10);
            Box(window, name + "Accent", 615, y, 4, 74, accent, 2);
            Text(window, name + "Multiplier", multiplier, 633, y + 9, 88, 54, 32, accent, FontStyles.Bold, TextAlignmentOptions.Center);
            Text(window, name + "Caption", caption, 738, y + 7, 351, 28, 19, Cream, FontStyles.Bold);
            Text(window, name + "Odds", odds, 738, y + 39, 351, 26, 17, accent);
        }

        UnityEngine.UI.Button ActionButton(string name, string label, float x, float y, float w, float h, Color face, Color foreground, UnityEngine.Events.UnityAction action, out TMP_Text caption)
        {
            var shape = Box(window, name, x, y, w, h, face, 10, true);
            var button = shape.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = shape; button.onClick.AddListener(action);
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(.72f, .72f, .72f);
            colors.disabledColor = new Color(.4f, .45f, .5f, 1); colors.fadeDuration = .09f; button.colors = colors;
            var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
            caption = Text(shape.rectTransform, "Label", label, 12, 5, w - 24, h - 10, 21, foreground, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            StyleText(text, value, size, color, style, alignment); return text;
        }
        void StyleText(TMP_Text text, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            text.font = owner.Font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = alignment;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Truncate;
            text.enableAutoSizing = true; text.fontSizeMax = size; text.fontSizeMin = size * .75f;
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        static RectTransform CenterRect(Transform parent, string name, float w, float h, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = new Vector2(w, h); return rect;
        }
        static void CenterPivot(RectTransform rect)
        {
            rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition += new Vector2(rect.sizeDelta.x * .5f, -rect.sizeDelta.y * .5f);
        }
        static LightsOutShape Box(Transform parent, string name, float x, float y, float w, float h, Color color, float radius, bool raycast = false)
        {
            var shape = Rect(parent, name, x, y, w, h).gameObject.AddComponent<LightsOutShape>(); shape.form = LightsOutShape.Form.Rounded;
            shape.radius = radius; shape.color = color; shape.raycastTarget = raycast; return shape;
        }
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    sealed class LightsOutPopupRouletteWheel : UnityEngine.UI.MaskableGraphic
    {
        int selectedVisualIndex = -1;
        public int SelectedVisualIndex { get => selectedVisualIndex; set { if (value != selectedVisualIndex) { selectedVisualIndex = value; SetVerticesDirty(); } } }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
        {
            helper.Clear(); Rect rect = GetPixelAdjustedRect();
            float outer = Mathf.Min(rect.width, rect.height) * .5f, inner = outer * .28f, step = Mathf.PI * 2 / 21;
            for (int slot = 0; slot < 21; slot++)
            {
                Color fill = LightsOutStyle.Hex(slot == 20 ? "ECC05D" : slot % 2 == 0 ? "216B62" : "733943");
                if (slot == selectedVisualIndex) fill = Color.Lerp(fill, LightsOutStyle.Hex("F5EFDA"), .4f);
                float start = Mathf.PI * .5f - slot * step + step * .5f - .006f, arc = step - .012f;
                for (int segment = 0; segment < 8; segment++)
                {
                    float a = start - arc * segment / 8, b = start - arc * (segment + 1) / 8;
                    Vector2 first = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), second = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                    int index = helper.currentVertCount;
                    helper.AddVert(rect.center + first * inner, fill, Vector2.zero); helper.AddVert(rect.center + first * outer, fill, Vector2.zero);
                    helper.AddVert(rect.center + second * outer, fill, Vector2.zero); helper.AddVert(rect.center + second * inner, fill, Vector2.zero);
                    helper.AddTriangle(index, index + 1, index + 2); helper.AddTriangle(index, index + 2, index + 3);
                }
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    sealed class LightsOutPopupRoulettePointer : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
        {
            helper.Clear(); Rect rect = GetPixelAdjustedRect();
            helper.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero); helper.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            helper.AddVert(new Vector3(rect.center.x, rect.yMin), color, Vector2.zero); helper.AddTriangle(0, 1, 2);
        }
    }
}
