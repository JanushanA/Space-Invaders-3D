namespace Game.Player
{
    // Gives every skill tree node a stable ID for saving, unlocking and slot assignment.
    public enum SkillId
    {
        // Keeps IDs in a fixed order because saved unlock lists store these numeric enum values.
        None = 0,
        Root,
        Health1, Health2, Health3, Health4,
        Speed1, Speed2, Speed3, Speed4,
        Damage1, Damage2, Damage3, Damage4,
        AttackCore,
        TwinShot, QuadShot,
        RapidFire, Overclock,
        HeavyShot, Cannon,
        SpreadShot,
        Critical,
        Dash, Afterburner,
        Shield, Barrier,
        Bomb, PlasmaBomb,
        Repair,
        HomingBurst
    }

    // Separates passive stat upgrades from active abilities.
    public enum SkillKind
    {
        // Lets the UI and hotbar tell passive upgrades from attack modifiers and usable abilities.
        Stat = 0,
        Attack = 1,
        Active = 2
    }

    // Describes one skill node, including its cost, required parent and menu position.
    public struct SkillNode
    {
        // Holds gameplay requirements and visual layout data together for one skill tree hexagon.
        public SkillId id;
        public SkillId parent;
        public string title;
        public string detail;
        public int cost;
        public SkillKind kind;
        public UnityEngine.Color colour;
        public char mark;
        public float x;
        public float y;
        public SkillId extraLink;
    }
}
