using System;
using System.Collections;
using System.Collections.Generic;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>Immutable WebIDL sequence snapshot for CSS math operands.</summary>
    public readonly struct CSSNumericArray : IReadOnlyList<CSSNumericValue>
    {
        private readonly CSSNumericValue[]? _values;
        public CSSNumericArray(IEnumerable<CSSNumericValue> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _values = values is CSSNumericValue[] array ? (CSSNumericValue[])array.Clone()
                : new List<CSSNumericValue>(values).ToArray();
        }
        internal CSSNumericArray(CSSNumericValue[] ownedValues, bool takeOwnership) => _values = ownedValues;
        public int Count => _values?.Length ?? 0;
        public int Length => Count;
        public CSSNumericValue this[int index] => (_values ?? Array.Empty<CSSNumericValue>())[index];
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<CSSNumericValue> IEnumerable<CSSNumericValue>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<CSSNumericValue>
        {
            private readonly CSSNumericArray _array;
            private int _index;
            internal Enumerator(CSSNumericArray array) { _array = array; _index = -1; }
            public CSSNumericValue Current => _index >= 0 && _index < _array.Count
                ? _array[_index] : throw new InvalidOperationException();
            object IEnumerator.Current => Current;
            public bool MoveNext() { if (_index < _array.Count) _index++; return _index < _array.Count; }
            public void Reset() => _index = -1;
            public void Dispose() { }
        }
    }
}
