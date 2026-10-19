using System.Text;

namespace Starward.Core.Steam;

/// <summary>Small strict text KeyValues reader. Rejects duplicate keys and malformed input.</summary>
public sealed class SteamVdf : Dictionary<string, object>
{
    public SteamVdf() : base(StringComparer.OrdinalIgnoreCase) { }
    public string Text(string key) => TryGetValue(key, out var v) && v is string s ? s : throw new InvalidDataException("Missing VDF string: " + key);
    public SteamVdf Node(string key) => TryGetValue(key, out var v) && v is SteamVdf n ? n : throw new InvalidDataException("Missing VDF object: " + key);

    public static SteamVdf Parse(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("VDF is too large.");
        int position = 0;
        string? Token()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]) || text[position] == '\uFEFF') { position++; continue; }
                if (position + 1 < text.Length && text[position..(position + 2)] == "//")
                { while (position < text.Length && text[position] != '\n') position++; continue; }
                break;
            }
            if (position == text.Length) return null;
            char ch = text[position++];
            if (ch is '{' or '}') return ch.ToString();
            if (ch != '"') throw new InvalidDataException("Expected quoted VDF token.");
            var result = new StringBuilder();
            while (position < text.Length)
            {
                ch = text[position++];
                if (ch == '"') return result.ToString();
                if (ch == '\\')
                {
                    if (position == text.Length) break;
                    char escaped = text[position++];
                    if (escaped is not ('\\' or '"')) throw new InvalidDataException("Unsupported VDF escape.");
                    ch = escaped;
                }
                result.Append(ch);
            }
            throw new InvalidDataException("Unterminated VDF string.");
        }
        SteamVdf Read(bool nested, int depth)
        {
            if (depth > 32) throw new InvalidDataException("VDF nesting limit.");
            var node = new SteamVdf();
            while (true)
            {
                string? key = Token();
                if (key == null && !nested || key == "}" && nested) return node;
                if (key is null or "{" or "}") throw new InvalidDataException("Unexpected VDF delimiter.");
                string? value = Token();
                if (value is null or "}") throw new InvalidDataException("Missing VDF value.");
                if (!node.TryAdd(key, value == "{" ? Read(true, depth + 1) : value)) throw new InvalidDataException("Duplicate VDF key.");
            }
        }
        return Read(false, 0);
    }

    public override string ToString()
    {
        var output = new StringBuilder();
        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        void Write(SteamVdf node, string indent)
        {
            foreach (var (key, value) in node)
            {
                output.Append(indent).Append(Quote(key));
                if (value is SteamVdf child)
                { output.AppendLine().Append(indent).AppendLine("{"); Write(child, indent + "\t"); output.Append(indent).AppendLine("}"); }
                else output.Append('\t').AppendLine(Quote((string)value));
            }
        }
        Write(this, "");
        return output.ToString();
    }
}
