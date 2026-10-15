using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightsOut
{
    public sealed class LightsOutVerticalGame : MonoBehaviour
    {
        public TMP_FontAsset font;
        public Sprite primitive;
        public AudioClip[] effects;
        public AudioClip music;
        public RectTransform canvasRoot;
        public UnityEngine.UI.RawImage viewport;
        public Camera worldCamera;
        public LightsOutMover player;
        public GameObject playerPrefab;
        public TMP_FontAsset Font => font;
        public RectTransform UIRoot => canvasRoot;
        public LightsOutVerticalModel Model { get; private set; }
        public LightsOutRentModel Career { get; private set; }
        public LightsOutPermanentModel Permanent => Career.Permanent;
        public bool IsPaused { get; private set; }
        public bool Testing { get; set; }
        public int VisibleFloorCount { get; private set; }
        public LightsOutCameraRig CameraRig { get; private set; }
        const string SaveKey = "LightsOut.Rent.v3";
        const int PoolFloors = 48;
        static readonly Color Amber = LightsOutStyle.Hex("F6C96D"), Mint = LightsOutStyle.Hex("8DE7C5"), Muted = LightsOutStyle.Hex("829BB3"), Ink = LightsOutStyle.Hex("091422"), Cream = LightsOutStyle.Hex("F5EFDA");
        [Serializable] sealed class SaveState { public int version = 3; public string career, workProgress; public float x, y; }
        sealed class RoomView { public Transform root; public SpriteRenderer body, lamp, glow; public Color displayed; }
        sealed class FloorView { public GameObject root; public TMP_Text label; public RoomView[] rooms; public SpriteRenderer slab, ceiling, leftFrame, rightFrame; public int floor = -1; }
        readonly List<FloorView> floorPool = new List<FloorView>();
        readonly List<TMP_Text> quickNames = new List<TMP_Text>(), quickDescriptions = new List<TMP_Text>(), quickCosts = new List<TMP_Text>();
        readonly List<UnityEngine.UI.Button> quickButtons = new List<UnityEngine.UI.Button>();
        TMP_Text money, zone, shift, progress, combo, total, hint, reward, automation, toast, pageHint, playerLocator;
        RectTransform progressFill, floating, modal;
        UnityEngine.UI.Button dashButton;
        LightsOutSkillTree tree;
        LightsOutRentPanel rentPanel;
        LightsOutAudio sound;
        LightsOutRiseEffect riseEffect;
        LightsOutWorkGauge workGauge;
        LightsOutLobbyPanel lobby;
        LightsOutFeedback feedback;
        InputAction pauseAction, treeAction;
        RenderTexture render;
        Transform skyline, roof, ground;
        TMP_Text buildingSign, finalCountdown;
        bool lastRising;
        float saveTimer, refreshTimer, toastTimer, lastSound;
        int quickPage;
        bool treeShowing, lobbyShowing, accessoriesShowing;
        RentPhase lastPhase;
        string saveNotice;

        void Awake()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Career = new LightsOutRentModel(); Model = Career.Work;
            string permanentJson=PlayerPrefs.GetString("LightsOut.Permanent.v1","");
            if(!string.IsNullOrEmpty(permanentJson))Permanent.ImportJson(permanentJson);
            CacheUI();
            Vector2 start = StartingPosition();
            string savedWorkProgress=null;
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    SaveState saved = JsonUtility.FromJson<SaveState>(json);
                    if (saved != null && saved.version == 3 && Career.ImportJson(saved.career))
                    {
                        float x = float.IsNaN(saved.x) || float.IsInfinity(saved.x) ? StartingPosition().x : saved.x;
                        float y = float.IsNaN(saved.y) || float.IsInfinity(saved.y) ? .9f : saved.y;
                        start = new Vector2(Mathf.Clamp(x,-Model.HalfWidth+.22f,Model.HalfWidth-.22f), Mathf.Clamp(y,.22f,Model.FloorCount*LightsOutMover.FloorHeight-.22f));
                        savedWorkProgress=saved.workProgress;
                    }
                    else saveNotice = "월세 근무 저장을 읽지 못해 1단계에서 시작합니다.";
                }
                catch (ArgumentException) { saveNotice = "저장 데이터를 읽지 못해 새 근무를 시작합니다."; }
            }
            sound = gameObject.AddComponent<LightsOutAudio>(); sound.Initialize(effects, music); sound.SetVolume(PlayerPrefs.GetFloat("LightsOut.Volume", .6f));
            SetUpWorld(start);
            if(Career.Phase==RentPhase.Working&&!string.IsNullOrEmpty(savedWorkProgress))player.ImportWorkJson(savedWorkProgress);
            Model.Changed += Refresh;
            Model.Extinguished += OnExtinguished;
            tree = gameObject.AddComponent<LightsOutSkillTree>(); tree.Initialize(this);
            rentPanel = gameObject.AddComponent<LightsOutRentPanel>(); rentPanel.Initialize(this);
            lobby=gameObject.AddComponent<LightsOutLobbyPanel>();lobby.Initialize(this);
            BindUI();
            ConfigureImmersiveView();
            riseEffect=gameObject.AddComponent<LightsOutRiseEffect>();riseEffect.Initialize(player,primitive);
            workGauge=gameObject.AddComponent<LightsOutWorkGauge>();workGauge.Initialize(player,worldCamera,primitive);
            feedback=gameObject.AddComponent<LightsOutFeedback>();feedback.Initialize(this,primitive,worldCamera);
            pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pauseAction.performed += _ => HandlePause(); pauseAction.Enable();
            treeAction = new InputAction("Skill Tree", InputActionType.Button, "<Keyboard>/t");
            treeAction.performed += _ => { if(rentPanel.IsRouletteOpen){rentPanel.TryCloseRoulette();return;} if (treeShowing) CloseSkillTree(); else OpenSkillTree(); }; treeAction.Enable();
            lastPhase = Career.Phase;
            Refresh();
            if(Career.Phase==RentPhase.Lobby)ShowLobby();
            else if(Career.Phase!=RentPhase.Working) ShowCheckout();
            if (!string.IsNullOrEmpty(saveNotice)) Notify(saveNotice);
        }
        Vector2 StartingPosition() => new Vector2(-Model.HalfWidth+Model.RoomWidth*.5f,.9f);
        void ConfigureImmersiveView()
        {
            // Keep authored screens and references intact while removing the work dashboard.
            for(int i=0;i<canvasRoot.childCount;i++)
            {
                var child=canvasRoot.GetChild(i);
                if(child==viewport.transform||child==modal||child.name=="SkillTree"||child.name=="RentDesk"||child.name=="RoulettePopup"||child.name=="CareerLobby")continue;
                child.gameObject.SetActive(false);
            }
            var vr=viewport.rectTransform;vr.anchorMin=Vector2.zero;vr.anchorMax=Vector2.one;vr.pivot=new Vector2(.5f,.5f);vr.offsetMin=vr.offsetMax=Vector2.zero;
            viewport.transform.SetAsFirstSibling();viewport.gameObject.SetActive(true);
            floating.gameObject.SetActive(false);
            finalCountdown=UIText(canvasRoot,"FinalCountdown","",740,26,120,54,30,Amber);
            finalCountdown.alignment=TextAlignmentOptions.Center;finalCountdown.gameObject.SetActive(false);
        }
        void CacheUI()
        {
            TMP_Text T(string name) => canvasRoot.Find(name).GetComponent<TMP_Text>();
            money=T("Money"); zone=T("Zone"); shift=T("Shift"); progress=T("Progress"); combo=T("Combo"); total=T("Total"); hint=T("Hint");
            reward=T("RewardPerLight"); automation=T("Automation"); toast=T("Toast"); pageHint=T("PageHint");
            progressFill=canvasRoot.Find("ProgressFill").GetComponent<RectTransform>();
            floating=canvasRoot.Find("FloatingRewards").GetComponent<RectTransform>(); modal=canvasRoot.Find("Modal").GetComponent<RectTransform>();
            modal.gameObject.SetActive(false);
            T("Subtitle").text="WASD / 방향키로 이동   ·   방에 머물러 소등   ·   T 스킬 트리";
            T("UpgradeTitle").text="더 높이, 더 빠르게";
            T("MoneyCaption").text="보유금 · 정산된 수입";
            for(int i=0;i<4;i++)
            {
                var b=canvasRoot.Find("Upgrade"+i).GetComponent<UnityEngine.UI.Button>(); quickButtons.Add(b);
                quickNames.Add(b.transform.Find("Name").GetComponent<TMP_Text>());
                quickDescriptions.Add(b.transform.Find("Description").GetComponent<TMP_Text>());
                quickCosts.Add(b.transform.Find("Cost").GetComponent<TMP_Text>());
            }
        }
        void BindUI()
        {
            Bind("HelpButton",ShowHelp); Bind("SettingsButton",ShowSettings); Bind("NextShiftButton",OpenSkillTree);
            Bind("UpgradePage0",()=>{quickPage=0;Refresh();}); Bind("UpgradePage1",()=>{quickPage=1;Refresh();});
            SetText(canvasRoot.Find("UpgradePage0").GetComponentInChildren<TMP_Text>(),"이동 · 연쇄");
            SetText(canvasRoot.Find("UpgradePage1").GetComponentInChildren<TMP_Text>(),"수직 · 자동화");
            SetText(canvasRoot.Find("NextShiftButton").GetComponentInChildren<TMP_Text>(),"스킬 트리 열기  T →");
            dashButton=canvasRoot.Find("AdvanceButton").GetComponent<UnityEngine.UI.Button>();
            Bind("AdvanceButton",()=> { if(!IsPaused&&Career.Phase==RentPhase.Working) player.RequestDash(); });
            Bind("AreaButton",()=>CameraRig.ChangeZoom(-1)); Bind("BlackoutButton",()=>CameraRig.ChangeZoom(1));
            SetText(canvasRoot.Find("AreaButton").GetComponentInChildren<TMP_Text>(),"가까이 보기\n줌 인  +");
            SetText(canvasRoot.Find("BlackoutButton").GetComponentInChildren<TMP_Text>(),"더 많은 층 보기\n줌 아웃  −");
            for(int i=0;i<4;i++) { int slot=i; quickButtons[i].onClick.RemoveAllListeners(); quickButtons[i].onClick.AddListener(()=>BuySkill(QuickId(slot))); }
        }
        void Bind(string name, UnityEngine.Events.UnityAction action)
        { var b=canvasRoot.Find(name).GetComponent<UnityEngine.UI.Button>(); b.onClick.RemoveAllListeners(); b.onClick.AddListener(action); b.interactable=true; }
        int QuickId(int slot) { int[] ids=quickPage==0 ? new[]{0,1,2,5} : new[]{10,11,15,16}; return ids[slot]; }
        void Update()
        {
            if(Model==null) return;
            ResizeWorldRender();
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            if(!IsPaused&&Career.Phase==RentPhase.Working)
            {
                float elapsed=Mathf.Min(Time.unscaledDeltaTime,Career.TimeRemaining);
                float workDt=elapsed;
                player.Step(workDt); Career.Tick(elapsed); CameraRig.Tick(player.Position,Model,Mathf.Min(workDt,.1f),player.IsRising);
                riseEffect?.Tick(workDt);
                feedback?.Tick(workDt);
                if(player.IsRising&&!lastRising)sound.Play(4,1.25f);
                lastRising=player.IsRising;
                saveTimer+=elapsed; if(saveTimer>=8){Save();saveTimer=0;}
            }
            if(Career.Phase!=lastPhase)
            {
                lastPhase=Career.Phase;
                if(Career.Phase==RentPhase.Lobby){ShowLobby();Save();}
                else if(Career.Phase!=RentPhase.Working){player.CancelWork();workGauge?.Hide();sound.Play(5);ShowCheckout();Save();}
            }
            RenderRooms(dt);
            workGauge?.Tick();
            feedback?.RefreshCombo();
            refreshTimer+=dt; if(refreshTimer>=.12f){Refresh();refreshTimer=0;}
            toastTimer-=dt; toast.gameObject.SetActive(toastTimer>0&&(Career.Phase!=RentPhase.Working||IsPaused));
            bool warning=Career.Phase==RentPhase.Working&&!IsPaused&&Career.TimeRemaining<=5;
            finalCountdown.gameObject.SetActive(warning);
            if(warning){SetText(finalCountdown,Mathf.CeilToInt(Career.TimeRemaining).ToString());finalCountdown.alpha=.72f+.28f*Mathf.Sin(Time.unscaledTime*8);}
        }
        public bool CanBuySkill(int id) => Career!=null&&!(rentPanel&&rentPanel.IsRouletteOpen)&&Career.CanBuySkill(id);
        public string SkillLockReason(int id) => Career.GetSkillLockReason(id);
        public bool BuySkill(int id)
        {
            if(CanBuySkill(id)&&Career.BuySkill(id)){sound.Play(3);Notify(LightsOutVerticalModel.Skills[id].Name+" 강화 완료");Save();Refresh();return true;}
            Notify(SkillLockReason(id)); return false;
        }
        public void Notify(string text) { toast.text=text; toastTimer=3; toast.gameObject.SetActive(Career.Phase!=RentPhase.Working||IsPaused); }
        public void PlayRouletteCue(int index,float pitch=1,float gain=1) => sound?.PlayRouletteCue(index,pitch,gain);
        public void SetRouletteTension(float intensity) => sound?.SetRouletteTension(intensity);
        void Refresh()
        {
            if(Model==null||money==null) return;
            int standing=Mathf.Clamp(Mathf.FloorToInt(player.Position.y/LightsOutMover.FloorHeight)+1,1,Model.FloorCount);
            SetText(money,LightsOutStyle.Format(Model.Money)); SetText(zone,Model.FloorCount+"층 야간 소등 업무");
            SetText(shift,"DAY "+Career.Stage+"  ·  기회 "+Career.AttemptNumber+" / 5");
            SetText(progress,"현재 "+standing+"층  ·  완료 "+Model.ClearedFloors+" / "+Model.FloorCount+"층  ·  밀린 월세 "+LightsOutStyle.Format(Career.RentDue));
            progressFill.sizeDelta=new Vector2(1000*Mathf.Clamp01(Model.ClearedFloors/(float)Model.FloorCount),5);
            int seconds=Mathf.CeilToInt(Career.TimeRemaining);
            SetText(combo,"남은 시간  "+(seconds/60).ToString("00")+":"+(seconds%60).ToString("00"));
            combo.color=seconds<=10?LightsOutStyle.Hex("F08B86"):Amber;
            SetText(total,"이번 근무 수입 "+LightsOutStyle.Format(Model.PendingIncome));
            SetText(reward,"이동속도 "+Model.MoveSpeed.ToString("0.0")+"  ·  소등 범위 "+Model.ExtinguishRadius.ToString("0.0"));
            SetText(automation,Model.AutomationRate>0?"자동 소등 "+Model.AutomationRate.ToString("0.0")+"개 / 초":"WASD · 방향키로 방을 지나가세요");
            dashButton.interactable=Model.DashUnlocked&&!IsPaused&&player.DashCooldown<=0;
            SetText(dashButton.GetComponentInChildren<TMP_Text>(),!Model.DashUnlocked?"대시를 해금하면 Space로 돌파":player.DashCooldown>0?"대시 준비 "+player.DashCooldown.ToString("0.0")+"초":"대시  SPACE →");
            SetText(pageHint,quickPage==0?"이동하면서 성장하세요":"위층으로 퍼지는 소등과 자동화");
            SetText(hint,"5번 안에 월세를 갚으세요 · 근무 시작 시 기회 1회 사용 · 퇴근 후 수입 정산");
            for(int slot=0;slot<4;slot++)
            {
                int id=QuickId(slot),level=Model.SkillLevels[id];var s=LightsOutVerticalModel.Skills[id];
                SetText(quickNames[slot],s.Name+"  <color=#829BB3>"+level+" / "+s.MaxLevel+"</color>");
                SetText(quickDescriptions[slot],Model.GetNextEffect(id));
                bool max=level>=s.MaxLevel;
                SetText(quickCosts[slot],max?"강화 완료":s.PrerequisiteId>=0&&Model.SkillLevels[s.PrerequisiteId]<s.PrerequisiteLevel?"선행 스킬 필요":LightsOutStyle.Format(Model.GetSkillCost(id))+"  +");
                quickButtons[slot].interactable=CanBuySkill(id)&&!IsPaused;
                quickCosts[slot].color=CanBuySkill(id)?Amber:Muted;
            }
        }
        void SetUpWorld(Vector2 start)
        {
            render=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32){name="LightsOut Full Game View",antiAliasing=4}; render.Create();
            viewport.texture=render; viewport.raycastTarget=false; worldCamera.targetTexture=render;
            worldCamera.orthographic=true; worldCamera.backgroundColor=Ink;worldCamera.clearFlags=CameraClearFlags.SolidColor;
            CameraRig=worldCamera.GetComponent<LightsOutCameraRig>();if(!CameraRig)CameraRig=worldCamera.gameObject.AddComponent<LightsOutCameraRig>();CameraRig.WorldCamera=worldCamera;
            if(!player)
            {
                GameObject p=playerPrefab?Instantiate(playerPrefab,transform):new GameObject("Player");
                if(!playerPrefab){var sr=p.AddComponent<SpriteRenderer>();sr.sprite=primitive;sr.color=Mint;p.transform.localScale=Vector3.one*.38f;p.AddComponent<LightsOutMover>();}
                player=p.GetComponent<LightsOutMover>();
            }
            player.transform.position=new Vector3(start.x,start.y,-1);player.Initialize(Model);CameraRig.Snap(start,Model);
            var root=new GameObject("Visible Building Floors");root.transform.SetParent(transform,false);
            for(int i=0;i<PoolFloors;i++)
            {
                var row=new GameObject("Pooled Floor "+i);row.transform.SetParent(root.transform,false);
                var view=new FloorView{root=row,rooms=new RoomView[20]};
                var label=new GameObject("Floor Number",typeof(TextMeshPro));label.transform.SetParent(row.transform,false);
                view.label=label.GetComponent<TextMeshPro>();view.label.font=font;view.label.fontSize=2.2f;view.label.color=Muted;view.label.alignment=TextAlignmentOptions.Right;view.label.rectTransform.sizeDelta=new Vector2(1,.8f);view.label.textWrappingMode=TextWrappingModes.NoWrap;
                view.slab=WorldRect(row.transform,"Floor Slab",new Vector3(0,.03f,-.25f),Vector2.one,LightsOutStyle.Hex("355166"));
                view.ceiling=WorldRect(row.transform,"Ceiling Beam",new Vector3(0,1.77f,-.25f),Vector2.one,LightsOutStyle.Hex("203B4C"));
                view.leftFrame=WorldRect(row.transform,"Left Frame",new Vector3(0,.9f,-.3f),new Vector2(.12f,1.8f),LightsOutStyle.Hex("405B70"));
                view.rightFrame=WorldRect(row.transform,"Right Frame",new Vector3(0,.9f,-.3f),new Vector2(.12f,1.8f),LightsOutStyle.Hex("405B70"));
                for(int c=0;c<view.rooms.Length;c++)
                {
                    var room=new GameObject("Room "+c);room.transform.SetParent(row.transform,false);
                    var rv=new RoomView{root=room.transform};
                    rv.body=WorldRect(room.transform,"Interior",new Vector3(0,.9f,0),new Vector2(2.28f,1.64f),Amber);
                    rv.glow=WorldRect(room.transform,"Soft Light",new Vector3(0,.92f,-.05f),new Vector2(1.97f,1.32f),new Color(1,.85f,.45f,.12f));
                    rv.lamp=WorldRect(room.transform,"Fluorescent Light",new Vector3(0,1.47f,-.1f),new Vector2(1.34f,.07f),Cream);
                    view.rooms[c]=rv;rv.displayed=Amber;
                }
                floorPool.Add(view);row.SetActive(false);
            }
            var bg=new GameObject("Night Skyline");bg.transform.SetParent(transform,false);skyline=bg.transform;
            for(int i=0;i<36;i++) WorldRect(bg.transform,"Star",new Vector3(-24+(i*17%49),3+(i*11%19),4),new Vector2(.025f,.025f),new Color(.55f,.70f,.85f,.40f));
            for(int i=0;i<16;i++)
            {
                float x=-22+i*3,height=3+(i*7%12);
                WorldRect(bg.transform,"Tower "+i,new Vector3(x,height*.5f,2),new Vector2(2.6f,height),LightsOutStyle.Hex("132C40"));
                for(int j=0;j<5;j++) WorldRect(bg.transform,"Distant Window",new Vector3(x+(j%2==0?-.5f:.5f),1+j*1.1f,1.9f),new Vector2(.14f,.20f),new Color(.9f,.62f,.25f,.45f));
            }
            var roofObject=new GameObject("Building Roof");roofObject.transform.SetParent(transform,false);roof=roofObject.transform;
            WorldRect(roof,"Roof Cap",Vector3.zero,new Vector2(1,.12f),LightsOutStyle.Hex("527084"));
            var signObject=new GameObject("Night Building Sign",typeof(TextMeshPro));signObject.transform.SetParent(roof,false);signObject.transform.localPosition=new Vector3(0,.65f,-.1f);
            buildingSign=signObject.GetComponent<TextMeshPro>();buildingSign.font=font;buildingSign.fontSize=3.6f;buildingSign.color=Amber;buildingSign.alignment=TextAlignmentOptions.Center;buildingSign.rectTransform.sizeDelta=new Vector2(28,1);buildingSign.textWrappingMode=TextWrappingModes.NoWrap;
            var groundObject=new GameObject("Street");groundObject.transform.SetParent(transform,false);ground=groundObject.transform;
            WorldRect(ground,"Pavement",new Vector3(0,-.65f,1),new Vector2(80,1.25f),LightsOutStyle.Hex("142A39"));
            WorldRect(ground,"Road",new Vector3(0,-3,1),new Vector2(80,3.4f),LightsOutStyle.Hex("101E2B"));
            for(int i=0;i<16;i++)WorldRect(ground,"Road Marking",new Vector3(-30+i*4,-3,-.1f),new Vector2(1.2f,.04f),LightsOutStyle.Hex("596050"));
            RenderRooms(1);
        }
        void ResizeWorldRender()
        {
            if(!render||!viewport||!worldCamera)return;
            Vector2 size=viewport.rectTransform.rect.size;
            if(size.x<1||size.y<1)return;
            float aspect=Mathf.Clamp(size.x/size.y,.75f,3);
            int width=1600,height=Mathf.RoundToInt(width/aspect);
            if(height>1600){height=1600;width=Mathf.RoundToInt(height*aspect);}
            if(Mathf.Abs(render.width-width)<4&&Mathf.Abs(render.height-height)<4)return;
            var replacement=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="LightsOut Full Game View",antiAliasing=4};replacement.Create();
            worldCamera.targetTexture=replacement;viewport.texture=replacement;render.Release();Destroy(render);render=replacement;
        }
        SpriteRenderer WorldRect(Transform parent,string name,Vector3 at,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(SpriteRenderer));go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=new Vector3(size.x,size.y,1);
            var sr=go.GetComponent<SpriteRenderer>();sr.sprite=primitive;sr.color=color;return sr;
        }
        void RenderRooms(float dt)
        {
            if(CameraRig==null) return;
            float bottom=worldCamera.transform.position.y-worldCamera.orthographicSize,top=worldCamera.transform.position.y+worldCamera.orthographicSize;
            int first=Mathf.Max(0,Mathf.FloorToInt(bottom/LightsOutMover.FloorHeight)-1);
            int last=Mathf.Min(Model.FloorCount-1,Mathf.CeilToInt(top/LightsOutMover.FloorHeight)+1);VisibleFloorCount=Mathf.Max(0,last-first+1);
            float halfView=worldCamera.orthographicSize*worldCamera.aspect;
            int firstColumn=Mathf.Max(0,Mathf.FloorToInt((worldCamera.transform.position.x-halfView+Model.HalfWidth)/Model.RoomWidth)-1);
            int lastColumn=Mathf.Min(Model.FloorRooms-1,Mathf.CeilToInt((worldCamera.transform.position.x+halfView+Model.HalfWidth)/Model.RoomWidth)+1);
            for(int i=0;i<floorPool.Count;i++)
            {
                var v=floorPool[i];int floor=first+i;bool active=floor<=last;
                if(v.root.activeSelf!=active)v.root.SetActive(active);if(!active)continue;
                bool reused=v.floor!=floor;v.floor=floor;v.root.transform.position=new Vector3(0,floor*LightsOutMover.FloorHeight,0);v.label.text=(floor+1).ToString("000")+"F";
                v.label.transform.localPosition=new Vector3(-Model.HalfWidth-.65f,.90f,-.5f);v.label.gameObject.SetActive(Model.FloorCount>8);
                v.slab.transform.localScale=new Vector3(Model.BuildingWidth+.2f,.09f,1);v.ceiling.transform.localScale=new Vector3(Model.BuildingWidth+.2f,.07f,1);
                v.leftFrame.transform.localPosition=new Vector3(-Model.HalfWidth,.90f,-.3f);v.rightFrame.transform.localPosition=new Vector3(Model.HalfWidth,.90f,-.3f);
                for(int c=0;c<v.rooms.Length;c++)
                {
                    var rv=v.rooms[c];bool visible=c<Model.FloorRooms&&c>=firstColumn&&c<=lastColumn;
                    if(rv.root.gameObject.activeSelf!=visible)rv.root.gameObject.SetActive(visible);if(!visible)continue;
                    bool on=Model.IsLightOn(floor,c);RoomSpecialKind kind=Model.GetRoomKind(floor,c);
                    Color lightColor=RoomLightColor(kind);Color target=on?Color.Lerp(Amber,lightColor,kind==RoomSpecialKind.Normal?0:.27f):LightsOutStyle.Hex("1C3145");
                    rv.root.localPosition=new Vector3(-Model.HalfWidth+(c+.5f)*Model.RoomWidth,0,0);rv.root.localScale=new Vector3(Model.RoomWidth/LightsOutMover.RoomWidth,1,1);
                    rv.displayed=reused?target:Color.Lerp(rv.displayed,target,1-Mathf.Exp(-dt*16));rv.body.color=rv.displayed;
                    rv.lamp.color=on?lightColor:LightsOutStyle.Hex("405D72");rv.glow.color=on?new Color(lightColor.r,lightColor.g,lightColor.b,.12f):Color.clear;
                }
            }
            roof.position=new Vector3(0,Model.FloorCount*LightsOutMover.FloorHeight+.10f,-.2f);
            roof.GetChild(0).localScale=new Vector3(Model.BuildingWidth+.55f,.12f,1);
            buildingSign.text=Model.FloorCount<=8?"NIGHT MART  /  야간 마트":"LIGHTS OUT  /  야간 빌딩";
            ground.gameObject.SetActive(bottom<1);
            if(skyline)skyline.position=new Vector3(0,bottom,0);
            if(playerLocator)
            {
                Vector3 vp=worldCamera.WorldToViewportPoint(player.transform.position);
                playerLocator.gameObject.SetActive(vp.y>=0&&vp.y<=1&&vp.x>=0&&vp.x<=1);
                var vr=viewport.rectTransform;
                playerLocator.rectTransform.anchoredPosition=new Vector2(vr.anchoredPosition.x+vp.x*vr.rect.width-43,vr.anchoredPosition.y-(1-vp.y)*vr.rect.height+52);
            }
        }
        static Color RoomLightColor(RoomSpecialKind kind)
        {
            switch(kind)
            {
                case RoomSpecialKind.Golden:return LightsOutStyle.Hex("FFD45C");
                case RoomSpecialKind.Electrical:return LightsOutStyle.Hex("FFC07A");
                case RoomSpecialKind.Security:return LightsOutStyle.Hex("73CCE8");
                case RoomSpecialKind.Linked:return LightsOutStyle.Hex("88E2AD");
                case RoomSpecialKind.Generator:return LightsOutStyle.Hex("EAB5FF");
                case RoomSpecialKind.Faulty:return LightsOutStyle.Hex("ED8591");
                default:return Cream;
            }
        }
        void OnExtinguished(VerticalExtinguishEvent e)
        {
            if(e.CashFound)sound.PlayCashDiscovery();
            else if(e.HighBonus)sound.Play(2,1.12f);
            if(Time.unscaledTime-lastSound>.06f){sound.Play(e.FloorCompleted?4:1,Mathf.Min(1.3f,.9f+Model.Combo*.012f));lastSound=Time.unscaledTime;}
            feedback?.OnExtinguished(e);
            if(!floating.gameObject.activeSelf||e.AffectedIds.Length==0)return;
            int id=e.AffectedIds[0],floor=id/Model.FloorRooms,col=id%Model.FloorRooms;
            Vector3 at=new Vector3(-Model.HalfWidth+(col+.5f)*Model.RoomWidth,floor*LightsOutMover.FloorHeight+.9f,0);
            Vector3 vp=worldCamera.WorldToViewportPoint(at);if(vp.y<0||vp.y>1)return;
            RectTransform vr=viewport.rectTransform;
            var r=UIRect(floating,"Reward",vr.anchoredPosition.x+vp.x*vr.rect.width-55,-vr.anchoredPosition.y+(1-vp.y)*vr.rect.height-20,180,36);
            var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=20;t.fontStyle=FontStyles.Bold;t.color=Mint;t.text="+ "+LightsOutStyle.Format(e.Reward);t.raycastTarget=false;
            StartCoroutine(FloatReward(r,t));
        }
        IEnumerator FloatReward(RectTransform r,TMP_Text t)
        {Vector2 p=r.anchoredPosition;for(float a=0;a<1;a+=Time.unscaledDeltaTime*1.5f){if(!r)yield break;r.anchoredPosition=p+Vector2.up*a*40;t.alpha=1-a;yield return null;}if(r)Destroy(r.gameObject);}
        public void OpenSkillTree()
        {
            if(rentPanel.IsRouletteOpen)return;
            if(Career.Phase==RentPhase.Checkout||Career.Phase==RentPhase.Bankrupt){Notify("먼저 퇴근 수입을 정산하세요.");return;}
            rentPanel.Hide();lobby.Hide();lobbyShowing=false;accessoriesShowing=false;modal.gameObject.SetActive(false);SetPaused(true);treeShowing=true;tree.Show();Refresh();
        }
        public void CloseSkillTree()
        {
            if(rentPanel.IsRouletteOpen){rentPanel.TryCloseRoulette();return;}
            tree.Hide();treeShowing=false;modal.gameObject.SetActive(false);
            if(Career.Phase==RentPhase.Lobby)ShowLobby();
            else if(Career.Phase==RentPhase.Working){rentPanel.Hide();SetPaused(false);}
            else ShowCheckout();
            Refresh();
        }
        void HandlePause()
        {
            if(rentPanel.IsRouletteOpen){rentPanel.TryCloseRoulette();return;}
            if(accessoriesShowing){CloseAccessoryView();return;}
            if(treeShowing){CloseSkillTree();return;}
            if(Career.Phase==RentPhase.Lobby){ShowLobby();return;}
            if(Career.Phase!=RentPhase.Working){ShowCheckout();return;}
            if(IsPaused)CloseSkillTree();else ShowSettings();
        }
        void ShowCheckout()
        {player.CancelWork();workGauge?.Hide();if(riseEffect)riseEffect.enabled=false;tree.Hide();treeShowing=false;lobby.Hide();lobbyShowing=false;accessoriesShowing=false;modal.gameObject.SetActive(false);SetPaused(true);rentPanel.Show();Refresh();}
        public void SettleAndOpenSkills()
        {
            if(rentPanel.IsRouletteOpen)return;
            if(Career.Phase==RentPhase.Checkout&&!KeepShiftIncome())return;
            if(Career.Phase==RentPhase.Bankrupt){ShowCheckout();return;}
            OpenSkillTree();
        }
        public bool SettleAndPayRent()
        {
            if(rentPanel.IsRouletteOpen)return false;
            if(Career.Phase==RentPhase.Checkout)
            {
                if(Model.Money+Career.LastShiftIncome<Career.RentDue||!KeepShiftIncome())return false;
            }
            return PayRent();
        }
        public bool SettleAndStartNextShift()
        {
            if(rentPanel.IsRouletteOpen)return false;
            if(Career.Phase==RentPhase.Checkout&&!KeepShiftIncome())return false;
            if(Career.Phase==RentPhase.Bankrupt){ShowCheckout();return false;}
            return StartNextShift();
        }
        public void OpenAccessoriesFromCheckout()
        {
            if(rentPanel.IsRouletteOpen||Career.Phase==RentPhase.Working||Career.Phase==RentPhase.Lobby)return;
            tree.Hide();treeShowing=false;rentPanel.Hide();lobbyShowing=false;accessoriesShowing=true;SetPaused(true);lobby.Show(true);
        }
        public void CloseAccessoryView(){if(accessoriesShowing)ShowCheckout();}
        public bool KeepShiftIncome()
        {
            if(rentPanel.IsRouletteOpen||!Career.SkipRoulette())return false;
            lastPhase=Career.Phase;Save();Refresh();rentPanel.Show();return true;
        }
        public bool RollShiftIncome()
        {
            if(rentPanel.IsSpinning||!Career.SpinRoulette())return false;
            // Persist the draw before the wheel animates; reloading cannot offer a second draw.
            lastPhase=Career.Phase;Save();return true;
        }
        public bool PayRent()
        {
            if(rentPanel.IsRouletteOpen||!Career.PayRent())return false;
            lastPhase=Career.Phase;Save();ShowLobby();return true;
        }
        public bool StartNextShift()
        {if(rentPanel.IsRouletteOpen||!Career.StartNextAttempt())return false;BeginWorkUi();return true;}
        public void RestartCareer()
        {if(rentPanel.IsRouletteOpen)return;Career.Reset();player.CancelWork();lastPhase=Career.Phase;Save();ShowLobby();}
        void BeginWorkUi()
        {
            lastPhase=Career.Phase;tree.Hide();treeShowing=false;rentPanel.Hide();lobby.Hide();lobbyShowing=false;accessoriesShowing=false;modal.gameObject.SetActive(false);
            player.ResetMotion(StartingPosition());if(riseEffect)riseEffect.enabled=true;CameraRig.Snap(player.Position,Model);SetPaused(false);saveTimer=0;Save();Refresh();
        }
        public bool CanConfigureAccessories => lobbyShowing&&Career.Phase==RentPhase.Lobby&&!rentPanel.IsRouletteOpen;
        public bool UpgradeAccessory(int id)
        {if(!CanConfigureAccessories||!Permanent.Upgrade(id))return false;Save();lobby.Refresh();return true;}
        public bool ToggleAccessory(int id)
        {if(!CanConfigureAccessories||!Permanent.ToggleEquip(id))return false;Save();lobby.Refresh();return true;}
        public bool StartLobbyShift()
        {if(!CanConfigureAccessories||!Career.StartReadyWork())return false;BeginWorkUi();return true;}
        void ShowLobby()
        {player.CancelWork();workGauge?.Hide();if(riseEffect)riseEffect.enabled=false;tree.Hide();treeShowing=false;rentPanel.Hide();modal.gameObject.SetActive(false);lobbyShowing=true;accessoriesShowing=false;SetPaused(true);lobby.Show();Refresh();}
        void ShowCareerIntro()
        {
            Modal("오늘도 밀린 월세 때문에 야근입니다.","건물의 불을 끄고 성과급을 모아 월세를 갚으세요.");
            UIText(modal,"Story","단계마다 근무 기회는 5번입니다.\n모든 불을 끄거나 제한 시간이 끝나면 기회가 줄어듭니다.\n\n월세를 먼저 내고 잔액을 가지고 다음 단계로 갈 수도,\n강화에 투자하며 남은 기회까지 일할 수도 있습니다.\n\n퇴근 수입은 그대로 받거나 룰렛에 걸 수 있습니다.\n×2 10칸 · 전액 손실 10칸 · ×10 1칸\n5번째 정산까지 월세가 부족하면 1단계부터 다시 시작합니다.",380,268,840,280,21,Cream);
            ModalButton("마트 첫 근무 · 3층 / 30초 →",580,CloseSkillTree,Amber);
        }
        void SetPaused(bool paused){IsPaused=paused;player.SetInputEnabled(!paused);}
        void ShowSettings()
        {
            if(rentPanel.IsRouletteOpen)return;
            Modal("잠깐 쉬어가기","이동과 자동화가 일시 정지됩니다.");
            ModalButton("계속 근무하기",270,CloseSkillTree,Amber);
            ModalButton("스킬 트리 보기",334,OpenSkillTree,LightsOutStyle.Hex("20384E"));
            bool audible=PlayerPrefs.GetFloat("LightsOut.Volume",.6f)>0;
            ModalButton(audible?"소리 끄기":"소리 켜기",398,()=>{float v=audible?0:.6f;PlayerPrefs.SetFloat("LightsOut.Volume",v);sound.SetVolume(v);ShowSettings();},LightsOutStyle.Hex("20384E"));
            ModalButton("저장하고 계속하기",462,()=>{Save();CloseSkillTree();Notify("저장했습니다.");},LightsOutStyle.Hex("20384E"));
            ModalButton("처음부터 새 근무",526,ConfirmReset,LightsOutStyle.Hex("603E4A"));
        }
        void ShowHelp()
        {
            if(rentPanel.IsRouletteOpen)return;
            Modal("관리인 업무 안내","켜진 방에 머물러 작업 게이지를 채우세요.");
            UIText(modal,"Instructions","WASD / 방향키: 자유 이동 · Space: 해금 후 대시\nR: 후반 수직 돌파 · T: 스킬 트리 · ESC: 일시 정지\n\n기본 소등 1초, 강화 후에도 최소 0.15초가 필요합니다.\n즉시 소등은 방마다 첫 진입에 한 번 추첨합니다.\n방을 떠난 진행도는 기본 1.5초 동안 보존됩니다.\n근무 시작 시 기회 1회 사용 · DAY마다 총 5회.\n수입은 퇴근 정산하며 기존 보유금은 룰렛에서 보호됩니다.\n\n월세 차감 후 잔액과 일반 스킬을 다음 DAY로 이월합니다.\n5회 안에 못 갚으면 현금과 일반 스킬이 초기화됩니다.\n영구 레벨, 특수 방 해금, 액세서리는 유지됩니다.",380,265,840,300,20,Cream);
            ModalButton("근무 시작 →",580,CloseSkillTree,Amber);
        }
        void ConfirmReset()
        {
            Modal("1단계부터 다시 시작할까요?","월세 단계, 보유금, 스킬과 소등 기록이 초기화됩니다.");
            ModalButton("돌아가기",340,ShowSettings,Amber);
            ModalButton("초기화하고 시작",406,RestartCareer,LightsOutStyle.Hex("7C4652"));
        }
        void Modal(string title,string subtitle)
        {
            SetPaused(true);tree.Hide();treeShowing=false;rentPanel?.Hide();for(int i=modal.childCount-1;i>=0;i--){var o=modal.GetChild(i).gameObject;o.SetActive(false);Destroy(o);}modal.gameObject.SetActive(true);
            UIShape(modal,"Shade",0,0,1600,900,new Color(.015f,.035f,.06f,.93f),0,true);
            UIShape(modal,"Dialog",340,130,920,620,LightsOutStyle.Hex("122437"),24);
            UIText(modal,"Title",title,380,158,840,60,34,Amber);
            UIText(modal,"Subtitle",subtitle,380,223,840,40,19,Muted);
        }
        void ModalButton(string text,float y,UnityEngine.Events.UnityAction click,Color bg)
        {
            var r=UIShape(modal,"Action",380,y,840,50,bg,10,true);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=r;b.onClick.AddListener(click);
            var t=UIText(r.transform,"Label",text,10,0,820,50,21,bg==Amber?Ink:Cream);t.alignment=TextAlignmentOptions.Center;
        }
        public void Save()
        {
            if(Testing||!Application.isPlaying||Model==null)return;
            var state=new SaveState{career=Career.ExportJson(),workProgress=player.ExportWorkJson(),x=player.Position.x,y=player.Position.y};string json=JsonUtility.ToJson(state);
            string permanentJson=Permanent.ExportJson();
            PlayerPrefs.SetString(SaveKey,json);PlayerPrefs.SetString("LightsOut.Permanent.v1",permanentJson);PlayerPrefs.Save();
            try{File.WriteAllText(Path.Combine(Application.persistentDataPath,"lightsout-rent-save.json"),json);File.WriteAllText(Path.Combine(Application.persistentDataPath,"lightsout-permanent-save.json"),permanentJson);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        void OnApplicationQuit(){Save();}void OnApplicationPause(bool paused){if(paused)Save();}void OnDisable(){Save();}
        void OnDestroy(){pauseAction?.Dispose();treeAction?.Dispose();if(worldCamera)worldCamera.targetTexture=null;if(render){render.Release();Destroy(render);}}
        static void SetText(TMP_Text t,string s){if(t.text!=s)t.text=s;}
        RectTransform UIRect(Transform parent,string name,float x,float y,float w,float h)
        {var g=new GameObject(name,typeof(RectTransform));var r=g.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
        LightsOutShape UIShape(Transform parent,string name,float x,float y,float w,float h,Color color,float radius=0,bool ray=false)
        {var r=UIRect(parent,name,x,y,w,h);var s=r.gameObject.AddComponent<LightsOutShape>();s.radius=radius;s.color=color;s.raycastTarget=ray;return s;}
        TMP_Text UIText(Transform parent,string name,string content,float x,float y,float w,float h,float size,Color color)
        {var r=UIRect(parent,name,x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.color=color;t.text=content;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    }
}
