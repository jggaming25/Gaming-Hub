using System.Text;

namespace GamingHub.Core.Services;

/// <summary>
/// Minimal parser for Valve's VDF format. Produces a tree of
/// <see cref="Dictionary{TKey,TValue}"/> where values are either strings or
/// nested dictionaries.
/// </summary>
public static class VdfReader
{
    public static Dictionary<string, object?> Parse(string content)
    {
        var root = new Dictionary<string, object?>();
        int pos = 0;
        ParseBlock(content, ref pos, root);
        return root;
    }

    /// <summary>Navigates a nested chain of keys; returns the final object.</summary>
    public static bool Navigate(Dictionary<string, object?> root, out object? value, params string[] path)
    {
        value = null;
        object? current = root;
        foreach (string key in path)
        {
            if (current is not Dictionary<string, object?> dict || !dict.TryGetValue(key, out object? next))
                return false;
            current = next;
        }
        value = current;
        return true;
    }

    private static void ParseBlock(string s, ref int pos, Dictionary<string, object?> target)
    {
        while (true)
        {
            SkipWhitespaceAndComments(s, ref pos);
            if (pos >= s.Length) return;

            char c = s[pos];
            if (c == '}')
            {
                pos++;
                return;
            }

            string key = ReadToken(s, ref pos);
            SkipWhitespaceAndComments(s, ref pos);

            if (pos >= s.Length) return;
            if (s[pos] == '{')
            {
                pos++;
                var child = new Dictionary<string, object?>();
                target[key] = child;
                ParseBlock(s, ref pos, child);
            }
            else
            {
                target[key] = ReadToken(s, ref pos);
            }
        }
    }

    private static string ReadToken(string s, ref int pos)
    {
        SkipWhitespaceAndComments(s, ref pos);
        if (pos >= s.Length) return string.Empty;
        if (s[pos] == '"')
        {
            var sb = new StringBuilder();
            pos++; // skip opening quote
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '\\' && pos + 1 < s.Length)
                {
                    sb.Append(s[pos + 1]);
                    pos += 2;
                    continue;
                }
                if (c == '"')
                {
                    pos++;
                    break;
                }
                sb.Append(c);
                pos++;
            }
            return sb.ToString();
        }

        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not '{' and not '}')
            pos++;
        return s[start..pos];
    }

    private static void SkipWhitespaceAndComments(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            char c = s[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
                continue;
            }
            // VDF comments start with //
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                pos += 2;
                while (pos < s.Length && s[pos] != '\n')
                    pos++;
                continue;
            }
            break;
        }
    }
}