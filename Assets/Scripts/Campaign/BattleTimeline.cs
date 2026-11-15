using System;
using UnityEngine;
namespace Game.Campaign
{
    // An owner-local clock: disabling/destroying a mission or boss cancels all its work.
    public sealed class BattleTimeline : MonoBehaviour
    {
        readonly BattleSchedule schedule = new BattleSchedule();

        public int PendingCount => schedule.Count;

        public float Countdown => schedule.Count == 0 ? 0 : Mathf.Max(0, (float)schedule.NextTime - Time.time);

        public static BattleTimeline For(GameObject owner) => owner.AddComponent<BattleTimeline>();

        public int After(float seconds, Action callback) => schedule.Schedule(Time.time + Mathf.Max(0, seconds), callback);

        public bool Cancel(int eventId) => schedule.Cancel(eventId);

        public bool Reschedule(int eventId, float seconds) => schedule.Reschedule(eventId, Time.time + Mathf.Max(0, seconds));

        public void Clear() => schedule.Clear();

        void Update()
        {
            if (Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost)
            {
                return;
            }

            schedule.Advance(Time.time);
        }

        void OnDisable() => Clear();

        void OnDestroy() => Clear();
    }
}
