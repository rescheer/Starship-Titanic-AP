using System.Text;

namespace StarshipTitanicAp;

/// <summary>The PET lines that deliver each Progressive Clue. A clue at one of the seed's thresholds shows a line from
/// its part's list. Lines can use these placeholders, filled from the target stateroom:
/// {elevator} - elevator number, {floor} - floor number, {room} - room number, {class} - "1st Class" or "2nd Class".
/// Every other clue shows a line from <see cref="RedHerringLines"/>, which should sound like clues but give nothing away.
/// Choices are seeded from the seed and slot names, so they stay the same across reconnects and reloads without
/// being saved, and no red herring repeats until every one has been shown.</summary>
public sealed class ParrotHuntClueText
{
    private static readonly string[] RedHerringLines =
    [
        "The DoorBot knows where the parrot is, but he wandered off before you could ask",
        "You hear distant squawking and screeching. Maybe you should just give this up",
        "The BellBot offered to \"get you a new parrot.\" He \"has a parrot guy.\" You considered it",
        "The DeskBot declined to assist you with this investigation, but she used different words",
        "The BarBot has been spinning in circles for nearly an hour. You decide to question him another time",
        "You asked the Bomb about the parrot. It suggested nailing it to the perch next time",
        "According to the Bomb, if you're looking for a parrot you should check the fjords",
        "It's so quiet here without the parrot. You feel a momentary pang of sadness. Just kidding",
        "The DoorBot suggested you check your pockets for the parrot",
        "You hear a screech in this room! But it's just the DoorBot's wheels as he repeatedly runs into the wall",
        "You stop what you're doing briefly and wonder if the parrot ever existed at all",
        "The BellBot insists there has never, ever been a parrot on board. A feather is stuck in his hip joint",
        "The DoorBot finds the idea that there has ever been a parrot on board absurd",
        "The BellBot swears that he will find the parrot. A splash is heard as he falls headfirst into the canal",
    ];

    private static readonly string[] ElevatorLines =
    [
        "Genius detective work (and a peek in the spoiler log) confirms that Elevator {elevator} is important",
        "A stray feather was found wedged in the doors of Elevator {elevator}",
        "Parrot droppings have been spotted on the floor of Elevator {elevator}",
        "The LiftBot of Elevator {elevator} complains of a recent passenger who wouldn't stop squawking",
    ];

    private static readonly string[] FloorLines =
    [
        "A quick peek at the spoiler log tells you the parrot is on Floor {floor}. Way to go, detective",
        "The DeskBot announces {class} passengers should avoid Floor {floor} due to the incessant squawking",
        "Pistachio shells have been reported all over Floor {floor}, among the {class} staterooms",
        "The BellBot returned from the {class} area on Floor {floor} covered in feathers",
    ];

    private static readonly string[] RoomLines =
    [
        "You give up and check the spoiler log. The parrot's room number is {room}",
        "Mother Succ-U-Bus has been complaining of an issue in room {room}",
        "The DoorBot is in the corner repeating \"Room {room}. Room {room}. Room {room}.\" This actually seems promising!",
        "Leaning close to the mouth of a nearby Succ-U-Bus, you hear a distant, muffled voice through the pipes: \"Room {room}! {room}!\"",
    ];

    private readonly ParrotHuntInfo _info;
    private readonly string _seedKey;

    public ParrotHuntClueText(ParrotHuntInfo info, string seedName, string slotName)
    {
        _info = info;
        _seedKey = $"{seedName}\n{slotName}";
    }

    /// <summary>The line revealing <paramref name="part"/> ("elevator", "floor" or "room") of the target stateroom.</summary>
    public string Clue(string part)
    {
        string[] lines = part switch
        {
            "elevator" => ElevatorLines,
            "floor" => FloorLines,
            "room" => RoomLines,
            _ => [$"Clue: {part}"],
        };
        return lines[RandomFor(part).Next(lines.Length)]
            .Replace("{elevator}", _info.Elevator.ToString())
            .Replace("{floor}", _info.Floor.ToString())
            .Replace("{room}", _info.Room.ToString())
            .Replace("{class}", _info.ClassName);
    }

    /// <summary>The red herring for reaching exactly <paramref name="clues"/> Progressive Clues, a count that isn't one
    /// of the thresholds.</summary>
    public string RedHerring(int clues)
    {
        string[] order = [.. RedHerringLines];
        RandomFor("red herring").Shuffle(order);
        // Which red herring this is, counting only the Progressive Clues that weren't real clues
        int number = Math.Max(1, clues - _info.Thresholds.Count(t => t <= clues));
        return order[(number - 1) % order.Length];
    }

    /// <summary>The line for reaching exactly <paramref name="clues"/> Progressive Clues: a real clue at a threshold, or else a red herring.</summary>
    public string LineFor(int clues) => _info.PartRevealedAt(clues) is { } part ? Clue(part) : RedHerring(clues);

    /// <summary>A generator seeded from the slot and <paramref name="purpose"/>. Ensures line picks are unique and
    /// consistent across a run.</summary>
    private Random RandomFor(string purpose)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes($"{_seedKey}\n{purpose}"))
            hash = (hash ^ b) * 16777619;
        return new Random(unchecked((int)hash));
    }
}
