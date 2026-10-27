using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Track;
using UnityEngine;

namespace MapEnhancer
{
    // Commands go through the same target-angle property as the native controls.
    // The game's controller owns motion, obstruction checks and synchronization.
    public sealed class TurntableHelper : MonoBehaviour
    {
        private TurntableController? controller;
        private int? previousStop;
        public Action? OnRotationComplete;
        public TurntableController? Controller => controller;
        public bool IsInitialized => controller != null && controller.turntable != null;
        public List<int> ActiveTrackIndexes
        {
            get
            {
                var result = new List<int>();
                if (!EnsureInitialized()) return result;
                var table = controller!.turntable;
                for (var i = 0; i < table.nodes.Count; i++)
                {
                    var other = (i + table.nodes.Count / 2) % table.nodes.Count;
                    if (Graph.Shared.SegmentsConnectedTo(table.nodes[i]).Any(s => !s.Ephemeral && s.Available) ||
                        Graph.Shared.SegmentsConnectedTo(table.nodes[other]).Any(s => !s.Ephemeral && s.Available))
                        result.Add(i);
                }
                return result;
            }
        }

        public bool EnsureInitialized(bool logFailures = false)
        {
            if (controller == null) controller = GetComponent<TurntableController>();
            return IsInitialized && controller!.isActiveAndEnabled && controller.turntable.IsInitialized;
        }

        private void LateUpdate()
        {
            if (!EnsureInitialized()) return;
            var stop = controller!.turntable.StopIndex;
            if (stop != previousStop)
            {
                previousStop = stop;
                if (stop.HasValue) OnRotationComplete?.Invoke();
            }
        }

        public bool MoveToIndex(int index)
        {
            if (!EnsureInitialized() || !ActiveTrackIndexes.Contains(index)) return false;
            controller!.TargetAngle = controller.turntable.AngleForIndex(index);
            return true;
        }

        public int GetCurrentIndex() => EnsureInitialized() ? controller!.turntable.StopIndex ?? -1 : -1;

        public int GetNextTrackIndex(bool clockwise = true)
        {
            if (!EnsureInitialized()) return -1;
            var table = controller!.turntable;
            var angle = table.Angle;
            return ActiveTrackIndexes
                .Select(i => new { Index = i, Distance = Mathf.Repeat(
                    clockwise ? table.AngleForIndex(i) - angle : angle - table.AngleForIndex(i), 360f) })
                .Where(x => x.Distance > 0.1f)
                .OrderBy(x => x.Distance)
                .Select(x => x.Index)
                .DefaultIfEmpty(-1).First();
        }

        public bool RotateClockwise() => MoveToIndex(GetNextTrackIndex());
        public bool RotateCounterClockwise() => MoveToIndex(GetNextTrackIndex(false));
        public bool Rotate180()
        {
            if (!EnsureInitialized()) return false;
            controller!.TargetAngle = Mathf.Repeat(controller.turntable.Angle + 180f, 360f);
            return true;
        }

        public Vector3 GetTrackNodePosition(int index)
        {
            if (!EnsureInitialized() || index < 0 || index >= controller!.turntable.nodes.Count)
                return transform.position;
            return WorldTransformer.GameToWorld(controller.turntable.nodes[index].Position);
        }

        public string GetTrackIndexLabel(int index) => (index + 1).ToString();
    }
}
