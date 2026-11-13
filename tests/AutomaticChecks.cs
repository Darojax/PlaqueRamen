using PlaqueRamen;
using System.Text.Json;

internal static class AutomaticChecks
{
    internal static void Run(HolderDefinition holder, Action<bool, string> check, Action<Action, string> refuses)
    {
        var schedule = new DiscoverySchedule();
        check(!schedule.Due(100), "no unrequested scene scans");
        schedule.Request(100);
        check(!schedule.Due(100.1) && schedule.Due(100.25), "load discovery waits briefly for spawned decorations");
        check(!schedule.Due(100.25) && schedule.Due(101), "each scheduled pass runs only once");
        check(schedule.Due(104) && schedule.Due(112) && !schedule.Due(1000), "loading retries are bounded and stop without a polling loop");
        schedule.Request(1200); schedule.Request(1200.1);
        check(!schedule.Due(1200.25) && schedule.Due(1200.35), "overlapping load/menu requests coalesce");
        check(schedule.Due(1201.1) && schedule.Due(1204.1) && schedule.Due(1212.1) && !schedule.Due(1300), "coalescing does not multiply scene sweeps");
        schedule.Request(5000);
        check(schedule.Due(5000.25), "a board placed much later is still found without any hotkey");
        schedule.Request(6000);
        check(schedule.Due(6000.25), "returning to a shop schedules fresh discovery");
        var automatic = AutoLayout.RightToLeft(holder);
        var targets=new TargetDiscovery<string>();targets.Request("new board",0);
        check(targets.Due(0).SequenceEqual(new[]{"new board"})&&targets.Due(.1).Length==0,"new placement is checked immediately, without a scene sweep or per-frame repeated work");
        check(targets.Due(.25).SequenceEqual(new[]{"new board"})&&targets.Due(1).Length==1&&targets.Due(4).Length==1&&targets.Due(12).Length==1&&targets.Count==0,
            "targeted retries retain delayed prefab coverage and stop after the final load window");
        targets.Request("paper sign",20);targets.Request("second board",20);
        check(targets.Due(20).Order().SequenceEqual(new[]{"paper sign","second board"}),"multiple late placements are tracked independently");
        targets.RemoveWhere(t=>t=="paper sign");
        check(targets.Due(20.25).SequenceEqual(new[]{"second board"}),"unloaded or destroyed decorations can be retired without losing other targets");
        targets.Request("second board",21);
        check(targets.Due(21).Length==1&&targets.Due(21).Length==0,"repeated requests for one decoration coalesce rather than multiplying work");
        check(automatic.SequenceEqual(Enumerable.Range(1, 9).Reverse()), "automatic order matches the accepted native front-view hook order");
        var json = new JsonSerializerOptions { IncludeFields = true };
        var reordered = JsonSerializer.Deserialize<HolderDefinition>(JsonSerializer.Serialize(holder, json), json)!;
        reordered.Plaques = reordered.Plaques.Reverse().ToArray();
        check(AutoLayout.RightToLeft(reordered).SequenceEqual(automatic), "manifest enumeration cannot reverse the physical reading order");
        reordered.Plaques = reordered.Plaques.Take(8).ToArray();
        refuses(() => AutoLayout.RightToLeft(reordered), "incomplete automatic holder layout refused");
        var first = MenuPlan.CenteredPlaques(automatic, 2); var second = MenuPlan.CenteredPlaques(automatic, 5);
        check(first.SequenceEqual(new[] { 6, 5 }) && second.SequenceEqual(new[] { 7, 6, 5, 4, 3 })
            && automatic.SequenceEqual(Enumerable.Range(1, 9).Reverse()), "multiple holders use independent occupied-slot arrays without changing fixed hook order");
    }
}
