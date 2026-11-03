using System;

namespace NecronomiDom.Bindings
{
    /// <summary>A typed source-to-DOM binding failed; the original failure is retained as the inner exception.</summary>
    public sealed class BindingException : Exception
    {
        public BindingException(Type sourceType, string sourceProperty, string targetSelector, string targetProperty, Exception innerException)
            : base($"Binding {sourceType.FullName}.{sourceProperty} -> '{targetSelector}'.{targetProperty} failed: {innerException.Message}", innerException)
        {
            SourceType = sourceType;
            SourceProperty = sourceProperty;
            TargetSelector = targetSelector;
            TargetProperty = targetProperty;
        }

        public Type SourceType { get; }
        public string SourceProperty { get; }
        public string TargetSelector { get; }
        public string TargetProperty { get; }
    }
}
