using System.Collections.Generic;
using System.Diagnostics;

namespace NecronomiDom.Dom
{
    /// <summary><c>EventInit</c> (DOM § 2.2).</summary>
    public class EventInit
    {
        public bool Bubbles { get; init; }

        public bool Cancelable { get; init; }

        public bool Composed { get; init; }
    }

    /// <summary>
    /// DOM § 2.2 <c>Event</c>: flags (stop propagation, stop immediate propagation, canceled, in passive listener,
    /// composed, initialized, dispatch), legacy <see cref="CancelBubble"/>/<see cref="ReturnValue"/>,
    /// <see cref="ComposedPath"/> and <see cref="InitEvent"/>.
    /// </summary>
    public class Event
    {
        public const ushort None = 0;
        public const ushort CapturingPhase = 1;
        public const ushort AtTarget = 2;
        public const ushort BubblingPhase = 3;

        private static readonly long TimeOrigin = Stopwatch.GetTimestamp();

        /// <summary>The event constructor (DOM § 2.5): inner event creation steps then type initialisation.</summary>
        public Event(string type, EventInit? init = null)
        {
            InitializedFlag = true;
            TimeStamp = Stopwatch.GetElapsedTime(TimeOrigin).TotalMilliseconds;
            Type = type;
            Bubbles = init?.Bubbles ?? false;
            Cancelable = init?.Cancelable ?? false;
            ComposedFlag = init?.Composed ?? false;
        }

        public string Type { get; private set; }

        public EventTarget? Target { get; internal set; }

        public EventTarget? SrcElement => Target;

        public EventTarget? CurrentTarget { get; internal set; }

        public ushort EventPhase { get; internal set; }

        public bool Bubbles { get; private set; }

        public bool Cancelable { get; private set; }

        public bool DefaultPrevented => CanceledFlag;

        public bool Composed => ComposedFlag;

        public bool IsTrusted { get; internal set; }

        /// <summary>Milliseconds since a process-wide time origin (relative high resolution coarse time).</summary>
        public double TimeStamp { get; }

        /// <summary>Legacy: false once canceled; setting false cancels.</summary>
        public bool ReturnValue
        {
            get => !CanceledFlag;
            set
            {
                if (!value) SetCanceledFlag();
            }
        }

        /// <summary>Legacy alias of the stop propagation flag; setting false does nothing.</summary>
        public bool CancelBubble
        {
            get => StopPropagationFlag;
            set
            {
                if (value) StopPropagationFlag = true;
            }
        }

        internal bool StopPropagationFlag { get; set; }

        internal bool StopImmediatePropagationFlag { get; set; }

        internal bool CanceledFlag { get; set; }

        internal bool InPassiveListenerFlag { get; set; }

        internal bool ComposedFlag { get; set; }

        internal bool InitializedFlag { get; set; }

        internal bool DispatchFlag { get; set; }

        /// <summary>The event's relatedTarget (used by UI event subclasses; retargeted during dispatch).</summary>
        protected internal EventTarget? RelatedTargetCore { get; set; }

        /// <summary>True for click MouseEvents (DOM § 2.9 isActivationEvent); overridden by MouseEvent.</summary>
        protected internal virtual bool IsActivationEvent => false;

        internal List<EventPathItem> Path { get; } = new();

        public void StopPropagation() => StopPropagationFlag = true;

        public void StopImmediatePropagation()
        {
            StopPropagationFlag = true;
            StopImmediatePropagationFlag = true;
        }

        public void PreventDefault() => SetCanceledFlag();

        /// <summary>initEvent(type, bubbles, cancelable) — no-op while dispatching.</summary>
        public void InitEvent(string type, bool bubbles = false, bool cancelable = false)
        {
            if (DispatchFlag) return;
            Initialize(type, bubbles, cancelable);
        }

        /// <summary>DOM § 2.2 composedPath(), hiding nodes in closed shadow trees relative to the current target.</summary>
        public IReadOnlyList<EventTarget> ComposedPath()
        {
            var composedPath = new List<EventTarget>();
            if (Path.Count == 0) return composedPath;
            var currentTarget = CurrentTarget!;
            composedPath.Add(currentTarget);
            int currentTargetIndex = 0, currentTargetHiddenSubtreeLevel = 0;
            for (var index = Path.Count - 1; index >= 0; index--)
            {
                if (Path[index].RootOfClosedTree) currentTargetHiddenSubtreeLevel++;
                if (ReferenceEquals(Path[index].InvocationTarget, currentTarget))
                {
                    currentTargetIndex = index;
                    break;
                }
                if (Path[index].SlotInClosedTree) currentTargetHiddenSubtreeLevel--;
            }
            int currentHiddenLevel = currentTargetHiddenSubtreeLevel, maxHiddenLevel = currentTargetHiddenSubtreeLevel;
            for (var index = currentTargetIndex - 1; index >= 0; index--)
            {
                if (Path[index].RootOfClosedTree) currentHiddenLevel++;
                if (currentHiddenLevel <= maxHiddenLevel) composedPath.Insert(0, Path[index].InvocationTarget);
                if (Path[index].SlotInClosedTree)
                {
                    currentHiddenLevel--;
                    if (currentHiddenLevel < maxHiddenLevel) maxHiddenLevel = currentHiddenLevel;
                }
            }
            currentHiddenLevel = maxHiddenLevel = currentTargetHiddenSubtreeLevel;
            for (var index = currentTargetIndex + 1; index < Path.Count; index++)
            {
                if (Path[index].SlotInClosedTree) currentHiddenLevel++;
                if (currentHiddenLevel <= maxHiddenLevel) composedPath.Add(Path[index].InvocationTarget);
                if (Path[index].RootOfClosedTree)
                {
                    currentHiddenLevel--;
                    if (currentHiddenLevel < maxHiddenLevel) maxHiddenLevel = currentHiddenLevel;
                }
            }
            return composedPath;
        }

        /// <summary>"initialize" an event (DOM § 2.2).</summary>
        internal void Initialize(string type, bool bubbles, bool cancelable)
        {
            InitializedFlag = true;
            StopPropagationFlag = false;
            StopImmediatePropagationFlag = false;
            CanceledFlag = false;
            IsTrusted = false;
            Target = null;
            Type = type;
            Bubbles = bubbles;
            Cancelable = cancelable;
        }

        /// <summary>Used by legacy createEvent(): such events start uninitialized (type "").</summary>
        internal void MarkUninitialized() => InitializedFlag = false;

        internal void SetTypeForLegacyInvoke(string type) => Type = type;

        private void SetCanceledFlag()
        {
            if (Cancelable && !InPassiveListenerFlag) CanceledFlag = true;
        }
    }

    /// <summary>An event path item (DOM § 2.2 "path").</summary>
    internal sealed class EventPathItem
    {
        public EventPathItem(EventTarget invocationTarget, bool inShadowTree, EventTarget? shadowAdjustedTarget,
                             EventTarget? relatedTarget, bool rootOfClosedTree, bool slotInClosedTree)
        {
            InvocationTarget = invocationTarget;
            InvocationTargetInShadowTree = inShadowTree;
            ShadowAdjustedTarget = shadowAdjustedTarget;
            RelatedTarget = relatedTarget;
            RootOfClosedTree = rootOfClosedTree;
            SlotInClosedTree = slotInClosedTree;
        }

        public EventTarget InvocationTarget { get; }

        public bool InvocationTargetInShadowTree { get; }

        public EventTarget? ShadowAdjustedTarget { get; }

        public EventTarget? RelatedTarget { get; }

        public bool RootOfClosedTree { get; }

        public bool SlotInClosedTree { get; }
    }
}
