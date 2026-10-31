using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>
    /// DOM § 2.9 "dispatch" with "append to an event path", "invoke" and "inner invoke", including shadow-tree
    /// retargeting (§ 4.2.2 "retarget") and activation behavior. Listener exceptions are "reported": passed to the
    /// current target's <see cref="Document.ListenerExceptionHandler"/> when set, otherwise collected and rethrown
    /// as an <see cref="AggregateException"/> after dispatch has completed (so dispatch state is never left dirty).
    /// </summary>
    internal static class EventDispatcher
    {
        public static bool Dispatch(Event e, EventTarget target)
        {
            List<Exception>? unreported = null;
            e.DispatchFlag = true;
            var targetOverride = target;
            EventTarget? activationTarget = null;
            var relatedTarget = Retarget(e.RelatedTargetCore, target);
            var clearTargets = false;
            if (!ReferenceEquals(target, relatedTarget) || ReferenceEquals(target, e.RelatedTargetCore))
            {
                AppendToEventPath(e, target, targetOverride, relatedTarget, false);
                var isActivationEvent = e.IsActivationEvent;
                if (isActivationEvent && target.HasActivationBehavior) activationTarget = target;
                var slottable = target is Node tn && tn.AssignedSlotInternal != null ? target : null;
                var slotInClosedTree = false;
                var parent = target.GetTheParent(e);
                while (parent != null)
                {
                    if (slottable != null)
                    {
                        slottable = null;
                        if (((Node)parent).Root is ShadowRoot { Mode: ShadowRootMode.Closed }) slotInClosedTree = true;
                    }
                    if (parent is Node pn && pn.AssignedSlotInternal != null) slottable = parent;
                    relatedTarget = Retarget(e.RelatedTargetCore, parent);
                    if (parent is Node parentNode && target is Node targetNode &&
                        targetNode.Root.IsShadowIncludingInclusiveAncestorOf(parentNode))
                    {
                        if (isActivationEvent && e.Bubbles && activationTarget == null && parent.HasActivationBehavior)
                            activationTarget = parent;
                        AppendToEventPath(e, parent, null, relatedTarget, slotInClosedTree);
                    }
                    else if (ReferenceEquals(parent, relatedTarget))
                    {
                        parent = null;
                    }
                    else
                    {
                        target = parent;
                        if (isActivationEvent && activationTarget == null && target.HasActivationBehavior) activationTarget = target;
                        AppendToEventPath(e, parent, target, relatedTarget, slotInClosedTree);
                    }
                    if (parent != null) parent = parent.GetTheParent(e);
                    slotInClosedTree = false;
                }
                EventPathItem? clearTargetsItem = null;
                for (var i = e.Path.Count - 1; i >= 0; i--)
                {
                    if (e.Path[i].ShadowAdjustedTarget == null) continue;
                    clearTargetsItem = e.Path[i];
                    break;
                }
                clearTargets = IsInShadowTree(clearTargetsItem?.ShadowAdjustedTarget) || IsInShadowTree(clearTargetsItem?.RelatedTarget);
                if (activationTarget != null) activationTarget.LegacyPreActivationBehavior();
                for (var i = e.Path.Count - 1; i >= 0; i--)
                {
                    e.EventPhase = e.Path[i].ShadowAdjustedTarget != null ? Event.AtTarget : Event.CapturingPhase;
                    Invoke(i, e, capturing: true, ref unreported);
                }
                for (var i = 0; i < e.Path.Count; i++)
                {
                    if (e.Path[i].ShadowAdjustedTarget != null) e.EventPhase = Event.AtTarget;
                    else
                    {
                        if (!e.Bubbles) continue;
                        e.EventPhase = Event.BubblingPhase;
                    }
                    Invoke(i, e, capturing: false, ref unreported);
                }
            }
            e.EventPhase = Event.None;
            e.CurrentTarget = null;
            e.Path.Clear();
            e.DispatchFlag = false;
            e.StopPropagationFlag = false;
            e.StopImmediatePropagationFlag = false;
            if (clearTargets)
            {
                e.Target = null;
                e.RelatedTargetCore = null;
            }
            if (activationTarget != null)
            {
                if (!e.CanceledFlag) activationTarget.ActivationBehavior(e);
                else activationTarget.LegacyCanceledActivationBehavior();
            }
            if (unreported != null) throw new AggregateException("Event listener threw during dispatch of '" + e.Type + "'.", unreported);
            return !e.CanceledFlag;
        }

        /// <summary>"retarget A against B" (DOM § 4.2.2).</summary>
        internal static EventTarget? Retarget(EventTarget? a, EventTarget b)
        {
            while (true)
            {
                if (a is not Node node || node.Root is not ShadowRoot root) return a;
                if (b is Node bn && root.IsShadowIncludingInclusiveAncestorOf(bn)) return a;
                a = root.Host;
            }
        }

        private static bool IsInShadowTree(EventTarget? t) => t is Node n && n.Root is ShadowRoot;

        private static void AppendToEventPath(Event e, EventTarget invocationTarget, EventTarget? shadowAdjustedTarget,
                                              EventTarget? relatedTarget, bool slotInClosedTree)
        {
            var inShadowTree = invocationTarget is Node n && n.Root is ShadowRoot;
            var rootOfClosedTree = invocationTarget is ShadowRoot { Mode: ShadowRootMode.Closed };
            e.Path.Add(new EventPathItem(invocationTarget, inShadowTree, shadowAdjustedTarget, relatedTarget,
                rootOfClosedTree, slotInClosedTree));
        }

        private static void Invoke(int index, Event e, bool capturing, ref List<Exception>? unreported)
        {
            var targetIndex = index;
            while (e.Path[targetIndex].ShadowAdjustedTarget == null) targetIndex--;
            var item = e.Path[index];
            e.Target = e.Path[targetIndex].ShadowAdjustedTarget;
            e.RelatedTargetCore = item.RelatedTarget;
            if (e.StopPropagationFlag) return;
            e.CurrentTarget = item.InvocationTarget;
            var list = item.InvocationTarget.Listeners;
            if (list == null || list.Count == 0) return;
            var listeners = list.ToArray();
            var found = InnerInvoke(e, listeners, capturing, ref unreported);
            if (found || !e.IsTrusted) return;
            var legacy = e.Type switch
            {
                "animationend" => "webkitAnimationEnd",
                "animationiteration" => "webkitAnimationIteration",
                "animationstart" => "webkitAnimationStart",
                "transitionend" => "webkitTransitionEnd",
                _ => null,
            };
            if (legacy == null) return;
            var original = e.Type;
            e.SetTypeForLegacyInvoke(legacy);
            InnerInvoke(e, listeners, capturing, ref unreported);
            e.SetTypeForLegacyInvoke(original);
        }

        private static bool InnerInvoke(Event e, EventListenerEntry[] listeners, bool capturing, ref List<Exception>? unreported)
        {
            var found = false;
            foreach (var listener in listeners)
            {
                if (listener.Removed || listener.Type != e.Type) continue;
                found = true;
                if (capturing != listener.Capture) continue;
                if (listener.Once) e.CurrentTarget!.RemoveListenerEntry(listener);
                if (listener.Passive) e.InPassiveListenerFlag = true;
                try
                {
                    listener.Callback.HandleEvent(e);
                }
                catch (Exception ex)
                {
                    Report(e.CurrentTarget!, ex, ref unreported);
                }
                e.InPassiveListenerFlag = false;
                if (e.StopImmediatePropagationFlag) break;
            }
            return found;
        }

        private static void Report(EventTarget currentTarget, Exception ex, ref List<Exception>? unreported)
        {
            var handler = (currentTarget as Node)?.NodeDocument.ListenerExceptionHandler;
            if (handler != null)
            {
                try { handler(ex); }
                catch (Exception reportingError) { (unreported ??= new List<Exception>()).Add(reportingError); }
            }
            else (unreported ??= new List<Exception>()).Add(ex);
        }
    }
}
