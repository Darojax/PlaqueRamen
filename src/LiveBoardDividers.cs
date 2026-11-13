using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace PlaqueRamen;

// Preserve the native description's divider thickness while adding text. The
// shop reuses its preview elements, so restore their exact inline styles when
// another item is selected or the panel detaches. No gameplay polling.
internal static class LiveBoardDividers
{
    private static readonly Dictionary<Label,State> Active=new();
    internal static void Preserve(Label label,string original)
    {
        Restore(label);
        var parent=label.parent;if(parent==null)return;
        var lines=parent.Query<VisualElement>(className:"InfoTextStoreDivider").ToList().ToArray();
        if(lines.Length!=2)return;
        var state=new State(label,parent,lines,original);Active.Add(label,state);state.Start();
    }
    internal static void Restore(Label? label)
    {
        if(label==null || !Active.TryGetValue(label,out var state))return;
        Active.Remove(label);state.Dispose();
    }
    internal static void RestoreAll()
    {
        foreach(var label in Active.Keys.ToArray())Restore(label);
    }
    private sealed class State : IDisposable
    {
        private readonly Label _label;
        private readonly VisualElement _parent;
        private readonly VisualElement[] _lines;
        private readonly StyleLength[] _previous;
        private readonly string _original;
        private IVisualElementScheduledItem? _scheduled;
        private bool _disposed;
        private float _last=float.NaN;
        internal State(Label label,VisualElement parent,VisualElement[] lines,string original)
        { _label=label;_parent=parent;_lines=lines;_original=original;_previous=lines.Select(e=>e.style.maxHeight).ToArray(); }
        internal void Start()
        {
            _label.RegisterCallback<GeometryChangedEvent>(Geometry);
            _parent.RegisterCallback<GeometryChangedEvent>(Geometry);
            _label.RegisterCallback<DetachFromPanelEvent>(Detached);
            _scheduled=_label.schedule.Execute(Adjust);_scheduled.ExecuteLater(1);
        }
        private void Geometry(GeometryChangedEvent _)=>Adjust();
        private void Detached(DetachFromPanelEvent _)=>Restore(_label);
        private void Adjust()
        {
            if(_disposed || _label.panel==null || _label.contentRect.width<=0 || _parent.contentRect.height<=0)return;
            var style=_label.resolvedStyle;
            float text=_label.MeasureTextSize(_original,_label.contentRect.width,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined).y
                +style.paddingTop+style.paddingBottom+style.borderTopWidth+style.borderBottomWidth;
            float other=_parent.contentRect.height-_label.layout.height-_lines.Sum(e=>e.layout.height);
            float cap=NativeDividerLayout.Cap(text,other,_lines.Length);
            if(float.IsNaN(cap) || (!float.IsNaN(_last) && Math.Abs(cap-_last)<.001f))return;
            _last=cap;foreach(var line in _lines)line.style.maxHeight=cap;
        }
        public void Dispose()
        {
            _disposed=true;_scheduled?.Pause();
            _label.UnregisterCallback<GeometryChangedEvent>(Geometry);_parent.UnregisterCallback<GeometryChangedEvent>(Geometry);
            _label.UnregisterCallback<DetachFromPanelEvent>(Detached);
            for(int i=0;i<_lines.Length;i++)_lines[i].style.maxHeight=_previous[i];
        }
    }
}
