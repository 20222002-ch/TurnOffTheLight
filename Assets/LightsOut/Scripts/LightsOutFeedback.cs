using TMPro;
using UnityEngine;

namespace LightsOut
{
    /// <summary>Bounded work feedback: pooled money labels, small flashes and a compact combo caption.</summary>
    public sealed class LightsOutFeedback : MonoBehaviour
    {
        const int Popups=24, Particles=48, EventBudget=8;
        struct Popup{public RectTransform Rect;public TMP_Text Text;public float Life;public Vector2 Home;}
        struct Spark{public SpriteRenderer Renderer;public Vector2 Home,Velocity;public float Life;}
        readonly Popup[] popups=new Popup[Popups];readonly Spark[] sparks=new Spark[Particles];
        LightsOutVerticalGame owner;Camera cameraView;RectTransform panel,comboRect;TMP_Text comboText;
        int popupCursor,sparkCursor,eventsThisFrame,lastFrame=-1,previousCombo;float punch;
        static readonly Color Mint=LightsOutStyle.Hex("8DE7C5"),Gold=LightsOutStyle.Hex("F6C96D");
        public int ParticlePoolSize=>Particles;
        public int PopupPoolSize=>Popups;
        public void Initialize(LightsOutVerticalGame game,Sprite sprite,Camera camera)
        {
            owner=game;cameraView=camera;
            panel=new GameObject("GameplayFeedback",typeof(RectTransform)).GetComponent<RectTransform>();panel.SetParent(owner.UIRoot,false);panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;panel.offsetMin=panel.offsetMax=Vector2.zero;
            comboRect=Label("ComboCaption",230,60,out comboText);comboRect.pivot=Vector2.zero;comboRect.anchoredPosition=new Vector2(35,-80);comboText.fontSize=23;comboText.fontStyle=FontStyles.Bold;
            for(int i=0;i<Popups;i++){var r=Label("IncomePopup"+i,210,38,out var t);t.fontSize=19;t.fontStyle=FontStyles.Bold;r.gameObject.SetActive(false);popups[i]=new Popup{Rect=r,Text=t};}
            var root=new GameObject("Extinguish Spark Pool").transform;root.SetParent(transform,false);
            for(int i=0;i<Particles;i++)
            {
                var go=new GameObject("Spark"+i,typeof(SpriteRenderer));go.transform.SetParent(root,false);var sr=go.GetComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=104;sr.enabled=false;sparks[i].Renderer=sr;
            }
        }
        RectTransform Label(string name,float width,float height,out TMP_Text text)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(panel,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(width,height);
            text=r.gameObject.AddComponent<TextMeshProUGUI>();text.font=owner.Font;text.color=Mint;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;return r;
        }
        public void OnExtinguished(VerticalExtinguishEvent e)
        {
            if(!owner||owner.Career.Phase!=RentPhase.Working||e.AffectedIds==null)return;
            if(lastFrame!=Time.frameCount){lastFrame=Time.frameCount;eventsThisFrame=0;}
            int combo=owner.Model.Combo;
            if((previousCombo<10&&combo>=10)||(previousCombo<20&&combo>=20)){punch=1;owner.PlayRouletteCue(2,combo>=20?1.18f:1,.25f);}
            previousCombo=combo;
            if(eventsThisFrame++>=EventBudget)return;
            Color tint=combo>=20?Gold:Mint;bool amountShown=false;
            for(int n=0;n<e.AffectedIds.Length&&n<12;n++)
            {
                int id=e.AffectedIds[n],f=id/owner.Model.FloorRooms,c=id%owner.Model.FloorRooms;
                var at=new Vector3(-owner.Model.HalfWidth+(c+.5f)*owner.Model.RoomWidth,f*LightsOutMover.FloorHeight+.9f,-1);
                Vector3 view=cameraView.WorldToViewportPoint(at);if(view.x<0||view.x>1||view.y<0||view.y>1)continue;
                if(!amountShown)
                {
                    int p=popupCursor++%Popups;var pop=popups[p];pop.Home=new Vector2(view.x*panel.rect.width,-(1-view.y)*panel.rect.height-20);pop.Rect.anchoredPosition=pop.Home;pop.Rect.gameObject.SetActive(true);pop.Text.text="+ "+LightsOutStyle.Format(e.Reward);pop.Text.color=tint;pop.Life=.8f;popups[p]=pop;amountShown=true;
                }
                for(int k=0;k<3;k++)
                {
                    int p=sparkCursor++%Particles;var spark=sparks[p];float a=(k*120+(id*17%90))*Mathf.Deg2Rad;spark.Home=at;spark.Velocity=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*1.2f;spark.Life=.28f;spark.Renderer.enabled=true;spark.Renderer.color=tint;spark.Renderer.transform.position=at;spark.Renderer.transform.localScale=new Vector3(.10f,.10f,1);sparks[p]=spark;
                }
            }
        }
        public void Tick(float dt)
        {
            punch=Mathf.Max(0,punch-dt*3);
            for(int i=0;i<Popups;i++)
            {
                var pop=popups[i];if(pop.Life<=0)continue;pop.Life=Mathf.Max(0,pop.Life-dt);pop.Text.alpha=Mathf.Min(1,pop.Life/.28f);pop.Rect.anchoredPosition=pop.Home+Vector2.up*(.8f-pop.Life)*45;if(pop.Life==0)pop.Rect.gameObject.SetActive(false);popups[i]=pop;
            }
            for(int i=0;i<Particles;i++)
            {
                var spark=sparks[i];if(spark.Life<=0)continue;spark.Life=Mathf.Max(0,spark.Life-dt);Color color=spark.Renderer.color;color.a=spark.Life/.28f;spark.Renderer.color=color;spark.Renderer.transform.position=spark.Home+spark.Velocity*(.28f-spark.Life);if(spark.Life==0)spark.Renderer.enabled=false;sparks[i]=spark;
            }
        }
        public void RefreshCombo()
        {
            if(!owner||!panel)return;
            bool working=owner.Career.Phase==RentPhase.Working&&!owner.IsPaused;
            panel.gameObject.SetActive(working);
            int combo=owner.Model.Combo;comboRect.gameObject.SetActive(working&&combo>0);
            if(combo>0){comboText.text=combo+" COMBO  ·  ×"+owner.Model.ComboRewardMultiplier.ToString("0.0");comboText.color=combo>=20?Gold:Mint;comboRect.localScale=Vector3.one*(1+punch*.17f);}
            if(combo==0)previousCombo=0;
            if(owner.Career.Phase!=RentPhase.Working)
            {
                for(int i=0;i<Popups;i++){popups[i].Life=0;popups[i].Rect.gameObject.SetActive(false);}
                for(int i=0;i<Particles;i++){sparks[i].Life=0;sparks[i].Renderer.enabled=false;}
            }
        }
    }
}
