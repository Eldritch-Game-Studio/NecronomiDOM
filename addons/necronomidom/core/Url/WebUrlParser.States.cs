using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>The state machine of URL Standard § 4.4 "basic URL parser" (states scheme start … fragment).</summary>
    internal sealed partial class WebUrlParser
    {
        private WebUrl? Run()
        {
            var state = State.SchemeStart;
            var buffer = new StringBuilder();
            bool atSignSeen = false, insideBrackets = false, passwordTokenSeen = false;
            for (var pointer = 0; ; pointer++)
            {
                var c = At(pointer);
                switch (state)
                {
                    case State.SchemeStart:
                        if (IsAsciiAlpha(c))
                        {
                            buffer.Append(char.ToLowerInvariant((char)c));
                            state = State.Scheme;
                        }
                        else
                        {
                            state = State.NoScheme;
                            pointer--;
                        }
                        break;
                    case State.Scheme:
                        if (IsAsciiAlpha(c) || c is >= '0' and <= '9' or '+' or '-' or '.')
                        {
                            buffer.Append(char.ToLowerInvariant((char)c));
                        }
                        else if (c == ':')
                        {
                            _scheme = buffer.ToString();
                            buffer.Clear();
                            if (_scheme == "file") state = State.File;
                            else if (Special && _base != null && _base.Scheme == _scheme) state = State.SpecialRelativeOrAuthority;
                            else if (Special) state = State.SpecialAuthoritySlashes;
                            else if (RemainingStartsWith(pointer, '/'))
                            {
                                state = State.PathOrAuthority;
                                pointer++;
                            }
                            else
                            {
                                _opaquePath = new StringBuilder();
                                state = State.OpaquePath;
                            }
                        }
                        else
                        {
                            buffer.Clear();
                            state = State.NoScheme;
                            pointer = -1;
                        }
                        break;
                    case State.NoScheme:
                        if (_base == null || (_base.HasOpaquePath && c != '#')) return null;
                        if (_base.HasOpaquePath)
                        {
                            _scheme = _base.Scheme;
                            _opaquePath = new StringBuilder(_base.OpaquePath);
                            _query = _base.Query;
                            _fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                        else
                        {
                            state = _base.Scheme != "file" ? State.Relative : State.File;
                            pointer--;
                        }
                        break;
                    case State.SpecialRelativeOrAuthority:
                        if (c == '/' && RemainingStartsWith(pointer, '/'))
                        {
                            state = State.SpecialAuthorityIgnoreSlashes;
                            pointer++;
                        }
                        else
                        {
                            state = State.Relative;
                            pointer--;
                        }
                        break;
                    case State.PathOrAuthority:
                        if (c == '/') state = State.Authority;
                        else
                        {
                            state = State.Path;
                            pointer--;
                        }
                        break;
                    case State.Relative:
                        _scheme = _base!.Scheme;
                        if (c == '/' || (Special && c == '\\')) state = State.RelativeSlash;
                        else
                        {
                            CopyAuthorityFromBase();
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
                                ShortenPath();
                                state = State.Path;
                                pointer--;
                            }
                        }
                        break;
                    case State.RelativeSlash:
                        if (Special && c is '/' or '\\') state = State.SpecialAuthorityIgnoreSlashes;
                        else if (c == '/') state = State.Authority;
                        else
                        {
                            CopyAuthorityFromBase();
                            state = State.Path;
                            pointer--;
                        }
                        break;
                    case State.SpecialAuthoritySlashes:
                        state = State.SpecialAuthorityIgnoreSlashes;
                        if (c == '/' && RemainingStartsWith(pointer, '/')) pointer++;
                        else pointer--;
                        break;
                    case State.SpecialAuthorityIgnoreSlashes:
                        if (c is not ('/' or '\\'))
                        {
                            state = State.Authority;
                            pointer--;
                        }
                        break;
                    case State.Authority:
                        if (c == '@')
                        {
                            if (atSignSeen) buffer.Insert(0, "%40");
                            atSignSeen = true;
                            AppendCredentials(buffer.ToString(), ref passwordTokenSeen);
                            buffer.Clear();
                        }
                        else if (c is Eof or '/' or '?' or '#' || (Special && c == '\\'))
                        {
                            if (atSignSeen && buffer.Length == 0) return null;
                            pointer -= CodePointLength(buffer) + 1;
                            buffer.Clear();
                            state = State.Host;
                        }
                        else AppendCodePoint(buffer, c);
                        break;
                    case State.Host:
                        if (c == ':' && !insideBrackets)
                        {
                            if (buffer.Length == 0) return null;
                            _host = HostParser.Parse(buffer.ToString(), !Special);
                            if (_host == null) return null;
                            buffer.Clear();
                            state = State.Port;
                        }
                        else if (c is Eof or '/' or '?' or '#' || (Special && c == '\\'))
                        {
                            pointer--;
                            if (Special && buffer.Length == 0) return null;
                            _host = HostParser.Parse(buffer.ToString(), !Special);
                            if (_host == null) return null;
                            buffer.Clear();
                            state = State.PathStart;
                        }
                        else
                        {
                            if (c == '[') insideBrackets = true;
                            if (c == ']') insideBrackets = false;
                            AppendCodePoint(buffer, c);
                        }
                        break;
                    case State.Port:
                        if (c is >= '0' and <= '9') buffer.Append((char)c);
                        else if (c is Eof or '/' or '?' or '#' || (Special && c == '\\'))
                        {
                            if (buffer.Length != 0)
                            {
                                if (buffer.Length > 5 || !int.TryParse(buffer.ToString(), out var port) || port > 65535) return null;
                                _port = port == WebUrl.DefaultPort(_scheme) ? null : port;
                                buffer.Clear();
                            }
                            state = State.PathStart;
                            pointer--;
                        }
                        else return null;
                        break;
                    default:
                        if (!RunFileAndPathStates(ref state, ref pointer, c, buffer)) return null;
                        break;
                }
                if (pointer >= _input.Length) return Build();
            }
        }

        private void AppendCredentials(string buffer, ref bool passwordTokenSeen)
        {
            var user = new StringBuilder(_username);
            var pass = new StringBuilder(_password);
            for (var i = 0; i < buffer.Length; i++)
            {
                var cp = PercentEncoding.ReadCodePoint(buffer, ref i);
                if (cp == ':' && !passwordTokenSeen)
                {
                    passwordTokenSeen = true;
                    continue;
                }
                PercentEncoding.AppendEncoded(passwordTokenSeen ? pass : user, cp, PercentEncodeSet.Userinfo);
            }
            _username = user.ToString();
            _password = pass.ToString();
        }

        private static void AppendCodePoint(StringBuilder sb, int c)
        {
            if (c > 0xFFFF) sb.Append(char.ConvertFromUtf32(c));
            else sb.Append((char)c);
        }

        private static int CodePointLength(StringBuilder sb)
        {
            var n = 0;
            for (var i = 0; i < sb.Length; i++)
            {
                if (char.IsHighSurrogate(sb[i]) && i + 1 < sb.Length && char.IsLowSurrogate(sb[i + 1])) i++;
                n++;
            }
            return n;
        }
    }
}
