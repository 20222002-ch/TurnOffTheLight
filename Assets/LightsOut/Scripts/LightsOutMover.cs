using UnityEngine;
using UnityEngine.InputSystem;

namespace LightsOut
{
    // Logical contact volumes replace wall collisions. Sweeping the entire segment makes dash reliable.
    public sealed class LightsOutMover : MonoBehaviour
    {
        public const float RoomWidth = 2.4f, FloorHeight = 1.8f;
        public LightsOutVerticalModel Model { get; private set; }
        public Vector2 Direction { get; private set; } = Vector2.up;
        public float DashRemaining { get; private set; }
        public float DashCooldown { get; private set; }
        public Vector2 Position => transform.position;
        InputAction movement, dash;
        bool dashRequested;
        Vector2 dashDirection;
        Transform pointer;
        public void Initialize(LightsOutVerticalModel model)
        {
            Model = model;
            movement = new InputAction("Move", InputActionType.Value);
            movement.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            movement.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            dash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dash.performed += _ => dashRequested = true;
            pointer = transform.Find("Direction");
            SetInputEnabled(true);
            Sweep(Model, Position, Position, Model.ExtinguishRadius);
        }
        public void SetInputEnabled(bool active)
        {
            if (movement == null) return;
            if (active) { movement.Enable(); dash.Enable(); }
            else { movement.Disable(); dash.Disable(); dashRequested = false; }
        }
        public void ResetMotion(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, -1);
            DashRemaining = 0; DashCooldown = 0; dashRequested = false; Direction = Vector2.up;
            dashDirection = Vector2.up;
            if (pointer) { pointer.localRotation = Quaternion.identity; pointer.localPosition = Vector3.up * .8f; }
        }
        public void Step(float dt)
        {
            Vector2 input = movement.ReadValue<Vector2>();
            StepInput(input, dt, dashRequested); dashRequested = false;
        }
        public void StepInput(Vector2 input, float dt, bool requestDash = false)
        {
            if (Model == null || dt <= 0 || Model.IsComplete) return;
            input = Vector2.ClampMagnitude(input, 1);
            if (input.sqrMagnitude > .001f) Direction = input.normalized;
            DashCooldown = Mathf.Max(0, DashCooldown - dt);
            if (requestDash && Model.DashUnlocked && DashCooldown <= 0)
            {
                dashDirection = Direction;
                DashRemaining = .18f;
                DashCooldown = Model.DashCooldown;
            }
            Vector2 before = Position;
            Vector2 after;
            if (DashRemaining > 0)
            {
                float duration = Mathf.Min(dt, DashRemaining);
                after = before + dashDirection * (Model.DashDistance / .18f) * duration;
                DashRemaining = Mathf.Max(0, DashRemaining - dt);
            }
            else after = before + input * Model.MoveSpeed * dt;
            float left = -Model.FloorRooms * RoomWidth * .5f;
            int available = Mathf.Min(Model.FloorCount - 1, Mathf.Max(11, Model.CurrentFloor + 12));
            after.x = Mathf.Clamp(after.x, left + .22f, -left - .22f);
            after.y = Mathf.Clamp(after.y, .22f, (available + 1) * FloorHeight - .22f);
            transform.position = new Vector3(after.x, after.y, -1);
            Sweep(Model, before, after, Model.ExtinguishRadius);
            if (pointer) { pointer.localPosition = new Vector3(Direction.x * .8f, Direction.y * .8f, 0); pointer.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg - 90); }
        }
        public static int Sweep(LightsOutVerticalModel model, Vector2 start, Vector2 end, float radius)
        {
            int first = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(start.y, end.y) - radius) / FloorHeight), 0, model.FloorCount - 1);
            int last = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(start.y, end.y) + radius) / FloorHeight), 0, model.FloorCount - 1);
            float left = -model.FloorRooms * RoomWidth * .5f;
            int contacts = 0;
            for (int f = first; f <= last; f++)
                for (int c = 0; c < model.FloorRooms; c++)
                {
                    if (!model.IsLightOn(f, c)) continue;
                    Rect bounds = new Rect(left + c * RoomWidth + .10f - radius, f * FloorHeight + .10f - radius,
                        RoomWidth - .20f + radius * 2, FloorHeight - .20f + radius * 2);
                    if (SegmentIntersects(start, end, bounds) && model.Contact(f, c)) contacts++;
                }
            return contacts;
        }
        public static bool SegmentIntersects(Vector2 start, Vector2 end, Rect rect)
        {
            float t0 = 0, t1 = 1;
            Vector2 d = end - start;
            return Slab(start.x, d.x, rect.xMin, rect.xMax, ref t0, ref t1)
                && Slab(start.y, d.y, rect.yMin, rect.yMax, ref t0, ref t1);
        }
        static bool Slab(float origin, float delta, float min, float max, ref float t0, ref float t1)
        {
            if (Mathf.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float temp = a; a = b; b = temp; }
            t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, b);
            return t0 <= t1;
        }
        void OnDestroy() { movement?.Dispose(); dash?.Dispose(); }
    }
}
