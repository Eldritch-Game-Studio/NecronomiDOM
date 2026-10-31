using System;
using System.Collections.Generic;

namespace NecronomiDom.Runtime;

/// <summary>
///     Which hosts currently run a live document (inside a tree with its DOM input/paint resources), wherever they
///     are mounted. Lets application-wide observers (for example a global click sound) attach to every document
///     without a scene-wide node search.
/// </summary>
public partial class DocumentHost
{
    private static readonly HashSet<DocumentHost> LiveHosts = new();

    /// <summary>Every host whose <see cref="Document"/> is currently live.</summary>
    public static IReadOnlyCollection<DocumentHost> LiveDocumentHosts => LiveHosts;

    /// <summary>A host's <see cref="Document"/> became live: tree entry with a loaded document, or a load while mounted.</summary>
    public static event Action<DocumentHost>? DocumentLive;

    /// <summary>A host's live <see cref="Document"/> is about to be withdrawn (tree exit or replacement); it is still current.</summary>
    public static event Action<DocumentHost>? DocumentWithdrawing;

    private void PublishLiveDocument()
    {
        LiveHosts.Add(this);
        DocumentLive?.Invoke(this);
    }

    private void RetractLiveDocument()
    {
        if (LiveHosts.Remove(this)) DocumentWithdrawing?.Invoke(this);
    }
}
