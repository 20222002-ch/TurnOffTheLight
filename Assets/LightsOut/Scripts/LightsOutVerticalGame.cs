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
        public bool IsPaused { get; private set; }
        public bool Testing { get; set; }
        public int VisibleFloorCount { get; private set; }
        public LightsOutCameraRig CameraRig { get; private set; }
        const string SaveKey = "LightsOut.Movement.v2";
        const int PoolFloors = 48;
        static readonly Color Amber = LightsOutStyle.Hex("F6C96D"), Mint = LightsOutStyle.Hex("8DE7C5"), Muted = LightsOutStyle.Hex("829BB3"), Ink = LightsOutStyle.Hex("091422"), Cream = LightsOutStyle.Hex("F5EFDA");
        [Serializable] sealed class SaveState { public int version = 2; public string model; public float x, y; }
        sealed class RoomView { public SpriteRenderer body, lamp, glow; public Color displayed; }
        sealed class FloorView { public GameObject root; public TMP_Text label; public RoomView[] rooms; public int floor = -1; }
        readonly List<FloorView> floorPool = new List<FloorView>();
        readonly List<TMP_Text> quickNames = new List<TMP_Text>(), quickDescriptions = new List<TMP_Text>(), quickCosts = new List<TMP_Text>();
        readonly List<UnityEngine.UI.Button> quickButtons = new List<UnityEngine.UI.Button>();
        TMP_Text money, zone, shift, progress, combo, total, hint, reward, automation, toast, pageHint, playerLocator;
        RectTransform progressFill, floating, modal;
        UnityEngine.UI.Button dashButton;
        LightsOutSkillTree tree;
        LightsOutAudio sound;
        InputAction pauseAction, treeAction;
        RenderTexture render;
        Transform skyline;
        float saveTimer, refreshTimer, toastTimer, lastSound;
        int quickPage;
        bool wasComplete;
        string saveNotice;

        void Awake()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Model = new LightsOutVerticalModel();
            CacheUI();
            Vector2 start = new Vector2(-3.6f, .9f);
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    SaveState saved = JsonUtility.FromJson<SaveState>(json);
                    if (saved != null && saved.version == 2 && Model.ImportJson(saved.model))
                    {
                        float x = float.IsNaN(saved.x) || float.IsInfinity(saved.x) ? -3.6f : saved.x;
                        float y = float.IsNaN(saved.y) || float.IsInfinity(saved.y) ? .9f : saved.y;
                        start = new Vector2(Mathf.Clamp(x,-4.55f,4.55f), Mathf.Clamp(y,.22f,Model.FloorCount*LightsOutMover.FloorHeight-.22f));
                    }
                    else saveNotice = "이동 버전의 저장 데이터가 유효하지 않아 새 근무를 시작합니다.";
                }
                catch (ArgumentException) { saveNotice = "저장 데이터를 읽지 못해 새 근무를 시작합니다."; }
            }
            sound = gameObject.AddComponent<LightsOutAudio>(); sound.Initialize(effects, music); sound.SetVolume(PlayerPrefs.GetFloat("LightsOut.Volume", .6f));
            SetUpWorld(start);
            Model.Changed += Refresh;
            Model.Extinguished += OnExtinguished;
            tree = gameObject.AddComponent<LightsOutSkillTree>(); tree.Initialize(this);
            BindUI();
            playerLocator=UIText(floating,"Player Locator","관리인",0,0,86,28,14,Mint);
            playerLocator.alignment=TextAlignmentOptions.Center;
            pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pauseAction.performed += _ => { if (IsPaused) CloseSkillTree(); else ShowSettings(); }; pauseAction.Enable();
            treeAction = new InputAction("Skill Tree", InputActionType.Button, "<Keyboard>/t");
            treeAction.performed += _ => { if (IsPaused) CloseSkillTree(); else OpenSkillTree(); }; treeAction.Enable();
            // A completed save must reopen the finish screen so the next building stays reachable.
            wasComplete = false;
            Refresh();
            if (!string.IsNullOrEmpty(saveNotice)) Notify(saveNotice);
        }
        void CacheUI()
        {
            TMP_Text T(string name) => canvasRoot.Find(name).GetComponent<TMP_Text>();
            money=T("Money"); zone=T("Zone"); shift=T("Shift"); progress=T("Progress"); combo=T("Combo"); total=T("Total"); hint=T("Hint");
            reward=T("RewardPerLight"); automation=T("Automation"); toast=T("Toast"); pageHint=T("PageHint");
            progressFill=canvasRoot.Find("ProgressFill").GetComponent<RectTransform>();
            floating=canvasRoot.Find("FloatingRewards").GetComponent<RectTransform>(); modal=canvasRoot.Find("Modal").GetComponent<RectTransform>();
            modal.gameObject.SetActive(false);
            T("Subtitle").text="WASD / 방향키로 이동   ·   닿으면 소등   ·   T 스킬 트리";
            T("UpgradeTitle").text="더 높이, 더 빠르게";
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
            Bind("AdvanceButton",()=> { if(!IsPaused) player.StepInput(player.Direction,.016f,true); });
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
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            if(!IsPaused)
            {
                player.Step(dt); Model.Tick(dt); CameraRig.Tick(player.Position,Model,dt);
                saveTimer+=dt; if(saveTimer>=8){Save();saveTimer=0;}
            }
            RenderRooms(dt);
            refreshTimer+=dt; if(refreshTimer>=.12f){Refresh();refreshTimer=0;}
            toastTimer-=dt; toast.gameObject.SetActive(toastTimer>0);
            if(Model.IsComplete&&!wasComplete){sound.Play(5);Save();ShowFinish();} wasComplete=Model.IsComplete;
        }
        public bool BuySkill(int id)
        {
            if(Model.BuySkill(id)){sound.Play(3);Notify(LightsOutVerticalModel.Skills[id].Name+" 강화 완료");Save();Refresh();return true;}
            Notify(Model.GetLockReason(id)); return false;
        }
        public void Notify(string text) { toast.text=text; toastTimer=3; toast.gameObject.SetActive(true); }
        void Refresh()
        {
            if(Model==null||money==null) return;
            int standing=Mathf.Clamp(Mathf.FloorToInt(player.Position.y/LightsOutMover.FloorHeight)+1,1,Model.FloorCount);
            SetText(money,LightsOutStyle.Format(Model.Money)); SetText(zone,Model.StageName);
            SetText(shift,"NIGHT SHIFT "+Model.RunNumber.ToString("00")+"  /  "+(Model.StageIndex+1)+"단계");
            SetText(progress,"현재 "+standing+"층  ·  완료 "+Model.ClearedFloors+"층  ·  다음 관리층 "+(Model.CurrentFloor+1)+"층");
            progressFill.sizeDelta=new Vector2(1000*Mathf.Clamp01(Model.ClearedFloors/(float)Model.FloorCount),5);
            SetText(combo,Model.SurgeRemaining>0?"초고속 돌파 "+Model.SurgeRemaining.ToString("0.0")+"s":Model.BlackoutCharge>0?"BLACKOUT "+Model.BlackoutCharge+" / 30":"MOVE & LIGHTS OUT");
            SetText(total,"누적 소등 "+Model.TotalOff.ToString("N0")+"개");
            SetText(reward,"이동속도 "+Model.MoveSpeed.ToString("0.0")+"  ·  소등 범위 "+Model.ExtinguishRadius.ToString("0.0"));
            SetText(automation,Model.AutomationRate>0?"자동 소등 "+Model.AutomationRate.ToString("0.0")+"개 / 초":"WASD · 방향키로 방을 지나가세요");
            dashButton.interactable=Model.DashUnlocked&&!IsPaused&&player.DashCooldown<=0;
            SetText(dashButton.GetComponentInChildren<TMP_Text>(),!Model.DashUnlocked?"대시를 해금하면 Space로 돌파":player.DashCooldown>0?"대시 준비 "+player.DashCooldown.ToString("0.0")+"초":"대시  SPACE →");
            SetText(pageHint,quickPage==0?"이동하면서 성장하세요":"위층으로 퍼지는 소등과 자동화");
            SetText(hint,"방에 닿으면 자동 소등 · 꺼진 방은 중복 보상 없음 · 화면 밖 상태도 유지됩니다.");
            for(int slot=0;slot<4;slot++)
            {
                int id=QuickId(slot),level=Model.SkillLevels[id];var s=LightsOutVerticalModel.Skills[id];
                SetText(quickNames[slot],s.Name+"  <color=#829BB3>"+level+" / "+s.MaxLevel+"</color>");
                SetText(quickDescriptions[slot],Model.GetNextEffect(id));
                string locked=Model.GetLockReason(id);bool max=level>=s.MaxLevel;
                SetText(quickCosts[slot],max?"강화 완료":s.PrerequisiteId>=0&&Model.SkillLevels[s.PrerequisiteId]<s.PrerequisiteLevel?"선행 스킬 필요":LightsOutStyle.Format(Model.GetSkillCost(id))+"  +");
                quickButtons[slot].interactable=Model.CanBuySkill(id)&&!IsPaused;
                quickCosts[slot].color=Model.CanBuySkill(id)?Amber:Muted;
            }
        }
        void SetUpWorld(Vector2 start)
        {
            render=new RenderTexture(1533,588,24,RenderTextureFormat.ARGB32){name="LightsOut Building View",antiAliasing=4}; render.Create();
            viewport.texture=render; viewport.raycastTarget=false; worldCamera.targetTexture=render;
            worldCamera.orthographic=true; worldCamera.backgroundColor=Ink;worldCamera.clearFlags=CameraClearFlags.SolidColor;
            CameraRig=worldCamera.GetComponent<LightsOutCameraRig>();if(!CameraRig)CameraRig=worldCamera.gameObject.AddComponent<LightsOutCameraRig>();CameraRig.WorldCamera=worldCamera;
            if(!player)
            {
                GameObject p=playerPrefab?Instantiate(playerPrefab,transform):new GameObject("Player");
                if(!playerPrefab){var sr=p.AddComponent<SpriteRenderer>();sr.sprite=primitive;sr.color=Mint;p.transform.localScale=Vector3.one*.38f;p.AddComponent<LightsOutMover>();}
                player=p.GetComponent<LightsOutMover>();
            }
            player.transform.position=new Vector3(start.x,start.y,-1);player.Initialize(Model);CameraRig.Snap(start);
            var root=new GameObject("Visible Building Floors");root.transform.SetParent(transform,false);
            for(int i=0;i<PoolFloors;i++)
            {
                var row=new GameObject("Pooled Floor "+i);row.transform.SetParent(root.transform,false);
                var view=new FloorView{root=row,rooms=new RoomView[Model.FloorRooms]};
                var label=new GameObject("Floor Number",typeof(TextMeshPro));label.transform.SetParent(row.transform,false);label.transform.localPosition=new Vector3(-5.45f,.88f,-.5f);
                view.label=label.GetComponent<TextMeshPro>();view.label.font=font;view.label.fontSize=3.2f;view.label.color=Muted;view.label.alignment=TextAlignmentOptions.Right;view.label.rectTransform.sizeDelta=new Vector2(1,.8f);view.label.textWrappingMode=TextWrappingModes.NoWrap;
                for(int c=0;c<Model.FloorRooms;c++)
                {
                    float x=-Model.FloorRooms*LightsOutMover.RoomWidth*.5f+(c+.5f)*LightsOutMover.RoomWidth;
                    var rv=new RoomView();
                    rv.body=WorldRect(row.transform,"Room "+c,new Vector3(x,.9f,0),new Vector2(2.22f,1.60f),Amber);
                    rv.glow=WorldRect(row.transform,"Soft Light "+c,new Vector3(x,.92f,-.05f),new Vector2(1.82f,1.20f),new Color(1,.85f,.45f,.12f));
                    rv.lamp=WorldRect(row.transform,"Lamp "+c,new Vector3(x,1.47f,-.1f),new Vector2(.42f,.09f),Cream);
                    view.rooms[c]=rv;rv.displayed=Amber;
                }
                floorPool.Add(view);row.SetActive(false);
            }
            var bg=new GameObject("Night Skyline");bg.transform.SetParent(transform,false);skyline=bg.transform;
            for(int i=0;i<16;i++)
            {
                float x=-22+i*3,height=3+(i*7%12);
                WorldRect(bg.transform,"Tower "+i,new Vector3(x,height*.5f,2),new Vector2(2.6f,height),LightsOutStyle.Hex("132C40"));
                for(int j=0;j<5;j++) WorldRect(bg.transform,"Distant Window",new Vector3(x+(j%2==0?-.5f:.5f),1+j*1.1f,1.9f),new Vector2(.14f,.20f),new Color(.9f,.62f,.25f,.45f));
            }
            RenderRooms(1);
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
            int available=Mathf.Min(Model.FloorCount-1,Mathf.Max(11,Model.CurrentFloor+12));
            int last=Mathf.Min(available,Mathf.CeilToInt(top/LightsOutMover.FloorHeight)+1);VisibleFloorCount=Mathf.Max(0,last-first+1);
            for(int i=0;i<floorPool.Count;i++)
            {
                var v=floorPool[i];int floor=first+i;bool active=floor<=last;
                if(v.root.activeSelf!=active)v.root.SetActive(active);if(!active)continue;
                bool reused=v.floor!=floor;v.floor=floor;v.root.transform.position=new Vector3(0,floor*LightsOutMover.FloorHeight,0);v.label.text=(floor+1).ToString("000")+"F";
                for(int c=0;c<v.rooms.Length;c++)
                {
                    var rv=v.rooms[c];bool on=Model.IsLightOn(floor,c);Color target=on?Amber:LightsOutStyle.Hex("1C3145");
                    rv.displayed=reused?target:Color.Lerp(rv.displayed,target,1-Mathf.Exp(-dt*16));rv.body.color=rv.displayed;
                    rv.lamp.color=on?Cream:LightsOutStyle.Hex("405D72");rv.glow.color=on?new Color(1,.85f,.45f,.12f):Color.clear;
                }
            }
            if(skyline)skyline.position=new Vector3(0,bottom,0);
            if(playerLocator)
            {
                Vector3 vp=worldCamera.WorldToViewportPoint(player.transform.position);
                playerLocator.gameObject.SetActive(vp.y>=0&&vp.y<=1&&vp.x>=0&&vp.x<=1);
                var vr=viewport.rectTransform;
                playerLocator.rectTransform.anchoredPosition=new Vector2(vr.anchoredPosition.x+vp.x*vr.rect.width-43,vr.anchoredPosition.y-(1-vp.y)*vr.rect.height+52);
            }
        }
        void OnExtinguished(VerticalExtinguishEvent e)
        {
            if(Time.unscaledTime-lastSound>.06f){sound.Play(e.FloorCompleted?4:1,e.Automated ? .8f : 1f);lastSound=Time.unscaledTime;}
            if(e.AffectedIds.Length==0)return;
            int id=e.AffectedIds[0],floor=id/Model.FloorRooms,col=id%Model.FloorRooms;
            Vector3 at=new Vector3(-Model.FloorRooms*LightsOutMover.RoomWidth*.5f+(col+.5f)*LightsOutMover.RoomWidth,floor*LightsOutMover.FloorHeight+.9f,0);
            Vector3 vp=worldCamera.WorldToViewportPoint(at);if(vp.y<0||vp.y>1)return;
            RectTransform vr=viewport.rectTransform;
            var r=UIRect(floating,"Reward",vr.anchoredPosition.x+vp.x*vr.rect.width-55,-vr.anchoredPosition.y+(1-vp.y)*vr.rect.height-20,180,36);
            var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=20;t.fontStyle=FontStyles.Bold;t.color=Mint;t.text="+ "+LightsOutStyle.Format(e.Reward);t.raycastTarget=false;
            StartCoroutine(FloatReward(r,t));
        }
        IEnumerator FloatReward(RectTransform r,TMP_Text t)
        {Vector2 p=r.anchoredPosition;for(float a=0;a<1;a+=Time.unscaledDeltaTime*1.5f){if(!r)yield break;r.anchoredPosition=p+Vector2.up*a*40;t.alpha=1-a;yield return null;}if(r)Destroy(r.gameObject);}
        public void OpenSkillTree(){if(IsPaused)return;SetPaused(true);tree.Show();}
        public void CloseSkillTree(){tree.Hide();modal.gameObject.SetActive(false);SetPaused(false);Refresh();}
        void SetPaused(bool paused){IsPaused=paused;player.SetInputEnabled(!paused);}
        void ShowSettings()
        {
            Modal("잠깐 쉬어가기","이동과 자동화가 일시 정지됩니다.");
            ModalButton("계속 근무하기",270,CloseSkillTree,Amber);
            ModalButton("스킬 트리 보기",334,()=>{modal.gameObject.SetActive(false);tree.Show();},LightsOutStyle.Hex("20384E"));
            bool audible=PlayerPrefs.GetFloat("LightsOut.Volume",.6f)>0;
            ModalButton(audible?"소리 끄기":"소리 켜기",398,()=>{float v=audible?0:.6f;PlayerPrefs.SetFloat("LightsOut.Volume",v);sound.SetVolume(v);ShowSettings();},LightsOutStyle.Hex("20384E"));
            ModalButton("저장하고 계속하기",462,()=>{Save();CloseSkillTree();Notify("저장했습니다.");},LightsOutStyle.Hex("20384E"));
            ModalButton("처음부터 새 근무",526,ConfirmReset,LightsOutStyle.Hex("603E4A"));
        }
        void ShowHelp()
        {
            Modal("관리인 업무 안내","방을 지나가는 것만으로 불이 꺼집니다.");
            UIText(modal,"Instructions","WASD / 방향키: 상하좌우 자유 이동\n대각선도 같은 속도 · 중력, 점프, 벽 충돌 없음\n\n방에 닿으면 자동 소등하고 성과급을 받습니다.\nT: 스킬 트리 · ESC: 쉬어가기 · Space: 해금 후 대시\n\n이동 → 연쇄 → 수직 소등 → 자동화로 성장하세요.\n스킬 수치는 검증을 위한 초기 밸런스입니다.",380,265,840,280,22,Cream);
            ModalButton("근무 시작 →",580,CloseSkillTree,Amber);
        }
        void ConfirmReset()
        {
            Modal("새 근무를 시작할까요?","이동 버전의 소등 기록과 스킬이 초기화됩니다.");
            ModalButton("돌아가기",340,ShowSettings,Amber);
            ModalButton("초기화하고 시작",406,()=>{Model.Reset();player.ResetMotion(new Vector2(-3.6f,.9f));CameraRig.Snap(player.Position);Save();wasComplete=false;CloseSkillTree();},LightsOutStyle.Hex("7C4652"));
        }
        void ShowFinish()
        {
            Modal("마지막 층까지 불을 껐습니다.","도시가 잠들었습니다. 오늘도 수고했어요.");
            UIText(modal,"Record","완료한 층 "+Model.ClearedFloors+"\n누적 소등 "+Model.TotalOff.ToString("N0")+"개\n총 성과급 "+LightsOutStyle.Format(Model.TotalEarned),380,310,840,170,28,Mint);
            ModalButton("강화를 유지하고 다음 건물로 →",550,()=>{Model.StartNextRun();player.ResetMotion(new Vector2(-3.6f,.9f));CameraRig.Snap(player.Position);Save();CloseSkillTree();},Amber);
        }
        void Modal(string title,string subtitle)
        {
            SetPaused(true);tree.Hide();for(int i=modal.childCount-1;i>=0;i--){var o=modal.GetChild(i).gameObject;o.SetActive(false);Destroy(o);}modal.gameObject.SetActive(true);
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
            var state=new SaveState{model=Model.ExportJson(),x=player.Position.x,y=player.Position.y};string json=JsonUtility.ToJson(state);
            PlayerPrefs.SetString(SaveKey,json);PlayerPrefs.Save();
            try{File.WriteAllText(Path.Combine(Application.persistentDataPath,"lightsout-movement-save.json"),json);}catch(IOException){}catch(UnauthorizedAccessException){}
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
