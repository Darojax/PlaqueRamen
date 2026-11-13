using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace PlaqueRamen;

// Same brief loading label as the other Ramen mods. Optional peers are read
// once at first paint, without linking their DLLs or changing their state.
internal sealed class StartupBadge
{
    internal const string RelaxedId = "com.danie.ramenshop.relaxed";
    internal const string RadioId = "com.darojax.ramenshop.radio";
    private readonly GUIContent _text;
    private readonly List<(BaseUnityPlugin Plugin, FieldInfo Active)> _peers = new();
    private GUIStyle? _style, _shadow;
    private bool _peersChecked, _expired;
    private float _shownAt = -1f;

    internal StartupBadge(string version) { _text = new GUIContent("PlaqueRamen v" + version); }

    internal void Draw()
    {
        if (_expired || Event.current.type != EventType.Repaint) return;
        float now = Time.realtimeSinceStartup;
        if (_shownAt < 0) _shownAt = now;
        if (now - _shownAt >= 3f) { _expired = true; return; }
        if (!_peersChecked)
        {
            _peersChecked = true;
            AddPeer(RelaxedId, "_active");
            AddPeer(RadioId, "_ready");
        }
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerLeft };
            _style.normal.textColor = new Color(0.96f, 0.94f, 0.87f, 0.85f);
            _shadow = new GUIStyle(_style);
            _shadow.normal.textColor = new Color(0, 0, 0, 0.75f);
        }
        int row = 0;
        foreach (var peer in _peers)
            if (peer.Plugin && peer.Plugin.isActiveAndEnabled && peer.Active.GetValue(peer.Plugin) is true) row++;
        var rect = new Rect(12, Screen.height - 34 - row * 22, 240, 24);
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _text, _shadow);
        GUI.Label(rect, _text, _style);
    }

    private void AddPeer(string id, string activeField)
    {
        if (!Chainloader.PluginInfos.TryGetValue(id, out var info) || !info.Instance) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = info.Instance.GetType();
        var active = type.GetField(activeField, flags);
        // Older releases without a loading label do not occupy a row.
        if (type.GetMethod("OnGUI", flags) != null && active?.FieldType == typeof(bool))
            _peers.Add((info.Instance, active));
    }
}
