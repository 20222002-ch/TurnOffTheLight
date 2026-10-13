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
        public void Tick(Vector2 player, LightsOutVerticalModel model, float dt)
        {
            float floor = player.y / LightsOutMover.FloorHeight;
            float targetZoom = Mathf.Clamp(3.5f + Mathf.Sqrt(Mathf.Max(0, floor)) * .6f + ZoomAdjustment, 3.2f, 22);
            WorldCamera.orthographicSize = Mathf.Lerp(WorldCamera.orthographicSize, targetZoom, 1 - Mathf.Exp(-dt * 2));
            float y = Mathf.Max(WorldCamera.orthographicSize, player.y + WorldCamera.orthographicSize * .16f);
            Vector3 target = new Vector3(0, y, -10);
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, .14f / model.CameraSpeedMultiplier, 250, dt);
        }
        public void Snap(Vector2 player)
        {
            WorldCamera.orthographicSize = 3.5f;
            transform.position = new Vector3(0, Mathf.Max(3.5f, player.y), -10); velocity = Vector3.zero;
        }
    }
}
