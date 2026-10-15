using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

namespace LightsOut
{
    // Logical rooms replace wall collisions; exact segment residence drives retained per-room work.
    public sealed class LightsOutMover : MonoBehaviour
    {
        public const float RoomWidth = 2.4f, FloorHeight = 1.8f;
        public LightsOutVerticalModel Model { get; private set; }
        public Vector2 Direction { get; private set; } = Vector2.up;
        public float DashRemaining { get; private set; }
        public float DashCooldown { get; private set; }
        public float RiseRemaining { get; private set; }
        public float RiseCooldown { get; private set; }
        public bool IsRising => RiseRemaining > 0;
        public float RiseProgress => IsRising ? 1 - RiseRemaining / riseDuration : 0;
        public event System.Action RiseStarted;
        public event System.Action RiseFinished;
        public Vector2 Position => transform.position;
        public int WorkingRoomId => HasActiveWorkRoom() ? activeWorkRoom : -1;
        public float WorkElapsed => WorkingRoomId >= 0 && workStates.TryGetValue(activeWorkRoom, out WorkState state) ? state.Elapsed : 0;
        public float WorkDuration => WorkingRoomId >= 0 ? Model.GetRoomWorkDuration(activeWorkRoom / Model.FloorRooms, activeWorkRoom % Model.FloorRooms) : 0;
        public float WorkProgress => WorkDuration > 0 ? Mathf.Clamp01(WorkElapsed / WorkDuration) : 0;
        public bool IsWorking => WorkingRoomId >= 0 && WorkElapsed > 0 && WorkDuration > 0;
        public int RetainedWorkCount => workStates.Count;
        InputAction movement, dash, rise;
        bool dashRequested, riseRequested, inputEnabled;
        Vector2 dashDirection;
        float riseDistance, riseDuration;
        Transform pointer;
        const int MaxWorkStates = 256;
        int activeWorkRoom = -1, workRun, workFloors, workColumns;
        float workRoomWidth;
        sealed class WorkState { public float Elapsed, Duration, RetentionRemaining; }
        struct WorkInterval { public int Id; public float Enter, Exit; }
        readonly Dictionary<int, WorkState> workStates = new Dictionary<int, WorkState>();
        readonly List<WorkInterval> workIntervals = new List<WorkInterval>(16);
        readonly List<WorkInterval> bodyIntervals = new List<WorkInterval>(16);
        readonly Dictionary<int, float> workVisitExits = new Dictionary<int, float>();
        readonly List<int> removedWork = new List<int>(16);
        static readonly Comparison<WorkInterval> IntervalOrder = (a, b) => a.Enter.CompareTo(b.Enter);
        [Serializable] sealed class WorkSave
        {
            public int version = 2, run, floors, columns;
            public float roomWidth, duration;
            public SavedRoomWork[] roomstates;
        }
        [Serializable] sealed class SavedRoomWork { public int id; public float elapsed, duration, retentionremaining; }
        public void Initialize(LightsOutVerticalModel model)
        {
            movement?.Dispose(); dash?.Dispose(); rise?.Dispose();
            Model = model;
            movement = new InputAction("Move", InputActionType.Value);
            movement.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            movement.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            dash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dash.performed += _ => dashRequested = true;
            rise = new InputAction("VerticalRise", InputActionType.Button, "<Keyboard>/r");
            rise.performed += _ => riseRequested = true;
            DashRemaining = 0; DashCooldown = 0;
            RiseRemaining = 0; RiseCooldown = 0; dashRequested = false; riseRequested = false;
            dashDirection = Vector2.up; Direction = Vector2.up;
            pointer = transform.Find("Direction");
            SetInputEnabled(true);
            CancelWork(); RememberWorkGeometry();
        }
        public void SetInputEnabled(bool active)
        {
            inputEnabled = active;
            if (movement == null) return;
            if (active) { movement.Enable(); dash.Enable(); rise.Enable(); }
            else { movement.Disable(); dash.Disable(); rise.Disable(); dashRequested = false; riseRequested = false; }
        }
        public void ResetMotion(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, -1);
            FinishRise();
            CancelWork(); RememberWorkGeometry();
            DashRemaining = 0; DashCooldown = 0; RiseCooldown = 0;
            dashRequested = false; riseRequested = false; Direction = Vector2.up;
            dashDirection = Vector2.up;
            if (pointer) { pointer.localRotation = Quaternion.identity; pointer.localPosition = Vector3.up * .8f; }
        }
        public void Step(float dt)
        {
            Vector2 input = movement != null ? movement.ReadValue<Vector2>() : Vector2.zero;
            StepInput(input, dt, dashRequested, riseRequested);
            dashRequested = false; riseRequested = false;
        }
        public void RequestDash() { if (inputEnabled) dashRequested = true; }
        public void RequestRise() { if (inputEnabled) riseRequested = true; }
        public void StepInput(Vector2 input, float dt, bool requestDash = false, bool requestRise = false)
        {
            if (Model == null || dt <= 0 || !Finite(dt)) return;
            if (Model.IsComplete || Model.BuildingClosed)
            { DashRemaining = 0; FinishRise(); CancelWork(); return; }
            if (!inputEnabled) return;
            bool geometryChanged = WorkGeometryChanged();
            if (geometryChanged) { CancelWork(); RememberWorkGeometry(); }
            input = Vector2.ClampMagnitude(input, 1);
            if (!IsRising && input.sqrMagnitude > .001f) Direction = input.normalized;
            DashCooldown = Mathf.Max(0, DashCooldown - dt);
            RiseCooldown = Mathf.Max(0, RiseCooldown - dt);
            if (requestRise && Model.RiseUnlocked && RiseCooldown <= 0 && !IsRising)
            {
                Direction = Vector2.up; DashRemaining = 0;
                riseDuration = Model.RiseDuration; riseDistance = Model.RiseDistance;
                RiseRemaining = riseDuration; RiseCooldown = Model.RiseCooldown;
                RiseStarted?.Invoke();
            }
            if (!IsRising && requestDash && Model.DashUnlocked && DashCooldown <= 0)
            {
                dashDirection = Direction;
                DashRemaining = .18f;
                DashCooldown = Model.DashCooldown;
            }
            Vector2 before = Position;
            Vector2 after;
            bool finishRise = false;
            float movementSeconds = dt;
            if (IsRising)
            {
                // Integrating the easing curve over each frame preserves travel at any frame rate.
                // Contact still uses the whole segment, so no intermediate floor can be skipped.
                float from = 1 - RiseRemaining / riseDuration;
                movementSeconds = Mathf.Min(dt, RiseRemaining);
                RiseRemaining = Mathf.Max(0, RiseRemaining - dt);
                float to = 1 - RiseRemaining / riseDuration;
                after = before + Vector2.up * riseDistance * (RiseEase(to) - RiseEase(from));
                Direction = Vector2.up; finishRise = RiseRemaining <= 0;
            }
            else if (DashRemaining > 0)
            {
                float duration = Mathf.Min(dt, DashRemaining);
                movementSeconds = duration;
                after = before + dashDirection * (Model.DashDistance / .18f) * duration;
                DashRemaining = Mathf.Max(0, DashRemaining - dt);
            }
            else after = before + input * Model.MoveSpeed * dt;
            float left = -Model.HalfWidth;
            after.x = Mathf.Clamp(after.x, left + .22f, -left - .22f);
            after.y = Mathf.Clamp(after.y, .22f, Model.FloorCount * FloorHeight - .22f);
            transform.position = new Vector3(after.x, after.y, -1);
            if (geometryChanged) activeWorkRoom = -1;
            else
            {
                AdvanceWork(before, after, movementSeconds);
                if (!Model.IsComplete && !Model.BuildingClosed && dt > movementSeconds) AdvanceWork(after, after, dt - movementSeconds);
            }
            if (finishRise) RiseFinished?.Invoke();
            else if ((Model.IsComplete || Model.BuildingClosed) && IsRising) FinishRise();
            if (pointer) { pointer.localPosition = new Vector3(Direction.x * .8f, Direction.y * .8f, 0); pointer.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg - 90); }
        }
        static float RiseEase(float progress) { float remaining = 1 - Mathf.Clamp01(progress); return 1 - remaining * remaining * remaining; }
        void FinishRise()
        {
            bool wasRising = IsRising; RiseRemaining = 0;
            if (wasRising) RiseFinished?.Invoke();
        }
        public void CancelWork()
        {
            activeWorkRoom = -1; workStates.Clear(); workIntervals.Clear(); bodyIntervals.Clear(); workVisitExits.Clear(); removedWork.Clear();
        }
        void RememberWorkGeometry()
        {
            if (Model == null) return;
            workRun = Model.RunNumber; workFloors = Model.FloorCount; workColumns = Model.FloorRooms; workRoomWidth = Model.RoomWidth;
        }
        bool WorkGeometryChanged()
        {
            return Model.RunNumber != workRun || Model.FloorCount != workFloors || Model.FloorRooms != workColumns || Mathf.Abs(Model.RoomWidth - workRoomWidth) > .0001f;
        }
        bool HasActiveWorkRoom()
        {
            return Model != null && !WorkGeometryChanged() && !Model.IsComplete && !Model.BuildingClosed && activeWorkRoom >= 0
                && activeWorkRoom < Model.FloorCount * Model.FloorRooms && Model.IsLightOn(activeWorkRoom / Model.FloorRooms, activeWorkRoom % Model.FloorRooms)
                && WorkBounds(Model, activeWorkRoom / Model.FloorRooms, activeWorkRoom % Model.FloorRooms).Contains(Position);
        }
        void AdvanceWork(Vector2 start, Vector2 end, float dt)
        {
            workIntervals.Clear(); bodyIntervals.Clear(); workVisitExits.Clear();
            int priorActive = activeWorkRoom; activeWorkRoom = -1;
            {
                int firstFloor = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(start.y, end.y) / FloorHeight), 0, Model.FloorCount - 1);
                int lastFloor = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(start.y, end.y) / FloorHeight), 0, Model.FloorCount - 1);
                int firstColumn = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(start.x, end.x) + Model.HalfWidth) / Model.RoomWidth), 0, Model.FloorRooms - 1);
                int lastColumn = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(start.x, end.x) + Model.HalfWidth) / Model.RoomWidth), 0, Model.FloorRooms - 1);
                for (int floor = firstFloor; floor <= lastFloor; floor++)
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        if (!Model.IsLightOn(floor, column)) continue;
                        if (SegmentInterval(start, end, RoomBounds(Model, floor, column), out float enter, out float exit) && exit - enter > .000001f)
                            bodyIntervals.Add(new WorkInterval { Id = floor * Model.FloorRooms + column, Enter = enter, Exit = exit });
                    }
                bodyIntervals.Sort(IntervalOrder);
            }
            // Physical room interiors always take priority. In gaps or extinguished rooms, expanded
            // range selects one nearest lit room rather than advancing every overlapping rectangle.
            float cursor = 0;
            for (int i = 0; i < bodyIntervals.Count; i++)
            {
                AddRangeInterval(start, end, cursor, bodyIntervals[i].Enter, priorActive);
                AddWorkInterval(bodyIntervals[i]); cursor = Mathf.Max(cursor, bodyIntervals[i].Exit);
            }
            AddRangeInterval(start, end, cursor, 1, priorActive);
            // Every retained room has its own countdown. Time spent inside is excluded from that countdown.
            removedWork.Clear();
            foreach (KeyValuePair<int, WorkState> item in workStates)
            {
                bool visited = false;
                for (int i = 0; i < workIntervals.Count; i++) if (workIntervals[i].Id == item.Key) { visited = true; break; }
                if (!Model.IsLightOn(item.Key / Model.FloorRooms, item.Key % Model.FloorRooms)) { removedWork.Add(item.Key); continue; }
                if (visited) continue;
                item.Value.RetentionRemaining = Mathf.Max(0, item.Value.RetentionRemaining - dt);
                if (item.Value.RetentionRemaining <= 0) removedWork.Add(item.Key);
            }
            for (int i = 0; i < removedWork.Count; i++) workStates.Remove(removedWork[i]);
            for (int i = 0; i < workIntervals.Count; i++)
            {
                WorkInterval interval = workIntervals[i];
                if (!Model.IsLightOn(interval.Id / Model.FloorRooms, interval.Id % Model.FloorRooms)) continue;
                if (Model.TryInstantContact(interval.Id / Model.FloorRooms, interval.Id % Model.FloorRooms))
                {
                    workStates.Remove(interval.Id);
                    if (Model.IsComplete || Model.BuildingClosed) { CancelWork(); return; }
                    continue;
                }
                float duration = Model.GetRoomWorkDuration(interval.Id / Model.FloorRooms, interval.Id % Model.FloorRooms);
                if (workStates.TryGetValue(interval.Id, out WorkState state))
                {
                    float previousExit = workVisitExits.TryGetValue(interval.Id, out float visitedExit) ? visitedExit : 0;
                    state.RetentionRemaining -= Mathf.Max(0, interval.Enter - previousExit) * dt;
                    if (state.RetentionRemaining <= 0) { workStates.Remove(interval.Id); state = null; }
                }
                if (state == null)
                {
                    MakeRoomForWork();
                    state = new WorkState { Duration = duration }; workStates[interval.Id] = state;
                }
                if (Mathf.Abs(state.Duration - duration) > .00001f)
                { state.Elapsed = Mathf.Clamp01(state.Elapsed / Mathf.Max(.00001f, state.Duration)) * duration; state.Duration = duration; }
                state.Elapsed = Mathf.Min(duration, state.Elapsed + (interval.Exit - interval.Enter) * dt);
                state.RetentionRemaining = Model.HoldSeconds; workVisitExits[interval.Id] = interval.Exit;
                if (state.Elapsed + .00001f >= duration)
                {
                    workStates.Remove(interval.Id);
                    Model.Contact(interval.Id / Model.FloorRooms, interval.Id % Model.FloorRooms);
                    if (Model.IsComplete || Model.BuildingClosed) { CancelWork(); return; }
                }
                else if (interval.Exit >= 1 - .000001f && WorkBounds(Model, interval.Id / Model.FloorRooms, interval.Id % Model.FloorRooms).Contains(end)) activeWorkRoom = interval.Id;
            }
            foreach (KeyValuePair<int, float> visit in workVisitExits)
                if (workStates.TryGetValue(visit.Key, out WorkState state))
                { state.RetentionRemaining = Mathf.Max(0, state.RetentionRemaining - (1 - visit.Value) * dt); if (state.RetentionRemaining <= 0) workStates.Remove(visit.Key); }
        }
        void AddWorkInterval(WorkInterval interval)
        {
            if (interval.Exit - interval.Enter <= .000001f) return;
            if (workIntervals.Count > 0)
            {
                int last = workIntervals.Count - 1; WorkInterval previous = workIntervals[last];
                if (previous.Id == interval.Id && interval.Enter <= previous.Exit + .000001f)
                { previous.Exit = Mathf.Max(previous.Exit, interval.Exit); workIntervals[last] = previous; return; }
            }
            workIntervals.Add(interval);
        }
        void AddRangeInterval(Vector2 start, Vector2 end, float from, float to, int priorActive)
        {
            float radius = Mathf.Max(0, Model.ExtinguishRadius - .12f);
            if (radius <= 0 || to - from <= .000001f) return;
            Vector2 midpoint = Vector2.Lerp(start, end, (from + to) * .5f);
            int selected = -1; float nearest = radius * radius + .000001f;
            int firstFloor = Mathf.Clamp(Mathf.FloorToInt((midpoint.y - radius) / FloorHeight), 0, Model.FloorCount - 1);
            int lastFloor = Mathf.Clamp(Mathf.FloorToInt((midpoint.y + radius) / FloorHeight), 0, Model.FloorCount - 1);
            int firstColumn = Mathf.Clamp(Mathf.FloorToInt((midpoint.x - radius + Model.HalfWidth) / Model.RoomWidth), 0, Model.FloorRooms - 1);
            int lastColumn = Mathf.Clamp(Mathf.FloorToInt((midpoint.x + radius + Model.HalfWidth) / Model.RoomWidth), 0, Model.FloorRooms - 1);
            for (int floor = firstFloor; floor <= lastFloor; floor++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    if (!Model.IsLightOn(floor, column)) continue;
                    Rect room = RoomBounds(Model, floor, column); Vector2 closest = new Vector2(Mathf.Clamp(midpoint.x, room.xMin, room.xMax), Mathf.Clamp(midpoint.y, room.yMin, room.yMax));
                    float distance = (midpoint - closest).sqrMagnitude; int id = floor * Model.FloorRooms + column;
                    if (distance < nearest - .000001f || Mathf.Abs(distance - nearest) <= .000001f && id == priorActive)
                    { nearest = distance; selected = id; }
                }
            if (selected >= 0 && SegmentInterval(start, end, WorkBounds(Model, selected / Model.FloorRooms, selected % Model.FloorRooms), out float enter, out float exit))
                AddWorkInterval(new WorkInterval { Id = selected, Enter = Mathf.Max(from, enter), Exit = Mathf.Min(to, exit) });
        }
        void MakeRoomForWork()
        {
            if (workStates.Count < MaxWorkStates) return;
            int oldest = -1; float remaining = float.MaxValue;
            foreach (KeyValuePair<int, WorkState> item in workStates)
                if (item.Value.RetentionRemaining < remaining) { remaining = item.Value.RetentionRemaining; oldest = item.Key; }
            if (oldest >= 0) workStates.Remove(oldest);
        }
        public string ExportWorkJson()
        {
            if (Model == null || Model.BuildingClosed || Model.IsComplete || WorkGeometryChanged()) return "";
            var entries = new List<SavedRoomWork>(workStates.Count);
            foreach (KeyValuePair<int, WorkState> item in workStates)
            {
                if (item.Value.RetentionRemaining <= 0 || !Model.IsLightOn(item.Key / Model.FloorRooms, item.Key % Model.FloorRooms)) continue;
                entries.Add(new SavedRoomWork { id = item.Key, elapsed = item.Value.Elapsed, duration = item.Value.Duration, retentionremaining = item.Value.RetentionRemaining });
            }
            entries.Sort((a, b) => a.id.CompareTo(b.id));
            return JsonUtility.ToJson(new WorkSave { run = Model.RunNumber, floors = Model.FloorCount, columns = Model.FloorRooms,
                roomWidth = Model.RoomWidth, duration = Model.ExtinguishSeconds, roomstates = entries.ToArray() });
        }
        public bool ImportWorkJson(string json)
        {
            if (Model == null || Model.BuildingClosed || Model.IsComplete || string.IsNullOrEmpty(json)) return false;
            WorkSave saved;
            try { saved = JsonUtility.FromJson<WorkSave>(json); } catch (ArgumentException) { return false; }
            if (saved == null || (saved.version != 1 && saved.version != 2) || saved.run != Model.RunNumber || saved.floors != Model.FloorCount || saved.columns != Model.FloorRooms
                || !Finite(saved.roomWidth) || Mathf.Abs(saved.roomWidth - Model.RoomWidth) > .0001f || !Finite(saved.duration)
                || saved.duration <= 0 || saved.duration > 120 || saved.roomstates == null || saved.roomstates.Length > MaxWorkStates) return false;
            var restored = new Dictionary<int, WorkState>();
            for (int i = 0; i < saved.roomstates.Length; i++)
            {
                SavedRoomWork entry = saved.roomstates[i];
                float oldDuration = entry == null ? 0 : saved.version == 1 ? saved.duration : entry.duration;
                if (entry == null || entry.id < 0 || entry.id >= Model.FloorCount * Model.FloorRooms || restored.ContainsKey(entry.id)
                    || !Finite(oldDuration) || oldDuration <= 0 || oldDuration > 120 || !Finite(entry.elapsed) || entry.elapsed <= 0 || entry.elapsed >= oldDuration || !Finite(entry.retentionremaining)
                    || entry.retentionremaining <= 0 || entry.retentionremaining > Model.HoldSeconds + .0001f
                    || !Model.IsLightOn(entry.id / Model.FloorRooms, entry.id % Model.FloorRooms)) return false;
                float currentDuration = Model.GetRoomWorkDuration(entry.id / Model.FloorRooms, entry.id % Model.FloorRooms);
                restored[entry.id] = new WorkState { Elapsed = entry.elapsed / oldDuration * currentDuration, Duration = currentDuration, RetentionRemaining = entry.retentionremaining };
            }
            CancelWork(); RememberWorkGeometry();
            foreach (KeyValuePair<int, WorkState> item in restored)
            {
                workStates[item.Key] = item.Value;
                if (WorkBounds(Model, item.Key / Model.FloorRooms, item.Key % Model.FloorRooms).Contains(Position)) activeWorkRoom = item.Key;
            }
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static Rect RoomBounds(LightsOutVerticalModel model, int floor, int column)
        {
            return new Rect(-model.HalfWidth + column * model.RoomWidth + .10f, floor * FloorHeight + .10f, model.RoomWidth - .20f, FloorHeight - .20f);
        }
        public static Rect WorkBounds(LightsOutVerticalModel model, int floor, int column)
        {
            Rect bounds = RoomBounds(model, floor, column); float radius = Mathf.Max(0, model.ExtinguishRadius - .12f);
            return Rect.MinMaxRect(bounds.xMin - radius, bounds.yMin - radius, bounds.xMax + radius, bounds.yMax + radius);
        }
        public static float ResidenceFraction(Vector2 start, Vector2 end, Rect rect)
        { return SegmentInterval(start, end, rect, out float enter, out float exit) ? Mathf.Max(0, exit - enter) : 0; }
        static bool SegmentInterval(Vector2 start, Vector2 end, Rect rect, out float enter, out float exit)
        {
            enter = 0; exit = 1; Vector2 d = end - start;
            return Slab(start.x, d.x, rect.xMin, rect.xMax, ref enter, ref exit) && Slab(start.y, d.y, rect.yMin, rect.yMax, ref enter, ref exit);
        }
        public static int Sweep(LightsOutVerticalModel model, Vector2 start, Vector2 end, float radius)
        {
            if (model == null || model.BuildingClosed || model.IsComplete) return 0;
            radius = Mathf.Max(0, radius);
            int first = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(start.y, end.y) - radius) / FloorHeight), 0, model.FloorCount - 1);
            int last = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(start.y, end.y) + radius) / FloorHeight), 0, model.FloorCount - 1);
            float width = model.RoomWidth;
            float left = -model.HalfWidth;
            int contacts = 0;
            for (int f = first; f <= last; f++)
                for (int c = 0; c < model.FloorRooms; c++)
                {
                    if (!model.IsLightOn(f, c)) continue;
                    Rect bounds = new Rect(left + c * width + .10f - radius, f * FloorHeight + .10f - radius,
                        width - .20f + radius * 2, FloorHeight - .20f + radius * 2);
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
        void OnDisable() { SetInputEnabled(false); }
        void OnDestroy() { movement?.Dispose(); dash?.Dispose(); rise?.Dispose(); }
    }
}
