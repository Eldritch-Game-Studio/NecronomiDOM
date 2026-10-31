using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>URL Standard § 4.4: file, file slash, file host, path start, path, opaque path, query and fragment states.</summary>
    internal sealed partial class WebUrlParser
    {
        private bool RunFileAndPathStates(ref State state, ref int pointer, int c, StringBuilder buffer)
        {
            switch (state)
            {
                case State.File:
                    _scheme = "file";
                    _host = string.Empty;
                    if (c is '/' or '\\') state = State.FileSlash;
                    else if (_base != null && _base.Scheme == "file")
                    {
                        _host = _base.Host;
                        _path = new(_base.Segments);
                        _query = _base.Query;
                        if (c == '?')
                        {
                            _query = string.Empty;
                            state = State.Query;
                        }
                        else if (c == '#')
                        {
                            _fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                        else if (c != Eof)
                        {
                            _query = null;
                            if (!StartsWithWindowsDriveLetter(pointer)) ShortenPath();
                            else _path.Clear();
                            state = State.Path;
                            pointer--;
                        }
                    }
                    else
                    {
                        state = State.Path;
                        pointer--;
                    }
                    return true;
                case State.FileSlash:
                    if (c is '/' or '\\') state = State.FileHost;
                    else
                    {
                        if (_base != null && _base.Scheme == "file")
                        {
                            _host = _base.Host;
                            if (!StartsWithWindowsDriveLetter(pointer) && _base.Segments.Count > 0 &&
                                IsNormalizedWindowsDriveLetter(_base.Segments[0]))
                                _path.Add(_base.Segments[0]);
                        }
                        state = State.Path;
                        pointer--;
                    }
                    return true;
                case State.FileHost:
                    if (c is Eof or '/' or '\\' or '?' or '#')
                    {
                        pointer--;
                        if (IsWindowsDriveLetter(buffer.ToString())) state = State.Path;
                        else if (buffer.Length == 0)
                        {
                            _host = string.Empty;
                            state = State.PathStart;
                        }
                        else
                        {
                            var host = HostParser.Parse(buffer.ToString(), false);
                            if (host == null) return false;
                            _host = host == "localhost" ? string.Empty : host;
                            buffer.Clear();
                            state = State.PathStart;
                        }
                    }
                    else AppendCodePoint(buffer, c);
                    return true;
                case State.PathStart:
                    if (Special)
                    {
                        state = State.Path;
                        if (c is not ('/' or '\\')) pointer--;
                    }
                    else if (c == '?')
                    {
                        _query = string.Empty;
                        state = State.Query;
                    }
                    else if (c == '#')
                    {
                        _fragment = new StringBuilder();
                        state = State.Fragment;
                    }
                    else if (c != Eof)
                    {
                        state = State.Path;
                        if (c != '/') pointer--;
                    }
                    return true;
                case State.Path:
                    RunPathState(ref state, c, buffer);
                    return true;
                case State.OpaquePath:
                    if (c == '?')
                    {
                        _query = string.Empty;
                        state = State.Query;
                    }
                    else if (c == '#')
                    {
                        _fragment = new StringBuilder();
                        state = State.Fragment;
                    }
                    else if (c == ' ')
                    {
                        _opaquePath!.Append(At(pointer + 1) is '?' or '#' ? "%20" : " ");
                    }
                    else if (c != Eof)
                    {
                        PercentEncoding.AppendEncoded(_opaquePath!, c, PercentEncodeSet.C0Control);
                    }
                    return true;
                case State.Query:
                    if (c is '#' or Eof)
                    {
                        var set = Special ? PercentEncodeSet.SpecialQuery : PercentEncodeSet.Query;
                        _query += PercentEncoding.Encode(buffer.ToString(), set);
                        buffer.Clear();
                        if (c == '#')
                        {
                            _fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                    }
                    else AppendCodePoint(buffer, c);
                    return true;
                default:
                    if (c != Eof) PercentEncoding.AppendEncoded(_fragment!, c, PercentEncodeSet.Fragment);
                    return true;
            }
        }

        private void RunPathState(ref State state, int c, StringBuilder buffer)
        {
            var slash = c == '/' || (Special && c == '\\');
            if (c == Eof || slash || c is '?' or '#')
            {
                var segment = buffer.ToString();
                if (IsDoubleDot(segment))
                {
                    if (_path.Count == 0) _climbed = true;
                    ShortenPath();
                    if (!slash) _path.Add(string.Empty);
                }
                else if (IsSingleDot(segment))
                {
                    if (!slash) _path.Add(string.Empty);
                }
                else
                {
                    if (_scheme == "file" && _path.Count == 0 && IsWindowsDriveLetter(segment))
                        segment = segment.Substring(0, 1) + ":";
                    _path.Add(segment);
                }
                buffer.Clear();
                if (c == '?')
                {
                    _query = string.Empty;
                    state = State.Query;
                }
                else if (c == '#')
                {
                    _fragment = new StringBuilder();
                    state = State.Fragment;
                }
            }
            else
            {
                PercentEncoding.AppendEncoded(buffer, c, PercentEncodeSet.Path);
            }
        }
    }
}
