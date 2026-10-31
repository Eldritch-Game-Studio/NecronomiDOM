namespace NecronomiDom.Dom
{
    public sealed partial class Attr
    {
        internal void FreezeValueProvider()
        {
            if (ValueProvider == null) return;
            _value = Value;
            ValueProvider = null;
        }
    }
}
