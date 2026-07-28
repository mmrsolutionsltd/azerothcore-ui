namespace AzerothCore_UI.Api.Services;

internal static class DungeonGuideCatalog
{
    internal sealed record Entry(
        string Overview, string Route, string[] Notes,
        IReadOnlyDictionary<string, string>? BossTactics = null);

    public static Entry Find(string dungeonName)
    {
        var match = Entries.FirstOrDefault(entry =>
            dungeonName.Contains(entry.Key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? Generic;
    }

    public static string Tactics(Entry guide, string bossName) =>
        guide.BossTactics?.FirstOrDefault(entry =>
            bossName.Contains(entry.Key, StringComparison.OrdinalIgnoreCase)).Value
        ?? "Let the tank establish threat and face the boss away from the party. "
        + "Interrupt dangerous casts, move out of persistent ground effects, and keep "
        + "the group close enough that companion bots do not take a different route.";

    private static readonly Entry Generic = new(
        "Clear steadily and let the tank engage first. The encounter list below follows "
        + "the order recorded by this server's instance data.",
        "Follow the main route and clear patrols before pulling a boss. Optional encounters "
        + "can be skipped when the party is struggling or inventory space is low.",
        [
            "Use narrow doorways to control large trash pulls.",
            "Wait for mana and resurrect dead companions before each boss.",
            "Quest items and scripted encounters may require the real player to interact."
        ]);

    private static readonly Dictionary<string, Entry> Entries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ragefire Chasm"] = new(
                "A short, mostly linear Horde dungeon with several caster-heavy packs.",
                "Clear the central route, take Taragaman's side chamber, then continue through "
                + "Jergosh to Bazzalan. Oggleflint is an optional early detour.",
                [
                    "Interrupt or focus enemy casters before melee targets.",
                    "The lava edges are navigable but are a common place for followers to separate."
                ],
                new Dictionary<string, string>
                {
                    ["Taragaman"] = "Tank him away from the group and interrupt his fire abilities.",
                    ["Jergosh"] = "Kill or control nearby cultists first, then interrupt Jergosh's casts.",
                    ["Bazzalan"] = "Keep him on the tank; poison and melee pressure make this a healing check."
                }),
            ["Deadmines"] = new(
                "A long linear dungeon ending on the pirate ship.",
                "Progress through Rhahk'Zor, the lumber room, forge and cannon door. Clear the "
                + "ship from the lower deck upward before engaging VanCleef.",
                [
                    "Patrols and fleeing miners can add extra packs.",
                    "On the ship, pull enemies away from ramps so bots do not run through another deck."
                ],
                new Dictionary<string, string>
                {
                    ["Sneed"] = "Defeat the shredder, then immediately pick up Sneed when he appears.",
                    ["Mr. Smite"] = "Clear the deck first. He changes weapons and briefly stuns the group.",
                    ["VanCleef"] = "Kill adds promptly and keep the fight on the upper deck."
                }),
            ["Wailing Caverns"] = new(
                "A sprawling dungeon whose four Fanglords unlock the final escort event.",
                "Take the left and right cave loops to defeat every Fanglord, return to the entrance "
                + "for the Disciple of Naralex escort, and finish the Mutanus event.",
                [
                    "The route crosses itself; keep companions close before jumping or changing levels.",
                    "Do not leave until all four Fanglords are dead or the final event will not start."
                ]),
            ["Shadowfang Keep"] = new(
                "A vertical castle with doors and events unlocked by defeating preceding bosses.",
                "Free the courtyard prisoner, climb through the keep, clear each landing, and finish "
                + "in Arugal's observatory.",
                [
                    "Pull casters back around corners rather than fighting on stairs.",
                    "Clear Arugal's room before the final pull and keep ranged characters spread."
                ]),
            ["Blackfathom Deeps"] = new(
                "A long coastal ruin with underwater sections and a final shrine event.",
                "Follow the ruins through the naga and Twilight areas. Defeat Kelris, then light the "
                + "four shrine flames one at a time before Aku'mai.",
                [
                    "Each shrine flame releases enemies; do not activate all four together.",
                    "Followers can struggle with water transitions, so pause after swimming sections."
                ]),
            ["Gnomeregan"] = new(
                "A very large mechanical dungeon with optional wings and dangerous alarm bots.",
                "Use the workshop route toward the clean zone, clear Electrocutioner and Crowd "
                + "Pummeler, then descend to Mekgineer Thermaplugg.",
                [
                    "Destroy alarm bots immediately to prevent reinforcements.",
                    "Avoid jumping shortcuts while using companions; take ramps and lifts together."
                ]),
            ["Razorfen Kraul"] = new(
                "A branching quilboar stronghold built around elevated thorn walkways.",
                "Clear the lower caverns and side bosses, then follow the upper walkway toward "
                + "Charlga Razorflank.",
                [
                    "Pull patrols away from walkway junctions.",
                    "Stay together at drops; companions may choose a long alternative path."
                ]),
            ["Scarlet Monastery"] = new(
                "A set of compact wings dominated by humanoid packs with healers and runners.",
                "Clear each room completely and pull dangerous packs back through doors. In Cathedral, "
                + "clear the nave and side aisles before pulling Mograine.",
                [
                    "Interrupt Scarlet healers and kill runners before they reach another group.",
                    "The Cathedral finale continues after Mograine falls; remain ready for Whitemane."
                ]),
            ["Razorfen Downs"] = new(
                "An undead-filled spiral dungeon with several caster and disease-heavy encounters.",
                "Follow the outer spiral, complete the gong event if desired, then climb toward "
                + "Amnennar the Coldbringer.",
                [
                    "Interrupt necromancers and clear summoned adds.",
                    "Do not fight on the edge of the spiral where knockbacks split the party."
                ]),
            ["Zul'Farrak"] = new(
                "An outdoor troll city with optional summons and a major stairway event.",
                "Clear the side routes, complete the pyramid prisoner event, then continue through "
                + "the chief's area. Optional bosses require their associated item or event.",
                [
                    "Start the pyramid only with full health and mana; it arrives in several waves.",
                    "Keep bots gathered on the stairs so they do not chase enemies into unopened packs."
                ]),
            ["Maraudon"] = new(
                "A large multi-wing cavern converging on the inner falls and Princess Theradras.",
                "Choose the purple or orange entrance, complete side objectives, then follow the inner "
                + "route through Celebras and Landslide to the princess.",
                [
                    "Use ramps rather than jumping into the water when leading companions.",
                    "The dungeon is long; repair and clear bag space before entering."
                ]),
            ["Blackrock Depths"] = new(
                "A vast non-linear city with many optional bosses, quests and locked routes.",
                "Choose a goal before entering. For the emperor route, progress through the prison/"
                + "arena area, Shadowforge mechanisms, the bar, golem foundry and Lyceum.",
                [
                    "This is not intended to be fully cleared in one short run.",
                    "The Lyceum requires lighting both braziers; keep the group together during respawns."
                ]),
            ["Stratholme"] = new(
                "A large city divided into living and undead routes with timed and optional events.",
                "Choose the main gate for the living side or service entrance for the undead side. "
                + "Clear ziggurats on the undead route before entering the slaughter square.",
                [
                    "Destroy all ziggurat crystals and their defenders to unlock progression.",
                    "Abominations must be cleared before the final gate opens."
                ]),
            ["Scholomance"] = new(
                "A caster-heavy school with locked rooms and a final sequence of instructors.",
                "Descend through the school, clear each instructor room around the final chamber, "
                + "then defeat Darkmaster Gandling.",
                [
                    "Interrupt heals, fears and shadow casts.",
                    "Clear rooms fully before crossing thresholds that may close doors."
                ])
        };
}
