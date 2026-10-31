using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

/// <summary>Property-specific width state. Sampling never overwrites the DOM's authored target.</summary>
public sealed class WidthTransitionController : IAnimatedWidthSource
{
    private readonly record struct Value(AnimatedWidth Width, bool Percent);
    private sealed class State
    {
        internal Value Target, Current, From, ReversingStart;
        internal double StartTime, Duration, InitialElapsed, Shortening = 1;
        internal CssCubicEasing Easing;
        internal string? TimingKeyword;
        internal bool Running, Started;
    }
    private readonly Dictionary<Element, State> _states = new();
    private readonly List<(Element Target, TransitionEvent Event)> _events = new();
    private double _now = double.NegativeInfinity;
    private bool _dispatching;
    public GeometryTransitionProfile Profile { get; }
    public int Count => _states.Count;
    public bool HasRunningTransitions { get; private set; }
    public WidthTransitionController(GeometryTransitionProfile profile = GeometryTransitionProfile.Css) { Profile = profile; }
    public bool TryGetWidth(Element element, out AnimatedWidth width)
    {
        if (_states.TryGetValue(element, out var state)) { width = state.Current.Width; return true; }
        width = default; return false;
    }
    /// <param name="previousParentResolvedWidth">Parent width from the previous completed layout, used ONLY for simplified-profile px/% start conversion; not the current CSS layout percentage basis.</param>
    public void UpdateStyle(Element element, CSSStyleDeclaration computedValues, double now,
        double? previousParentResolvedWidth = null, WidthTransitionStyle? beforeChangeStyle = null)
    {
        Clock(now);
        var style = WidthTransitionStyle.Resolve(computedValues);
        if (!style.Width.HasValue) { Remove(element, now); return; }
        Value target = Target(style);
        if (!_states.TryGetValue(element, out var state))
        {
            Value initial = beforeChangeStyle is { Width: not null } before ? Target(before) : target;
            state = new State { Current = initial, Target = initial };
            _states.Add(element, state);
            if (beforeChangeStyle is not { Width: not null }) return;
        }
        Advance(element, state, now);
        if (Same(state.Target, target) && style.Enabled) return;
        Value from = state.Current;
        bool reverse = state.Running && Same(state.ReversingStart, target);
        double shortening = reverse ? Math.Clamp(Math.Abs(Progress(state, now) * state.Shortening + (1 - state.Shortening)), 0, 1) : 1;
        Value reversingStart = reverse ? state.Target : from;
        bool alreadyCurrent = Same(from, target);
        Cancel(element, state, now);
        state.Target = target;
        double duration = style.Duration * shortening;
        double delay = style.Delay < 0 ? style.Delay * shortening : style.Delay;
        if (!style.Enabled || Math.Max(duration, 0) + delay <= 0 || alreadyCurrent
            || !ConvertStart(ref from, target, previousParentResolvedWidth))
        {
            state.Current = target; RefreshRunning(); return;
        }
        state.From = from; state.Duration = duration; state.StartTime = now + delay;
        state.InitialElapsed = Math.Min(Math.Max(-delay, 0), duration);
        state.Easing = style.Easing; state.TimingKeyword = style.TimingKeyword;
        state.Shortening = shortening; state.ReversingStart = reversingStart;
        state.Running = true; state.Started = false; HasRunningTransitions = true;
        Queue(element, "transitionrun", state.InitialElapsed); Advance(element, state, now);
    }
    public bool Sample(double now)
    {
        Clock(now); bool changed = false, running = false;
        foreach (var pair in _states)
        {
            var previous = pair.Value.Current;
            Advance(pair.Key, pair.Value, now);
            changed |= previous.Width != pair.Value.Current.Width;
            running |= pair.Value.Running;
        }
        HasRunningTransitions = running; return changed;
    }
    public void Remove(Element element, double now)
    {
        Clock(now);
        if (!_states.Remove(element, out var state)) return;
        Advance(element, state, now); Cancel(element, state, now); RefreshRunning();
    }
    public void Clear(double now)
    {
        Clock(now);
        foreach (var pair in _states) { Advance(pair.Key, pair.Value, now); Cancel(pair.Key, pair.Value, now); }
        _states.Clear(); HasRunningTransitions = false;
    }
    public void DispatchEvents()
    {
        if (_dispatching) return;
        _dispatching = true;
        try { for (int i = 0; i < _events.Count; i++) { var pending = _events[i]; pending.Target.DispatchUserEvent(pending.Event); } }
        finally { _events.Clear(); _dispatching = false; }
    }
    private Value Target(WidthTransitionStyle style)
    {
        var value = style.Width!.Value;
        if (Profile == GeometryTransitionProfile.Simplified)
            value = new((float)value.Pixels, (float)value.Percent, value.HasPercentage);
        if (!double.IsFinite(value.Pixels) || !double.IsFinite(value.Percent))
            throw new NotSupportedException("Nonfinite width transition target.");
        return new(value, style.IsPercent);
    }
    private bool ConvertStart(ref Value start, Value end, double? parentWidth)
    {
        if (Profile != GeometryTransitionProfile.Simplified || start.Percent == end.Percent) return true;
        float from = (float)(start.Percent ? start.Width.Percent : start.Width.Pixels);
        if (!end.Percent && Approximately(from, 0)) { start = new(new(0, 0), false); return true; }
        float basis = (float)(parentWidth ?? double.NaN);
        if (end.Percent ? !(basis > 0) : !(basis >= 0)) return false;
        float converted = end.Percent ? from * 100 / basis : from * basis / 100;
        if (!float.IsFinite(converted)) return false;
        start = end.Percent ? new(new(0, converted, true), true) : new(new(converted, 0), false);
        return true;
    }
    private bool Same(Value a, Value b)
    {
        if (Profile != GeometryTransitionProfile.Simplified) return a.Width == b.Width;
        return a.Percent == b.Percent && Approximately((float)(a.Percent ? a.Width.Percent : a.Width.Pixels),
            (float)(b.Percent ? b.Width.Percent : b.Width.Pixels));
    }
    private static bool Approximately(float a, float b) => Math.Abs(b - a)
        < Math.Max(.000001f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8);
    private Value Interpolate(Value a, Value b, double progress)
    {
        if (Profile == GeometryTransitionProfile.Simplified)
        {
            float from = (float)(a.Percent ? a.Width.Percent : a.Width.Pixels);
            float to = (float)(b.Percent ? b.Width.Percent : b.Width.Pixels);
            float value = from + (to - from) * (float)progress;
            return b.Percent ? new(new(0, value, true), true) : new(new(value, 0), false);
        }
        return new(new(a.Width.Pixels + (b.Width.Pixels - a.Width.Pixels) * progress,
            a.Width.Percent + (b.Width.Percent - a.Width.Percent) * progress,
            a.Width.HasPercentage || b.Width.HasPercentage), b.Percent);
    }
    private void Advance(Element element, State state, double now)
    {
        if (!state.Running) return;
        if (now < state.StartTime) { state.Current = state.From; return; }
        if (!state.Started) { state.Started = true; Queue(element, "transitionstart", state.InitialElapsed); }
        if (now < state.StartTime + state.Duration) state.Current = Interpolate(state.From, state.Target, Progress(state, now));
        else { state.Current = state.Target; state.Running = false; Queue(element, "transitionend", state.Duration); }
    }
    private double Progress(State state, double now)
    {
        if (state.Duration <= 0) return 1;
        double progress = Math.Clamp((now - state.StartTime) / state.Duration, 0, 1);
        return Profile == GeometryTransitionProfile.Simplified
            ? PolynomialKeywordEasing.Sample(state.TimingKeyword, state.Easing, progress) : state.Easing.Sample(progress);
    }
    private void Cancel(Element element, State state, double now)
    {
        if (!state.Running) return;
        state.Running = false; Queue(element, "transitioncancel", Math.Clamp(now - state.StartTime, 0, state.Duration));
    }
    private void Queue(Element element, string type, double elapsed) => _events.Add((element,
        new TransitionEvent(type, new TransitionEventInit { Bubbles = true, Cancelable = true, PropertyName = "width", ElapsedTime = elapsed })));
    private void Clock(double now)
    {
        if (!double.IsFinite(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now), "Transition clock must be finite monotonic seconds.");
        _now = now;
    }
    private void RefreshRunning()
    {
        HasRunningTransitions = false;
        foreach (var state in _states.Values) HasRunningTransitions |= state.Running;
    }
}
