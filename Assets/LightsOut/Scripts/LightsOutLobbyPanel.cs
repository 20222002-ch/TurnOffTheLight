using TMPro;
using UnityEngine;

namespace LightsOut
{
    /// <summary>Permanent equipment is changed only here, before a DAY's first shift starts.</summary>
    public sealed class LightsOutLobbyPanel : MonoBehaviour
    {
        LightsOutVerticalGame owner;
        RectTransform panel;
        TMP_Text day, money, points, highest, instructions, mainLabel, title;
        UnityEngine.UI.Button mainButton, skillsButton;
        bool readOnly;
        readonly TMP_Text[] slots=new TMP_Text[3], levels=new TMP_Text[5], prices=new TMP_Text[5], equipLabels=new TMP_Text[5];
        readonly UnityEngine.UI.Button[] upgrades=new UnityEngine.UI.Button[5], equips=new UnityEngine.UI.Button[5];
        static readonly Color Ink=LightsOutStyle.Hex("071321"), Face=LightsOutStyle.Hex("102638"), Cream=LightsOutStyle.Hex("F5EFDA"), Muted=LightsOutStyle.Hex("8DA6BA"), Mint=LightsOutStyle.Hex("8DE7C5"), Gold=LightsOutStyle.Hex("F6C96D");
        public void Initialize(LightsOutVerticalGame game){owner=game;Build();Hide();}
        public void Show(bool readOnlyView=false){readOnly=readOnlyView;panel.gameObject.SetActive(true);panel.SetAsLastSibling();Refresh();}
        public void Hide(){if(panel)panel.gameObject.SetActive(false);}
        public void Refresh()
        {
            if(!panel)return;
            var career=owner.Career;var permanent=owner.Permanent;
            title.text=readOnly?"영구 액세서리":"근무 준비";
            mainLabel.text=readOnly?"정산 화면으로 돌아가기 →":"준비 완료  ·  근무 시작 →";
            skillsButton.gameObject.SetActive(!readOnly);
            day.text="DAY "+career.Stage+"  ·  "+owner.Model.FloorRooms+"열 × "+owner.Model.FloorCount+"층  ·  "+career.AttemptDuration.ToString("0")+"초";
            money.text="보유금  "+LightsOutStyle.Format(owner.Model.Money)+"  /  월세  "+LightsOutStyle.Format(career.RentDue);
            points.text="관리인 포인트  "+permanent.Points;
            highest.text="월세 완납 최고 기록  DAY "+permanent.HighestPaidDay;
            for(int s=0;s<3;s++)slots[s].text=(s+1)+"  ·  "+(permanent.Equipped[s]<0?"빈 장착 슬롯":LightsOutPermanentModel.Names[permanent.Equipped[s]]+"  Lv."+permanent.Levels[permanent.Equipped[s]]);
            for(int i=0;i<5;i++)
            {
                int lv=permanent.Levels[i];bool equipped=permanent.IsEquipped(i);
                levels[i].text=lv==0?"미소유":(equipped?"장착 중  ·  ":"")+"Lv. "+lv+" / "+permanent.MaxLevel;
                levels[i].color=equipped?Mint:Muted;
                prices[i].text=lv>=permanent.MaxLevel?"최대 강화":(lv==0?"획득":"강화")+"  ·  "+permanent.UpgradeCost(i)+" 포인트";
                upgrades[i].interactable=owner.CanConfigureAccessories&&permanent.CanUpgrade(i);
                equips[i].interactable=owner.CanConfigureAccessories&&lv>0;
                equipLabels[i].text=readOnly?"로비에서만 변경":equipped?"장착 해제":"장착하기";
            }
            instructions.text=readOnly?"장착과 강화는 다음 DAY의 근무 준비 로비에서 진행하세요.\n이 화면을 여는 것은 이번 근무의 수입이나 룰렛 선택을 확정하지 않습니다.":"최대 3개 장착  ·  근무가 시작되면 교체할 수 없습니다.\n일반 스킬과 보유금은 게임오버 시 초기화되고, 액세서리·경험치·관리인 포인트는 유지됩니다.";
        }
        void Build()
        {
            panel=Rect(owner.UIRoot,"CareerLobby",0,0,1600,900);
            Box(panel,"NightBackdrop",0,0,1600,900,Ink,true);
            Box(panel,"HeaderAccent",40,38,5,80,Gold);
            title=Text(panel,"Title","근무 준비",65,28,700,66,40,Cream,true);
            day=Text(panel,"Day", "",68,103,1020,38,23,Gold,true);
            money=Text(panel,"Money","",68,153,990,34,19,Muted);
            points=Text(panel,"Points","",1130,51,400,45,26,Gold,true);
            highest=Text(panel,"HighestDay","",1130,107,400,33,17,Muted);
            Text(panel,"SlotHeading","이번 DAY의 장착 액세서리",42,214,1300,35,22,Cream,true);
            for(int s=0;s<3;s++)
            {
                Box(panel,"Slot"+s,40+s*510,258,490,74,Face);
                slots[s]=Text(panel,"SlotName"+s,"",58+s*510,277,456,40,21,Mint,true);
            }
            for(int i=0;i<5;i++)
            {
                int id=i;float x=40+i*307;
                Box(panel,"AccessoryBorder"+i,x,366,292,353,LightsOutStyle.Hex("365269"));
                Box(panel,"AccessoryFace"+i,x+2,368,288,349,Face);
                Text(panel,"AccessoryName"+i,LightsOutPermanentModel.Names[i],x+17,383,258,40,23,Cream,true);
                levels[i]=Text(panel,"AccessoryLevel"+i,"",x+17,430,258,33,17,Muted);
                Text(panel,"AccessoryDescription"+i,LightsOutPermanentModel.Descriptions[i],x+17,476,258,109,18,Muted);
                upgrades[i]=Button(panel,"AccessoryUpgrade"+i,"획득",x+15,598,262,44,Gold,()=>owner.UpgradeAccessory(id),out prices[i]);
                equips[i]=Button(panel,"AccessoryEquip"+i,"장착",x+15,655,262,44,LightsOutStyle.Hex("264558"),()=>{if(!owner.ToggleAccessory(id))instructions.text="장착 슬롯이 가득 찼습니다. 기존 액세서리를 해제한 뒤 선택하세요.";},out equipLabels[i]);
            }
            instructions=Text(panel,"EquipmentInstructions","",43,735,1505,68,18,Muted);
            mainButton=Button(panel,"StartWorkButton","준비 완료  ·  근무 시작 →",40,818,1000,60,Gold,()=>{if(readOnly)owner.CloseAccessoryView();else owner.StartLobbyShift();},out mainLabel);
            skillsButton=Button(panel,"LobbySkillsButton","일반 스킬 트리 보기",1062,818,498,60,LightsOutStyle.Hex("264558"),owner.OpenSkillTree,out _);
            Text(panel,"LobbyFooter","근무 시작 시 기회 1회 사용  ·  최고 DAY 최초 월세 완납 +1 포인트, 조기 납부 시 최대 +3 포인트 추가",40,881,1520,18,13,Muted);
        }
        UnityEngine.UI.Button Button(Transform parent,string name,string label,float x,float y,float w,float h,Color color,UnityEngine.Events.UnityAction action,out TMP_Text caption)
        {
            var shape=Box(parent,name,x,y,w,h,color,true);var button=shape.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=shape;button.onClick.AddListener(action);
            var colors=button.colors;colors.disabledColor=LightsOutStyle.Hex("475968");colors.highlightedColor=new Color(1.1f,1.1f,1.1f);button.colors=colors;
            caption=Text(shape.transform,"Label",label,6,4,w-12,h-8,19,color==Gold?Ink:Cream,true);caption.alignment=TextAlignmentOptions.Center;return button;
        }
        TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size,Color color,bool bold=false)
        {
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=owner.Font;t.text=value;t.fontSize=size;t.color=color;t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;t.enableAutoSizing=true;t.fontSizeMin=size*.8f;t.fontSizeMax=size;return t;
        }
        static LightsOutShape Box(Transform parent,string name,float x,float y,float w,float h,Color color,bool ray=false)
        {var shape=Rect(parent,name,x,y,w,h).gameObject.AddComponent<LightsOutShape>();shape.radius=9;shape.color=color;shape.raycastTarget=ray;return shape;}
        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    }
}
