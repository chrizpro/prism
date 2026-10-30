using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Commands;

/// <summary>
/// The commands the app knows how to start, found again by a name that survives saving. A history step remembers which command made it
/// (as such a name), so a file opened later can still re-edit that step. A name is the command's type and title.
/// </summary>
public static class CommandCatalog
{
    static readonly List<Func<Command>> Registered = [];
    static readonly HashSet<Func<Command>> Seen = [];
    static Dictionary<string, Func<Command>?>? _map;
    static readonly object Gate = new();

    public static string KeyOf(Command c) => c.GetType().FullName + "|" + c.Title;

    /// <summary>Remember a way of making a command (the ribbon and the automation tools do this for everything they can start).</summary>
    public static void Register(Func<Command> make)
    {
        lock (Gate)
        {
            if (!Seen.Add(make)) return;
            Registered.Add(make); _map = null;
        }
    }

    /// <summary>How to make the command with this name, or null when it is unknown or two different commands share the name.</summary>
    public static Func<Command>? Find(string key)
    {
        lock (Gate)
        {
            _map ??= Build();
            return _map.TryGetValue(key, out var make) ? make : null;
        }
    }

    /// <summary>Names that more than one different command answers to. Those steps cannot be restored, so there should be none.</summary>
    public static IReadOnlyList<string> Ambiguous()
    {
        lock (Gate) { _map ??= Build(); return [.. _map.Where(kv => kv.Value == null).Select(kv => kv.Key)]; }
    }

    // two ways of making a command with the same name are the same command if they open with the same inputs
    static string Shape(Command c) => string.Join("|", c.Params.Select(p => p.GetType().Name + ":" + p.Label)) + "#" + c.Slots.Count + "#" + c.Icon;

    static Dictionary<string, Func<Command>?> Build()
    {
        var map = new Dictionary<string, Func<Command>?>(); var shapes = new Dictionary<string, string>();
        foreach (var make in Registered)
        {
            Command c;
            try { c = make(); } catch (Exception) { continue; }
            string key = KeyOf(c), shape = Shape(c);
            if (!map.TryGetValue(key, out _)) { map[key] = make; shapes[key] = shape; }
            else if (map[key] != null && shapes[key] != shape) map[key] = null;
        }
        return map;
    }
}
