using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LightsOut.Editor
{
    public static class LightsOutMovementSetup
    {
        [MenuItem("Lights Out/Open Game Scene")]
        public static void OpenGameScene() { EditorSceneManager.OpenScene("Assets/LightsOut/Scenes/LightsOut.unity"); }
        [MenuItem("Lights Out/Convert existing scene to movement")]
        public static void ConvertFromMenu() { Debug.Log(Convert()); }
        public static string Convert()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!="Assets/LightsOut/Scenes/LightsOut.unity")
                throw new InvalidOperationException("Open the existing LightsOut scene before converting it.");
            var root=GameObject.Find("LightsOut Game");
            if(!root)throw new InvalidOperationException("Existing game root missing. No scene was replaced.");
            var game=root.GetComponent<LightsOutVerticalGame>();
            if(!game) game=root.AddComponent<LightsOutVerticalGame>();
            if(!game.font)game.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/LightsOut/Fonts/LightsOut-Korean-SDF.asset");
            if(game.effects==null||game.effects.Length==0)game.effects=new[]{"01_SwitchClick","02_LightOff","03_GoldenReward","04_Upgrade","05_ZoneUnlock","06_ShiftComplete"}.Select(x=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/LightsOut/Audio/"+x+".wav")).ToArray();
            if(!game.music)game.music=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/LightsOut/Audio/NightOfficeLoop.wav");
            game.canvasRoot=root.transform.Find("LightsOutCanvas").GetComponent<RectTransform>();
            game.worldCamera=Camera.main;
            if(!game.worldCamera)throw new InvalidOperationException("Existing camera missing.");
            Sprite primitive=CreatePrimitive();game.primitive=primitive;
            string prefabPath="Assets/LightsOut/Prefabs/Player.prefab";
            Directory.CreateDirectory("Assets/LightsOut/Prefabs");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if(!prefab)
            {
                var p=new GameObject("Player",typeof(SpriteRenderer),typeof(LightsOutMover));
                var sr=p.GetComponent<SpriteRenderer>();sr.sprite=primitive;sr.color=LightsOutStyle.Hex("8DE7C5");sr.sortingOrder=100;
                p.transform.localScale=Vector3.one*.42f;
                var border=new GameObject("Outline",typeof(SpriteRenderer));border.transform.SetParent(p.transform,false);border.transform.localScale=Vector3.one*1.22f;
                var br=border.GetComponent<SpriteRenderer>();br.sprite=primitive;br.color=LightsOutStyle.Hex("F7E8B9");br.sortingOrder=99;
                var direction=new GameObject("Direction",typeof(SpriteRenderer));direction.transform.SetParent(p.transform,false);direction.transform.localPosition=Vector3.up*.8f;direction.transform.localScale=new Vector3(.19f,.35f,1);
                var dr=direction.GetComponent<SpriteRenderer>();dr.sprite=primitive;dr.color=Color.white;dr.sortingOrder=101;
                prefab=PrefabUtility.SaveAsPrefabAsset(p,prefabPath);UnityEngine.Object.DestroyImmediate(p);
            }
            game.playerPrefab=prefab;
            if(!game.player)
            {
                var p=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
                p.name="Player";p.transform.position=new Vector3(-3.6f,.9f,-1);game.player=p.GetComponent<LightsOutMover>();
            }
            Transform building=game.canvasRoot.Find("Building");
            if(!building.GetComponent<UnityEngine.UI.RawImage>())
            {
                // Only replace the old clickable room views; the surrounding authored HUD remains intact.
                for(int i=building.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(building.GetChild(i).gameObject);
                game.viewport=building.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            }
            else game.viewport=building.GetComponent<UnityEngine.UI.RawImage>();
            game.viewport.raycastTarget=false;game.viewport.color=Color.white;
            EditorUtility.SetDirty(game);
            game.worldCamera.orthographic=true;game.worldCamera.orthographicSize=3.5f;game.worldCamera.transform.position=new Vector3(0,3.5f,-10);
            if(!game.worldCamera.GetComponent<LightsOutCameraRig>())game.worldCamera.gameObject.AddComponent<LightsOutCameraRig>();
            game.worldCamera.GetComponent<LightsOutCameraRig>().WorldCamera=game.worldCamera;
            game.canvasRoot.Find("Subtitle").GetComponent<TMP_Text>().text="WASD / 방향키로 이동 · 닿으면 소등 · T 스킬 트리";
            game.canvasRoot.Find("UpgradeTitle").GetComponent<TMP_Text>().text="더 높이, 더 빠르게";
            foreach(var button in game.canvasRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true))button.onClick.RemoveAllListeners();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "Converted existing scene; reused HUD, fonts, audio, camera, and EventSystem. Player prefab assigned, old clickable room views removed.";
        }
        static Sprite CreatePrimitive()
        {
            const string path="Assets/LightsOut/Art/RoomPrimitive.asset";
            var sprite=File.Exists(path)?AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault():null;
            if(sprite)return sprite;
            Directory.CreateDirectory("Assets/LightsOut/Art");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="White Geometry",filterMode=FilterMode.Point};
            texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();
            AssetDatabase.CreateAsset(texture,path);
            sprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),2);sprite.name="Room Primitive";
            AssetDatabase.AddObjectToAsset(sprite,texture);AssetDatabase.SaveAssets();return sprite;
        }
    }
}
