using UnityEngine;

namespace LightsOut
{
    public sealed class LightsOutCameraRig : MonoBehaviour
    {
        public Camera WorldCamera;
        public float ZoomAdjustment { get; private set; }
        public float FollowHeight => transform.position.y;
        Vector3 velocity;
        public void ChangeZoom(float delta) { ZoomAdjustment = Mathf.Clamp(ZoomAdjustment + delta, -2, 8); }
        public void Tick(Vector2 player, LightsOutVerticalModel model, float dt, bool rising = false)
        {
            if (!WorldCamera || model == null || dt <= 0) return;
            float targetZoom = TargetZoom(player, model);
            WorldCamera.orthographicSize = Mathf.Lerp(WorldCamera.orthographicSize, targetZoom, 1 - Mathf.Exp(-dt * 2));
            Vector3 target = FollowTarget(player, model);
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, (rising ? .06f : .14f) / model.CameraSpeedMultiplier, 250, dt);
        }
        public void Snap(Vector2 player)
        {
            if (!WorldCamera) return;
            WorldCamera.orthographicSize = 3.5f;
            transform.position = new Vector3(0, Mathf.Max(3.5f, player.y), -10); velocity = Vector3.zero;
        }
        public void Snap(Vector2 player, LightsOutVerticalModel model)
        {
            if (model == null) { Snap(player); return; }
            if (!WorldCamera) return;
            WorldCamera.orthographicSize = TargetZoom(player, model);
            transform.position = FollowTarget(player, model);
            velocity = Vector3.zero;
        }
        float WidthZoom(LightsOutVerticalModel model)
        {
            float aspect = WorldCamera.aspect;
            if (aspect <= 0 || float.IsNaN(aspect) || float.IsInfinity(aspect)) aspect = 16f / 9;
            return (model.BuildingWidth + 2.2f) / (2 * aspect);
        }
        float TargetZoom(Vector2 player, LightsOutVerticalModel model)
        {
            float floor = player.y / LightsOutMover.FloorHeight;
            if (model.FloorCount <= 8) return Mathf.Clamp(5.1f + ZoomAdjustment, 4.7f, 5.8f);
            // Keep only a useful section of a wide shop on screen, then naturally fit narrow skyscrapers.
            float growth = Mathf.Clamp(3.8f + Mathf.Sqrt(Mathf.Max(0, floor)) * .6f + ZoomAdjustment, 3.8f, 22);
            return Mathf.Max(growth, Mathf.Min(WidthZoom(model), 5.5f));
        }
        Vector3 FollowTarget(Vector2 player, LightsOutVerticalModel model)
        {
            float aspect = WorldCamera.aspect > 0 ? WorldCamera.aspect : 16f / 9;
            float halfScreenWidth = WorldCamera.orthographicSize * aspect;
            float xExtent = Mathf.Max(0, model.HalfWidth + .7f - halfScreenWidth);
            float x = Mathf.Clamp(player.x, -xExtent, xExtent);
            float buildingHeight = model.FloorCount * LightsOutMover.FloorHeight;
            float y = buildingHeight < WorldCamera.orthographicSize * 2 - .8f ? buildingHeight * .5f + .3f
                : Mathf.Clamp(player.y + WorldCamera.orthographicSize * .16f, WorldCamera.orthographicSize - .3f, buildingHeight - WorldCamera.orthographicSize + .8f);
            return new Vector3(x, y, -10);
        }
    }
}
