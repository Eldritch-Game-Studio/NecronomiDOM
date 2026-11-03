#nullable enable
using System;
using NecronomiDom.Bindings;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>
    /// Creates a C# binding scope for the currently live document. The scope is disposed before
    /// that document is withdrawn; CSS-only reloads preserve it. Call on the owning UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">This host has no currently live document.</exception>
    public DocumentBindings CreateBindings()
    {
        if (!LiveHosts.Contains(this))
            throw new InvalidOperationException("Cannot create bindings without a currently live document.");

        var bindings = new DocumentBindings(Document);
        void Withdraw(DocumentHost host)
        {
            if (ReferenceEquals(host, this)) bindings.Dispose();
        }
        void Detach()
        {
            DocumentWithdrawing -= Withdraw;
            bindings.Disposed -= Detach;
        }

        bindings.Disposed += Detach;
        DocumentWithdrawing += Withdraw;
        return bindings;
    }
}
