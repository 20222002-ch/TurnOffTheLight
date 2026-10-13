using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightsOut
{
    /// <summary>Twenty-node skill tree, built on the existing 1600 x 900 overlay canvas.</summary>
    public sealed class LightsOutSkillTree : MonoBehaviour
    {
        private LightsOutVerticalGame owner;
        private RectTransform panel, treeContent;
        private TMP_Text money, title, level, description, currentEffect, nextEffect, requirement, price, buyLabel, zoomLabel, summary;
        private UnityEngine.UI.Button buyButton;
        private LightsOutShape detailBorder;
        private readonly NodeView[] nodes = new NodeView[20];
        private readonly LightsOutShape[] branchBorders = new LightsOutShape[4];
        private readonly TMP_Text[] branchLevels = new TMP_Text[4];
        private readonly LightsOutShape[] connections = new LightsOutShape[20];
        private readonly LightsOutShape[] connectionGlows = new LightsOutShape[20];
        private readonly RectTransform[] detailIcons = new RectTransform[4];
        private int selected;
        private float zoom = 1f;
        private float refreshTimer;

        private static readonly Color Ink = Hex("07131F"), Panel = Hex("0B1B29"), Card = Hex("112635"), White = Hex("F4F3E9"), Muted = Hex("8096A6"), Locked = Hex("4B5964");
        private static readonly Color[] BranchColors = { Hex("F7C65D"), Hex("6BC8FC"), Hex("BD8AFF"), Hex("83E0A0") };
        private static readonly string[] BranchNames = { "이동 · 소등", "연쇄 강화", "수직 돌파", "자동화" };
        private static readonly Vector2[] Positions =
        {
            new Vector2(335,240), new Vector2(235,175), new Vector2(345,70), new Vector2(155,75), new Vector2(85,230),
            new Vector2(335,410), new Vector2(235,475), new Vector2(345,570), new Vector2(155,565), new Vector2(85,405),
            new Vector2(590,240), new Vector2(690,175), new Vector2(580,70), new Vector2(780,75), new Vector2(850,230),
            new Vector2(590,410), new Vector2(690,475), new Vector2(580,570), new Vector2(780,565), new Vector2(850,405)
        };
        private sealed class NodeView
        {
            public LightsOutShape outer, glow, face, icon;
            public LightsOutShape[] iconParts;
            public TMP_Text name, level, lockTag;
            public UnityEngine.UI.Button button;
        }

        public void Initialize(LightsOutVerticalGame game)
        {
            if (owner != null && owner.Model != null) owner.Model.Changed -= Refresh;
            owner = game;
            if (panel == null) Build();
            owner.Model.Changed += Refresh;
            Hide();
        }

        public void Show()
        {
            if (panel == null) return;
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            Refresh();
        }

        public void Hide()
        {
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (owner != null && owner.Model != null) owner.Model.Changed -= Refresh;
        }

        private void Update()
        {
            if (panel == null || !panel.gameObject.activeSelf) return;
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= .15f) { refreshTimer = 0; Refresh(); }
            float breathe = .035f * Mathf.Sin(Time.unscaledTime * 2.1f);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (i != selected && !owner.Model.CanBuySkill(i)) continue;
                Color c = BranchColors[i / 5];
                c.a = (i == selected ? .16f : .08f) + breathe;
                nodes[i].glow.color = c;
            }
        }

        private void Build()
        {
            panel = Rect(owner.UIRoot, "SkillTree", 0, 0, 1600, 900);
            Box(panel, "NightBackdrop", 0, 0, 1600, 900, Ink, 0, true);
            for (int x = 300; x < 1230; x += 50) Box(panel, "GridV", x, 125, 1, 690, Alpha(Hex("3C637F"), .1f), 0);
            for (int y = 130; y < 820; y += 50) Box(panel, "GridH", 285, y, 945, 1, Alpha(Hex("3C637F"), .1f), 0);
            CitySilhouette();
            DrawBulb(panel, 38, 31, 54, BranchColors[0]);
            Text(panel, "GameTitle", "불 끄고 퇴근해!", 105, 20, 650, 62, 42, White, FontStyles.Bold);
            Text(panel, "Subtitle", "스킬 트리", 109, 84, 290, 34, 22, BranchColors[0], FontStyles.Bold);
            Text(panel, "ProgressCaption", "더 빠르게 움직이고, 더 높이 소등하세요", 605, 63, 545, 38, 19, Muted, FontStyles.Normal, TextAlignmentOptions.Right);

            Box(panel, "SidebarBorder", 18, 137, 255, 677, Hex("294960"), 11);
            Box(panel, "Sidebar", 20, 139, 251, 673, Panel, 10);
            Text(panel, "CashTitle", "사용 가능 재화", 40, 157, 210, 30, 21, White, FontStyles.Bold);
            DrawCoin(panel, 44, 204, 29, BranchColors[0]);
            money = Text(panel, "AvailableMoney", "0", 85, 195, 163, 50, 33, BranchColors[0], FontStyles.Bold);
            Box(panel, "SidebarRule", 39, 258, 214, 1, Hex("29475B"), 0);
            for (int branch = 0; branch < 4; branch++)
            {
                int b = branch;
                float y = 280 + branch * 86;
                branchBorders[branch] = Box(panel, "BranchBorder" + branch, 32, y, 227, 76, Alpha(BranchColors[branch], .6f), 8);
                var background = Box(panel, "Branch" + branch, 34, y + 2, 223, 72, Tint(Card, BranchColors[branch], .12f), 7, true);
                var button = Button(background, () => Select(b * 5));
                DrawIcon(background.rectTransform, branch, 15, 14, 40, BranchColors[branch]);
                Text(background.rectTransform, "Name", BranchNames[branch], 68, 9, 150, 28, 21, White, FontStyles.Bold);
                branchLevels[branch] = Text(background.rectTransform, "Levels", "0 / 0", 70, 41, 145, 25, 19, BranchColors[branch]);
            }
            Text(panel, "OverallTitle", "전체 성장", 41, 643, 200, 30, 19, White, FontStyles.Bold);
            summary = Text(panel, "OverallSummary", "", 41, 683, 205, 99, 17, Muted);

            RectTransform viewport = Rect(panel, "TreeViewport", 282, 132, 941, 676);
            Box(viewport, "HitSurface", 0, 0, 941, 676, Color.clear, 0, true);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            treeContent = Rect(viewport, "TreeContent", 0, 0, 935, 650);
            treeContent.anchorMin = treeContent.anchorMax = new Vector2(.5f, .5f);
            treeContent.pivot = new Vector2(.5f, .5f);
            treeContent.anchoredPosition = Vector2.zero;
            AddPanAndZoom(viewport);

            // Lines are created first so every connection stays behind its nodes.
            for (int i = 0; i < 20; i++)
            {
                var skill = LightsOutVerticalModel.Skills[i];
                Vector2 start = skill.PrerequisiteId >= 0 ? Positions[skill.PrerequisiteId] : new Vector2(462, 325);
                connections[i] = Line(treeContent, "Connection" + i, start, Positions[i], 2.4f, BranchColors[i / 5]);
                connectionGlows[i] = Line(treeContent, "ConnectionGlow" + i, start, Positions[i], 8, Alpha(BranchColors[i / 5], .1f));
                connections[i].rectTransform.SetAsLastSibling();
            }
            Box(treeContent, "CoreGlow", 412, 275, 100, 100, Alpha(BranchColors[0], .065f), 50);
            var core = Box(treeContent, "Core", 427, 290, 70, 70, Hex("193548"), 35);
            DrawBulb(core.rectTransform, 20, 14, 31, BranchColors[0]);
            Text(treeContent, "CoreLabel", "성장의 시작", 401, 363, 122, 24, 15, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            for (int i = 0; i < 20; i++) BuildNode(i);

            Box(panel, "DetailBorder", 1240, 105, 336, 701, Hex("31546F"), 12);
            Box(panel, "Details", 1242, 107, 332, 697, Panel, 11);
            detailBorder = Box(panel, "SelectedIconBorder", 1262, 129, 70, 70, BranchColors[0], 8);
            Box(panel, "SelectedIconFace", 1265, 132, 64, 64, Card, 6);
            for (int b = 0; b < 4; b++)
            {
                var icon = DrawIcon(panel, b, 1280, 147, 35, BranchColors[b]);
                detailIcons[b] = icon.transform.parent as RectTransform;
                detailIcons[b].gameObject.SetActive(b == 0);
            }
            title = Text(panel, "SelectedTitle", "", 1346, 128, 210, 36, 24, White, FontStyles.Bold);
            level = Text(panel, "SelectedLevel", "", 1348, 170, 205, 29, 21, BranchColors[0], FontStyles.Bold);
            description = Text(panel, "Description", "", 1262, 225, 291, 85, 18, Muted);
            DetailBox("Current", 322, "현재 효과", out currentEffect);
            DetailBox("Next", 439, "다음 레벨 효과", out nextEffect);
            Box(panel, "RequirementFrame", 1262, 556, 292, 76, Hex("29465A"), 7);
            Box(panel, "RequirementFace", 1264, 558, 288, 72, Ink, 6);
            Text(panel, "RequirementTitle", "해금 조건", 1277, 564, 260, 24, 16, White, FontStyles.Bold);
            requirement = Text(panel, "Requirement", "", 1277, 591, 263, 32, 15, Muted);
            Text(panel, "PriceTitle", "업그레이드 비용", 1268, 651, 265, 24, 16, White);
            price = Text(panel, "Price", "", 1268, 678, 279, 38, 27, BranchColors[0], FontStyles.Bold);
            var buyFace = Box(panel, "UpgradeButton", 1262, 731, 292, 56, BranchColors[0], 8, true);
            buyButton = Button(buyFace, Purchase);
            buyLabel = Text(buyFace.rectTransform, "Label", "스킬 강화", 10, 8, 272, 40, 24, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            BuyColors(BranchColors[0]);

            var back = Box(panel, "BackButton", 25, 838, 223, 46, Hex("1B3245"), 7, true);
            Button(back, () => owner.CloseSkillTree());
            Text(back.rectTransform, "Label", "←  근무로 돌아가기", 5, 5, 213, 36, 19, White, FontStyles.Bold, TextAlignmentOptions.Center);
            Text(panel, "TreeHint", "노드를 선택해 강화하세요  ·  드래그로 이동  ·  휠로 확대", 315, 847, 615, 30, 17, Muted);
            ZoomButton("ZoomOut", "−", 1037, () => SetZoom(zoom - .1f));
            ZoomButton("ZoomIn", "+", 1091, () => SetZoom(zoom + .1f));
            zoomLabel = Text(panel, "ZoomLabel", "100%", 1152, 846, 84, 32, 18, White, FontStyles.Normal, TextAlignmentOptions.Center);
            var fit = Box(panel, "FitButton", 1250, 838, 178, 46, Hex("1B3245"), 7, true);
            Button(fit, () => { treeContent.anchoredPosition = Vector2.zero; SetZoom(1); });
            Text(fit.rectTransform, "Label", "전체 보기", 5, 5, 168, 36, 19, White, FontStyles.Normal, TextAlignmentOptions.Center);
            var close = Box(panel, "CloseButton", 1513, 29, 61, 46, Hex("1B3245"), 7, true);
            Button(close, () => owner.CloseSkillTree());
            Text(close.rectTransform, "Label", "×", 2, 2, 57, 42, 29, White, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        private void BuildNode(int id)
        {
            int capture = id;
            Vector2 p = Positions[id];
            Color c = BranchColors[id / 5];
            bool final = id % 5 == 4;
            float size = final ? 74 : 64;
            var parent = Rect(treeContent, "SkillNode" + id, p.x - size / 2, p.y - size / 2, size, size);
            var n = new NodeView();
            n.glow = Box(parent, "Glow", -12, -12, size + 24, size + 24, Alpha(c, .08f), 18);
            n.outer = Box(parent, "Border", -3, -3, size + 6, size + 6, c, final ? 18 : 8);
            n.face = Box(parent, "Face", 0, 0, size, size, Tint(Card, c, .2f), final ? 16 : 6, true);
            n.button = Button(n.face, () => Select(capture));
            n.icon = DrawIcon(parent, id / 5, size / 2 - 17, 7, 34, c);
            n.iconParts = n.icon.transform.parent.GetComponentsInChildren<LightsOutShape>();
            n.level = Text(parent, "Level", "0 / 5", 3, size - 26, size - 6, 23, 14, c, FontStyles.Bold, TextAlignmentOptions.Center);
            n.name = Text(parent, "Name", LightsOutVerticalModel.Skills[id].Name, size / 2 - 72, size + 7, 144, 26, 16, White, FontStyles.Normal, TextAlignmentOptions.Center);
            n.lockTag = Text(parent, "Lock", "잠김", size / 2 - 30, -23, 60, 21, 13, Locked, FontStyles.Bold, TextAlignmentOptions.Center);
            nodes[id] = n;
        }

        private void DetailBox(string name, float y, string heading, out TMP_Text effect)
        {
            Box(panel, name + "Frame", 1262, y, 292, 110, Hex("29465A"), 7);
            Box(panel, name + "Face", 1264, y + 2, 288, 106, Ink, 6);
            Text(panel, name + "Title", heading, 1277, y + 12, 260, 28, 18, BranchColors[0], FontStyles.Bold);
            effect = Text(panel, name + "Effect", "", 1277, y + 42, 263, 62, 18, White);
            effect.enableAutoSizing = true;
            effect.fontSizeMin = 14;
            effect.fontSizeMax = 18;
        }

        private void Select(int id)
        {
            selected = Mathf.Clamp(id, 0, 19);
            Refresh();
        }

        private void Purchase()
        {
            owner.BuySkill(selected);
            Refresh();
        }

        private void Refresh()
        {
            if (owner == null || panel == null || owner.Model == null || !panel.gameObject.activeSelf) return;
            var model = owner.Model;
            money.text = LightsOutStyle.Format(model.Money);
            int total = 0, acquired = 0;
            for (int b = 0; b < 4; b++)
            {
                int levels = 0, maximum = 0;
                for (int j = 0; j < 5; j++)
                {
                    int id = b * 5 + j;
                    int value = model.SkillLevels[id];
                    var s = LightsOutVerticalModel.Skills[id];
                    levels += value; maximum += s.MaxLevel; total += value;
                    if (value > 0) acquired++;
                    bool locked = s.PrerequisiteId >= 0 && model.SkillLevels[s.PrerequisiteId] < s.PrerequisiteLevel;
                    bool maxed = value >= s.MaxLevel;
                    bool canBuy = model.CanBuySkill(id);
                    Color c = locked ? Locked : BranchColors[b];
                    nodes[id].outer.color = id == selected ? White : Alpha(c, maxed || canBuy ? 1 : .5f);
                    nodes[id].face.color = locked ? Hex("18232C") : Tint(Card, c, maxed ? .35f : .16f);
                    for (int p = 0; p < nodes[id].iconParts.Length; p++)
                        if (nodes[id].iconParts[p].name != "GearHole") nodes[id].iconParts[p].color = c;
                    nodes[id].name.color = locked ? Muted : White;
                    nodes[id].level.color = c;
                    nodes[id].level.text = maxed ? "MAX" : value + " / " + s.MaxLevel;
                    nodes[id].lockTag.text = locked ? "잠김" : maxed ? "완료" : canBuy ? "강화 가능" : "";
                    nodes[id].lockTag.color = maxed || canBuy ? c : Muted;
                    nodes[id].glow.color = Alpha(c, id == selected ? .16f : canBuy ? .1f : .015f);
                    connections[id].color = Alpha(c, locked ? .3f : value > 0 ? .9f : .5f);
                    connectionGlows[id].color = Alpha(c, locked ? .015f : .07f);
                }
                branchLevels[b].text = levels + " / " + maximum;
                branchBorders[b].color = Alpha(BranchColors[b], selected / 5 == b ? 1 : .45f);
            }
            summary.text = "해금한 스킬  " + acquired + " / 20\n누적 강화  " + total + " 레벨\n네 계열을 함께 성장시키세요";
            var skill = LightsOutVerticalModel.Skills[selected];
            int lv = model.SkillLevels[selected];
            bool completed = lv >= skill.MaxLevel;
            Color category = BranchColors[selected / 5];
            title.text = skill.Name;
            level.text = "Lv. " + lv + " / " + skill.MaxLevel;
            level.color = category;
            detailBorder.color = category;
            for (int b = 0; b < 4; b++) detailIcons[b].gameObject.SetActive(b == selected / 5);
            description.text = skill.EffectDescription;
            currentEffect.text = model.GetCurrentEffect(selected);
            nextEffect.text = completed ? "최대 레벨을 달성했습니다." : model.GetNextEffect(selected);
            requirement.text = skill.PrerequisiteId < 0 ? "처음부터 강화 가능" : LightsOutVerticalModel.Skills[skill.PrerequisiteId].Name + " Lv. " + skill.PrerequisiteLevel + " 이상";
            bool unlocked = skill.PrerequisiteId < 0 || model.SkillLevels[skill.PrerequisiteId] >= skill.PrerequisiteLevel;
            requirement.color = unlocked ? BranchColors[3] : Muted;
            price.text = completed ? "강화 완료" : LightsOutStyle.Format(model.GetSkillCost(selected));
            price.color = category;
            buyButton.interactable = model.CanBuySkill(selected);
            buyLabel.text = completed ? "최대 레벨 달성" : !unlocked ? "선행 스킬 필요" : !buyButton.interactable ? "재화가 부족합니다" : "스킬 강화  +";
            BuyColors(category);
        }

        private void BuyColors(Color category)
        {
            var graphic = buyButton.targetGraphic as LightsOutShape;
            if (graphic != null) graphic.color = category;
            var colors = buyButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("FFFFFF");
            colors.pressedColor = Hex("C0D0DB");
            colors.disabledColor = Hex("4A5967");
            colors.fadeDuration = .12f;
            buyButton.colors = colors;
            buyLabel.color = buyButton.interactable ? Ink : Hex("B5C2CA");
        }

        private void SetZoom(float value)
        {
            zoom = Mathf.Clamp(value, .7f, 1.5f);
            treeContent.localScale = Vector3.one * zoom;
            if (zoomLabel != null) zoomLabel.text = Mathf.RoundToInt(zoom * 100) + "%";
        }

        private void AddPanAndZoom(RectTransform viewport)
        {
            var trigger = viewport.gameObject.AddComponent<EventTrigger>();
            var scroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scroll.callback.AddListener(e => SetZoom(zoom + ((PointerEventData)e).scrollDelta.y * .075f));
            trigger.triggers.Add(scroll);
            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(e =>
            {
                var p = (PointerEventData)e;
                float scale = Mathf.Max(.01f, owner.UIRoot.lossyScale.x);
                Vector2 pos = treeContent.anchoredPosition + p.delta / scale;
                treeContent.anchoredPosition = new Vector2(Mathf.Clamp(pos.x, -300, 300), Mathf.Clamp(pos.y, -220, 220));
            });
            trigger.triggers.Add(drag);
        }

        private void ZoomButton(string name, string label, float x, UnityEngine.Events.UnityAction action)
        {
            var b = Box(panel, name, x, 838, 44, 46, Hex("1B3245"), 6, true);
            Button(b, action);
            Text(b.rectTransform, "Label", label, 2, 2, 40, 42, 27, White, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        private void CitySilhouette()
        {
            var random = new System.Random(112);
            for (int x = 0; x < 1600; x += 49)
            {
                int height = random.Next(30, 108);
                var building = Box(panel, "NightCity", x, 820 - height, 45, height + 80, Hex("0A1A2A"), 0);
                for (int row = 0; row < height / 13; row++)
                    for (int col = 0; col < 3; col++)
                        if (random.NextDouble() < .18)
                            Box(building.rectTransform, "Window", 7 + col * 12, 8 + row * 13, 4, 6, Alpha(BranchColors[0], .3f), 0);
            }
        }

        private static LightsOutShape Line(Transform parent, string name, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            Vector2 middle = (a + b) * .5f;
            var shape = Box(parent, name, middle.x - d.magnitude / 2, middle.y - width / 2, d.magnitude, width, c, 0);
            shape.rectTransform.pivot = new Vector2(.5f, .5f);
            shape.rectTransform.anchoredPosition = new Vector2(middle.x, -middle.y);
            shape.rectTransform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return shape;
        }

        private LightsOutShape DrawIcon(Transform parent, int branch, float x, float y, float size, Color color)
        {
            var root = Rect(parent, "BranchIcon" + branch, x, y, size, size);
            LightsOutShape main;
            if (branch == 0)
            {
                main = Line(root, "MoveArrow1", new Vector2(size * .22f, size * .18f), new Vector2(size * .62f, size * .5f), size * .12f, color);
                Line(root, "MoveArrow2", new Vector2(size * .62f, size * .5f), new Vector2(size * .22f, size * .82f), size * .12f, color);
                Line(root, "MoveArrow3", new Vector2(size * .58f, size * .18f), new Vector2(size * .94f, size * .5f), size * .1f, color);
                Line(root, "MoveArrow4", new Vector2(size * .94f, size * .5f), new Vector2(size * .58f, size * .82f), size * .1f, color);
            }
            else if (branch == 1)
            {
                main = Box(root, "ChainCore", size * .36f, size * .36f, size * .28f, size * .28f, color, size / 2);
                Vector2 center = Vector2.one * size * .5f;
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2 / 3 - Mathf.PI / 2;
                    Vector2 edge = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * .39f;
                    Line(root, "ChainLink", center, edge, size * .065f, color);
                    Box(root, "ChainEnd", edge.x - size * .1f, edge.y - size * .1f, size * .2f, size * .2f, color, size / 2);
                }
            }
            else if (branch == 2)
            {
                main = Box(root, "UpStem", size * .43f, size * .3f, size * .14f, size * .58f, color, 0);
                Line(root, "UpHead1", new Vector2(size * .17f, size * .42f), new Vector2(size * .5f, size * .08f), size * .13f, color);
                Line(root, "UpHead2", new Vector2(size * .5f, size * .08f), new Vector2(size * .83f, size * .42f), size * .13f, color);
            }
            else
            {
                main = Box(root, "Gear", size * .18f, size * .18f, size * .64f, size * .64f, color, size);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    Vector2 center = Vector2.one * size * .5f;
                    Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * .41f;
                    Line(root, "GearTooth", center, p, size * .16f, color);
                }
                Box(root, "GearHole", size * .36f, size * .36f, size * .28f, size * .28f, Panel, size);
            }
            return main;
        }

        private void DrawBulb(Transform parent, float x, float y, float size, Color c)
        {
            var r = Rect(parent, "BulbIcon", x, y, size, size);
            Box(r, "Glow", -5, -5, size + 10, size + 10, Alpha(c, .09f), size);
            Box(r, "Bulb", size * .13f, 0, size * .74f, size * .74f, c, size);
            Line(r, "Filament1", new Vector2(size * .33f, size * .36f), new Vector2(size * .44f, size * .65f), size * .04f, White);
            Line(r, "Filament2", new Vector2(size * .65f, size * .36f), new Vector2(size * .54f, size * .65f), size * .04f, White);
            Box(r, "Base", size * .34f, size * .7f, size * .32f, size * .22f, c, 2);
            Box(r, "BaseLine", size * .33f, size * .78f, size * .34f, size * .04f, Ink, 0);
        }

        private void DrawCoin(Transform parent, float x, float y, float size, Color c)
        {
            var coin = Box(parent, "Coin", x, y, size, size, c, size);
            Box(coin.rectTransform, "CoinInner", 4, 4, size - 8, size - 8, Ink, size);
            Text(coin.rectTransform, "CoinMark", "$", 0, -1, size, size + 2, size * .72f, c, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        private TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color c, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var rect = Rect(parent, name, x, y, w, h);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = owner.Font; text.text = value; text.fontSize = size; text.color = c;
            text.fontStyle = style; text.alignment = alignment; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true; text.fontSizeMax = size; text.fontSizeMin = size * .75f;
            return text;
        }

        private static UnityEngine.UI.Button Button(LightsOutShape face, UnityEngine.Events.UnityAction action)
        {
            face.raycastTarget = true;
            var button = face.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(action);
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            var nav = button.navigation; nav.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = nav;
            return button;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        private static LightsOutShape Box(Transform parent, string name, float x, float y, float w, float h, Color c, float radius, bool raycast = false)
        {
            var r = Rect(parent, name, x, y, w, h);
            var shape = r.gameObject.AddComponent<LightsOutShape>();
            shape.form = LightsOutShape.Form.Rounded; shape.radius = radius; shape.color = c; shape.raycastTarget = raycast;
            return shape;
        }

        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out Color color); return color; }
        private static Color Alpha(Color c, float a) { c.a = a; return c; }
        private static Color Tint(Color baseColor, Color tint, float amount) { return Color.Lerp(baseColor, tint, amount); }
    }
}
