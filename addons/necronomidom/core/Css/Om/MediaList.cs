using System;
using System.Collections;
using System.Collections.Generic;
using NecronomiDom.Css.Media;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSSOM § 4.2 MediaList parsing, serialization and mutation.</summary>
    public sealed class MediaList : IEnumerable<string>
    {
        public MediaList() : this(new MediaQueryList(Array.Empty<MediaQuery>())) { }
        public MediaList(MediaQueryList queries) => Queries = queries;
        public MediaQueryList Queries { get; private set; }
        public Action? Changed { get; set; }
        public int Length => Queries.Queries.Count;
        public string MediaText { get => Queries.Serialize(); set { Queries = MediaQueryParser.Parse(value); Changed?.Invoke(); } }
        public string? Item(int index) => index >= 0 && index < Length ? Queries.Queries[index].Serialize() : null;
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public string? this[int index] => Item(index);
        public void AppendMedium(string medium)
        {
            var parsed = MediaQueryParser.Parse(medium);
            if (parsed.Queries.Count != 1) return;
            var text = parsed.Serialize();
            foreach (var existing in Queries.Queries) if (existing.Serialize() == text) return;
            var result = new List<MediaQuery>(Queries.Queries) { parsed.Queries[0] };
            Queries = new MediaQueryList(result);
            Changed?.Invoke();
        }
        public void DeleteMedium(string medium)
        {
            var parsed = MediaQueryParser.Parse(medium);
            if (parsed.Queries.Count != 1) return;
            var text = parsed.Serialize();
            var result = new List<MediaQuery>();
            foreach (var query in Queries.Queries) if (query.Serialize() != text) result.Add(query);
            if (result.Count == Length) throw new Dom.DomException("Media query is absent.", "NotFoundError");
            Queries = new MediaQueryList(result);
            Changed?.Invoke();
        }
        public IEnumerator<string> GetEnumerator() { foreach (var query in Queries.Queries) yield return query.Serialize(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
