using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>Immutable document opt-in; ordinary CSS is never silently given simplified interpolation.</summary>
public enum BackgroundColorTransitionProfile { Css, Simplified }

public interface IAnimatedBackgroundColorSource
{
    bool TryGetBackgroundColor(Element element, out Rgba color);
}

/// <summary>
/// Background-only state following the opacity controller's before-change, reversal, cancellation
/// and deferred trusted-event conventions. Ordinary CSS uses premultiplied sRGB and CSS cubic easing.
/// </summary>
public sealed class BackgroundColorTransitionController : IAnimatedBackgroundColorSource
{
    private sealed class State
    {
        internal Rgba Target, Current, From, ReversingStart;
        internal double StartTime, Duration, InitialElapsed, Shortening = 1;
        internal CssCubicEasing Easing;
        internal string? TimingKeyword;
        internal bool Running, Started;
    }
    private readonly Dictionary<Element, State> _states = new();
    private readonly List<(Element Target, TransitionEvent Event)> _events = new();
    private double _now = double.NegativeInfinity;
    private bool _dispatching;
    public int Count => _states.Count;
    public bool HasRunningTransitions { get; private set; }
    public BackgroundColorTransitionProfile Profile { get; }
    public BackgroundColorTransitionController(BackgroundColorTransitionProfile profile = BackgroundColorTransitionProfile.Css)
    { Profile = profile; }

    public bool TryGetBackgroundColor(Element element, out Rgba color)
    {
        if (_states.TryGetValue(element, out State? state)) { color = state.Current; return true; }
        color = default; return false;
    }

    public void UpdateStyle(Element element, CSSStyleDeclaration computed, double now, Rgba? beforeChangeColor = null)
    {
        Clock(now);
        var style = BackgroundColorTransitionStyle.Resolve(computed);
        if (!_states.TryGetValue(element, out State? state))
        {
            Rgba initial = beforeChangeColor ?? style.Color;
            state = new State { Current = initial, Target = initial };
            _states.Add(element, state);
            if (!beforeChangeColor.HasValue) return;
        }
        Advance(element, state, now);
        if (state.Target == style.Color && style.Enabled) return;
        Rgba from = state.Current;
        bool reverse = state.Running && state.ReversingStart == style.Color;
        double shortening = reverse ? Math.Clamp(Math.Abs(Progress(state, now) * state.Shortening + (1 - state.Shortening)), 0, 1) : 1;
        Rgba reversingStart = reverse ? state.Target : from;
        Cancel(element, state, now);
        state.Target = style.Color;
        double duration = style.Duration * shortening;
        double delay = style.Delay < 0 ? style.Delay * shortening : style.Delay;
        if (!style.Enabled || Math.Max(duration, 0) + delay <= 0 || from == state.Target)
        {
            state.Current = state.Target;
            RefreshRunning();
            return;
        }
        state.From = from; state.Duration = duration; state.StartTime = now + delay;
        state.InitialElapsed = Math.Min(Math.Max(-delay, 0), duration);
        state.Easing = style.Easing; state.Shortening = shortening; state.ReversingStart = reversingStart;
        state.TimingKeyword = style.TimingKeyword;
        state.Running = true; state.Started = false; HasRunningTransitions = true;
        Queue(element, "transitionrun", state.InitialElapsed);
        Advance(element, state, now);
    }

    /// <returns>True only when paint changes. Unchanged and event-free running frames allocate nothing.</returns>
    public bool Sample(double now)
    {
        Clock(now);
        bool changed = false, running = false;
        foreach (var pair in _states)
        {
            Rgba previous = pair.Value.Current;
            Advance(pair.Key, pair.Value, now);
            changed |= previous != pair.Value.Current;
            running |= pair.Value.Running;
        }
        HasRunningTransitions = running;
        return changed;
    }
    public void Remove(Element element, double now)
    {
        Clock(now);
        if (!_states.Remove(element, out State? state)) return;
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
        try
        {
            for (int i = 0; i < _events.Count; i++)
            {
                var pending = _events[i];
                pending.Target.DispatchUserEvent(pending.Event);
            }
        }
        finally { _events.Clear(); _dispatching = false; }
    }
    private void Advance(Element element, State state, double now)
    {
        if (!state.Running) return;
        if (now < state.StartTime) { state.Current = state.From; return; }
        if (!state.Started) { state.Started = true; Queue(element, "transitionstart", state.InitialElapsed); }
        if (now < state.StartTime + state.Duration) state.Current = Interpolate(state.From, state.Target, Progress(state, now));
        else
        {
            state.Current = state.Target; state.Running = false;
            Queue(element, "transitionend", state.Duration);
        }
    }
    private Rgba Interpolate(Rgba a, Rgba b, double progress)
    {
        if (Profile == BackgroundColorTransitionProfile.Simplified)
        {
            // Simplified: unclamped straight float32 RGBA lerp, no gamma conversion or premultiplication.
            float t = (float)progress;
            return new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
        }
        var color = CssColorInterpolation.Interpolate(new("srgb", a.R, a.G, a.B, a.A),
            new("srgb", b.R, b.G, b.B, b.A), "srgb", "shorter", progress);
        return new((float)color.C1, (float)color.C2, (float)color.C3, (float)color.Alpha);
    }
    private double Progress(State state, double now)
    {
        if (state.Duration <= 0) return 1;
        double progress = Math.Clamp((now - state.StartTime) / state.Duration, 0, 1);
        if (Profile != BackgroundColorTransitionProfile.Simplified) return state.Easing.Sample(progress);
        return PolynomialKeywordEasing.Sample(state.TimingKeyword, state.Easing, progress);
    }
    private void Cancel(Element element, State state, double now)
    {
        if (!state.Running) return;
        state.Running = false;
        Queue(element, "transitioncancel", Math.Clamp(now - state.StartTime, 0, state.Duration));
    }
    private void Queue(Element element, string type, double elapsed) => _events.Add((element,
        new TransitionEvent(type, new TransitionEventInit { Bubbles = true, Cancelable = true,
            PropertyName = "background-color", ElapsedTime = elapsed })));
    private void Clock(double now)
    {
        if (!double.IsFinite(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now), "Transition clock must be finite monotonic seconds.");
        _now = now;
    }
    private void RefreshRunning()
    {
        HasRunningTransitions = false;
        foreach (State state in _states.Values) HasRunningTransitions |= state.Running;
    }
}
