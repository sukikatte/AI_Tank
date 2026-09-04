using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CE6127.Tanks.AI
{
    /// <summary>
    /// Utility AI：每帧为存活 AI 坦克打分并分配互不重叠的站位（正面 / 左翼 / 右翼）。
    /// 单车仍由 FSM 执行追击、绕障、开火。
    /// </summary>
    internal static class PlatoonUtility
    {
        private enum Slot
        {
            Front,
            Left,
            Right
        }

        private static int s_Frame = -1;
        private static readonly Dictionary<TankSM, Vector3> s_HoldPoints = new();

        const float c_EngageRange = 15f;   // 再近一点，溅射更接近满伤 10，仍避开贴脸半径 5。
        const float c_Spread = 11f;
        const float c_RetreatRange = 18f;

        /// <summary>
        /// 返回该坦克本帧应前往的协同占位点。
        /// </summary>
        public static Vector3 GetHoldPoint(TankSM tank)
        {
            AssignIfNeeded();
            if (tank != null && s_HoldPoints.TryGetValue(tank, out Vector3 point))
                return point;
            return tank != null ? tank.transform.position : Vector3.zero;
        }

        private static void AssignIfNeeded()
        {
            if (s_Frame == Time.frameCount)
                return;
            s_Frame = Time.frameCount;
            s_HoldPoints.Clear();

            var gm = GameManager.Instance;
            if (gm == null || gm.PlayerPlatoon == null || gm.AIPlatoon == null)
                return;
            if (gm.PlayerPlatoon.Tanks == null || gm.PlayerPlatoon.Tanks.Count == 0)
                return;
            if (gm.PlayerPlatoon.Tanks[0].Instance == null)
                return;

            Transform player = gm.PlayerPlatoon.Tanks[0].Instance.transform;
            var tanks = CollectAlive(gm);
            if (tanks.Count == 0)
                return;

            Vector3 playerPos = player.position;
            Vector3 avg = Vector3.zero;
            for (int i = 0; i < tanks.Count; ++i)
                avg += tanks[i].transform.position;
            avg /= tanks.Count;

            Vector3 approach = playerPos - avg;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f)
                approach = -Flatten(player.forward);
            approach.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, approach);

            var slots = BuildSlots(tanks.Count, playerPos, approach, right, tanks);

            // 贪心：每次选「坦克 × 槽位」最高分，保证一车一位。
            var usedTank = new bool[tanks.Count];
            var usedSlot = new bool[slots.Length];
            int pairs = Mathf.Min(tanks.Count, slots.Length);
            for (int n = 0; n < pairs; ++n)
            {
                float best = float.NegativeInfinity;
                int bi = 0, bj = 0;
                for (int i = 0; i < tanks.Count; ++i)
                {
                    if (usedTank[i])
                        continue;
                    for (int j = 0; j < slots.Length; ++j)
                    {
                        if (usedSlot[j])
                            continue;
                        float s = Score(tanks[i], slots[j].world, slots[j].slot, playerPos, approach);
                        if (s > best)
                        {
                            best = s;
                            bi = i;
                            bj = j;
                        }
                    }
                }
                usedTank[bi] = true;
                usedSlot[bj] = true;
                s_HoldPoints[tanks[bi]] = SnapToNavMesh(slots[bj].world, tanks[bi].transform.position);
            }
        }

        private static List<TankSM> CollectAlive(GameManager gm)
        {
            var list = new List<TankSM>();
            foreach (var tm in gm.AIPlatoon.Tanks)
            {
                if (tm == null || tm.Instance == null || !tm.Instance.activeSelf)
                    continue;
                var sm = tm.Instance.GetComponent<TankSM>();
                if (sm != null)
                    list.Add(sm);
            }
            return list;
        }

        private static (Slot slot, Vector3 world)[] BuildSlots(
            int alive, Vector3 playerPos, Vector3 approach, Vector3 right, List<TankSM> tanks)
        {
            bool anyHurt = false;
            for (int i = 0; i < tanks.Count; ++i)
            {
                if (Health01(tanks[i]) < 0.35f)
                    anyHurt = true;
            }
            float range = (alive == 1 || anyHurt) ? c_RetreatRange : c_EngageRange;
            float spread = c_Spread;

            Vector3 front = playerPos - approach * range;
            Vector3 left = playerPos - approach * (range * 0.85f) - right * spread;
            Vector3 rightPt = playerPos - approach * (range * 0.85f) + right * spread;

            if (alive == 1)
                return new[] { (Slot.Front, front) };
            if (alive == 2)
                return new[] { (Slot.Left, left), (Slot.Right, rightPt) };
            return new[] { (Slot.Front, front), (Slot.Left, left), (Slot.Right, rightPt) };
        }

        /// <summary>
        /// Utility 打分：距离、已在该侧、槽位视线、残血避免当诱饵。
        /// </summary>
        private static float Score(TankSM tank, Vector3 slotWorld, Slot slot, Vector3 playerPos, Vector3 approach)
        {
            float dist = Vector3.Distance(tank.transform.position, slotWorld);
            float score = 80f - dist;

            Vector3 fromPlayer = tank.transform.position - playerPos;
            fromPlayer.y = 0f;
            Vector3 slotDir = slotWorld - playerPos;
            slotDir.y = 0f;
            if (fromPlayer.sqrMagnitude > 0.01f && slotDir.sqrMagnitude > 0.01f
                && Vector3.Dot(fromPlayer.normalized, slotDir.normalized) > 0.25f)
                score += 28f;

            if (HasLosFrom(slotWorld, tank.Target != null ? tank.Target : null, tank.transform))
                score += 40f;

            float hp = Health01(tank);
            if (slot == Slot.Front)
            {
                if (hp < 0.35f)
                    score -= 70f;
                else
                    score += 12f;
            }
            else if (hp < 0.35f)
                score += 22f;

            return score;
        }

        private static float Health01(TankSM tank)
        {
            var health = tank.GetComponent<TankHealth>();
            if (health == null || health.StartingHealth <= 0.01f)
                return 1f;
            return Mathf.Clamp01(health.CurrentHealth / health.StartingHealth);
        }

        private static bool HasLosFrom(Vector3 from, Transform player, Transform self)
        {
            if (player == null)
                return false;
            Vector3 origin = from + Vector3.up * 1.2f;
            Vector3 aim = player.position + Vector3.up;
            Vector3 delta = aim - origin;
            float dist = delta.magnitude;
            if (dist < 0.5f)
                return true;

            if (!Physics.Raycast(origin, delta.normalized, out RaycastHit hit, dist,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;

            Transform t = hit.collider.transform;
            if (self != null && (t == self || t.IsChildOf(self)))
                return true;
            return t == player || t.IsChildOf(player);
        }

        private static Vector3 SnapToNavMesh(Vector3 point, Vector3 fallback)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 14f, NavMesh.AllAreas))
                return hit.position;
            return fallback;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward;
        }
    }
}
