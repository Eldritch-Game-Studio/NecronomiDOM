using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public interface IAnimatedOpacitySource
{
    bool TryGetOpacity(Element element, out double opacity);
}

/// <summary>
/// Host-owned opacity-only transition state. Observe true computed styles after style changes,
/// sample one monotonic seconds clock, then dispatch queued UA events after layout/paint iteration.
/// First binding (including rebind after Remove/Clear) does not invent a before-change style.
/// </summary>
public sealed class OpacityTransitionController : IAnimatedOpacitySource
{
    private sealed class State
    {
        internal double Target, Current, From, StartTime, Duration, InitialElapsed, ReversingStart, Shortening = 1;
        internal CssCubicEasing Easing;
        internal bool Running, Started;
    }
    private readonly Dictionary<Element, State> _states = new();
    private readonly List<(Element Target, TransitionEvent Event)> _events = new();
    private double _now = double.NegativeInfinity;
    private bool _dispatching;
    public int Count => _states.Count;
    public bool HasRunningTransitions { get; private set; }

    public bool TryGetOpacity(Element element, out double opacity)
    {
        if (_states.TryGetValue(element, out State? state)) { opacity = state.Current; return true; }
        opacity = 0; return false;
    }

    /// <summary>Optional before-change opacity is an actual earlier host style observation, not an invented initial style.</summary>
    public void UpdateStyle(Element element, CSSStyleDeclaration computed, double now, double? beforeChangeOpacity = null)
    {
        Clock(now);
        OpacityTransitionStyle style = OpacityTransitionStyle.Resolve(computed);
        if (!_states.TryGetValue(element, out State? state))
        {
            double initial = beforeChangeOpacity ?? style.Opacity;
            if (!double.IsFinite(initial) || initial < 0 || initial > 1) throw new ArgumentOutOfRangeException(nameof(beforeChangeOpacity));
            state = new State { Current = initial, Target = initial };
            _states.Add(element, state);
            if (!beforeChangeOpacity.HasValue) return;
        }
        Advance(element, state, now);
        bool propertyRemoved = !style.Enabled;
        if (state.Target == style.Opacity && !propertyRemoved) return;
        double from = state.Current;
        bool reverse = state.Running && state.ReversingStart == style.Opacity;
        double shortening = reverse ? Math.Clamp(Math.Abs(Progress(state, now) * state.Shortening + (1 - state.Shortening)), 0, 1) : 1;
        double reversingStart = reverse ? state.Target : from;
        Cancel(element, state, now);
        state.Target = style.Opacity;
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
        state.Running = true; state.Started = false; HasRunningTransitions = true;
        Queue(element, "transitionrun", state.InitialElapsed);
        Advance(element, state, now);
    }

    /// <returns>Whether any actual paint opacity changed; steady frames allocate nothing.</returns>
    public bool Sample(double now)
    {
        Clock(now);
        bool changed = false, running = false;
        foreach (var pair in _states)
        {
            double previous = pair.Value.Current;
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
        Advance(element, state, now);
        Cancel(element, state, now);
        RefreshRunning();
    }

    public void Clear(double now)
    {
        Clock(now);
        foreach (var pair in _states)
        {
            Advance(pair.Key, pair.Value, now);
            Cancel(pair.Key, pair.Value, now);
        }
        _states.Clear(); HasRunningTransitions = false;
    }

    /// <summary>Uses the existing trusted UA dispatch route, not synthetic dispatchEvent(). Reentrant flushing is ignored.</summary>
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
        if (!state.Started)
        {
            state.Started = true;
            Queue(element, "transitionstart", state.InitialElapsed);
        }
        state.Current = Math.Clamp(state.From + (state.Target - state.From) * Progress(state, now), 0, 1);
        if (now < state.StartTime + state.Duration) return;
        state.Current = state.Target; state.Running = false;
        Queue(element, "transitionend", state.Duration);
    }
    private static double Progress(State state, double now) => state.Duration <= 0 ? 1
        : state.Easing.Sample(Math.Clamp((now - state.StartTime) / state.Duration, 0, 1));
    private void Cancel(Element element, State state, double now)
    {
        if (!state.Running) return;
        state.Running = false;
        Queue(element, "transitioncancel", Math.Clamp(now - state.StartTime, 0, state.Duration));
    }
    private void Queue(Element element, string type, double elapsed) => _events.Add((element,
        new TransitionEvent(type, new TransitionEventInit { Bubbles = true, Cancelable = true, PropertyName = "opacity", ElapsedTime = elapsed })));
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
