using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

/// <summary>Individual 2D scale state following the existing deferred transition-event conventions.</summary>
public sealed class ScaleTransitionController : IAnimatedScaleSource
{
    private sealed class State
    {
        internal AnimatedScale Target, Current, From, ReversingStart;
        internal double StartTime, Duration, InitialElapsed, Shortening = 1;
        internal CssCubicEasing Easing;
        internal string? TimingKeyword;
        internal bool TargetIsNone, Running, Started;
    }
    private readonly Dictionary<Element, State> _states = new();
    private readonly List<(Element Target, TransitionEvent Event)> _events = new();
    private double _now = double.NegativeInfinity;
    private bool _dispatching;
    public GeometryTransitionProfile Profile { get; }
    public int Count => _states.Count;
    public bool HasRunningTransitions { get; private set; }
    public ScaleTransitionController(GeometryTransitionProfile profile = GeometryTransitionProfile.Css) { Profile = profile; }
    public bool TryGetScale(Element element, out AnimatedScale scale)
    {
        if (_states.TryGetValue(element, out var state) && (state.Running || !state.TargetIsNone))
        { scale = state.Current; return true; }
        scale = default; return false;
    }
    public void UpdateStyle(Element element, CSSStyleDeclaration computedValues, double now, ScaleTransitionStyle? beforeChangeStyle = null)
    {
        Clock(now);
        var style = ScaleTransitionStyle.Resolve(computedValues);
        AnimatedScale target = Target(style);
        bool targetIsNone = !style.Scale.HasValue;
        if (!_states.TryGetValue(element, out var state))
        {
            AnimatedScale initial = beforeChangeStyle.HasValue ? Target(beforeChangeStyle.Value) : target;
            state = new State { Current = initial, Target = initial,
                TargetIsNone = beforeChangeStyle.HasValue ? !beforeChangeStyle.Value.Scale.HasValue : targetIsNone };
            _states.Add(element, state);
            if (!beforeChangeStyle.HasValue) return;
        }
        Advance(element, state, now);
        if (Same(state.Target, target) && state.TargetIsNone == targetIsNone && style.Enabled) return;
        AnimatedScale from = state.Current;
        bool reverse = state.Running && Same(state.ReversingStart, target);
        double shortening = reverse ? Math.Clamp(Math.Abs(Progress(state, now) * state.Shortening + (1 - state.Shortening)), 0, 1) : 1;
        AnimatedScale reversingStart = reverse ? state.Target : from;
        Cancel(element, state, now);
        state.Target = target; state.TargetIsNone = targetIsNone;
        double duration = style.Duration * shortening;
        double delay = style.Delay < 0 ? style.Delay * shortening : style.Delay;
        if (!style.Enabled || Math.Max(duration, 0) + delay <= 0 || Same(from, target))
        { state.Current = target; RefreshRunning(); return; }
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
            var state = pair.Value;
            var previous = state.Current;
            bool hadTransform = state.Running || !state.TargetIsNone;
            Advance(pair.Key, state, now);
            changed |= previous != state.Current || hadTransform != (state.Running || !state.TargetIsNone);
            running |= state.Running;
        }
        HasRunningTransitions = running; return changed;
    }
    public bool Contains(Element element) => _states.ContainsKey(element);
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
    private AnimatedScale Target(ScaleTransitionStyle style)
    {
        var value = style.Scale ?? AnimatedScale.Identity;
        if (Profile == GeometryTransitionProfile.Simplified) value = new((float)value.X, (float)value.Y);
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y)) throw new NotSupportedException("Nonfinite scale transition target.");
        return value;
    }
    private bool Same(AnimatedScale a, AnimatedScale b)
    {
        if (Profile != GeometryTransitionProfile.Simplified) return a == b;
        // Simplified equality: squared float32 distance below 1e-5 squared (z is identically one here).
        float x = (float)a.X - (float)b.X, y = (float)a.Y - (float)b.Y;
        return x * x + y * y < .00001f * .00001f;
    }
    private AnimatedScale Interpolate(AnimatedScale a, AnimatedScale b, double progress)
    {
        if (Profile == GeometryTransitionProfile.Simplified)
        {
            float x = (float)a.X, y = (float)a.Y, t = (float)progress;
            return new(x + ((float)b.X - x) * t, y + ((float)b.Y - y) * t);
        }
        return new(a.X + (b.X - a.X) * progress, a.Y + (b.Y - a.Y) * progress);
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
        new TransitionEvent(type, new TransitionEventInit { Bubbles = true, Cancelable = true, PropertyName = "scale", ElapsedTime = elapsed })));
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
