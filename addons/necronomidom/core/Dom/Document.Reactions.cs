using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    public partial class Document
    {
        private readonly Queue<Action> _reactions = new();
        private int _reactionDepth;
        private bool _deliveringReactions;
        internal ReactionScope Reactions() { _reactionDepth++; return new ReactionScope(this); }
        internal void EnqueueReaction(Action reaction)
        {
            _reactions.Enqueue(reaction);
            if (_reactionDepth == 0) DeliverReactions();
        }
        private void DeliverReactions()
        {
            if (_deliveringReactions) return;
            _deliveringReactions = true;
            List<Exception>? errors = null;
            try
            {
                while (_reactions.Count > 0)
                {
                    try { _reactions.Dequeue()(); }
                    catch (Exception error)
                    {
                        if (ListenerExceptionHandler == null) (errors ??= new()).Add(error);
                        else
                        {
                            try { ListenerExceptionHandler(error); }
                            catch (Exception reportingError) { (errors ??= new()).Add(reportingError); }
                        }
                    }
                }
            }
            finally { _deliveringReactions = false; }
            if (errors != null) throw new AggregateException("Custom element reaction failed.", errors);
        }
        internal readonly struct ReactionScope : IDisposable
        {
            private readonly Document _document;
            internal ReactionScope(Document document) => _document = document;
            public void Dispose() { if (--_document._reactionDepth == 0) _document.DeliverReactions(); }
        }
    }
}
