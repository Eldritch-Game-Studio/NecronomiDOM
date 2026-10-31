namespace NecronomiDom.Dom
{
    /// <summary><c>CustomEventInit</c> (DOM § 2.4).</summary>
    public class CustomEventInit : EventInit
    {
        public object? Detail { get; init; }
    }

    /// <summary>DOM § 2.4 <c>CustomEvent</c> with <see cref="Detail"/> and legacy <see cref="InitCustomEvent"/>.</summary>
    public class CustomEvent : Event
    {
        public CustomEvent(string type, CustomEventInit? init = null) : base(type, init) => Detail = init?.Detail;

        public object? Detail { get; private set; }

        public void InitCustomEvent(string type, bool bubbles = false, bool cancelable = false, object? detail = null)
        {
            if (DispatchFlag) return;
            Initialize(type, bubbles, cancelable);
            Detail = detail;
        }
    }
}
